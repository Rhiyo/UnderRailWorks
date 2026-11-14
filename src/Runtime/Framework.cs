// UnderRailWorks: per-save structured data, translations, and APIs between mods.
//
// Save data (as tModLoader's SaveData/LoadData, SMAPI's data API): each mod gets a JSON-like store per game, written
// next to the save as Saves/<save>/UnderRailWorks.json and read back when the save loads. The stock game never reads
// that file, so the save stays loadable without Works; the data of mods that aren't installed is kept and written back.
//   SaveData.Of(this).Set("kills", 12); var k = SaveData.Of(this).Get("kills", 0);
//   SaveData.Of(this).Set("visited", new List<object> { "zoneA", "zoneB" });
//
// Translations (tModLoader's .hjson, SMAPI's i18n folder): Mods/<mod>/i18n/<language>.json, flat "key": "text"
// ({0}, {1} for arguments). The language is the game's (English is "english"; others are their Localization folder
// names); missing keys fall back to english.json, then to the key. Mod.T("key", args).
//
// APIs between mods (SMAPI's GetApi, tModLoader's Mod.Call): a mod publishes an object (Mods.Provide) that others get
// by mod id (Mods.Api<T>, by an interface both compile against, or Mods.Call(id, name, args) without a shared DLL),
// and named events (Mods.Event(name)).
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Reflection;
using System.Linq;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class SaveData
    {
        readonly string mod;     // the section's key in UnderRailWorks.json: the mod id, or "character:<id>" (CharacterData)
        SaveData(string mod) { this.mod = mod; }
        static readonly Dictionary<string, SaveData> stores = new Dictionary<string, SaveData>(StringComparer.OrdinalIgnoreCase);
        public static SaveData Of(WorksMod mod) { return Store(mod.Id); }
        // The mod's data about the player character: saved with the game like SaveData, and carried into an exported
        // character (a side file next to the .char) and back into the game that imports it.
        public static SaveData CharacterOf(WorksMod mod) { return Store(CharacterFiles.Prefix + mod.Id); }
        static SaveData Store(string key) { SaveData s; if (!stores.TryGetValue(key, out s)) stores[key] = s = new SaveData(key); return s; }

        Dictionary<string, object> Section
        {
            get
            {
                object o;
                if (!SaveFiles.Current.TryGetValue(mod, out o) || !(o is Dictionary<string, object>)) SaveFiles.Current[mod] = o = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
                return (Dictionary<string, object>)o;
            }
        }

        // A value: string, bool, a number, a List<object> or a Dictionary<string, object> of those (as Json reads them;
        // numbers come back as double, converted to T where it can).
        public T Get<T>(string key, T fallback = default(T))
        {
            object v;
            if (!Section.TryGetValue(key, out v) || v == null) return fallback;
            if (v is T) return (T)v;
            try { return (T)Convert.ChangeType(v, typeof(T), CultureInfo.InvariantCulture); } catch { return fallback; }
        }
        public object Get(string key) { object v; return Section.TryGetValue(key, out v) ? v : null; }
        public void Set(string key, object value)
        {
            if (key == null) throw new ArgumentNullException("key");
            Check(value, key);
            // Kept in the shape JSON reads it back in (lists as List<object>, numbers as double), so Get answers the same
            // before and after a save is loaded (a typed list read as List<object> came back null in the same session).
            if (value is int || value is long || value is float || value is short || value is byte || value is uint) value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            else if (value is System.Collections.IEnumerable && !(value is string)) value = Json.Parse(Json.Write(value, false));
            Section[key] = value;
        }
        // Only what JSON can hold, all the way down: a bad value is refused here, where the mod can see it, instead of
        // failing the save's file later. Numbers are doubles (exact up to 2^53: keep larger ones, ids, ticks, as strings).
        static void Check(object v, string where)
        {
            if (v == null || v is string || v is bool) return;
            if (v is double || v is float || v is int || v is long || v is short || v is byte || v is uint)
            {
                double d = Convert.ToDouble(v, CultureInfo.InvariantCulture);
                if (double.IsNaN(d) || double.IsInfinity(d)) throw new ArgumentException("save data: " + where + " is " + d + " (only finite numbers)");
                return;
            }
            if (v is System.Collections.IDictionary)
            {
                foreach (System.Collections.DictionaryEntry e in (System.Collections.IDictionary)v)
                {
                    if (!(e.Key is string)) throw new ArgumentException("save data: " + where + ": dictionary keys must be strings");
                    Check(e.Value, where + "." + e.Key);
                }
                return;
            }
            if (v is System.Collections.IEnumerable) { int i = 0; foreach (var e in (System.Collections.IEnumerable)v) Check(e, where + "[" + i++ + "]"); return; }
            throw new ArgumentException("save data: " + where + ": holds strings, bools, numbers, lists and dictionaries, not " + v.GetType().Name);
        }
        public bool Has(string key) { return Section.ContainsKey(key); }
        public void Remove(string key) { Section.Remove(key); }
        public IList<string> Keys { get { return Section.Keys.ToList(); } }
        // Everything, e.g. to migrate an older layout.
        public void Clear() { Section.Clear(); }
        // The version of the mod that wrote this data (the save's, as loaded; this version's once the game is saved).
        // Null: none yet, or a save from before Works recorded it. For migrating an older layout when the save loads:
        //   GameEvents.GameLoaded.Add(this, () => { var v = SaveData.WrittenBy; if (v != null && v < new Version(2, 0)) Migrate(); });
        public Version WrittenBy { get { string v; return SaveFiles.Versions.TryGetValue(mod, out v) ? ModInfo.ParseVersion(v) : null; } }
    }

    public static class Translations
    {
        // The game's language: "english", or its Localization folder name (e.g. "srpski").
        public static string Language { get { var l = ActiveField.GetValue(null) as evo; return l == null || string.IsNullOrEmpty(l.a) ? "english" : l.a.ToLowerInvariant(); } }
        static readonly System.Reflection.FieldInfo ActiveField = typeof(aji).GetField("a");     // aji.a: the active localization (a field/method name clash)

        static readonly Dictionary<string, Dictionary<string, string>> loaded = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        internal static void Forget() { loaded.Clear(); }
        static readonly HashSet<string> warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        internal static string Get(WorksMod mod, string key, params object[] args)
        {
            string text = Lookup(mod.Info.Folder, Language, key) ?? Lookup(mod.Info.Folder, "english", key) ?? Lookup(mod.Info.Folder, "en", key);
            if (text == null)
            {
                if (warned.Add(mod.Id) && Directory.Exists(Path.Combine(mod.Info.Folder, "i18n")))
                    Log.Write("i18n: " + mod.Id + ": no text for \"" + key + "\" in i18n/" + (Language == "english" ? "" : Language + ".json or i18n/") + "english.json (shown: the key)");
                text = key;
            }
            if (args == null || args.Length == 0) return text;
            try { return string.Format(CultureInfo.InvariantCulture, text, args); } catch (FormatException) { return text; }
        }

        static string Lookup(string folder, string language, string key)
        {
            var file = Path.Combine(folder, "i18n", language + ".json");
            Dictionary<string, string> table;
            if (!loaded.TryGetValue(file, out table))
            {
                table = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
                if (File.Exists(file))
                {
                    try
                    {
                        var d = Json.Parse(File.ReadAllText(file)) as Dictionary<string, object>;
                        if (d != null) foreach (var kv in d) table[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                    }
                    catch (Exception ex) { Log.Write("i18n: " + file + ": " + ex.Message); }
                }
                loaded[file] = table;
            }
            string v;
            return table.TryGetValue(key, out v) ? v : null;
        }
    }

    public static class Mods
    {
        static readonly Dictionary<string, object> apis = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, Hook<Action<object>>> events = new Dictionary<string, Hook<Action<object>>>(StringComparer.OrdinalIgnoreCase);

        // Is that mod loaded (the same as ModLoader.IsLoaded).
        internal static bool IsLoaded(string id) { return ModLoader.IsLoaded(id); }

        // Publishes this mod's API object (call in Load). Others get it by the mod's id.
        public static void Provide(WorksMod mod, object api) { apis[mod.Id] = api; }
        // A mod's code was loaded again (hot reload, HotReload=1 / works.reload): its id. A mod that keeps another's API
        // asks Mods.Api again here (the old object belongs to the old code).
        public static readonly Hook<Action<string>> Reloaded = new Hook<Action<string>>("Mods.Reloaded");
        internal static void Forget(string mod) { apis.Remove(mod); }

        // Another mod's API. T is best a public interface in your own mod listing the members you use (same names and
        // types as the other mod's API object): Works makes an object implementing it that forwards to the API, so
        // neither mod references the other's DLL (as SMAPI's GetApi). T may also be the API's own type, when you
        // reference its DLL. Null if the mod isn't loaded, provides none, or the interface doesn't fit (logged once,
        // naming the member). Call from PostLoad or later; after the other mod reloads, ask again.
        public static T Api<T>(string modId) where T : class
        {
            object o;
            if (modId == null || !apis.TryGetValue(modId, out o) || o == null) return null;
            var direct = o as T;
            if (direct != null || !typeof(T).IsInterface) return direct;
            return ApiProxy.For<T>(o, modId);
        }

        // Calls a public method of another mod's API by name, without referencing its DLL (tModLoader's Mod.Call).
        // Returns its result, or null if the mod, its API or the method is missing.
        public static object Call(string modId, string method, params object[] args)
        {
            object o;
            if (modId == null || method == null || !apis.TryGetValue(modId, out o) || o == null) return null;
            args = args ?? new object[0];
            // The overload whose parameters take these arguments (null fits any reference type; a number fits any number or
            // enum parameter, converted), the closest first.
            var candidates = o.GetType().GetMethods().Where(x => x.Name == method && !x.IsGenericMethodDefinition && x.GetParameters().Length == args.Length).ToList();
            var m = candidates.FirstOrDefault(x => x.GetParameters().Select((p, i) => args[i] == null ? !p.ParameterType.IsValueType || Nullable.GetUnderlyingType(p.ParameterType) != null
                    : p.ParameterType.IsInstanceOfType(args[i])).All(ok => ok))
                ?? candidates.FirstOrDefault(x => x.GetParameters().Select((p, i) => args[i] == null ? !p.ParameterType.IsValueType
                    : p.ParameterType.IsInstanceOfType(args[i]) || Numeric(args[i]) && (Numeric(p.ParameterType) || p.ParameterType.IsEnum)).All(ok => ok));
            if (m != null)
            {
                var ps = m.GetParameters(); args = (object[])args.Clone();
                for (int i = 0; i < args.Length; i++)
                    if (args[i] != null && !ps[i].ParameterType.IsInstanceOfType(args[i]))
                        args[i] = ps[i].ParameterType.IsEnum ? Enum.ToObject(ps[i].ParameterType, Convert.ToInt64(args[i], CultureInfo.InvariantCulture))
                            : Convert.ChangeType(args[i], ps[i].ParameterType, CultureInfo.InvariantCulture);
            }
            if (m == null) { Log.Write("mods: " + modId + " has no " + method + " taking (" + string.Join(", ", args.Select(a => a == null ? "null" : a.GetType().Name).ToArray()) + ")"); return null; }
            try { return m.Invoke(o, args); }
            catch (Exception ex) { var inner = ex is TargetInvocationException && ex.InnerException != null ? ex.InnerException : ex; Log.Write("mods: " + modId + "." + method + " failed: " + inner); return null; }
        }

        static bool Numeric(object v) { return v != null && (Numeric(v.GetType()) || v.GetType().IsEnum); }
        static bool Numeric(Type t) { return t == typeof(int) || t == typeof(long) || t == typeof(float) || t == typeof(double) || t == typeof(short) || t == typeof(byte) || t == typeof(uint) || t == typeof(decimal); }

        // A named event any mod can raise and handle (e.g. "Bounties.Completed"), with one object of data.
        public static Hook<Action<object>> Event(string name)
        {
            Hook<Action<object>> h;
            if (!events.TryGetValue(name, out h)) events[name] = h = new Hook<Action<object>>("event " + name);
            return h;
        }
        public static void Raise(string name, object data = null) { var h = Event(name); if (h.Any) h.Run(x => x(data)); }

        // The mods running now, as "id@version" in load order, for comparing with another game (a save's list, another
        // player's): shared = only those every player of a multiplayer game needs (mod.json "multiplayer": "both").
        public static string[] Running(bool shared = false)
        {
            return ModLoader.Mods.Where(m => m.Loaded && (!shared || !m.ClientOnly)).OrderBy(m => m.LoadIndex).Select(m => m.Id + "@" + m.VersionText).ToArray();
        }

        // How the mods "theirs" (Running's form) differ from "mine", as lines for a player: missing here, only here, other
        // versions. Empty: the same mods and versions (order isn't compared: in co-op the host's game is the one that runs).
        public static string[] Differences(IEnumerable<string> theirs, IEnumerable<string> mine, string theirName = "they have", string myName = "you have")
        {
            Func<IEnumerable<string>, Dictionary<string, string>> parse = l => (l ?? new string[0]).Where(x => !string.IsNullOrEmpty(x))
                .Select(x => { int at = x.LastIndexOf('@'); return at < 0 ? new[] { x, "" } : new[] { x.Substring(0, at), x.Substring(at + 1) }; })
                .GroupBy(p => p[0], StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First()[1], StringComparer.OrdinalIgnoreCase);
            var a = parse(theirs); var b = parse(mine);
            Func<string, string> name = id => { var m = ModLoader.Find(id); return m == null ? id : m.Name; };
            var lines = new List<string>();
            var missing = a.Keys.Where(k => !b.ContainsKey(k)).OrderBy(k => k).ToList();
            var extra = b.Keys.Where(k => !a.ContainsKey(k)).OrderBy(k => k).ToList();
            var other = a.Keys.Where(k => b.ContainsKey(k) && !LoadWarning.SameVersion(a[k], b[k])).OrderBy(k => k).ToList();
            if (missing.Count > 0) lines.Add("only " + theirName + ": " + string.Join(", ", missing.Select(k => name(k) + " " + a[k]).ToArray()));
            if (extra.Count > 0) lines.Add("only " + myName + ": " + string.Join(", ", extra.Select(k => name(k) + " " + b[k]).ToArray()));
            if (other.Count > 0) lines.Add("other versions: " + string.Join(", ", other.Select(k => name(k) + " " + a[k] + " / " + b[k]).ToArray()));
            return lines.ToArray();
        }
    }

    public abstract partial class WorksMod
    {
        // This mod's text in the player's language (i18n/<language>.json).
        public string T(string key, params object[] args) { return Translations.Get(this, key, args); }
        // This mod's structured data for the current game.
        public SaveData SaveData { get { return SaveData.Of(this); } }
        // This mod's data about the player character: saved with the game, carried into exported characters and imports.
        public SaveData CharacterData { get { return SaveData.CharacterOf(this); } }
    }
}

