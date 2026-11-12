// Before a mod's code loads: does everything it uses from UnderRailWorks exist in this version? Every type and member
// it references in the Works assembly is resolved against the installed UnderRailWorks.dll (Mono.Cecil, metadata only:
// nothing runs). Something missing means the mod was built for a newer Works (or one with a break): it isn't loaded,
// with an error naming what's missing, instead of failing later in the middle of a game (MissingMethodException).
// Also noted (a warning, the mod still loads): uses of Works' internals (UnderRailWorks.Internal: not API, may change
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
                bool game = module.AssemblyReferences.Any(a => a.Name.Equals(GameName, StringComparison.OrdinalIgnoreCase));
                foreach (var t in module.GetTypeReferences())
                {
                    if (!FromWorks(t)) continue;
                    if (Internal(t)) internals.Add(t.FullName);
                    TypeDefinition def = null;
                    try { def = t.Resolve(); } catch (Exception) { }
                    if (def == null) missing.Add(t.FullName.Replace("/", "."));
                    else if (PreviewType(def)) previews.Add(def.FullName.Replace("/", "."));
                }
                foreach (var m in module.GetMemberReferences())
                {
                    var owner = m.DeclaringType;
                    if (owner == null || !FromWorks(owner)) continue;
                    if (Internal(owner)) internals.Add(owner.FullName);
                    IMemberDefinition def = null;
                    try { def = m.Resolve(); } catch (Exception) { }
                    if (def == null) missing.Add(Describe(m));
                    else if (IsPreview(def as ICustomAttributeProvider) || AccessorPreview(def as MethodDefinition) || PreviewType(def.DeclaringType)) previews.Add(def.DeclaringType.FullName.Replace("/", ".") + "." + def.Name.Replace("get_", "").Replace("set_", "").Replace("add_", "").Replace("remove_", ""));
                }
                if (missing.Count > 0)
                    r.Error = "needs a newer UnderRailWorks (this is API " + WorksApi.Version.ToString(2) + "): not here: " + string.Join(", ", missing.Take(5).ToArray()) + (missing.Count > 5 ? " and " + (missing.Count - 5) + " more" : "");
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

    static bool IsPreview(ICustomAttributeProvider p) { return p != null && p.HasCustomAttributes && p.CustomAttributes.Any(a => a.AttributeType.Name == "PreviewAttribute"); }
    static bool PreviewType(TypeDefinition t) { for (; t != null; t = t.DeclaringType) if (IsPreview(t)) return true; return false; }
    // A property's or event's accessor: the attribute sits on the property or event.
    static bool AccessorPreview(MethodDefinition m)
    {
        if (m == null || !(m.IsGetter || m.IsSetter || m.IsAddOn || m.IsRemoveOn)) return false;
        var t = m.DeclaringType;
        return t.Properties.Any(p => (p.GetMethod == m || p.SetMethod == m) && IsPreview(p)) || t.Events.Any(e => (e.AddMethod == m || e.RemoveMethod == m) && IsPreview(e));
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
