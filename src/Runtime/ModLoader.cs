// UnderRailWorks: loads mods from Mods\<folder>\ in the game folder (64-bit mode only).
// Each mod may have a mod.json manifest (readable JSON; everything optional):
//   { "id": "UnderRailFixes", "name": "UnderRailFixes", "version": "2.0.0", "author": "...", "description": "...",
//     "works": "1.0", "requires": ["OtherMod@1.2"], "optional": ["SomeMod"], "sortAfter": [], "sortBefore": [] }
// works: the Works API version the mod was built for (WorksApi.Version: same major, at least that minor).
// As tModLoader's build.txt: requires = modReferences (must be present, on and at least that version, and is loaded
// first), optional = weakReferences (loaded first when present), sortAfter / sortBefore = load order only.
// The id defaults to the folder name, the version to the DLL's.
// A mod's code: one public class deriving from WorksMod (WorksMod.cs: Load, then PostLoad once every mod has
// loaded), or, minimal, any public class with a static OnLoad(Harmony) or OnLoad(Harmony, Action<string> log).
// Mods\mods.ini switches mods on and off (<id>=0; the Mods tab writes it; applies at the next start).
// A mod whose requirements aren't met, or that fails, is skipped with the reason shown in the Mods tab.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

public sealed class ModDependency
{
    public string Id;
    public Version Min;     // null: any version
    public override string ToString() { return Min == null ? Id : Id + " " + Min + "+"; }
    internal static ModDependency Parse(string text)
    {
        int at = text.IndexOf('@');
        var d = new ModDependency { Id = (at < 0 ? text : text.Substring(0, at)).Trim() };
        if (at >= 0) d.Min = ModInfo.ParseVersion(text.Substring(at + 1));
        return d;
    }
}

public sealed class ModInfo
{
    public string Id, Name, Folder, Author, Description, Error;
    public Version Version, Works;
    public WorksMod Instance;
    public bool Enabled, Loaded;
    public List<ModDependency> Requires = new List<ModDependency>(), Optional = new List<ModDependency>();
    public List<string> SortAfter = new List<string>(), SortBefore = new List<string>();

    internal static Version ParseVersion(string s)
    {
        Version v;
        s = (s ?? "").Trim();
        if (s.Length > 0 && s.IndexOf('.') < 0) s += ".0";
        return Version.TryParse(s, out v) ? v : null;
    }
    public string VersionText { get { return Version == null ? "" : Version.Revision > 0 ? Version.ToString() : Version.Build >= 0 ? Version.ToString(3) : Version.ToString(2); } }
}

public static class ModLoader
{
    public static readonly string ModsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");
    static string ListPath { get { return Path.Combine(ModsFolder, "mods.ini"); } }
    // Every mod folder, in load order (mods that can't load at the end, by name).
    public static readonly List<ModInfo> Mods = new List<ModInfo>();
    static readonly List<string> Folders = new List<string>();