namespace UnderRailWorks.Internal
{
    static class SaveFiles
    {
        internal const string FileName = "UnderRailWorks.json";
        internal static Dictionary<string, object> Current = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        internal static string Folder(string save) { try { return d5x.d(save); } catch { return null; } }

        // Section key -> the version of the mod that wrote it ("versions" in the file).
        internal static Dictionary<string, string> Versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

        internal static void NewGame() { Current = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); Versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }

        internal static void Load(string save)
        {
            Current = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            Versions = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            var dir = Folder(save);
            var file = dir == null ? null : Path.Combine(dir, FileName);
            if (file == null || !File.Exists(file)) return;
            try
            {
                var d = Json.Parse(File.ReadAllText(file)) as Dictionary<string, object>;
                if (d != null && d.ContainsKey("mods") && d["mods"] is Dictionary<string, object>)
                    foreach (var kv in (Dictionary<string, object>)d["mods"]) Current[kv.Key] = kv.Value;
                if (d != null && d.ContainsKey("versions") && d["versions"] is Dictionary<string, object>)
                    foreach (var kv in (Dictionary<string, object>)d["versions"]) Versions[kv.Key] = Convert.ToString(kv.Value, CultureInfo.InvariantCulture);
                double format = d != null && d.ContainsKey("format") ? Convert.ToDouble(d["format"], CultureInfo.InvariantCulture) : 1;
                if (format >= 2) Log.Always("saves: " + save + ": mod data written by a newer UnderRailWorks (format " + format + "); read as well as this version can");
                Log.Write("saves: mod data for " + Current.Count + " mods from " + save);
            }
            catch (Exception ex)
            {
                // Unreadable (cut short by a crash, edited by hand): kept aside, so the next save doesn't quietly replace
                // the only copy of the mods' data.
                string kept = file + ".bad";
                try { if (File.Exists(kept)) File.Delete(kept); File.Move(file, kept); } catch { kept = "(couldn't move it)"; }
                Current.Clear(); Versions.Clear();
                Log.Always("saves: " + file + " can't be read (" + ex.Message + "): the mods' data of this save is missing; the file was kept as " + kept);
                Notify.Log("UnderRailWorks: this save's mod data couldn't be read (details in UnderRailWorks.log).");
            }
        }

