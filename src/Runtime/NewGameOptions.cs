// UnderRailWorks: options on the game's own new-game screen (Game Rules: c9v, shown between New Game and character
// creation; the new game already exists then: StartNewGame = create (GameEvents.NewGame), this screen, character
// creation), in a "Mods" tab beside the game's Rules and Custom tabs, built with the screen's own row builder (the
// slider rows of its Custom tab). What the player sets is kept with the new game (the mod's SaveData, "newgame.<key>")
// and read back with Get; a save from before the option existed gives the fallback. Not changeable later: as the
// game's own difficulty.
//   NewGameOptions.AddChoice(this, "challenge", "Challenge", new[] { "Off", "Tough", "Brutal" }, 0);
//   NewGameOptions.AddSlider(this, "taken", "Damage you take", 100, 100, 300, 10, percent: true);
//   NewGameOptions.Applied.Add(this, () => Use(NewGameOptions.Get(this, "challenge", 0)));   // the player pressed Accept
//   GameEvents.GameLoaded.Add(this, () => Use(NewGameOptions.Get(this, "challenge", 0)));    // a saved game
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static class NewGameOptions
    {
        internal sealed class Option
        {
            public WorksMod Mod; public string Key, Label; public int Value, Min, Max, Step; public bool Percent; public string[] Names;
        }
        internal static readonly List<Option> Options = new List<Option>();
        internal static readonly Dictionary<Option, oz> Shown = new Dictionary<Option, oz>();

        // A slider from min to max (step: its small step), shown as a number or a percentage.
        public static void AddSlider(WorksMod mod, string key, string label, int value, int min, int max, int step = 1, bool percent = false)
        {
            lock (Options) { Options.RemoveAll(o => o.Mod == mod && o.Key == key); Options.Add(new Option { Mod = mod, Key = key, Label = label, Value = value, Min = min, Max = max, Step = Math.Max(1, step), Percent = percent }); }
        }

        // A choice between names (a slider over them, showing the chosen name). Its value is the index.
        public static void AddChoice(WorksMod mod, string key, string label, string[] names, int value = 0)
        {
            if (names == null || names.Length < 2) throw new ArgumentException("a choice needs two or more names");
            lock (Options) { Options.RemoveAll(o => o.Mod == mod && o.Key == key); Options.Add(new Option { Mod = mod, Key = key, Label = label, Value = value, Min = 0, Max = names.Length - 1, Step = 1, Names = names }); }
        }

        // The value chosen for the current game (fallback: a game started without it, or no game).
        public static int Get(WorksMod mod, string key, int fallback)
        {
            var d = SaveData.Of(mod);
            return d.Has("newgame." + key) ? d.Get("newgame." + key, fallback) : fallback;
        }
        public static bool Chosen(WorksMod mod, string key) { return SaveData.Of(mod).Has("newgame." + key); }

        // The player accepted the new-game screen: the values are in each mod's data (Get) for the new game.
        public static readonly Hook<Action> Applied = new Hook<Action>("NewGameOptions.Applied");

        // Accept on the new-game screen: the values shown go into each mod's data for the game being created.
        internal static void Accepted()
        {
            List<KeyValuePair<Option, int>> values;
            lock (Options) values = Shown.Select(kv => new KeyValuePair<Option, int>(kv.Key, kv.Value.l())).ToList();
            Shown.Clear();
            foreach (var kv in values)
            {
                SaveData.Of(kv.Key.Mod).Set("newgame." + kv.Key.Key, kv.Value);
                Log.Write("new game: " + kv.Key.Mod.Id + " " + kv.Key.Key + " = " + kv.Value);
            }
            if (values.Count > 0 && Applied.Any) Applied.Run(h => h());
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class NewGameScreenHooks
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        static FieldInfo F(string name) { return typeof(c9v).GetField(name, Any); }
        // The row builder's state: the tab control (b), the page rows go on (aj), the y of the next row (ai), the column
        // (ah), dropdowns to add last (ak), the first row's y (aa: a field that shares its name with a method).
        static readonly FieldInfo Tabs = F("b"), Page = F("aj"), Y = F("ai"), Column = F("ah"), Dropdowns = F("ak"), FirstY = F("aa");
        static readonly MethodInfo Slider = typeof(c9v).GetMethods(Any).FirstOrDefault(m => m.Name == "a" && m.ReturnType == typeof(oz) && m.GetParameters().Length == 11);
        static readonly MethodInfo Hover = typeof(c9v).GetMethod("s", Any, null, new[] { typeof(object), typeof(b25) }, null);

        // End of c9v's constructor: after its Rules and Custom tabs, a Mods tab.
        static void Built(c9v __instance)
        {
            List<NewGameOptions.Option> options;
            lock (NewGameOptions.Options) options = NewGameOptions.Options.ToList();
            NewGameOptions.Shown.Clear();
            if (options.Count == 0) return;
            try
            {
                var tabs = Tabs.GetValue(__instance);
                var page = new cvh();
                page.Text = "Mods";
                if (Hover != null) { var h = (EventHandler<b25>)Delegate.CreateDelegate(typeof(EventHandler<b25>), __instance, Hover); page.j(h); page.g(h); }
                var pages = tabs.GetType().GetMethod("n", Type.EmptyTypes).Invoke(tabs, null) as System.Collections.IList;
                pages.Add(page);
                Column.SetValue(__instance, 0);
                Y.SetValue(__instance, FirstY.GetValue(__instance));
                Dropdowns.SetValue(__instance, new List<aw9>());
                Page.SetValue(__instance, page);
                foreach (var o in options)
                {
                    Func<int, string> show = null;
                    if (o.Names != null) { var names = o.Names; show = v => names[Math.Max(0, Math.Min(names.Length - 1, v))]; }
                    var slider = (oz)Slider.Invoke(__instance, new object[] { o.Mod.Id + "." + o.Key, o.Label, o.Value, o.Min, o.Max, o.Step, o.Step * 5, o.Percent, null, show, true });
                    NewGameOptions.Shown[o] = slider;
                }
                Log.Write("new game: a Mods tab with " + options.Count + " option(s)");
            }
            catch (Exception ex) { Log.Write("new game: the Mods tab failed: " + ex); }
        }

        // c9v.a(bool): Accept (it reads the difficulty and experience system and goes on to character creation).
        static void Accepted() { try { NewGameOptions.Accepted(); } catch (Exception ex) { Log.Write("new game: " + ex.Message); } }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(typeof(c9v).GetMethod("a", Any, null, new[] { typeof(bool) }, null), postfix: new HarmonyLib.HarmonyMethod(typeof(NewGameScreenHooks).GetMethod("Accepted", BindingFlags.Static | BindingFlags.NonPublic)));
            harmony.Patch(typeof(c9v).GetConstructor(new[] { typeof(bool), typeof(bool) }), postfix: new HarmonyLib.HarmonyMethod(typeof(NewGameScreenHooks).GetMethod("Built", BindingFlags.Static | BindingFlags.NonPublic)));
        }
    }
}
