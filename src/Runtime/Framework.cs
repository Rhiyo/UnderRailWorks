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
using System.Linq;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class SaveData
    {
        readonly string mod;
        SaveData(string mod) { this.mod = mod; }
        static readonly Dictionary<string, SaveData> stores = new Dictionary<string, SaveData>(StringComparer.OrdinalIgnoreCase);
        public static SaveData Of(WorksMod mod) { SaveData s; if (!stores.TryGetValue(mod.Id, out s)) stores[mod.Id] = s = new SaveData(mod.Id); return s; }

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
            if (value != null && !(value is string || value is bool || value is double || value is float || value is int || value is long || value is System.Collections.IEnumerable))
                throw new ArgumentException("save data holds strings, bools, numbers, lists and dictionaries, not " + value.GetType().Name);
            // Kept in the shape JSON reads it back in (lists as List<object>, numbers as double), so Get answers the same
            // before and after a save is loaded (a typed list read as List<object> came back null in the same session).
            if (value is int || value is long || value is float) value = Convert.ToDouble(value, CultureInfo.InvariantCulture);
            else if (value is System.Collections.IEnumerable && !(value is string)) value = Json.Parse(Json.Write(value, false));
            Section[key] = value;
        }
        public bool Has(string key) { return Section.ContainsKey(key); }
        public void Remove(string key) { Section.Remove(key); }
        public IList<string> Keys { get { return Section.Keys.ToList(); } }
        // Everything, e.g. to migrate an older layout (keep a "version" key of your own).
        public void Clear() { Section.Clear(); }
    }

    public static class Translations
    {
        // The game's language: "english", or its Localization folder name (e.g. "srpski").
        public static string Language { get { var l = ActiveField.GetValue(null) as evo; return l == null || string.IsNullOrEmpty(l.a) ? "english" : l.a.ToLowerInvariant(); } }
        static readonly System.Reflection.FieldInfo ActiveField = typeof(aji).GetField("a");     // aji.a: the active localization (a field/method name clash)

        static readonly Dictionary<string, Dictionary<string, string>> loaded = new Dictionary<string, Dictionary<string, string>>(StringComparer.OrdinalIgnoreCase);
        internal static void Forget() { loaded.Clear(); }
        static readonly HashSet<string> warned = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        public static string Get(WorksMod mod, string key, params object[] args)
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

        public static bool IsLoaded(string id) { return ModLoader.IsLoaded(id); }

        // Publishes this mod's API object (call in Load). Others get it by the mod's id.
        public static void Provide(WorksMod mod, object api) { apis[mod.Id] = api; }

        // Another mod's API as an interface or class both mods compile against (a small shared DLL, or the providing
        // mod's own DLL as a reference). Null if the mod isn't loaded or provides none (call from PostLoad or later).
        public static T Api<T>(string modId) where T : class { object o; return apis.TryGetValue(modId, out o) ? o as T : null; }

        // Calls a public method of another mod's API by name, without referencing its DLL (tModLoader's Mod.Call).
        // Returns its result, or null if the mod, its API or the method is missing.
        public static object Call(string modId, string method, params object[] args)
        {
            object o;
            if (!apis.TryGetValue(modId, out o) || o == null) return null;
            args = args ?? new object[0];
            var m = o.GetType().GetMethods().FirstOrDefault(x => x.Name == method && x.GetParameters().Length == args.Length);
            if (m == null) return null;
            try { return m.Invoke(o, args); }
            catch (System.Reflection.TargetInvocationException ex) { Log.Write("mods: " + modId + "." + method + " failed: " + ex.InnerException); return null; }
        }

        // A named event any mod can raise and handle (e.g. "Bounties.Completed"), with one object of data.
        public static Hook<Action<object>> Event(string name)
        {
            Hook<Action<object>> h;
            if (!events.TryGetValue(name, out h)) events[name] = h = new Hook<Action<object>>("event " + name);
            return h;
        }
        public static void Raise(string name, object data = null) { var h = Event(name); if (h.Any) h.Run(x => x(data)); }
    }

    public abstract partial class WorksMod
    {
        // This mod's text in the player's language (i18n/<language>.json).
        public string T(string key, params object[] args) { return Translations.Get(this, key, args); }
        // This mod's structured data for the current game.
        public SaveData SaveData { get { return SaveData.Of(this); } }
    }
}

namespace UnderRailWorks.Internal
{
    static class SaveFiles
    {
        internal const string FileName = "UnderRailWorks.json";
        internal static Dictionary<string, object> Current = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);

        static string Folder(string save) { try { return d5x.d(save); } catch { return null; } }

        internal static void NewGame() { Current = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase); }

        internal static void Load(string save)
        {
            Current = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
            var dir = Folder(save);
            var file = dir == null ? null : Path.Combine(dir, FileName);
            if (file == null || !File.Exists(file)) return;
            try
            {
                var d = Json.Parse(File.ReadAllText(file)) as Dictionary<string, object>;
                if (d != null && d.ContainsKey("mods") && d["mods"] is Dictionary<string, object>)
                    foreach (var kv in (Dictionary<string, object>)d["mods"]) Current[kv.Key] = kv.Value;
                Log.Write("saves: mod data for " + Current.Count + " mods from " + save);
            }
            catch (Exception ex) { Log.Write("saves: " + file + ": " + ex.Message); }
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
                if (Current.Count == 0) { if (File.Exists(file)) File.Delete(file); return; }
                var root = new Dictionary<string, object> { { "format", 1.0 }, { "works", FixVersion.Value }, { "mods", Current } };
                File.WriteAllText(file, Json.Write(root));
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
