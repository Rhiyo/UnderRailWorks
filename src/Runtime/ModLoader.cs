// UnderRailWorks: loads mods from Mods\<folder>\ in the game folder (64-bit mode only).
// Each mod may have a mod.json manifest (readable JSON; everything optional):
//   { "id": "UnderRailFixes", "name": "UnderRailFixes", "version": "2.0.0", "author": "...", "description": "...",
//     "works": "0.9", "requires": ["OtherMod >= 1.2"], "optional": ["SomeMod"], "sortAfter": [], "sortBefore": [],
//     "incompatible": ["Rival@2.0"], "homepage": "https://...", "issues": "https://...", "license": "MIT", "game": "1.3.1.2",
//     "multiplayer": "both" }
// icon.png in the folder is shown in the Mods tab.
// works: the Works API version the mod was built for (WorksApi.Version: same major, at least that minor).
// As tModLoader's build.txt: requires = modReferences (must be present, on and at least that version, and is loaded
// first), optional = weakReferences (loaded first when present), sortAfter / sortBefore = load order only (the player
// can change the rest: Options -> Mods, mods.ini "@order"). multiplayer: "both" (default; everyone in a multiplayer game needs it, same version) or
// "client" (only this player's screen: a minimap, a HUD layout).
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
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class ModDependency
{ internal ModDependency() { }
    public string Id { get; internal set; }
    public Version Min { get; set; }     // null: any version
    public override string ToString() { return Min == null ? Id : Id + " " + ModInfo.Text(Min) + "+"; }
    // "OtherMod", "OtherMod >= 1.2" (as Factorio's and npm's manifests write a minimum) or "OtherMod@1.2" (the same).
    internal static ModDependency Parse(string text)
    {
        text = text ?? "";
        int ge = text.IndexOf(">=", StringComparison.Ordinal), at = text.IndexOf('@');
        int cut = ge >= 0 ? ge : at, skip = ge >= 0 ? 2 : 1;
        var d = new ModDependency { Id = (cut < 0 ? text : text.Substring(0, cut)).Trim() };
        if (cut >= 0) d.Min = ModInfo.ParseVersion(text.Substring(cut + skip));
        return d;
    }
}

public sealed class ModInfo
{ internal ModInfo() { }
    public string Id { get; internal set; }
    public string Name { get; internal set; }
    public string Folder { get; internal set; }
    public string Author { get; internal set; }
    public string Description { get; internal set; }
    public string Error { get; internal set; }
    // Loaded, but something to know (it uses Works' internals or the game's own code: it may break with an update).
    public string Warning { get; internal set; }
    public Version Version { get; internal set; }
    public Version Works { get; internal set; }
    internal WorksMod Instance { get; set; }
    public bool Enabled { get; internal set; }
    // mod.json "startsOff": true: switched off until the player switches it on (gameplay rules, difficulty changes).
    public bool StartsOff { get; internal set; }
    public bool Loaded { get; internal set; }
    public IReadOnlyList<ModDependency> Requires { get; internal set; } = new List<ModDependency>();
    public IReadOnlyList<ModDependency> Optional { get; internal set; } = new List<ModDependency>();
    public IReadOnlyList<string> SortAfter { get; internal set; } = new List<string>();
    public IReadOnlyList<string> SortBefore { get; internal set; } = new List<string>();
    public IReadOnlyList<ModDependency> Incompatible { get; internal set; } = new List<ModDependency>();
    public string Homepage { get; internal set; }
    public string Issues { get; internal set; }
    public string License { get; internal set; }
    public string Game { get; internal set; }
    // mod.json "multiplayer": "client": only changes this player's own screen (a minimap, the HUD's layout), so players
    // in one multiplayer game needn't all have it. "both" (the default): everyone in the game needs it, same version.
    public bool ClientOnly { get; internal set; }
    // Where it is in the load order (0 first); -1 not loaded.
    public int LoadIndex { get; internal set; } = -1;
    // The mod's icon (icon.png in its folder), or null.
    public string Icon { get { var f = Path.Combine(Folder, "icon.png"); return File.Exists(f) ? f : null; } }
    // Handlers of this mod that failed while the game ran (HookBase), for the Mods tab.
    public int Failures { get; internal set; }
    public string LastFailure { get; internal set; }

