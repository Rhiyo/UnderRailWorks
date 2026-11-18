// Before a mod's code loads: does everything it uses from UnderRailWorks exist in this version? Every type and member
// it references in the Works assembly is resolved against the installed UnderRailWorks.dll (Mono.Cecil, metadata only:
// nothing runs). Something missing means the mod was built for a newer Works (or one with a break): it isn't loaded,
// with an error naming what's missing, instead of failing later in the middle of a game (MissingMethodException).
// The mod's own classes are checked against the Works classes and interfaces they build on: an override of a Works
// method that's gone (it would never be called), and an abstract or interface member of Works' it doesn't implement
// (it would fail to load), are missing too. This, not the "works" version in mod.json, decides whether a mod built for
// another Works loads: if everything it uses is here, it works.
// Also noted (a warning, the mod still loads): uses of retired API ([Obsolete]: removed at the next major version), uses of Works' internals (UnderRailWorks.Internal: not API, may change
// in any version), of the game's own code, and of Harmony (patches on the game's scrambled names: they may break with
// any game update).
// docs/Versioning.md.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Mono.Cecil;
using UnderRailWorks;

namespace UnderRailWorks.Internal
{
static class ModCheck
{
    sealed class Resolver : DefaultAssemblyResolver
    {
        public Resolver(string modFolder) { AddSearchDirectory(AppDomain.CurrentDomain.BaseDirectory); AddSearchDirectory(modFolder); }
    }

    internal static double Time; internal static int Count;
    internal sealed class Result { public string Error; public List<string> Warnings = new List<string>(); }

    static readonly string WorksName = typeof(ModCheck).Assembly.GetName().Name;
    static readonly string GameName = typeof(cnk).Assembly.GetName().Name;

    internal static Result Scan(ModInfo mod, string dll)
    {
        var r = new Result();
        var resolver = new Resolver(mod.Folder);
        try
        {
            using (var module = ModuleDefinition.ReadModule(dll, new ReaderParameters { AssemblyResolver = resolver, ReadingMode = ReadingMode.Deferred }))
            {
                var missing = new SortedSet<string>(StringComparer.Ordinal);
                var internals = new SortedSet<string>(StringComparer.Ordinal);
                var previews = new SortedSet<string>(StringComparer.Ordinal);
                var retired = new SortedSet<string>(StringComparer.Ordinal);
                bool game = module.AssemblyReferences.Any(a => a.Name.Equals(GameName, StringComparison.OrdinalIgnoreCase));
                foreach (var t in module.GetTypeReferences())
                {
                    if (!FromWorks(t)) continue;
                    if (Internal(t)) internals.Add(t.FullName);
                    TypeDefinition def = null;
                    try { def = t.Resolve(); } catch (Exception) { }
                    if (def == null || !Reachable(def)) missing.Add(t.FullName.Replace("/", "."));
                    else if (PreviewType(def)) previews.Add(def.FullName.Replace("/", "."));
                    else if (IsObsolete(def)) retired.Add(def.FullName.Replace("/", "."));
                }
                foreach (var m in module.GetMemberReferences())
                {
                    var owner = m.DeclaringType;
                    if (owner == null || !FromWorks(owner)) continue;
                    if (Internal(owner)) internals.Add(owner.FullName);
                    IMemberDefinition def = null;
                    try { def = m.Resolve(); } catch (Exception) { }
                    if (def == null || !Reachable(def)) missing.Add(Describe(m));
                    else if (IsPreview(def as ICustomAttributeProvider) || AccessorPreview(def as MethodDefinition) || PreviewType(def.DeclaringType)) previews.Add(Plain(def));
                    else if (IsObsolete(def as ICustomAttributeProvider) || AccessorObsolete(def as MethodDefinition)) retired.Add(Plain(def));
                }
                foreach (var t in module.GetTypes()) Inherited(t, missing);
                if (missing.Count > 0)
                    r.Error = "uses parts of UnderRailWorks this version doesn't have (API " + WorksApi.Version.ToString(2) + "; the mod needs an update, or a newer Works): " + string.Join(", ", missing.Take(5).ToArray()) + (missing.Count > 5 ? " and " + (missing.Count - 5) + " more" : "");
                if (retired.Count > 0) r.Warnings.Add("uses retired API (still works; removed at the next major Works version): " + string.Join(", ", retired.Take(3).ToArray()) + (retired.Count > 3 ? "..." : ""));
                if (internals.Count > 0) r.Warnings.Add("uses UnderRailWorks internals (may break with any Works update): " + string.Join(", ", internals.Take(3).ToArray()) + (internals.Count > 3 ? "..." : ""));
                if (previews.Count > 0) r.Warnings.Add("uses preview API (may change in any Works update): " + string.Join(", ", previews.Take(3).ToArray()) + (previews.Count > 3 ? "..." : ""));
                if (game) r.Warnings.Add("uses the game's own code directly (may break with a game update)");
                else if (module.AssemblyReferences.Any(a => a.Name == "0Harmony")) r.Warnings.Add("patches code with Harmony (may break with a game update)");
            }
        }
        catch (BadImageFormatException) { }     // not a .NET assembly (a native library the mod ships): nothing to check
        finally { resolver.Dispose(); }
        return r;
    }

