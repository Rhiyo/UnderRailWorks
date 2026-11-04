// Remap: after a game update, finds the game types and members Works uses in the new exe. The obfuscator gives
// everything new scrambled names each build, but some clues can't change: save tags ([eiz("ZTT")], stored in every
// save, so the game must keep them), the original names in the save code ("ZoneTransitionTrigger+_Destination"),
// names the obfuscator kept, strings in method bodies, and the shape of the code (base types, signatures, field types).
// Usage: mono Remap.exe <old underrail.exe> <new underrail.exe> <UnderRailWorks.dll built against the old one>
//   prints "old -> new" for every game type and member the DLL references, and UNMATCHED for any it couldn't find
//   (fix those by hand); exit code 1 if any are unmatched.
// Also: mono Remap.exe --scramble <in.exe> <out.exe> [seed]: a copy with every scrambled name replaced by a random one,
// to test the matching (tools/names/test-remap.sh).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class Remap
{
    static int Main(string[] args)
    {
        if (args[0] == "--scramble") return Scramble(args[1], args[2], args.Length > 3 ? int.Parse(args[3]) : 1);
        var oldModule = ModuleDefinition.ReadModule(args[0]);
        var newModule = ModuleDefinition.ReadModule(args[1]);
        var works = ModuleDefinition.ReadModule(args[2]);
        var game = oldModule.Assembly.Name.Name;

        var types = MatchTypes(oldModule, newModule);
        // What Works uses: type, field and method references into the game assembly.
        var usedTypes = new HashSet<TypeDefinition>(); var usedFields = new HashSet<FieldDefinition>(); var usedMethods = new HashSet<MethodDefinition>();
        Func<TypeReference, TypeDefinition> resolveOld = r => { r = r.GetElementType(); return r.Scope.Name == game || r.Scope.Name == game + ".exe" ? oldModule.GetType(r.FullName) : null; };
        foreach (var t in works.GetTypes())
        {
            Use(resolveOld(t.BaseType ?? t), usedTypes);
            foreach (var m in t.Methods)
            {
                if (!m.HasBody) continue;
                foreach (var i in m.Body.Instructions)
                {
                    var tr = i.Operand as TypeReference; if (tr != null) Use(resolveOld(tr), usedTypes);
                    var fr = i.Operand as FieldReference;
                    if (fr != null) { var dt = resolveOld(fr.DeclaringType); if (dt != null) { usedTypes.Add(dt); var fd = dt.Fields.FirstOrDefault(f => f.Name == fr.Name); if (fd != null) usedFields.Add(fd); } }
                    var mr = i.Operand as MethodReference;
                    if (mr != null) { var dt = resolveOld(mr.DeclaringType); if (dt != null) { usedTypes.Add(dt); var md = Find(dt, mr); if (md != null) usedMethods.Add(md); } }
                    var s = i.Operand as string;     // reflection by name: GetField("b"), GetMethod("at")... reported for checking
                }
            }
        }
        var debug = Environment.GetEnvironmentVariable("REMAP_DEBUG");
        if (debug != null)
        {
            var dt = oldModule.GetType(debug);
            Console.Error.WriteLine("A strict: " + Shape(dt, types, true) + "\nA loose:  " + Shape(dt, types, false));
            var partner = Environment.GetEnvironmentVariable("REMAP_DEBUG_NEW");
            if (partner != null) { var nt = newModule.GetType(partner); Console.Error.WriteLine("B strict: " + Shape(nt, types, true) + "\nB loose:  " + Shape(nt, types, false)); }
        }
        int unmatched = 0;
        Func<TypeDefinition, string> newName = t => { TypeDefinition n; return types.TryGetValue(t, out n) ? n.FullName : null; };
        foreach (var t in usedTypes.OrderBy(t => t.FullName))
        {
            var n = newName(t);
            Console.WriteLine("type   " + t.FullName + " -> " + (n ?? "UNMATCHED"));
            if (n == null) unmatched++;
        }
        foreach (var f in usedFields.OrderBy(f => f.DeclaringType.FullName + "." + f.Name))
        {
            TypeDefinition nt; FieldDefinition nf = types.TryGetValue(f.DeclaringType, out nt) ? MatchField(f, nt, types) : null;
            Console.WriteLine("field  " + f.DeclaringType.FullName + "." + f.Name + " -> " + (nf == null ? "UNMATCHED" : nt.FullName + "." + nf.Name));
            if (nf == null) unmatched++;
        }
        foreach (var m in usedMethods.OrderBy(m => m.DeclaringType.FullName + "." + m.Name))
        {
            TypeDefinition nt; MethodDefinition nm = types.TryGetValue(m.DeclaringType, out nt) ? MatchMethod(m, nt, types) : null;
            Console.WriteLine("method " + m.DeclaringType.FullName + "." + Exact(m) + " -> " + (nm == null ? "UNMATCHED" : nt.FullName + "." + nm.Name));
            if (nm == null) unmatched++;
        }
        Console.Error.WriteLine("types " + usedTypes.Count + ", fields " + usedFields.Count + ", methods " + usedMethods.Count + ", unmatched " + unmatched);
        return unmatched == 0 ? 0 : 1;
    }

    static void Use(TypeDefinition t, HashSet<TypeDefinition> used) { for (; t != null; t = t.DeclaringType) used.Add(t); }

    static MethodDefinition Find(TypeDefinition t, MethodReference r)
    {
        return t.Methods.FirstOrDefault(m => m.Name == r.Name && m.Parameters.Count == r.Parameters.Count
            && m.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(r.Parameters.Select(p => p.ParameterType.FullName))
            && m.ReturnType.FullName == r.ReturnType.FullName);
    }

    // --- Types: stable keys first, then shape, repeated while new matches make more shapes comparable --------------

    static Dictionary<TypeDefinition, TypeDefinition> MatchTypes(ModuleDefinition a, ModuleDefinition b)
    {
        var map = new Dictionary<TypeDefinition, TypeDefinition>();
        var ta = a.GetTypes().ToList(); var tb = b.GetTypes().ToList();
        var taken = Taken;
        Action<Func<TypeDefinition, string>> byKey = key =>
        {
            var ib = tb.Where(t => !taken.Contains(t)).GroupBy(key).Where(g => g.Key != null && g.Count() == 1).ToDictionary(g => g.Key, g => g.First());
            foreach (var g in ta.Where(t => !map.ContainsKey(t)).GroupBy(key).Where(g => g.Key != null && g.Count() == 1))
            {
                TypeDefinition m;
                if (ib.TryGetValue(g.Key, out m)) { map[g.First()] = m; taken.Add(m); }
            }
        };
        byKey(t => GameMap.Scrambled(t.Name) && !t.IsNested ? null : t.FullName);      // kept names
        byKey(t => Tag(t) == null ? null : "tag:" + Tag(t));
        byKey(t => SaveName(t));
        byKey(t => Strings(t));
        // Shape: base type, interfaces, generic arity, field types and method signatures in terms of already matched
        // types; each round lets more types be named, until nothing changes.
        for (int round = 0; round < 12; round++)
        {
            int before = map.Count;
            byKey(t => Shape(t, map, true));
            byKey(t => Shape(t, map, false));
            byKey(t => OneString(t, map));
            byKey(t => t.DeclaringType != null && map.ContainsKey(t.DeclaringType) ? "nested:" + map[t.DeclaringType].FullName + ":" + Shape(t, map, false) : null);
            if (map.Count == before) break;
        }
        return map;
    }

    static readonly HashSet<TypeDefinition> Taken = new HashSet<TypeDefinition>();     // the new module's matched types

    static string Tag(TypeDefinition t)
    {
        var a = t.CustomAttributes.FirstOrDefault(c => c.AttributeType.Name.Length <= 4 && c.ConstructorArguments.Count == 1 && c.ConstructorArguments[0].Value is string && GameMap.Scrambled(c.AttributeType.Name));
        return a == null ? null : (string)a.ConstructorArguments[0].Value;
    }

    static string SaveName(TypeDefinition t)
    {
        var names = t.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand).Where(s => s.Contains("+_") || (s.StartsWith("_") && s.Length > 2)).Distinct().OrderBy(s => s, StringComparer.Ordinal).ToArray();
        return names.Length == 0 ? null : "save:" + string.Join("|", names);
    }

    // Small types with few clues: their strings plus their base type (once that is matched).
    static string OneString(TypeDefinition t, Dictionary<TypeDefinition, TypeDefinition> map)
    {
        bool forB = !map.Keys.Any() || t.Module != map.Keys.First().Module;
        var s = t.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Where(i => i.OpCode == OpCodes.Ldstr).Select(i => (string)i.Operand).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        var b = Name(t.BaseType, map, forB);
        return s.Length == 0 || b.Contains("?") ? null : "str1:" + b + ":" + t.Methods.Count + ":" + string.Join("|", s);
    }

    static string Strings(TypeDefinition t)
    {
        var s = t.Methods.Where(m => m.HasBody).SelectMany(m => m.Body.Instructions).Where(i => i.OpCode == OpCodes.Ldstr)
            .Select(i => (string)i.Operand).Distinct().OrderBy(x => x, StringComparer.Ordinal).ToArray();
        return s.Length < 2 ? null : "str:" + t.Methods.Count + ":" + string.Join("|", s);
    }

    // A type's name in terms of the mapping: kept names stay, matched scrambled ones are replaced by their partner's
    // name in the OTHER module... both sides must agree, so both are written as "M<index>" via a shared id.
    static string Name(TypeReference r, Dictionary<TypeDefinition, TypeDefinition> map, bool forB)
    {
        if (r == null) return "-";
        if (r is GenericInstanceType)
        {
            var g = (GenericInstanceType)r;
            return Name(g.ElementType, map, forB) + "<" + string.Join(",", g.GenericArguments.Select(x => Name(x, map, forB))) + ">";
        }
        if (r is ArrayType) return Name(((ArrayType)r).ElementType, map, forB) + "[]";
        if (r is ByReferenceType) return Name(((ByReferenceType)r).ElementType, map, forB) + "&";
        if (r is GenericParameter) return "!" + ((GenericParameter)r).Position;
        if (!GameMap.Scrambled(r.Name)) return r.FullName;
        var d = r.Resolve();
        if (d == null) return "?";
        if (!forB) { TypeDefinition m; return map.TryGetValue(d, out m) ? m.FullName : "?"; }
        return Taken.Contains(d) ? d.FullName : "?";
    }

    static string Shape(TypeDefinition t, Dictionary<TypeDefinition, TypeDefinition> map, bool strict)
    {
        bool forB = !map.Keys.Any() || t.Module != map.Keys.First().Module;
        var sb = new StringBuilder();
        sb.Append(t.IsInterface ? "i" : t.IsEnum ? "e" : t.IsValueType ? "v" : "c").Append(t.GenericParameters.Count).Append(':').Append(Name(t.BaseType, map, forB));
        foreach (var i in t.Interfaces.Select(i => Name(i.InterfaceType, map, forB)).OrderBy(x => x, StringComparer.Ordinal)) sb.Append(',').Append(i);
        sb.Append("|f:");
        foreach (var f in t.Fields.Select(f => (f.IsStatic ? "s" : "") + Name(f.FieldType, map, forB) + (t.IsEnum && f.HasConstant ? "=" + f.Constant : "")).OrderBy(x => x, StringComparer.Ordinal)) sb.Append(f).Append(';');
        sb.Append("|m:");
        foreach (var m in t.Methods.Select(m => Sig(m, strict ? map : null, forB)).OrderBy(x => x, StringComparer.Ordinal)) sb.Append(m).Append(';');
        var s = sb.ToString();
        // Too generic to identify (e.g. an empty class): no key.
        return s.Contains("?") && strict ? null : s.Length < 40 ? null : s;
    }

    static string Sig(MethodDefinition m, Dictionary<TypeDefinition, TypeDefinition> map, bool forB = false)
    {
        Func<TypeReference, string> n = r => map == null ? Loose(r) : Name(r, map, forB);
        return (m.IsStatic ? "s " : "") + (GameMap.Scrambled(m.Name) ? "" : m.Name) + "(" + string.Join(",", m.Parameters.Select(p => n(p.ParameterType))) + ")" + n(m.ReturnType);
    }

    // A method with its exact signature (as named in its own module), for reports.
    static string Exact(MethodDefinition m)
    {
        return m.Name + "(" + string.Join(",", m.Parameters.Select(p => p.ParameterType.FullName)) + ")" + m.ReturnType.FullName;
    }

    // A type with every scrambled name (also inside generic arguments and arrays) as "*".
    static string Loose(TypeReference r)
    {
        var g = r as GenericInstanceType;
        if (g != null) return Loose(g.ElementType) + "<" + string.Join(",", g.GenericArguments.Select(Loose)) + ">";
        if (r is ArrayType) return Loose(((ArrayType)r).ElementType) + "[]";
        if (r is ByReferenceType) return Loose(((ByReferenceType)r).ElementType) + "&";
        if (r is GenericParameter) return "!" + ((GenericParameter)r).Position;
        return GameMap.Scrambled(r.Name) ? "*" : r.FullName;
    }

    // --- Members: by kept name, save name, then signature (unique), then position among same-signature members ---

    static FieldDefinition MatchField(FieldDefinition f, TypeDefinition nt, Dictionary<TypeDefinition, TypeDefinition> map)
    {
        var byName = nt.Fields.FirstOrDefault(x => x.Name == f.Name && !GameMap.Scrambled(Bare(f.Name)));
        if (byName != null) return byName;
        string save = FieldSaveName(f);
        if (save != null) { var s = nt.Fields.FirstOrDefault(x => FieldSaveName(x) == save); if (s != null) return s; }
        Func<FieldDefinition, bool, string> key = (x, b) => (x.IsStatic ? "s" : "") + Name(x.FieldType, map, b) + (x.HasConstant ? "=" + x.Constant : "");
        var same = f.DeclaringType.Fields.Where(x => key(x, false) == key(f, false)).ToList();
        var cands = nt.Fields.Where(x => key(x, true) == key(f, false)).ToList();
        if (cands.Count == same.Count) return cands[same.IndexOf(f)];     // the obfuscator keeps declaration order
        return null;
    }

    static string Bare(string n) { return n.StartsWith("m_") ? n.Substring(2) : n; }

    // The save-code string a field is written with (GetObjectData: ldstr "X+_Name", ldarg.0, ldfld f).
    static string FieldSaveName(FieldDefinition f)
    {
        foreach (var m in f.DeclaringType.Methods.Where(m => m.HasBody && m.Name == "GetObjectData"))
        {
            var code = m.Body.Instructions;
            for (int i = 0; i < code.Count; i++)
                if (code[i].OpCode == OpCodes.Ldstr)
                    for (int j = i + 1; j < Math.Min(code.Count, i + 6); j++)
                    {
                        if (code[j].OpCode == OpCodes.Ldstr) break;
                        var r = code[j].Operand as FieldReference;
                        if (r != null) { if (r.Name == f.Name) return (string)code[i].Operand; break; }
                    }
        }
        return null;
    }

    static MethodDefinition MatchMethod(MethodDefinition m, TypeDefinition nt, Dictionary<TypeDefinition, TypeDefinition> map)
    {
        if (!GameMap.Scrambled(m.Name) || m.IsConstructor)
        {
            var named = nt.Methods.Where(x => x.Name == m.Name && Sig(x, map, true) == Sig(m, map, false)).ToList();
            if (named.Count == 1) return named[0];
        }
        // Same signature (in matched types) and the same strings in the body; else position among same-signature methods.
        var same = m.DeclaringType.Methods.Where(x => Sig(x, map, false) == Sig(m, map, false)).ToList();
        var cands = nt.Methods.Where(x => Sig(x, map, true) == Sig(m, map, false)).ToList();
        if (cands.Count == 1 && same.Count == 1) return cands[0];
        var byBody = cands.Where(x => BodyKey(x) == BodyKey(m)).ToList();
        if (byBody.Count == 1) return byBody[0];
        if (cands.Count == same.Count) return cands[same.IndexOf(m)];
        return null;
    }

    static string BodyKey(MethodDefinition m)
    {
        if (!m.HasBody) return "-";
        // The opcodes, strings, and the names of called methods that aren't scrambled (framework calls such as
        // Delegate.Combine / Delegate.Remove tell an event's add and remove apart).
        var sb = new StringBuilder();
        foreach (var i in m.Body.Instructions)
        {
            sb.Append(i.OpCode.Code).Append(' ');
            var s = i.Operand as string; if (s != null) sb.Append('"').Append(s).Append('"');
            var c = i.Operand as MethodReference; if (c != null && !GameMap.Scrambled(c.Name)) sb.Append(c.Name);
        }
        return sb.ToString();
    }

    // --- Test helper: give every scrambled name a new random one (as a new game build would) ----------------------

    static int Scramble(string input, string output, int seed)
    {
        var module = ModuleDefinition.ReadModule(input);
        var rnd = new Random(seed);
        var used = new HashSet<string>();
        Func<string> fresh = () => { string n; do { n = "" + (char)('a' + rnd.Next(26)) + (char)('a' + rnd.Next(26)) + (char)('a' + rnd.Next(26)) + rnd.Next(10); } while (!used.Add(n)); return n; };
        // Every old name first (signatures mention other types, renamed later), then the renames.
        var renames = new List<Tuple<string, Action<string>, Func<string>>>();     // record prefix, set, read back
        foreach (var t0 in module.GetTypes().ToList())
        {
            var t = t0;
            string oldType = t.FullName;
            foreach (var f0 in t.Fields) { var f = f0; if (GameMap.Scrambled(Bare(f.Name))) renames.Add(Tuple.Create("field " + oldType + "." + f.Name, (Action<string>)(n => f.Name = n), (Func<string>)(() => f.Name))); }
            foreach (var m0 in t.Methods) { var m = m0; if (GameMap.Scrambled(m.Name) && !m.IsVirtual && !m.IsConstructor) renames.Add(Tuple.Create("method " + oldType + "." + Exact(m), (Action<string>)(n => m.Name = n), (Func<string>)(() => m.Name))); }
            if (GameMap.Scrambled(t.Name) && !t.Name.StartsWith("<")) renames.Add(Tuple.Create("type " + oldType, (Action<string>)(n => t.Name = n), (Func<string>)(() => t.FullName)));
        }
        foreach (var r in renames) r.Item2(fresh());
        var truth = renames.Select(r => r.Item1 + " " + r.Item3()).ToList();
        System.IO.File.WriteAllLines(output + ".truth", truth);
        // Shuffle declaration order a little too? No: the obfuscator keeps it, and position is one of the clues.
        module.Write(output);
        return 0;
    }
}