    // "1.2", "1.2.0", "v1.2", "1.2.0-beta" (the suffix is ignored): always major.minor.build.revision, missing parts 0, so
    // "1.2" and "1.2.0" are equal (System.Version alone says 1.2 < 1.2.0).
    internal static Version ParseVersion(string s)
    {
        Version v;
        s = (s ?? "").Trim();
        if (s.StartsWith("v", StringComparison.OrdinalIgnoreCase)) s = s.Substring(1);
        int cut = s.IndexOfAny(new[] { '-', '+', ' ' });
        if (cut > 0) s = s.Substring(0, cut);
        if (s.Length > 0 && s.IndexOf('.') < 0) s += ".0";
        return Version.TryParse(s, out v) ? Norm(v) : null;
    }
    internal static Version Norm(Version v) { return v == null ? null : new Version(v.Major, v.Minor, Math.Max(0, v.Build), Math.Max(0, v.Revision)); }
    // Letters, digits, '.', '_' and '-': ids go into mods.ini lines, @order lists, file names and Harmony ids.
    internal static bool ValidId(string id) { return !string.IsNullOrEmpty(id) && id.All(c => c < 128 && (char.IsLetterOrDigit(c) || c == '.' || c == '_' || c == '-')); }
    public string VersionText { get { return Text(Version); } }
    // 1.2.0 (major.minor.build, the revision only when it isn't 0).
    internal static string Text(Version v) { v = Norm(v); return v == null ? "" : v.Revision > 0 ? v.ToString() : v.ToString(3); }
    // The version of the DLL that holds the mod's code: the one named after the folder or id, else the only one, else
    // the first that isn't a library it ships (Newtonsoft.Json.dll...). Null: no DLL.
    internal static string MainDll(string folder, string id)
    {
        string[] dlls;
        try { dlls = Directory.GetFiles(folder, "*.dll"); } catch { return null; }
        if (dlls.Length == 0) return null;
        return dlls.FirstOrDefault(d => Path.GetFileNameWithoutExtension(d).Equals(id, StringComparison.OrdinalIgnoreCase))
            ?? dlls.FirstOrDefault(d => Path.GetFileNameWithoutExtension(d).Equals(Path.GetFileName(folder), StringComparison.OrdinalIgnoreCase))
            ?? (dlls.Length == 1 ? dlls[0] : null);
    }
}

public static class ModLoader
{
    public static readonly string ModsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");
    static string ListPath { get { return Path.Combine(ModsFolder, "mods.ini"); } }
    // Every mod folder, in load order (mods that can't load at the end, by name).
    // Every mod found (loaded or not), in load order then by name. Read-only to mods.
    public static IReadOnlyList<ModInfo> Mods { get { return view; } }
    internal static readonly List<ModInfo> All = new List<ModInfo>();
    static readonly System.Collections.ObjectModel.ReadOnlyCollection<ModInfo> view = new System.Collections.ObjectModel.ReadOnlyCollection<ModInfo>(All);
    static readonly List<string> Folders = new List<string>();