    static string Plain(IMemberDefinition def) { return def.DeclaringType.FullName.Replace("/", ".") + "." + def.Name.Replace("get_", "").Replace("set_", "").Replace("add_", "").Replace("remove_", ""); }
    static bool IsObsolete(ICustomAttributeProvider p) { return p != null && p.HasCustomAttributes && p.CustomAttributes.Any(a => a.AttributeType.FullName == "System.ObsoleteAttribute"); }
    static bool AccessorObsolete(MethodDefinition m)
    {
        if (m == null || !(m.IsGetter || m.IsSetter || m.IsAddOn || m.IsRemoveOn)) return false;
        var t = m.DeclaringType;
        return t.Properties.Any(p => (p.GetMethod == m || p.SetMethod == m) && IsObsolete(p)) || t.Events.Any(e => (e.AddMethod == m || e.RemoveMethod == m) && IsObsolete(e));
    }

    static readonly HashSet<string> ObjectVirtuals = new HashSet<string> { "ToString", "Equals", "GetHashCode", "Finalize" };

    // A mod class built on Works' classes and interfaces: each of its overrides must still override something of Works'
    // (else it's silently never called), and every abstract or interface member of Works' must be implemented.
    static void Inherited(TypeDefinition t, SortedSet<string> missing)
    {
        if (t.IsInterface) return;
        var bases = new List<TypeDefinition>();
        TypeReference b = t.BaseType;
        bool works = false;
        bool complete = true;     // every base resolved (else an override of something we couldn't see isn't judged)
        while (b != null)
        {
            TypeDefinition d = null;
            try { d = b.Resolve(); } catch (Exception) { }
            if (d == null) { complete = false; break; }
            if (FromWorks(b)) works = true;
            bases.Add(d);
            b = d.BaseType;
        }
        var faces = new List<TypeDefinition>();
        foreach (var i in t.Interfaces.Select(x => x.InterfaceType).Where(FromWorks))
        {
            TypeDefinition d = null;
            try { d = i.Resolve(); } catch (Exception) { }
            if (d != null) faces.Add(d);
        }
        if (!works && faces.Count == 0) return;
        Func<MethodDefinition, MethodDefinition, bool> same = (a, c) => a.Name == c.Name && a.Parameters.Count == c.Parameters.Count
            && a.Parameters.Select(p => p.ParameterType.FullName).SequenceEqual(c.Parameters.Select(p => p.ParameterType.FullName));
        var mine = t.Methods.ToList();
        // Overrides: virtual, not a new slot, not an explicit interface implementation.
        if (works)
            foreach (var m in mine.Where(m => m.IsVirtual && !m.IsNewSlot && !m.HasOverrides && (complete || !ObjectVirtuals.Contains(m.Name))))
                if (!bases.Any(d => d.Methods.Any(x => x.IsVirtual && same(x, m))))
                    missing.Add(t.FullName.Replace("/", ".") + "." + m.Name + " overrides " + bases[0].FullName.Replace("/", ".") + "." + m.Name + ", which is gone");
        // Abstract members of Works' bases that nothing in the chain below them implements.
        if (works)
            for (int k = 0; k < bases.Count; k++)
                foreach (var a in bases[k].Methods.Where(x => x.IsAbstract))
                {
                    bool done = mine.Any(m => m.IsVirtual && !m.IsAbstract && same(m, a)) || bases.Take(k).Any(d => d.Methods.Any(m => m.IsVirtual && !m.IsAbstract && same(m, a)));
                    if (!done) missing.Add(bases[k].FullName.Replace("/", ".") + "." + a.Name + " (abstract: " + t.Name + " must implement it)");
                }
        // Works interfaces: every member implemented (by name and parameters, or explicitly).
        foreach (var f in faces)
            foreach (var a in f.Methods.Where(x => x.IsAbstract))
            {
                bool done = mine.Any(m => same(m, a) || m.Overrides.Any(o => o.Name == a.Name && o.DeclaringType.FullName == f.FullName))
                    || bases.Any(d => d.Methods.Any(m => same(m, a)));
                if (!done) missing.Add(f.FullName.Replace("/", ".") + "." + a.Name + " (" + t.Name + " must implement it)");
            }
    }