        // What a save on disk says about its mods: the mods running when it was saved (null: saved before Works kept that
    // list, or not with Works), and the mods that left data in it (section key -> version).
    internal sealed class SaveMods { internal List<string> Running; internal Dictionary<string, string> WithData = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase); }
    internal static SaveMods Read(string save)
    {
        var r = new SaveMods();
        var dir = Folder(save);
        var file = dir == null ? null : Path.Combine(dir, FileName);
        if (file == null || !File.Exists(file)) return r;
        try
        {
            var d = Json.Parse(File.ReadAllText(file)) as Dictionary<string, object>;
            if (d == null) return r;
            object v;
            if (d.TryGetValue("running", out v) && v is List<object>) r.Running = ((List<object>)v).Where(x => x != null).Select(x => x.ToString()).ToList();
            var versions = d.TryGetValue("versions", out v) ? v as Dictionary<string, object> : null;
            if (d.TryGetValue("mods", out v) && v is Dictionary<string, object>)
                foreach (var key in ((Dictionary<string, object>)v).Keys)
                {
                    var id = key.StartsWith(CharacterFiles.Prefix, StringComparison.Ordinal) ? key.Substring(CharacterFiles.Prefix.Length) : key;
                    object ver = null; if (versions != null) versions.TryGetValue(key, out ver);
                    r.WithData[id] = ver == null ? "" : Convert.ToString(ver, CultureInfo.InvariantCulture);
                }
        }
        catch (Exception ex) { Log.Write("saves: " + file + ": " + ex.Message); }
        return r;
    }

    internal static void Save(string save)
        {
            var dir = Folder(save);
            if (dir == null || !Directory.Exists(dir)) return;
            var file = Path.Combine(dir, FileName);
            try
            {
                var empty = Current.Where(kv => kv.Value is Dictionary<string, object> && ((Dictionary<string, object>)kv.Value).Count == 0).Select(kv => kv.Key).ToList();
                foreach (var k in empty) Current.Remove(k);
                // Written with or without mod data: the mods that were running, for the Load Game warning (LoadWarning).
                // Who wrote each section: installed mods their version now; a missing mod's data keeps the version it had.
                foreach (var key in Current.Keys)
                {
                    var id = key.StartsWith(CharacterFiles.Prefix, StringComparison.Ordinal) ? key.Substring(CharacterFiles.Prefix.Length) : key;
                    var m = ModLoader.Find(id);
                    if (m != null && m.Loaded && m.Version != null) Versions[key] = m.VersionText;
                }
                var versions = Versions.Where(kv => Current.ContainsKey(kv.Key)).ToDictionary(kv => kv.Key, kv => (object)kv.Value);
                var running = Mods.Running().Cast<object>().ToList();
                var root = new Dictionary<string, object> { { "format", 1.0 }, { "works", FixVersion.Value }, { "mods", Current }, { "versions", versions }, { "running", running } };
                // Through a temporary file: a crash mid-write leaves the old file whole, never a cut one.
                var temp = file + ".tmp";
                File.WriteAllText(temp, Json.Write(root));
                if (File.Exists(file)) File.Replace(temp, file, null); else File.Move(temp, file);
            }
            catch (Exception ex) { Log.Write("saves: " + file + ": " + ex.Message); }
        }
    }
}