    public static ModInfo Find(string id) { return All.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase)); }
    public static bool IsLoaded(string id) { var m = Find(id); return m != null && m.Loaded; }

    internal static void Init()
    {
        if (!Directory.Exists(ModsFolder)) return;
        HashSet<string> listed;
        var off = Disabled(out listed);
        var all = Directory.GetDirectories(ModsFolder).OrderBy(d => d, StringComparer.OrdinalIgnoreCase).Select(Read).ToList();
        foreach (var m in all) m.Enabled = !off.Contains(m.Id);
        // Two folders with one id (an old copy left beside a new one): only the first loads.
        foreach (var g in all.GroupBy(m => m.Id, StringComparer.OrdinalIgnoreCase).Where(g => g.Count() > 1))
            foreach (var m in g.Skip(1)) if (m.Error == null) m.Error = "the same id as the mod in " + Path.GetFileName(g.First().Folder) + " (remove one of the two folders)";
        foreach (var m in all.Where(m => m.Error == null && !ModInfo.ValidId(m.Id))) m.Error = "its id \"" + m.Id + "\" may only use letters, digits, '.', '_' and '-'";
        // "startsOff" mods (gameplay changes a player should choose) that mods.ini doesn't name yet: off, written down
        // so the Mods tab and the file show it.
        foreach (var m in all.Where(m => m.StartsOff && !listed.Contains(m.Id))) { m.Enabled = false; SetEnabled(m.Id, false); Log.Write("mods: " + m.Id + " starts switched off (switch it on in Options -> Mods)"); }
        if (all.Count == 0) return;
        if (IntPtr.Size != 8)
        {
            foreach (var m in all) m.Error = "needs the 64-bit mode";
            All.AddRange(all);
            Log.Write("mods: not loaded, they need the 64-bit mode");
            return;
        }
        CheckRequirements(all);
        PlayerOrder = ReadPlayerOrder();
        var order = Order(all.Where(m => m.Enabled && m.Error == null).ToList());
        All.AddRange(order);
        All.AddRange(all.Except(order).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase));
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
        try { UiHooks.Install(); } catch (Exception ex) { WorksHealth.Fail("interface"); Log.Always("UnderRailWorks: UI hooks failed: " + ex); }

        // A mod's own dependencies sit in its folder (or a required mod's).
        AppDomain.CurrentDomain.AssemblyResolve += (sender, e) =>
        {
            string name = new AssemblyName(e.Name).Name;
            if (name == "0Harmony") return harmony;
            Assembly mine;
            if (ModCode.ByName.TryGetValue(name, out mine)) return mine;     // a mod's own (maybe loaded from memory)
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
            LoadCode(mod, FixSettings.HotReload);
        }
        Log.Write("mods: checked " + ModCheck.Count + " DLLs against this Works in " + (int)ModCheck.Time + " ms");
        // Content packs (content/*.json), after every mod's code: packs may use conditions and effects a DLL registered.
        foreach (var mod in order.Where(m => m.Loaded))
        {
            ModScope.Current = mod.Id;     // what the pack registers is the mod's (forgotten when it's reloaded)
            try { ContentPacks.Load(mod); } catch (Exception ex) { Log.Write("content: " + mod.Id + ": " + ex.Message); }
            ModScope.Current = null;
            try { AssetStore.ScanTexturePack(mod); } catch (Exception ex) { Log.Write("assets: " + mod.Id + ": " + ex.Message); }
        }
        // PostLoad: after every mod's Load, in the same order.
        foreach (var mod in order.Where(m => m.Loaded && m.Instance != null))
        {
            ModScope.Current = mod.Id;
            try { mod.Instance.PostLoad(); }
            catch (Exception ex) { mod.Error = "PostLoad failed: " + ex.Message; mod.Loaded = false; Log.Write("mods: " + mod.Id + " PostLoad failed: " + ex); Unregister(mod); }
        }
        ModScope.Current = null;
        // A mod whose requirement failed in PostLoad goes too (and so on down), so nothing runs against a missing mod.
        for (bool more = true; more; )
        {
            more = false;
            foreach (var mod in order.Where(m => m.Loaded))
            {
                var gone = mod.Requires.Select(d => Find(d.Id)).FirstOrDefault(d => d != null && !d.Loaded);
                if (gone == null) continue;
                mod.Error = "needs " + gone.Name + " (it failed to load)"; mod.Loaded = false; more = true;
                Log.Write("mods: " + mod.Id + " unloaded: " + mod.Error);
                Unregister(mod);
            }
        }
        foreach (var mod in All.Where(m => !m.Loaded)) mod.LoadIndex = -1;
    }

    static readonly string[] ManifestKeys = { "id", "name", "version", "author", "description", "works", "requires", "optional", "sortAfter", "sortBefore", "incompatible", "homepage", "issues", "license", "game", "startsOff", "multiplayer" };

    static ModInfo Read(string folder)
    {
        var m = new ModInfo { Folder = folder, Id = Path.GetFileName(folder), Name = Path.GetFileName(folder) };
        string manifest = Path.Combine(folder, "mod.json");
        if (!File.Exists(manifest)) return m;
        try
        {
            var o = Json.Parse(File.ReadAllText(manifest)) as Dictionary<string, object>;
            if (o == null) throw new FormatException("not a JSON object");
            // A key Works doesn't know is most likely a typo ("require" for "requires"): said, so it isn't silently ignored.
            var unknown = o.Keys.Where(k => !Array.Exists(ManifestKeys, x => x.Equals(k, StringComparison.OrdinalIgnoreCase))).ToList();     // read ignoring case, so checked that way
            if (unknown.Count > 0) Log.Write("mods: " + Path.GetFileName(folder) + "/mod.json: unknown key" + (unknown.Count > 1 ? "s " : " ") + string.Join(", ", unknown.ToArray())
                + " (ignored; known: " + string.Join(", ", ManifestKeys) + ")");
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
            m.Incompatible = list("incompatible").Select(ModDependency.Parse).ToList();
            m.Homepage = str("homepage");
            m.StartsOff = str("startsOff") != null && str("startsOff").Equals("true", StringComparison.OrdinalIgnoreCase);
            m.Issues = str("issues");
            m.License = str("license");
            var mp = (str("multiplayer") ?? "both").Trim().ToLowerInvariant();
            if (mp != "both" && mp != "client") Log.Write("mods: " + m.Id + "/mod.json: multiplayer \"" + mp + "\" isn't both or client (taken as both)");
            m.ClientOnly = mp == "client";
            m.Game = str("game");     // the game versions it was made for, e.g. "1.3.1.2" (shown, not enforced)
            if (str("version") != null && m.Version == null) Log.Write("mods: " + m.Id + "/mod.json: version \"" + str("version") + "\" isn't a version (like 1.2.0)");
            if (str("works") != null && m.Works == null) Log.Write("mods: " + m.Id + "/mod.json: works \"" + str("works") + "\" isn't a version (like 1.0)");
        }
        catch (Exception ex) { m.Error = "mod.json: " + ex.Message; }
        // No version in mod.json: its DLL's, known before requirements are checked.
        if (m.Version == null)
        {
            var dll = ModInfo.MainDll(folder, m.Id);
            if (dll != null) try { m.Version = ModInfo.Norm(AssemblyName.GetAssemblyName(dll).Version); } catch { }
        }
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
                if (m.Works.Major != WorksApi.Version.Major || m.Works > ModInfo.Norm(WorksApi.Version)
                    || (WorksApi.Version.Major == 0 && m.Works.Minor != WorksApi.Version.Minor))     // 0.x (preview): each minor may break
                {
                    m.Error = "built for UnderRailWorks API " + m.Works.ToString(2) + " (this is " + WorksApi.Version.ToString(2) + ")";
                    changed = true;
                }
            foreach (var m in all.Where(m => m.Enabled && m.Error == null))
                foreach (var d in m.Incompatible)
                {
                    var other = all.FirstOrDefault(x => x.Enabled && x.Error == null && x != m && x.Id.Equals(d.Id, StringComparison.OrdinalIgnoreCase) && (d.Min == null || (x.Version != null && x.Version >= d.Min)));
                    if (other == null) continue;
                    m.Error = "incompatible with " + other.Name + " (switch one off)";
                    changed = true;
                    break;
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
    // name; the player's order (Options -> Mods) before the alphabet. A loop leaves its mods out with an error.
    static List<ModInfo> Order(List<ModInfo> mods)
    {
        Func<string, ModInfo> get = id => mods.FirstOrDefault(m => m.Id.Equals(id, StringComparison.OrdinalIgnoreCase));
        var before = mods.ToDictionary(m => m, m => new HashSet<ModInfo>());     // mods that must load before m
        var soft = mods.ToDictionary(m => m, m => new HashSet<ModInfo>());       // of those, the ones only an order asks for
        foreach (var m in mods)
        {
            foreach (var id in m.Requires.Select(d => d.Id))
            { var o = get(id); if (o != null && o != m) { before[m].Add(o); soft[m].Remove(o); } }
            foreach (var id in m.Optional.Select(d => d.Id).Concat(m.SortAfter))
            { var o = get(id); if (o != null && o != m && before[m].Add(o)) soft[m].Add(o); }
            foreach (var id in m.SortBefore)
            { var o = get(id); if (o != null && o != m && before[o].Add(m)) soft[o].Add(m); }
        }
        // A requirement written after an ordering of the same pair is still a requirement.
        foreach (var m in mods) foreach (var id in m.Requires.Select(d => d.Id)) { var o = get(id); if (o != null) soft[m].Remove(o); }
        // The player's order (Options -> Mods, mods.ini "@order") comes before the alphabet; requirements and named sortAfter/sortBefore still hold. Mods it doesn't name go after those it does.
        Func<ModInfo, int> rank = m => { int i = PlayerOrder.FindIndex(x => x.Equals(m.Id, StringComparison.OrdinalIgnoreCase)); return i < 0 ? int.MaxValue : i; };
        var order = new List<ModInfo>();
        var left = new List<ModInfo>(mods);
        while (left.Count > 0)
        {
            var ready = left.Where(m => before[m].All(order.Contains)).OrderBy(rank).ThenBy(m => m.Id, StringComparer.OrdinalIgnoreCase).FirstOrDefault();
            if (ready == null)
            {
                // A loop: first drop the orderings that aren't requirements (sortAfter / sortBefore / optional), as
                // other loaders do, with a line saying so; only a loop of requirements keeps its mods out.
                // Only orderings inside a loop: o before m, where m is (directly or not) before o.
                Func<ModInfo, ModInfo, bool> reaches = (from, to) =>
                {
                    var seen = new HashSet<ModInfo>(); var todo = new Stack<ModInfo>(); todo.Push(from);
                    while (todo.Count > 0) { var x = todo.Pop(); if (!seen.Add(x)) continue; foreach (var y in before[x].Where(left.Contains)) { if (y == to) return true; todo.Push(y); } }
                    return false;
                };
                var inLoop = left.SelectMany(m => soft[m].Where(o => left.Contains(o) && reaches(o, m)).Select(o => new KeyValuePair<ModInfo, ModInfo>(m, o))).ToList();
                var dropped = inLoop.Select(kv => kv.Value.Id + " before " + kv.Key.Id).ToList();
                if (dropped.Count > 0)
                {
                    foreach (var kv in inLoop) { before[kv.Key].Remove(kv.Value); soft[kv.Key].Remove(kv.Value); }
                    Log.Write("mods: load order loop: ignored " + string.Join(", ", dropped.ToArray()) + " (sortAfter / sortBefore / optional)");
                    continue;
                }
                foreach (var m in left) m.Error = "load order loop with " + string.Join(", ", before[m].Where(left.Contains).Select(x => x.Name).ToArray());
                break;
            }
            ready.LoadIndex = order.Count;
            order.Add(ready);
            left.Remove(ready);
        }
        // Where the mods' own rules moved one against the player's order, say so (the Mods tab shows the order as it is).
        int said = 0;
        for (int i = 0; i < order.Count; i++)
            for (int j = i + 1; j < order.Count; j++)
                if (rank(order[i]) != int.MaxValue && rank(order[j]) != int.MaxValue && rank(order[i]) > rank(order[j]) && said++ < 8)
                    Log.Write("mods: " + order[i].Id + " loads before " + order[j].Id + " although the player's order has it after (a requirement or sortBefore/sortAfter)");
        return order;
    }

    static HashSet<string> Disabled(out HashSet<string> listed)
    {
        var off = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        listed = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        try
        {
            if (File.Exists(ListPath))
                foreach (string raw in File.ReadAllLines(ListPath))
                {
                    string line = raw.Trim();
                    int eq = line.IndexOf('=');
                    if (line.Length == 0 || line[0] == ';' || eq < 0) continue;
                    string v = line.Substring(eq + 1).Trim();
                    listed.Add(line.Substring(0, eq).Trim());
                    if (v == "0" || v.Equals("off", StringComparison.OrdinalIgnoreCase)) off.Add(line.Substring(0, eq).Trim());
                }
        }
        catch (Exception ex) { Log.Write("mods: mods.ini: " + ex.Message); }
        return off;
    }

    // mods.ini "@order=A,B,C": the player's load order (empty: the mods' own).
    internal static List<string> PlayerOrder = new List<string>();
    const string OrderKey = "@order";
    static List<string> ReadPlayerOrder()
    {
        try
        {
            if (!File.Exists(ListPath)) return new List<string>();
            var line = File.ReadAllLines(ListPath).Select(l => l.Trim()).FirstOrDefault(l => l.StartsWith(OrderKey + "=", StringComparison.OrdinalIgnoreCase));
            return line == null ? new List<string>() : line.Substring(OrderKey.Length + 1).Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
        }
        catch (Exception ex) { Log.Write("mods: mods.ini: " + ex.Message); return new List<string>(); }
    }
    // From the Mods tab: the player's load order for the next start (null: back to the mods' own).
    // A new order that names only some mods (the loaded ones) keeps the others where the old order had them, each
    // after the mod it followed: a mod switched off for a while comes back to its place, not to the end.
    internal static List<string> KeepPlaces(IList<string> ids, IList<string> old)
    {
        if (ids == null) return null;
        var result = ids.ToList();
        if (old == null) return result;
        Func<string, int> at = id => result.FindIndex(x => x.Equals(id, StringComparison.OrdinalIgnoreCase));
        for (int i = 0; i < old.Count; i++)
        {
            if (at(old[i]) >= 0) continue;
            int after = -1;
            for (int k = i - 1; k >= 0 && after < 0; k--) after = at(old[k]);
            result.Insert(after + 1, old[i]);
        }
        return result;
    }
    internal static void SetPlayerOrder(IList<string> ids)
    {
        try
        {
            var lines = File.Exists(ListPath) ? File.ReadAllLines(ListPath).ToList()
                : new List<string> { "; UnderRailWorks: which mods load. <mod id>=0 switches one off (applies at the next start)." };
            lines.RemoveAll(l => l.TrimStart().StartsWith(OrderKey + "=", StringComparison.OrdinalIgnoreCase) || l.TrimStart().StartsWith("; " + OrderKey, StringComparison.Ordinal));
            ids = KeepPlaces(ids, PlayerOrder);
            if (ids != null && ids.Count > 0)
            {
                lines.Add("; " + OrderKey + ": the load order set in Options -> Mods (first loads first; later has the final say). Delete the line for the mods' own order.");
                lines.Add(OrderKey + "=" + string.Join(",", ids.ToArray()));
            }
            File.WriteAllText(ListPath, string.Join("\n", lines) + "\n");
            PlayerOrder = ids == null ? new List<string>() : ids.ToList();     // the next change in this session merges against this one
            Log.Write("mods: load order set (next start): " + (ids == null || ids.Count == 0 ? "the mods' own" : string.Join(", ", ids.ToArray())));
        }
        catch (Exception ex) { Log.Write("mods: could not write mods.ini: " + ex.Message); }
    }

    // Mod profiles (as RimWorld's saved mod lists and tModLoader's mod packs): Mods/profiles/<name>.ini, the same lines
    // as mods.ini (<id>=1/0 and @order). Using one writes them into mods.ini (next start); mods it doesn't name keep
    // their current state. mods.ini "@profile" names the last one used.
    static string ProfileFolder { get { return Path.Combine(ModsFolder, "profiles"); } }
    internal static List<string> Profiles()
    {
        try { return Directory.Exists(ProfileFolder) ? Directory.GetFiles(ProfileFolder, "*.ini").Select(Path.GetFileNameWithoutExtension).OrderBy(n => n, StringComparer.OrdinalIgnoreCase).ToList() : new List<string>(); }
        catch (Exception ex) { Log.Write("mods: profiles: " + ex.Message); return new List<string>(); }
    }
    internal static string ProfileName(string name)
    {
        var clean = new string((name ?? "").Trim().Select(c => Path.GetInvalidFileNameChars().Contains(c) ? '_' : c).ToArray());
        return clean.Length == 0 ? null : clean;
    }
    internal static string CurrentProfile
    {
        get
        {
            try { var l = File.Exists(ListPath) ? File.ReadAllLines(ListPath).FirstOrDefault(x => x.StartsWith("@profile=", StringComparison.OrdinalIgnoreCase)) : null; return l == null ? null : l.Substring(9).Trim(); }
            catch { return null; }
        }
    }
    internal static bool SaveProfile(string name, IEnumerable<KeyValuePair<string, bool>> mods, IList<string> order)
    {
        name = ProfileName(name);
        if (name == null) return false;
        try
        {
            Directory.CreateDirectory(ProfileFolder);
            var lines = new List<string> { "; UnderRailWorks mod profile \"" + name + "\": which mods are on (<id>=1/0) and their load order. Options -> Mods -> Use profile." };
            lines.AddRange(mods.Select(kv => kv.Key + "=" + (kv.Value ? "1" : "0")));
            if (order != null && order.Count > 0) lines.Add(OrderKey + "=" + string.Join(",", order.ToArray()));
            File.WriteAllText(Path.Combine(ProfileFolder, name + ".ini"), string.Join("\n", lines) + "\n");
            SetLine("@profile", name);
            Log.Write("mods: profile " + name + " saved");
            return true;
        }
        catch (Exception ex) { Log.Write("mods: profile " + name + ": " + ex.Message); return false; }
    }
    // The profile's switches (id -> on) and order, or null.
    internal static KeyValuePair<Dictionary<string, bool>, List<string>>? ReadProfile(string name)
    {
        var file = Path.Combine(ProfileFolder, (ProfileName(name) ?? "") + ".ini");
        if (!File.Exists(file)) return null;
        var on = new Dictionary<string, bool>(StringComparer.OrdinalIgnoreCase); var order = new List<string>();
        string[] lines;
        try { lines = File.ReadAllLines(file); } catch (Exception ex) { Log.Write("mods: profile " + file + ": " + ex.Message); return null; }
        foreach (var raw in lines)
        {
            var line = raw.Trim(); int eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] == ';' || eq < 0) continue;
            string k = line.Substring(0, eq).Trim(), v = line.Substring(eq + 1).Trim();
            if (k.Equals(OrderKey, StringComparison.OrdinalIgnoreCase)) order = v.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToList();
            else if (!k.StartsWith("@")) on[k] = !(v == "0" || v.Equals("off", StringComparison.OrdinalIgnoreCase));
        }
        return new KeyValuePair<Dictionary<string, bool>, List<string>>(on, order);
    }
    // Into mods.ini for the next start.
    internal static bool UseProfile(string name)
    {
        var p = ReadProfile(name);
        if (p == null) return false;
        foreach (var kv in p.Value.Key) if (Find(kv.Key) != null) SetEnabled(kv.Key, kv.Value);
        SetPlayerOrder(p.Value.Value.Count > 0 ? p.Value.Value : null);
        SetLine("@profile", ProfileName(name));
        Log.Write("mods: profile " + name + " used (next start)");
        return true;
    }
    internal static bool DeleteProfile(string name)
    {
        try { var f = Path.Combine(ProfileFolder, (ProfileName(name) ?? "") + ".ini"); if (!File.Exists(f)) return false; File.Delete(f); Log.Write("mods: profile " + name + " deleted"); return true; }
        catch (Exception ex) { Log.Write("mods: profile " + name + ": " + ex.Message); return false; }
    }
    static void SetLine(string key, string value)
    {
        try
        {
            var lines = File.Exists(ListPath) ? File.ReadAllLines(ListPath).ToList() : new List<string>();
            lines.RemoveAll(l => l.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            if (value != null) lines.Add(key + "=" + value);
            File.WriteAllText(ListPath, string.Join("\n", lines) + "\n");
        }
        catch (Exception ex) { Log.Write("mods: could not write mods.ini: " + ex.Message); }
    }

    // From the Mods tab: switch a mod on or off for the next start.
    internal static void SetEnabled(string id, bool on)
    {
        try
        {
            var lines = File.Exists(ListPath) ? File.ReadAllLines(ListPath).ToList()
                : new List<string> { "; UnderRailWorks: which mods load. <mod id>=0 switches one off (applies at the next start)." };
            // Every line for this id (hand-written "Foo = 0", duplicates) becomes the one new line.
            Func<string, bool> mine = l => { var t = l.Trim(); int eq = t.IndexOf('='); return eq > 0 && t[0] != ';' && t.Substring(0, eq).Trim().Equals(id, StringComparison.OrdinalIgnoreCase); };
            int i = lines.FindIndex(l => mine(l));
            string line = id + "=" + (on ? "1" : "0");
            lines.RemoveAll(l => mine(l));
            if (i >= 0) lines.Insert(Math.Min(i, lines.Count), line); else lines.Add(line);
            File.WriteAllText(ListPath, string.Join("\n", lines) + "\n");
            Log.Write("mods: " + id + (on ? " switched on" : " switched off") + " (next start)");
        }
        catch (Exception ex) { Log.Write("mods: could not write mods.ini: " + ex.Message); }
    }

    // A mod that failed while loading: whatever it registered before the failure (handlers, Harmony patches, commands,
    // UI...) goes, so a mod shown as failed never half-runs.
    static void Unregister(ModInfo mod)
    {
        try { int ui = ModReloader.Forget(mod); mod.Instance = null; Log.Write("mods: " + mod.Id + ": what it registered before failing was removed"); }
        catch (Exception ex) { Log.Write("mods: " + mod.Id + ": removing what it registered failed: " + ex.Message); }
    }

    // A mod's DLLs (collectible: from memory into a context that can be unloaded, for reloading: ModReload.cs).
    internal static void LoadCode(ModInfo mod, bool collectible)
    {
        var harmonyType = typeof(HarmonyLib.Harmony);
        var dlls = Directory.GetFiles(mod.Folder, "*.dll").OrderBy(f => f, StringComparer.OrdinalIgnoreCase).ToList();
        if (dlls.Count == 0) { mod.Loaded = true; Log.Write("mods: loaded " + mod.Id + " (no code)"); return; }
        ModCode.Stamps[mod.Id] = ModCode.Stamp(mod);
        // Everything it uses from Works must exist here (ModCheck): otherwise not loaded, with what's missing.
        var warnings = new List<string>();
        foreach (string dll in dlls)
        {
            ModCheck.Result check;
            var watch = System.Diagnostics.Stopwatch.StartNew();
            try { check = ModCheck.Scan(mod, dll); }
            catch (Exception ex) { Log.Write("mods: " + mod.Id + ": couldn't check " + Path.GetFileName(dll) + ": " + ex.Message); continue; }
            ModCheck.Time += watch.Elapsed.TotalMilliseconds; ModCheck.Count++;
            if (check.Error != null) { mod.Error = check.Error; Log.Write("mods: " + mod.Id + " not loaded (" + Path.GetFileName(dll) + "): " + check.Error); return; }
            warnings.AddRange(check.Warnings);
        }
        if (warnings.Count > 0) { mod.Warning = string.Join("; ", warnings.Distinct().ToArray()); Log.Write("mods: " + mod.Id + ": " + mod.Warning); }
        // Every DLL first, then their entry points: a mod's own libraries are there when its Load needs them.
        var loaded = new List<KeyValuePair<string, Assembly>>();
        foreach (string dll in dlls)
        {
            try { loaded.Add(new KeyValuePair<string, Assembly>(dll, ModCode.Load(mod, dll, collectible))); }
            catch (Exception ex) { mod.Error = "failed: " + ex.Message; Log.Write("mods: " + mod.Id + " failed (" + Path.GetFileName(dll) + "): " + ex); return; }
        }
        var was = ModScope.Current;
        ModScope.Current = mod.Id;
        try { foreach (var kv in loaded) LoadEntries(mod, kv.Key, kv.Value, harmonyType); }
        finally { ModScope.Current = was; }
        if (!mod.Loaded && mod.Error == null) mod.Error = "no WorksMod class in its DLL";
    }

    static void LoadEntries(ModInfo mod, string dll, Assembly assembly, Type harmonyType)
    {
        {
            try
            {
                if (mod.Version == null && (dll == ModInfo.MainDll(mod.Folder, mod.Id) || ModInfo.MainDll(mod.Folder, mod.Id) == null)
                    && assembly.GetExportedTypes().Any(t => typeof(WorksMod).IsAssignableFrom(t))) mod.Version = ModInfo.Norm(assembly.GetName().Version);
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
                        ModConfig.Register(mod, assembly);
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
                Unregister(mod);
            }
        }
    }
}
}