    static bool IsPreview(ICustomAttributeProvider p) { return p != null && p.HasCustomAttributes && p.CustomAttributes.Any(a => a.AttributeType.Name == "PreviewAttribute"); }
    static bool PreviewType(TypeDefinition t) { for (; t != null; t = t.DeclaringType) if (IsPreview(t)) return true; return false; }
    // A property's or event's accessor: the attribute sits on the property or event.
    static bool AccessorPreview(MethodDefinition m)
    {
        if (m == null || !(m.IsGetter || m.IsSetter || m.IsAddOn || m.IsRemoveOn)) return false;
        var t = m.DeclaringType;
        return t.Properties.Any(p => (p.GetMethod == m || p.SetMethod == m) && IsPreview(p)) || t.Events.Any(e => (e.AddMethod == m || e.RemoveMethod == m) && IsPreview(e));
    }

    // Existing isn't enough: a member that was public and became internal fails at the call (MethodAccessException).
    static bool Reachable(TypeDefinition t) { for (; t != null; t = t.DeclaringType) if (!(t.IsPublic || t.IsNestedPublic || t.IsNestedFamily || t.IsNestedFamilyOrAssembly)) return false; return true; }
    static bool Reachable(IMemberDefinition d)
    {
        var m = d as MethodDefinition; if (m != null) return (m.IsPublic || m.IsFamily || m.IsFamilyOrAssembly) && Reachable(m.DeclaringType);
        var f = d as FieldDefinition; if (f != null) return (f.IsPublic || f.IsFamily || f.IsFamilyOrAssembly) && Reachable(f.DeclaringType);
        return Reachable(d.DeclaringType);
    }

    static bool FromWorks(TypeReference t)
    {
        while (t.DeclaringType != null) t = t.DeclaringType;
        var scope = t.Scope as AssemblyNameReference;
        return scope != null && scope.Name == WorksName;
    }
    static bool Internal(TypeReference t)
    {
        while (t.DeclaringType != null) t = t.DeclaringType;
        return t.Namespace.StartsWith("UnderRailWorks.Internal") || t.Namespace.Length == 0;
    }
    static string Describe(MemberReference m)
    {
        var owner = m.DeclaringType.FullName.Replace("/", ".");
        var method = m as MethodReference;
        if (method == null) return owner + "." + m.Name;
        return owner + "." + (m.Name == ".ctor" ? "new" : m.Name) + "(" + string.Join(", ", method.Parameters.Select(p => Short(p.ParameterType)).ToArray()) + ")";
    }
    static string Short(TypeReference t) { var n = t.Name; int i = n.IndexOf('`'); return i > 0 ? n.Substring(0, i) : n; }
}
}
