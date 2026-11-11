// UnderRailWorks: commands in the game's developer console (the ` key in game; SMAPI and tModLoader have their own
// consoles, UnderRail has one built in). The game's side: the console (ci3.k() opens j9) runs a line with bdg.c(line),
// which looks the first word up in its command table; Works answers its own and the mods' commands first, in a prefix,
// and prints with the console's bdg.a(format) (markup: [cmd] [val] [param] [war] [error]).
//   Commands.Add(this, "heal", "heal [amount]: heals the player", args => { ...; return "healed"; });
// Works' own: works (lists mods' commands), works.give <item path> [count], works.spawn <blueprint> [hostile],
// works.var <name> [value], works.mods, works.content (writes UnderRailWorks-content.txt), works.dialog <name>.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static class Commands
    {
        sealed class Command { public string Mod, Name, Help; public Func<string[], string> Run; }
        static readonly Dictionary<string, Command> all = new Dictionary<string, Command>(StringComparer.OrdinalIgnoreCase);

        // A console command: run gets the words after the name (quotes keep spaces) and returns a line to print (or
        // null). Name it after the mod to avoid clashes ("mymod.heal"); the game's own commands win otherwise.
        public static void Add(WorksMod mod, string name, string help, Func<string[], string> run) { Add(mod.Id, name, help, run); }
        internal static void Add(string mod, string name, string help, Func<string[], string> run) { all[name] = new Command { Mod = mod, Name = name, Help = help, Run = run }; }
        internal static void Forget(string mod) { foreach (var k in all.Where(kv => kv.Value.Mod == mod).Select(kv => kv.Key).ToList()) all.Remove(k); }

        // Opens the game's console (as its ` key does; in game only).
        public static void OpenConsole() { var hud = Hud.Current; if (hud != null) hud.k(); }
        public static void CloseConsole() { var hud = Hud.Current; if (hud != null) CloseField.Invoke(hud, null); }
        static readonly MethodInfo CloseField = typeof(ci3).GetMethod("j", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);

        // Prints a line in the console.
        public static void Print(string text) { try { bdg.a("{0}", text); } catch { } }

        // Runs a console line from code: a mod's or Works' command, else the game's own (e.g. "playerGiveXp 100").
        public static void Execute(string line)
        {
            if (Run(line)) return;
            try { var c = typeof(bdg).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).First(m => m.Name == "c" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string)); c.Invoke(null, new object[] { line }); }
            catch (Exception ex) { Log.Write("console: " + line + ": " + ex.Message); }
        }

        internal static bool Run(string line)
        {
            var words = Split(line);
            Command c;
            if (words.Count == 0 || !all.TryGetValue(words[0], out c)) return false;
            var was = ModScope.Current; ModScope.Current = c.Mod;
            try { var r = c.Run(words.Skip(1).ToArray()); if (r != null) Print(r); }
            catch (Exception ex) { Print("[error]" + c.Name + " failed: " + ex.Message); ModErrors.Report(c.Mod, "command " + c.Name, ex); }
            finally { ModScope.Current = was; }
            return true;
        }

        static List<string> Split(string line)
        {
            var r = new List<string>(); var cur = new System.Text.StringBuilder(); bool q = false, any = false;
            foreach (var ch in line ?? "")
            {
                if (ch == '"') { q = !q; any = true; continue; }
                if (char.IsWhiteSpace(ch) && !q) { if (any) r.Add(cur.ToString()); cur.Clear(); any = false; continue; }
                cur.Append(ch); any = true;
            }
            if (any) r.Add(cur.ToString());
            return r;
        }

        internal static void Builtins()
        {
            Add("UnderRailWorks", "works", "works: the commands mods added", a => string.Join("\n", all.Values.OrderBy(x => x.Name).Select(x => "[cmd]" + x.Name + "[/cmd]  " + x.Help + "  (" + x.Mod + ")")));
            Add("UnderRailWorks", "works.give", "works.give <item path> [count]: into the player's bag", a =>
            {
                if (a.Length == 0) return "[error]works.give <item path> [count]";
                int n = a.Length > 1 ? int.Parse(a[1]) : 1;
                return World.Player.Inventory.Add(a[0], n) ? "gave " + n + " " + (Items.Name(a[0]) ?? a[0]) : "[error]no item " + a[0];
            });
            Add("UnderRailWorks", "works.spawn", "works.spawn <blueprint> [hostile]: next to the player (gone when you leave)", a =>
            {
                if (a.Length == 0) return "[error]works.spawn <blueprint> [hostile]";
                var p = World.Player.Position;
                var t = World.FreeTilesNear((int)p.X, (int)p.Y, 4).Skip(2).FirstOrDefault();
                var c = World.Spawn(a[0], t.X, t.Y, a.Length > 1 && a[1].StartsWith("h", StringComparison.OrdinalIgnoreCase));
                return c == null ? "[error]couldn't spawn " + a[0] : "spawned " + c.Name + " at " + t.X + "," + t.Y;
            });
            Add("UnderRailWorks", "works.var", "works.var <name> [value]: read or write a global variable (saved)", a =>
            {
                if (a.Length == 0) return "[error]works.var <name> [value]";
                if (a.Length > 1) { int i; bool b; if (int.TryParse(a[1], out i)) Variables.Global.Set(a[0], i); else if (bool.TryParse(a[1], out b)) Variables.Global.Set(a[0], b); else Variables.Global.Set(a[0], a[1]); }
                return a[0] + " = " + (Variables.Global.Get<object>(a[0], null) ?? "(unset)");
            });
            Add("UnderRailWorks", "works.mods", "works.mods: the mods and their state", a => string.Join("\n", ModLoader.Mods.Select(m => m.Name + " " + m.VersionText + ": " + (m.Loaded ? "loaded" : m.Enabled ? "not loaded (" + m.Error + ")" : "off") + (m.Failures > 0 ? ", " + m.Failures + " errors" : ""))));
            Add("UnderRailWorks", "works.body", "works.body <name>: a character's footprint and size (debugging)", a =>
            {
                var c = World.Player == null || a.Length == 0 ? null : World.CharactersNear(World.Player.Position, 100).FirstOrDefault(x => x.Name != null && x.Name.IndexOf(a[0], StringComparison.OrdinalIgnoreCase) >= 0);
                if (c == null) return "nobody";
                var e = c.Entity; var z = typeof(b0b).GetField("z").GetValue(e);
                var line = c.Name + ": footprint " + (z == null ? "none" : z.ToString()) + ", size " + e.Size + ", sheet " + c.HasPlayerSheet + ", state " + typeof(b0b).GetField("j").GetValue(e) + ", in combat " + c.IsInCombat;
                Log.Write(line); return line;
            });
            Add("UnderRailWorks", "works.export", "works.export: export the player character (for a new game's Import)", a => World.ExportPlayer() ? "exported" : "[error]no game");
            Add("UnderRailWorks", "works.perf", "works.perf [seconds]: time each mod's handlers for a while (default 10 s), then list the busiest (also in the log)", a =>
            {
                double s = 10; if (a.Length > 0) double.TryParse(a[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out s);
                Profiler.Start(Math.Max(1, s));
                return "profiling for " + s + " s";
            });
            Add("UnderRailWorks", "works.reload", "works.reload: re-reads mods' pictures, texture packs, translations and settings; works.reload <mod id>: also loads its code again; works.reload code: every mod whose DLL changed", a =>
            {
                if (a.Length > 0 && a[0].Equals("code", StringComparison.OrdinalIgnoreCase)) { int c = ModReloader.RequestChanged(); return c == 0 ? "no mod's DLL changed" : c + " mod(s) to reload"; }
                if (a.Length > 0) { ModReloader.Request(a[0]); return null; }     // on the next update, outside the console's command
                AssetStore.ReloadFiles();
                Translations.Forget();
                int n = 0;
                foreach (var s in ModSettings.Every) s.ReadAgain();     // the files first: Reload reads from these
                foreach (var c in ModConfig.All) { c.Reload(); n++; }
                return "reloaded pictures and translations, settings of " + n + " mods";
            });
            Add("UnderRailWorks", "works.patches", "works.patches [owner]: Harmony patches on the game by owner (Works, each mod), or one owner's patched methods", a =>
            {
                var rows = new List<KeyValuePair<string, string>>();
                foreach (var m in HarmonyLib.Harmony.GetAllPatchedMethods())
                {
                    var info = HarmonyLib.Harmony.GetPatchInfo(m);
                    if (info == null) continue;
                    foreach (var p in info.Prefixes.Concat(info.Postfixes).Concat(info.Transpilers).Concat(info.Finalizers))
                        rows.Add(new KeyValuePair<string, string>(p.owner, m.DeclaringType.Name + "." + m.Name));
                }
                string text = a.Length > 0
                    ? string.Join(", ", rows.Where(r => r.Key.EndsWith(a[0], StringComparison.OrdinalIgnoreCase)).Select(r => r.Value).ToArray())
                    : string.Join(", ", rows.GroupBy(r => r.Key).Select(g => g.Key + " " + g.Count()).ToArray());
                Log.Write("patches: " + text);
                return text.Length == 0 ? "none" : text;
            });
            Add("UnderRailWorks", "works.dialog", "works.dialog <name>: writes an outline of a game dialog (lines, conditions, effects), e.g. works.dialog characters\\cc_jyles", a => a.Length == 0 ? "which dialog?" : "written: " + (Dialogs.WriteDescription(a[0]) ?? "no such dialog"));
            Add("UnderRailWorks", "works.split", "works.split: the game's own split-stack popup (for comparing Ui.AskNumber)", a => { CloseConsole(); UiPopups.RawSplit(10); return "split"; });
            Add("UnderRailWorks", "works.content", "works.content: writes UnderRailWorks-content.txt (item paths, blueprints, stores...)", a => "written: " + ContentIndex.Write());
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class CommandHooks
    {
        static bool Line(string __0) { try { return !Commands.Run(__0); } catch { return true; } }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Commands.Builtins();
            var run = typeof(bdg).GetMethods(BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic).First(m => m.Name == "c" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(string));
            harmony.Patch(run, prefix: new HarmonyLib.HarmonyMethod(typeof(CommandHooks).GetMethod("Line", BindingFlags.Static | BindingFlags.NonPublic)));
        }
    }
}