namespace UnderRailWorks.Internal
{
    // A mod's handler failed: always written to UnderRailWorks.log (whatever Log= says), counted for the Mods tab, and
    // once per mod and session the player is told in the game's log line, so a broken mod doesn't fail silently.
    static class ModErrors
    {
        static readonly HashSet<string> told = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        // The mod whose code a type is (by assembly), for errors in plain delegates; null for Works' own.
        internal static string ModOf(Type t)
        {
            if (t == null) return null;
            var m = ModLoader.Mods.FirstOrDefault(x => x.Instance != null && x.Instance.GetType().Assembly == t.Assembly);
            return m == null ? null : m.Id;
        }
        internal static void Report(string mod, string hook, Exception ex)
        {
            try
            {
                var info = ModLoader.Find(mod);
                if (info != null) { info.Failures++; info.LastFailure = hook + ": " + ex.GetType().Name + ": " + ex.Message; }
                Log.Always("mods: " + mod + ": " + hook + " failed: " + ex.GetType().Name + ": " + ex.Message);
                if (told.Add(mod)) Notify.Log("Mod " + (info == null ? mod : info.Name) + " had an error (details in UnderRailWorks.log; the mod keeps running).");
            }
            catch { }
        }
    }
}

namespace UnderRailWorks.Internal
{
    // Exported characters (My Games/Underrail/Characters/<name>.char: the game's ajk, BinaryFormatter like a save, read
    // by the stock game's import too): written with no mod statuses on them, and the mods' CharacterData beside them in
    // <name>.char.works.json; an import brings that data into the new game.
    static class CharacterFiles
    {
        internal const string Prefix = "character:";
        static readonly FieldInfo CharField = typeof(ajk).GetField("a", BindingFlags.Instance | BindingFlags.Public);     // ajk.a: a field and a method
        static e3q Char(ajk file) { return CharField.GetValue(file) as e3q; }
        static string Side(string charFile) { return charFile + ".works.json"; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<e3q, Dictionary<string, object>> imported = new System.Runtime.CompilerServices.ConditionalWeakTable<e3q, Dictionary<string, object>>();
        static Dictionary<string, object> pending; static string pendingName;

        // ajk.b(path): the export is being written.
        static void Writing(ajk __instance, string __0)
        {
            try { if (GameEvents.CharacterExporting.Any) GameEvents.CharacterExporting.Run(h => h(__0)); } catch (Exception ex) { Log.Write("export: " + ex.Message); }
            try { ModStatusStore.Strip(); } catch (Exception ex) { Log.Write("export: statuses: " + ex.Message); }
        }
        static Exception Written(string __0, Exception __exception)
        {
            try { ModStatusStore.PutBack(); } catch (Exception ex) { Log.Write("export: statuses: " + ex.Message); }
            if (__exception != null) return __exception;
            try
            {
                var data = SaveFiles.Current.Where(kv => kv.Key.StartsWith(Prefix, StringComparison.Ordinal) && kv.Value is Dictionary<string, object> && ((Dictionary<string, object>)kv.Value).Count > 0)
                    .ToDictionary(kv => kv.Key.Substring(Prefix.Length), kv => kv.Value);
                var side = Side(__0);
                if (data.Count == 0) { if (File.Exists(side)) File.Delete(side); }
                else File.WriteAllText(side, Json.Write(new Dictionary<string, object> { { "format", 1.0 }, { "works", FixVersion.Value }, { "character", data } }));
                Log.Write("export: " + Path.GetFileName(__0) + (data.Count == 0 ? "" : ", character data of " + data.Count + " mods"));
            }
            catch (Exception ex) { Log.Write("export: " + ex.Message); }
            return null;
        }

        // ajk.a(path): an exported character read (the import dialog's preview and choice).
        static void Read(string __0, ajk __result)
        {
            if (__result == null || Char(__result) == null) return;
            try
            {
                var side = Side(__0);
                if (!File.Exists(side)) return;
                var d = Json.Parse(File.ReadAllText(side)) as Dictionary<string, object>;
                var ch = d == null ? null : d.ContainsKey("character") ? d["character"] as Dictionary<string, object> : null;
                if (ch == null) return;
                imported.Remove(Char(__result));
                imported.Add(Char(__result), ch);
            }
            catch (Exception ex) { Log.Write("import: " + Path.GetFileName(__0) + ": " + ex.Message); }
        }

        // iw.a(object, el5): the creation screen took an imported character (k = the file's e3q).
        static void Chosen(object __0)
        {
            try
            {
                var g = __0 == null ? null : __0.GetType().GetField("g", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)?.GetValue(__0) as ajk;
                if (g == null) return;     // the dialog cancelled: the screen keeps the character it has (and its data)
                pending = null; pendingName = null;
                Dictionary<string, object> ch;
                var c = g == null ? null : Char(g);
                Clear();     // a character chosen before (or none): its data out
                if (c != null && imported.TryGetValue(c, out ch)) { pending = ch; pendingName = c.Name; Apply(ch); Log.Write("import: " + c.Name + ", character data of " + ch.Count + " mods"); }
            }
            catch (Exception ex) { Log.Write("import: " + ex.Message); }
        }

        // The game makes its new-game state before the creation screen (so before an import is chosen): the data goes in
        // when the character is chosen, again if a new game's state is made after that, and CharacterImported fires
        // when that game's first zone has loaded.
        static void Apply(Dictionary<string, object> ch) { foreach (var kv in ch) SaveFiles.Current[Prefix + kv.Key] = Json.Parse(Json.Write(kv.Value, false)); }
        static void Clear() { foreach (var k in SaveFiles.Current.Keys.Where(k => k.StartsWith(Prefix, StringComparison.Ordinal)).ToList()) SaveFiles.Current.Remove(k); }
        internal static void NewGame() { if (pending != null) Apply(pending); }
        internal static void Forget() { pending = null; pendingName = null; }     // a save loaded instead
        // The creation screen was cancelled (back to the main menu): an imported character's data goes with it.
        internal static void CreationCancelled() { if (pending != null) Log.Write("import: cancelled, " + pendingName + "'s data dropped"); Forget(); Clear(); }
        internal static void ZoneReady()
        {
            var ch = pending; pending = null;
            if (ch == null) return;
            Log.Write("import: " + pendingName + " started with its character data");
            if (GameEvents.CharacterImported.Any) GameEvents.CharacterImported.Run(h => h());
        }

        // The import dialog's delete, confirmed (e7u.b.g0 "Y": m_b the .char path): the side file goes with it.
        static readonly Type DeleteConfirm = typeof(e7u).GetNestedType("b", BindingFlags.NonPublic);
        static readonly FieldInfo DeletePath = DeleteConfirm == null ? null : DeleteConfirm.GetField("m_b", BindingFlags.Instance | BindingFlags.NonPublic) ?? DeleteConfirm.GetField("b", BindingFlags.Instance | BindingFlags.NonPublic);
        static void Deleted(object __instance, string A_0)
        {
            if (A_0 != "Y" || DeletePath == null) return;
            try
            {
                var path = DeletePath.GetValue(__instance) as string;
                if (path != null && !File.Exists(path) && File.Exists(Side(path))) { File.Delete(Side(path)); Log.Write("export: deleted " + Path.GetFileName(Side(path)) + " with its character"); }
            }
            catch (Exception ex) { Log.Write("export: " + ex.Message); }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(CharacterFiles).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(ajk).GetMethod("b", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null), prefix: m("Writing"), finalizer: m("Written"));
            harmony.Patch(typeof(ajk).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null), postfix: m("Read"));
            harmony.Patch(typeof(iw).GetMethods(any).First(x => x.Name == "a" && x.GetParameters().Length == 2 && x.GetParameters()[1].ParameterType == typeof(el5)), postfix: m("Chosen"));
            if (DeleteConfirm != null) harmony.Patch(DeleteConfirm.GetMethod("g0", BindingFlags.Instance | BindingFlags.Public), postfix: m("Deleted"));
            else Log.Write("export: the delete confirmation isn't where expected; side files stay after a delete");
        }
    }
}
