// The public API of UnderRailWorks.dll (namespace UnderRailWorks: what mods may use), one line per type and member,
// sorted, with its version (WorksApi.Major/Minor) on the first line. Also: game types showing through it (a game
// update renames those, which would break every mod using them: there must be none).
//   mono ApiSurface.exe UnderRailWorks.dll api.txt              writes the API
//   mono ApiSurface.exe --check UnderRailWorks.dll baseline.txt [--release]
//       compares with the last release's API. Exit 0: the same, or the differences are covered by the version (a
//       removal or change needs a new major, or a new minor while 0.x; an addition a new minor). Exit 1: additions
//       without a new minor (a warning while building; --release makes it an error). Exit 2: a break the version
//       doesn't cover, or a game type in the API. Prints what changed.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

static class ApiSurface
{
    static readonly HashSet<string> Leaks = new HashSet<string>();
    static string Game;

    static bool Visible(TypeDefinition t)
    {
        for (; t != null; t = t.DeclaringType) if (!(t.IsPublic || t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamilyOrAssembly)) return false;
        return true;
    }
    static bool Visible(MethodDefinition m) { return m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly; }
    static bool Visible(FieldDefinition f) { return f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly; }

    static string N(TypeReference t, string where)
    {
        if (t == null) return "?";
        var g = t as GenericInstanceType; if (g != null) return N(g.ElementType, where).Split('`')[0] + "<" + string.Join(", ", g.GenericArguments.Select(a => N(a, where))) + ">";
        var r = t as ByReferenceType; if (r != null) return "ref " + N(r.ElementType, where);
        var arr = t as ArrayType; if (arr != null) return N(arr.ElementType, where) + "[]";
        if (t is GenericParameter) return t.Name;
        var scope = t.Scope == null ? "" : t.Scope.Name; if (scope.EndsWith(".dll") || scope.EndsWith(".exe")) scope = scope.Substring(0, scope.Length - 4);
        if (scope == Game || scope == Game + ".exe" || scope.StartsWith("underrail")) Leaks.Add(t.FullName + "\t" + where);
        // Only .NET's, FNA's (the XNA API) and Works' own types: anything else (Harmony...) would freeze a third
        // party's version into the API.
        else if (!(scope == "" || scope == "mscorlib" || scope == "netstandard" || scope.StartsWith("System") || scope == "FNA" || scope == "UnderRailWorks" || scope.StartsWith("Microsoft.Xna"))) Leaks.Add(t.FullName + " (from " + scope + ")\t" + where);
        return t.FullName.Replace("/", ".");
    }

    static bool Preview(ICustomAttributeProvider p) { return p != null && p.HasCustomAttributes && p.CustomAttributes.Any(a => a.AttributeType.Name == "PreviewAttribute"); }
    static bool PreviewType(TypeDefinition t) { for (; t != null; t = t.DeclaringType) if (Preview(t)) return true; return false; }
    static readonly List<string> Previews = new List<string>();

    static bool Api(TypeDefinition t) { var ns = t; while (ns.DeclaringType != null) ns = ns.DeclaringType; return ns.Namespace == "UnderRailWorks"; }

    static Version VersionOf(AssemblyDefinition asm)
    {
        var t = asm.MainModule.GetType("UnderRailWorks.WorksApi");
        if (t == null) return null;
        var major = t.Fields.FirstOrDefault(f => f.Name == "Major"); var minor = t.Fields.FirstOrDefault(f => f.Name == "Minor");
        return major == null || minor == null ? null : new Version((int)major.Constant, (int)minor.Constant);
    }

    static int Main(string[] args)
    {
        if (args[0] == "--check") return Check(args[1], args[2], args.Contains("--release"));
        var lines = Surface(args[0]);
        File.WriteAllLines(args[1], lines);
        if (args.Length > 2) File.WriteAllLines(args[2], Leaks.OrderBy(x => x));
        Console.WriteLine(lines.Count + " API lines, " + Previews.Distinct().Count() + " preview parts left out, " + Leaks.Count + " game-type uses in the API");
        return 0;
    }

    static int Check(string dll, string baselinePath, bool release)
    {
        var now = Surface(dll);
        if (Leaks.Count > 0) { Console.WriteLine("API: game types, or preview types in stable members, in the public API (mark those members [Preview], or don't use the type):"); foreach (var l in Leaks.OrderBy(x => x)) Console.WriteLine("  " + l); return 2; }
        if (!File.Exists(baselinePath)) { Console.WriteLine("API: no baseline (" + baselinePath + "): writing it"); File.WriteAllLines(baselinePath, now); return 0; }
        var was = File.ReadAllLines(baselinePath).ToList();
        Version vNow = new Version(now[0].Split(' ').Last()), vWas = new Version(was[0].Split(' ').Last());
        var removed = was.Skip(1).Except(now.Skip(1)).ToList(); var added = now.Skip(1).Except(was.Skip(1)).ToList();
        if (removed.Count == 0 && added.Count == 0) { Console.WriteLine("API " + vNow.ToString(2) + ": unchanged since the last release"); return 0; }
        bool newMajor = vNow.Major > vWas.Major || (vWas.Major == 0 && vNow.Major == 0 && vNow.Minor > vWas.Minor);
        bool newMinor = vNow > vWas;
        Console.WriteLine("API " + vWas.ToString(2) + " (last release) -> " + vNow.ToString(2) + ": " + added.Count + " added, " + removed.Count + " removed or changed");
        foreach (var l in removed) Console.WriteLine("  - " + l);
        foreach (var l in added.Take(40)) Console.WriteLine("  + " + l);
        if (added.Count > 40) Console.WriteLine("  + ... " + (added.Count - 40) + " more");
        if (removed.Count > 0 && !newMajor)
        {
            Console.WriteLine("API: a break (removed or changed, above) without a new " + (vWas.Major == 0 ? "minor (0.x)" : "major") + " version: mods built for " + vWas.ToString(2)
                + " would fail. Keep the old member (mark it [Obsolete]) or raise WorksApi." + (vWas.Major == 0 ? "Minor" : "Major") + " (WorksMod.cs).");
            return 2;
        }
        if (!newMinor) { Console.WriteLine("API: additions: raise WorksApi.Minor (WorksMod.cs) before the release" + (release ? "" : " (a warning until then)")); return release ? 2 : 1; }
        return 0;
    }