    public static ModInfo Find(string id) { return Mods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); }
    public static bool IsLoaded(string id) { var m = Find(id); return m != null && m.Loaded; }

    public static void Init()
    {
        if (!Directory.Exists(ModsFolder)) return;
        var off = Disabled();
        var all = Directory.GetDirectories(ModsFolder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Select(Read).ToList();
        foreach (var m in all) m.Enabled = !off.Contains(m.Id);
        if (all.Count == 0) return;
        if (IntPtr.Size != 8)
        {
            foreach (var m in all) m.Error = "needs the 64-bit mode";
            Mods.AddRange(all);
            Log.Write("mods: not loaded, they need the 64-bit mode");
            return;
        }
        CheckRequirements(all);
        var order = Order(all.Where(m => m.Enabled && m.Error == null).ToList());
        Mods.AddRange(order);
        Mods.AddRange(all.Except(order).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase));
        Log.Write("mods: load order: " + string.Join(", ", order.Select(m => m.Id).ToArray()));
        foreach (var m in all.Where(m => m.Error != null)) Log.Write("mods: " + m.Id + " not loaded: " + m.Error);
        foreach (var m in all.Where(m => !m.Enabled)) Log.Write("mods: " + m.Id + " is switched off");

        try { GameNames.Check(); } catch (Exception ex) { Log.Always("UnderRailWorks: game names check failed: " + ex); }
        string harmonyPath = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "0Harmony.dll");
        Assembly harmony;
        try { harmony = Assembly.LoadFrom(harmonyPath); }
        catch (Exception ex) { Log.Write("mods: can't load Harmony: " + ex.Message); foreach (var m in order) m.Error = "Harmony missing"; return; }
        Type harmonyType = harmony.GetType("HarmonyLib.Harmony", true);
        Log.Write("mods: Harmony " + harmony.GetName().Version);

        // A mod's own dependencies sit in its folder (or a required mod's).
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string name = new AssemblyName(e.Name).Name;
            if (name == "0Harmony") return harmony;
            foreach (string folder in Folders)
            {
                string path = Path.Combine(folder, name + ".dll");
                if (File.Exists(path)) return Assembly.LoadFrom(path);
            }
            return null;
        };
        Folders.AddRange(order.Select(m => m.Folder));
        foreach (var mod in order)
        {
            // A required mod that failed while loading takes its dependents with it.
            var failed = mod.Requires.Select(d => Find(d.Id)).FirstOrDefault(d => d != null && !d.Loaded);
            if (failed != null) { mod.Error = "needs " + failed.Name + " (it failed to load)"; Log.Write("mods: " + mod.Id + " not loaded: " + mod.Error); continue; }
            Load(mod, harmonyType);
        }
        // PostLoad: after every mod's Load, in the same order.
        foreach (var mod in order.Where(m => m.Loaded && m.Instance != null))
        {
            try { mod.Instance.PostLoad(); }
            catch (Exception ex) { mod.Error = "PostLoad failed: " + ex.Message; Log.Write("mods: " + mod.Id + " PostLoad failed: " + ex); }
        }
    }

    static ModInfo Read(string folder)
    {
        var m = new ModInfo { Folder = folder, Id = Path.GetFileName(folder), Name = Path.GetFileName(folder) };
        string manifest = Path.Combine(folder, "mod.json");
        if (!File.Exists(manifest)) return m;
        try
        {
            var o = Json.Parse(File.ReadAllText(manifest)) as Dictionary<string, object>;
            if (o == null) throw new FormatException("not a JSON object");
            Func<string, string> str = k => { object v; return o.TryGetValue(k, out v) && v != null ? v.ToString() : null; };
            Func<string, List<string>> list = k => { object v; return o.TryGetValue(k, out v) && v is List<object> l ? l.Where(x => x != null).Select(x => x.ToString()).ToList() : new List<string>(); };
            m.Id = str("id") ?? m.Id;
            m.Name = str("name") ?? m.Id;
            m.Author = str("author");
            m.Description = str("description");
            m.Version = ModInfo.ParseVersion(str("version"));
            m.Works = ModInfo.ParseVersion(str("works"));
            m.Requires = list("requires").Select(ModDependency.Parse).ToList();
            m.Optional = list("optional").Select(ModDependency.Parse).ToList();
            m.SortAfter = list("sortAfter");
            m.SortBefore = list("sortBefore");
        }
        catch (Exception ex) { m.Error = "mod.json: " + ex.Message; }
        return m;
    }

    // Requirements: each required mod installed, switched on, new enough and itself loadable; repeated until stable,
    // so a mod whose requirement is skipped is skipped too.
    static void CheckRequirements(List<ModInfo> all)
    {
        bool changed = true;
        while (changed)
        {
            changed = false;
            foreach (var m in all.Where(m => m.Enabled && m.Error == null && m.Works != null))
                if (m.Works.Major != WorksApi.Version.Major || m.Works > WorksApi.Version)
                {
                    m.Error = "built for UnderRailWorks API " + m.Works.ToString(2) + " (this is " + WorksApi.Version.ToString(2) + ")";
                    changed = true;
                }
            foreach (var m in all.Where(m => m.Enabled && m.Error == null))
                foreach (var d in m.Requires)
                {
                    var dep = all.FirstOrDefault(x => x.Id.Equals(d.Id, StringComparison.OrdinalIgnoreCase));
                    string why = dep == null ? "not installed"
                        : !dep.Enabled ? "switched off"
                        : d.Min != null && (dep.Version == null || dep.Version < d.Min) ? "version " + (dep.Version == null ? "?" : dep.VersionText) + " installed"
                        : dep.Error != null ? "it can't load" : null;
                    if (why == null) continue;
                    m.Error = "needs " + d + " (" + why + ")";
                    changed = true;
                    break;
                }
        }
    }

    // Load order: requirements, present optional mods and sortAfter before a mod, sortBefore after it; otherwise by
    // name. A loop leaves its mods out with an error.
    static List<ModInfo> Order(List<ModInfo> mods)
    {
        Func<string, ModInfo> get = id => mods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var before = mods.ToDictionary(m => m, m => new HashSet<ModInfo>());     // mods that must load before m
        foreach (var m in mods)
        {
            foreach (var id in m.Requires.Select(d => d.Id).Concat(m.Optional.Select(d => d.Id)).Concat(m.SortAfter))
            { var o = get(id); if (o != null && o != m) before[m].Add(o); }
            foreach (var id in m.SortBefore)
            { var o = get(id); if (o != null && o != m) before[o].Add(m); }
        }
        var order = new List<ModInfo>();
        var left = new List<ModInfo>(mods);
        while (left.Count > 0)
        {
            var ready = left.Where(m => before[m].All(order.Contains)).OrderBy(m => m.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (ready == null)
            {
                foreach (var m in left) m.Error = "load order loop with " + string.Join(", ", before[m].Where(left.Contains).Select(x => x.Name).ToArray());
                break;
            }
            order.Add(ready);
            left.Remove(ready);
        }
        return order;
    }

    static HashSet<string> Disabled()
    {
        var off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(ListPath))
                foreach (string raw in File.ReadAllLines(ListPath))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (line.Length == 0 || line[0] == ';' || eq < 0) continue;
                    string v = line.Substring(eq + 1).Trim();
                    if (v == "0" || v.Equals("off", StringComparison.OrdinalIgnoreCase)) off.Add(line.Substring(0, eq).Trim());
                }
        }
        catch (Exception ex) { Log.Write("mods: mods.ini: " + ex.Message); }
        return off;
    }

    // From the Mods tab: switch a mod on or off for the next start.
    public static void SetEnabled(string id, bool on)
    {
        try
        {
            var lines = File.Exists(ListPath) ? File.ReadAllLines(ListPath).ToList()
                : new List<string> { "; UnderRailWorks: which mods load. <mod id>=0 switches one off (applies at the next start)." };
            int i = lines.FindIndex(l => l.TrimStart().StartsWith(id + "=", StringComparison.OrdinalIgnoreCase));
            string line = id + "=" + (on ? "1" : "0");
            if (i >= 0) lines[i] = line; else lines.Add(line);
            File.WriteAllText(ListPath, string.Join("\n", lines) + "\n");
            Log.Write("mods: " + id + (on ? " switched on" : " switched off") + " (next start)");
        }
        catch (Exception ex) { Log.Write("mods: could not write mods.ini: " + ex.Message); }
    }

    static void Load(ModInfo mod, Type harmonyType)
    {
        var dlls = Directory.GetFiles(mod.Folder, "*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (dlls.Count == 0) { mod.Loaded = true; Log.Write("mods: loaded " + mod.Id + " (no code)"); return; }
        foreach (string dll in dlls)
        {
            try
            {
                Assembly assembly = Assembly.LoadFrom(dll);
                if (mod.Version == null) mod.Version = assembly.GetName().Version;
                int entries = 0;
                foreach (Type type in assembly.GetExportedTypes())
                {
                    if (typeof(WorksMod).IsAssignableFrom(type) && !type.IsAbstract)
                    {
                        if (mod.Instance != null) { Log.Write("mods: " + mod.Id + ": a second WorksMod class (" + type.FullName + ") is ignored"); continue; }
                        var main = (WorksMod)Activator.CreateInstance(type);
                        main.Info = mod;
                        main.Log = new ModLog(mod.Id);
                        main.Harmony = new HarmonyLib.Harmony("UnderRailWorks." + mod.Id);
                        mod.Instance = main;
                        main.Load();
                        mod.Loaded = true;
                        var patchedBy = main.Harmony.GetPatchedMethods().Select(m => m.DeclaringType.Name + "." + m.Name).ToArray();
                        Log.Write("mods: loaded " + mod.Id + " " + mod.VersionText + " (" + Path.GetFileName(dll) + ", " + type.FullName + ")"
                            + (patchedBy.Length > 0 ? ", patched: " + string.Join(", ", patchedBy) : ""));
                        entries++;
                        continue;
                    }
                    MethodInfo onLoad = type.GetMethod("OnLoad", BindingFlags.Public | BindingFlags.Static);
                    if (onLoad == null) continue;
                    ParameterInfo[] p = onLoad.GetParameters();
                    if (p.Length == 0 || p[0].ParameterType != harmonyType || p.Length > 2) continue;
                    object instance = Activator.CreateInstance(harmonyType, "UnderRailWorks." + mod.Id);
                    string name = mod.Id;
                    Action<string> log = line => Log.Write("mods: " + name + ": " + line);
                    onLoad.Invoke(null, p.Length == 1 ? new[] { instance } : new[] { instance, log });
                    var patched = (IEnumerable<MethodBase>)harmonyType.GetMethod("GetPatchedMethods", Type.EmptyTypes).Invoke(instance, null);
                    mod.Loaded = true;
                    Log.Write("mods: loaded " + mod.Id + " " + mod.VersionText + " (" + Path.GetFileName(dll) + ", " + type.FullName + "), patched: "
                        + string.Join(", ", patched.Select(m => m.DeclaringType.Name + "." + m.Name).ToArray()));
                    entries++;
                }
                if (entries == 0) Log.Write("mods: " + mod.Id + ": no WorksMod class or OnLoad(Harmony) in " + Path.GetFileName(dll));
            }
            catch (Exception ex)
            {
                Exception inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex;
                mod.Error = "failed: " + inner.Message;
                mod.Loaded = false;
                Log.Write("mods: " + mod.Id + " failed (" + Path.GetFileName(dll) + "): " + inner);
            }
        }
        if (!mod.Loaded && mod.Error == null) mod.Error = "no WorksMod class in its DLL";
    }
}
