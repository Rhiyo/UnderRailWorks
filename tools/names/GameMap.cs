// GameMap: recovers what it can of the game's original names from its own exe, so Works' readable layer is built on
// evidence and can be re-mapped after a game update by running this again.
// Clues that survive the obfuscation:
//   - save code: ISerializable members are stored as "ClassName+_Field" (or "_Field"), next to the field they read or
//     write: the class's original name and its fields' names;
//   - save tags: [eiz("ZTT")] (ZoneTransitionTrigger), the initials of the original name;
//   - names the obfuscator kept: namespaced types (TimelapseVertigo.*), public properties, enum members, events.
// Usage: mono GameMap.exe <underrail.exe> <out.tsv>   (one line per type: scrambled, original or ?, tag, base, members)
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using Mono.Cecil;
using Mono.Cecil.Cil;

static class GameMap
{
    static readonly Regex Prefixed = new Regex(@"^([A-Z][A-Za-z0-9]+)\+_?([A-Za-z0-9_]+)$"), Plain = new Regex(@"^_([A-Za-z][A-Za-z0-9_]*)$");

    public static bool Scrambled(string name) { return name.Length <= 4 && Regex.IsMatch(name, @"^[a-z][a-z0-9]{0,3}$"); }

    sealed class Info
    {
        public TypeDefinition Type;
        public readonly Dictionary<string, int> Votes = new Dictionary<string, int>();
        public readonly Dictionary<string, string> Fields = new Dictionary<string, string>();     // scrambled field -> original
        public string Tag, Original, Source;
    }

    static int Main(string[] args)
    {
        var module = ModuleDefinition.ReadModule(args[0]);
        var infos = new Dictionary<TypeDefinition, Info>();
        foreach (var t in module.GetTypes()) infos[t] = new Info { Type = t };

        foreach (var info in infos.Values)
        {
            var t = info.Type;
            var tag = t.CustomAttributes.FirstOrDefault(a => a.AttributeType.Name == "eiz" && a.ConstructorArguments.Count == 1);
            if (tag != null) info.Tag = tag.ConstructorArguments[0].Value as string;
            foreach (var m in t.Methods.Where(m => m.HasBody))
            {
                var code = m.Body.Instructions;
                for (int i = 0; i < code.Count; i++)
                {
                    var s = code[i].Operand as string;
                    if (code[i].OpCode != OpCodes.Ldstr || s == null) continue;
                    string field = null;
                    var pm = Prefixed.Match(s);
                    if (pm.Success) { Vote(info, pm.Groups[1].Value); field = pm.Groups[2].Value; }
                    else { var p = Plain.Match(s); if (p.Success) field = p.Groups[1].Value; }
                    if (field == null) continue;
                    // The field it goes with: the nearest ldfld/stfld of this type's own fields, after the string
                    // (GetObjectData: ldstr, ldarg.0, ldfld, AddValue; ctor: ldstr, ..., GetValue, cast, stfld).
                    for (int j = i + 1; j < Math.Min(code.Count, i + 12); j++)
                    {
                        if (code[j].OpCode == OpCodes.Ldstr) break;
                        var f = code[j].Operand as FieldReference;
                        if (f == null || (code[j].OpCode != OpCodes.Ldfld && code[j].OpCode != OpCodes.Stfld && code[j].OpCode != OpCodes.Ldflda)) continue;
                        var fd = f.Resolve();
                        if (fd != null && fd.DeclaringType == t && Scrambled(fd.Name.StartsWith("m_") ? fd.Name.Substring(2) : fd.Name) && !info.Fields.ContainsKey(fd.Name)) info.Fields[fd.Name] = field;
                        break;
                    }
                }
            }
            if (!Scrambled(t.Name)) { info.Original = t.FullName; info.Source = "kept"; }
            else if (info.Votes.Count > 0)
            {
                // The class's own prefix wins; prefixes of its base classes appear in its code too (base.GetObjectData).
                var best = info.Votes.OrderByDescending(v => v.Value).First();
                info.Original = best.Key; info.Source = "save";
            }
        }
        // A prefix voted for by a subclass that is really its base's name: keep the name on the type whose own
        // fields it covers. Where two types claim one name, the one with the most votes keeps it.
        foreach (var g in infos.Values.Where(i => i.Source == "save").GroupBy(i => i.Original).Where(g => g.Count() > 1))
        {
            var keep = g.OrderByDescending(i => i.Votes[i.Original]).First();
            foreach (var other in g.Where(i => i != keep))
            {
                var alt = other.Votes.Where(v => v.Key != keep.Original).OrderByDescending(v => v.Value).FirstOrDefault();
                if (alt.Key != null && !infos.Values.Any(i => i.Original == alt.Key)) other.Original = alt.Key;
                else { other.Original = null; other.Source = null; }
            }
        }

        using (var w = new StreamWriter(args[1]))
        {
            w.WriteLine("# scrambled\toriginal\tsource\ttag\tbase\tfields (scrambled=original)\tkept members");
            foreach (var info in infos.Values.OrderBy(i => i.Type.FullName, StringComparer.Ordinal))
            {
                var t = info.Type;
                var kept = t.Properties.Select(p => p.Name).Concat(t.Events.Select(e => e.Name))
                    .Concat(t.Fields.Where(f => !Scrambled(f.Name.StartsWith("m_") ? f.Name.Substring(2) : f.Name) && !f.Name.Contains("<")).Select(f => f.Name))
                    .Concat(t.Methods.Where(m => !m.IsConstructor && !m.IsSpecialName && !Scrambled(m.Name) && !m.Name.Contains(".")).Select(m => m.Name + "()"))
                    .Distinct().ToArray();
                w.WriteLine(string.Join("\t", new[] {
                    t.FullName, info.Original ?? "?", info.Source ?? "", info.Tag ?? "", t.BaseType == null ? "" : t.BaseType.FullName,
                    string.Join(" ", info.Fields.Select(f => f.Key + "=" + f.Value)), string.Join(" ", kept) }));
            }
        }
        var all = infos.Values.Where(i => !i.Type.Name.StartsWith("<")).ToList();
        int scrambled = all.Count(i => Scrambled(i.Type.Name));
        Console.WriteLine("types " + all.Count + ", scrambled " + scrambled + ", named from save code " + all.Count(i => i.Source == "save")
            + ", with a save tag " + all.Count(i => i.Tag != null) + ", tag but no name " + all.Count(i => i.Tag != null && i.Source != "save" && Scrambled(i.Type.Name))
            + ", fields named " + all.Sum(i => i.Fields.Count));
        return 0;
    }

    static void Vote(Info info, string name) { int n; info.Votes.TryGetValue(name, out n); info.Votes[name] = n + 1; }
}