    static List<string> Surface(string path)
    {
        var asm = AssemblyDefinition.ReadAssembly(path);
        Game = asm.MainModule.AssemblyReferences.Select(r => r.Name).FirstOrDefault(n => n.StartsWith("underrail", StringComparison.OrdinalIgnoreCase)) ?? "underrail";
        var lines = new List<string>();
        foreach (var t in asm.MainModule.GetTypes().Where(t => Visible(t) && Api(t)))
        {
            if (PreviewType(t)) { Previews.Add(t.FullName.Replace("/", ".")); continue; }
            var tn = t.FullName.Replace("/", ".");
            string kind = t.IsEnum ? "enum" : t.IsInterface ? "interface" : t.IsValueType ? "struct" : t.IsAbstract && t.IsSealed ? "static class" : t.IsAbstract ? "abstract class" : t.IsSealed ? "sealed class" : "class";
            lines.Add(tn + " :: " + kind + (t.BaseType != null && t.BaseType.FullName != "System.Object" && !t.IsEnum && !t.IsValueType ? " : " + N(t.BaseType, tn) : "")
                + (t.Interfaces.Count > 0 ? " (" + string.Join(", ", t.Interfaces.Select(i => N(i.InterfaceType, tn)).OrderBy(x => x)) + ")" : ""));
            if (t.IsEnum) { foreach (var f in t.Fields.Where(f => f.IsStatic && f.IsPublic)) lines.Add(tn + " :: " + f.Name + " = " + f.Constant); continue; }
            foreach (var f in t.Fields.Where(f => Visible(f) && !Preview(f))) lines.Add(tn + " :: " + (f.IsStatic ? "static " : "") + (f.IsInitOnly ? "readonly " : f.HasConstant ? "const " : "") + "field " + N(f.FieldType, tn + "." + f.Name) + " " + f.Name + (f.HasConstant ? " = " + f.Constant : ""));
            foreach (var p in t.Properties)
            {
                var g = p.GetMethod != null && Visible(p.GetMethod); var s = p.SetMethod != null && Visible(p.SetMethod);
                if (!g && !s) continue;
                if (Preview(p)) { Previews.Add(tn + "." + p.Name); continue; }
                var st = (p.GetMethod ?? p.SetMethod).IsStatic ? "static " : "";
                lines.Add(tn + " :: " + st + "property " + N(p.PropertyType, tn + "." + p.Name) + " " + p.Name + (p.HasParameters ? "[" + string.Join(", ", p.Parameters.Select(x => N(x.ParameterType, tn + "." + p.Name))) + "]" : "") + " {" + (g ? " get;" : "") + (s ? " set;" : "") + " }");
            }
            foreach (var e in t.Events) { if (e.AddMethod == null || !Visible(e.AddMethod)) continue; if (Preview(e)) { Previews.Add(tn + "." + e.Name); continue; } lines.Add(tn + " :: " + (e.AddMethod.IsStatic ? "static " : "") + "event " + N(e.EventType, tn + "." + e.Name) + " " + e.Name); }
            foreach (var m in t.Methods.Where(Visible))
            {
                if (m.IsGetter || m.IsSetter || m.IsAddOn || m.IsRemoveOn) continue;
                if (Preview(m)) { Previews.Add(tn + "." + m.Name); continue; }
                var where = tn + "." + m.Name;
                var ps = string.Join(", ", m.Parameters.Select(x => (x.IsOut ? "out " : "") + N(x.ParameterType, where) + (x.HasDefault ? " = " + (x.Constant ?? "null") : "")));
                var gen = m.HasGenericParameters ? "<" + string.Join(", ", m.GenericParameters.Select(x => x.Name)) + ">" : "";
                lines.Add(tn + " :: " + (m.IsStatic ? "static " : "") + (m.IsAbstract ? "abstract " : m.IsVirtual && !m.IsFinal && !t.IsSealed ? "virtual " : "") + (m.IsConstructor ? "ctor" : "method " + N(m.ReturnType, where) + " " + m.Name) + gen + "(" + ps + ")");
            }
        }
        // A stable member can't use a preview type (it would freeze it through the back door).
        var previewTypes = new HashSet<string>(Previews.Where(x => asm.MainModule.GetType(x) != null || x.Count(ch => ch == '.') == 1));
        foreach (var l in lines)
            foreach (var pt in previewTypes)
                if (System.Text.RegularExpressions.Regex.IsMatch(l.Substring(l.IndexOf(" :: ") + 4), @"\b" + System.Text.RegularExpressions.Regex.Escape(pt) + @"\b"))
                    Leaks.Add("preview type " + pt + "\t" + l);
        lines.Sort(StringComparer.Ordinal);
        var v = VersionOf(asm);
        lines.Insert(0, "# UnderRailWorks API " + (v == null ? "0.0" : v.ToString(2)));
        return lines;
    }
}
