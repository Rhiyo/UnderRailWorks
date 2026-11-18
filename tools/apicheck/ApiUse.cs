// Which stable API members something exercises: every public, non-preview member of UnderRailWorks.dll's API
// (namespace UnderRailWorks; overloads counted as one member, accessors as their property or event), against the Works
// members that a set of mod DLLs reference (calls, fields, property and event accessors, constructors, overrides).
// Tested = referenced by the test mods smoke.sh and drawing.sh run (WorksTest, the test mods, the examples they load);
// shipped = referenced by a shipped mod only (it runs, but nothing checks the result); neither = no code of ours uses
// it. Docs/Versioning.md: a stable member with no test can change what it does unnoticed.
//   mono ApiUse.exe UnderRailWorks.dll out.txt --tested a.dll b.dll ... --shipped c.dll d.dll ...
// Prints one summary line; out.txt lists the members by group.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;

static class ApiUse
{
    static bool Visible(TypeDefinition t) { for (; t != null; t = t.DeclaringType) if (!(t.IsPublic || t.IsNestedPublic)) return false; return true; }
    static bool Preview(ICustomAttributeProvider p) { return p != null && p.HasCustomAttributes && p.CustomAttributes.Any(a => a.AttributeType.Name == "PreviewAttribute"); }
    static bool PreviewType(TypeDefinition t) { for (; t != null; t = t.DeclaringType) if (Preview(t)) return true; return false; }
    static bool Api(TypeDefinition t) { var o = t; while (o.DeclaringType != null) o = o.DeclaringType; return o.Namespace == "UnderRailWorks"; }

    static string TypeKey(TypeReference t)
    {
        var g = t as GenericInstanceType; if (g != null) t = g.ElementType;
        var n = t.FullName.Replace("/", ".");
        int i = n.IndexOf('`'); return i > 0 ? n.Substring(0, i) : n;
    }
    static string MemberName(string name)
    {
        foreach (var p in new[] { "get_", "set_", "add_", "remove_" }) if (name.StartsWith(p)) return name.Substring(p.Length);
        return name == ".ctor" ? "new" : name;
    }

    static int Main(string[] args)
    {
        var works = AssemblyDefinition.ReadAssembly(args[0]);
        var worksName = works.Name.Name;
        // The stable members.
        var stable = new SortedSet<string>(StringComparer.Ordinal);
        foreach (var t in works.MainModule.GetTypes().Where(t => Api(t) && Visible(t) && !PreviewType(t) && !t.IsEnum))
        {
            string tk = TypeKey(t);
            Func<IMemberDefinition, bool> skip = d => Preview(d as ICustomAttributeProvider);
            foreach (var m in t.Methods.Where(m => (m.IsPublic || m.IsFamily) && !skip(m)))
            {
                if (m.Name == "Equals" || m.Name == "GetHashCode" || m.Name == "ToString") continue;     // object's, called through object
                if (m.IsConstructor && m.IsStatic) continue;
                if (m.IsGetter || m.IsSetter || m.IsAddOn || m.IsRemoveOn)
                {
                    var prop = t.Properties.FirstOrDefault(p => p.GetMethod == m || p.SetMethod == m);
                    var ev = t.Events.FirstOrDefault(e => e.AddMethod == m || e.RemoveMethod == m);
                    if ((prop != null && Preview(prop)) || (ev != null && Preview(ev))) continue;
                }
                if (m.IsConstructor && t.IsAbstract) continue;     // only derived from
                stable.Add(tk + "." + MemberName(m.Name));
            }
            foreach (var f in t.Fields.Where(f => (f.IsPublic || f.IsFamily) && !skip(f) && !f.HasConstant)) stable.Add(tk + "." + f.Name);     // a const is compiled into the mod: no reference to see
        }
        // What the DLLs use.
        var tested = new HashSet<string>(StringComparer.Ordinal); var shipped = new HashSet<string>(StringComparer.Ordinal);
        HashSet<string> into = null;
        var resolver = new DefaultAssemblyResolver();
        resolver.AddSearchDirectory(Path.GetDirectoryName(Path.GetFullPath(args[0])));
        for (int i = 2; i < args.Length; i++)
        {
            if (args[i] == "--tested") { into = tested; continue; }
            if (args[i] == "--shipped") { into = shipped; continue; }
            if (into == null || !File.Exists(args[i])) continue;
            ModuleDefinition module;
            try { module = ModuleDefinition.ReadModule(args[i], new ReaderParameters { AssemblyResolver = resolver }); }
            catch (Exception) { continue; }
            foreach (var r in module.GetMemberReferences())
            {
                var owner = r.DeclaringType; if (owner == null) continue;
                var root = owner; while (root.DeclaringType != null) root = root.DeclaringType;
                var g = root as GenericInstanceType; if (g != null) root = g.ElementType;
                if (root.Scope == null || root.Scope.Name != worksName) continue;
                into.Add(TypeKey(owner) + "." + MemberName(r.Name));
                // A member reached through a derived Works type (window.Width: UiWindow -> UiElement): its declaring type too.
                try { var d = r.Resolve(); if (d != null && d.DeclaringType != null) into.Add(TypeKey(d.DeclaringType) + "." + MemberName(d.Name)); } catch (Exception) { }
            }
            // Overrides of Works' virtual members (WorksMod.Load...).
            foreach (var t in module.GetTypes())
            {
                var b = t.BaseType;
                while (b != null)
                {
                    TypeDefinition d = null; try { d = b.Resolve(); } catch (Exception) { }
                    if (d == null) break;
                    if (d.Module.Assembly.Name.Name == worksName)
                        foreach (var m in t.Methods.Where(m => m.IsVirtual && !m.IsNewSlot)) if (d.Methods.Any(x => x.Name == m.Name && x.IsVirtual)) into.Add(TypeKey(d) + "." + MemberName(m.Name));
                    b = d.BaseType;
                }
            }
            module.Dispose();
        }
        var t1 = stable.Where(tested.Contains).ToList();
        var s1 = stable.Where(m => !tested.Contains(m) && shipped.Contains(m)).ToList();
        var n1 = stable.Where(m => !tested.Contains(m) && !shipped.Contains(m)).ToList();
        var lines = new List<string> { "# UnderRailWorks API coverage: " + stable.Count + " stable members (overloads as one)", "",
            "## Not used by any code of ours (" + n1.Count + ")" };
        lines.AddRange(n1); lines.Add(""); lines.Add("## Used by a shipped mod, no test (" + s1.Count + ")"); lines.AddRange(s1);
        lines.Add(""); lines.Add("## Exercised by the tests (" + t1.Count + ")"); lines.AddRange(t1);
        File.WriteAllLines(args[1], lines);
        Console.WriteLine("API coverage: " + t1.Count + " of " + stable.Count + " stable members exercised by the tests, " + s1.Count + " only by shipped mods, " + n1.Count + " by nothing (" + Path.GetFileName(args[1]) + ")");
        return 0;
    }
}
