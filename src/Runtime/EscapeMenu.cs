// UnderRailWorks: the game's Escape menu (in a game: Resume, Save, Load, Export Character, Options, Quit) gets a
// "Mods" row, which opens Works' Mods window over it: the mods' actions for the game being played.
// Where things go:
//   Main menu -> Mods          which mods are on
//   Options -> Works           each mod's settings
//   Escape menu -> Mods        things to do in the game being played (open a mod's window, start a mode)
//   EscapeMenu.AddModAction(this, "Co-op", OpenWindow, "Players, Give, Leave", () => Hosting || Joined);
// Call it once, in Load. The last argument says when the action applies (null: always in a game); it's asked each time
// the window opens, and only those that apply are listed. Mods know nothing of each other's actions: Works lists them
// all, in the mods' load order. Clicking one closes the menu and runs it. The game's side: the menu is cef (a
// LocaleSubInterfaceController e26 opens it); its rows are cef.a(slot, text), slots 0-4 and 6 used, 5 free (the menu is
// sized for 7). The game's Options opens the same way, over the menu on the in-game screen.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static class EscapeMenu
    {
        public sealed class ModAction
        {
            internal string Mod; internal Action Click; internal Func<bool> When; internal int Order;
            public string Text { get; set; }
            public string Tooltip { get; set; }
            internal ModAction() { }
        }
        static readonly List<ModAction> actions = new List<ModAction>();

        public static ModAction AddModAction(WorksMod mod, string text, Action onClick, string tooltip = null, Func<bool> availableWhen = null)
        {
            if (mod == null) throw new ArgumentNullException("mod");
            var a = new ModAction { Mod = mod.Id, Text = text, Tooltip = tooltip, Click = onClick, When = availableWhen };
            lock (actions) { actions.RemoveAll(x => x.Mod == mod.Id && x.Text == text); a.Order = actions.Count; actions.Add(a); }
            return a;
        }
        internal static void Forget(string mod) { lock (actions) actions.RemoveAll(x => x.Mod == mod); }

        // The actions that apply now, in load order.
        internal static List<ModAction> Available()
        {
            List<ModAction> all;
            lock (actions) all = actions.ToList();
            return all.Where(a =>
            {
                if (a.When == null) return true;
                try { bool ok = false; ModScope.As(a.Mod, a.When, () => ok = a.When()); return ok; }
                catch (Exception ex) { ModErrors.Report(a.Mod, "its Escape menu action's condition", ex); return false; }
            }).OrderBy(a => { var m = ModLoader.Find(a.Mod); return m == null ? 0 : m.LoadIndex; }).ThenBy(a => a.Order).ToList();
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class EscapeMenuHooks
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly MethodInfo Row = typeof(cef).GetMethod("a", Any, null, new[] { typeof(int), typeof(string) }, null);
        const int FreeSlot = 5;

        // After the menu built its rows: "Mods" in the free slot (greyed when no action applies).
        static void Built(cef __instance)
        {
            try
            {
                if (Row == null) return;
                var row = (zt)Row.Invoke(__instance, new object[] { FreeSlot, "Mods" });
                var menu = __instance;
                row.Enabled = EscapeMenu.Available().Count > 0;
                row.c(new EventHandler<b25>((s, e) => Open(menu)));
            }
            catch (Exception ex) { Log.Write("escape menu: " + ex.Message); }
        }

        static UiWindow window;
        static void Open(cef menu)
        {
            if (window != null && window.IsOpen) return;
            var list = EscapeMenu.Available();
            int pad = Ui.S(12), gap = Ui.S(6), w = Ui.S(320), inner = w - 2 * pad;
            var rows = new List<UiElement>();
            foreach (var a in list)
            {
                var action = a;
                var b = Ui.Button(a.Text, () =>
                {
                    if (window != null) window.Close();
                    try { menu.z(); } catch (Exception ex) { Log.Write("escape menu: closing: " + ex.Message); }
                    try { ModScope.As(action.Mod, action.Click, action.Click); } catch (Exception ex) { ModErrors.Report(action.Mod, "its Escape menu action", ex); }
                });
                var mod = ModLoader.Find(a.Mod);
                Ui.Tooltip(b, (string.IsNullOrEmpty(a.Tooltip) ? "" : a.Tooltip + "\n") + "From " + (mod == null ? a.Mod : mod.Name));
                Ui.Resize(b, inner, b.Height);
                rows.Add(b);
            }
            var back = Ui.Button("Back", () => { if (window != null) window.Close(); });
            int bh = back.Height, h = rows.Sum(r => r.Height + gap) + (rows.Count == 0 ? Ui.LineHeight + gap : 0) + Ui.S(10) + bh;
            window = Ui.OpenWindow(null, "escmods", "Mods", w, 22 + Ui.S(4) + pad + h + pad, true);
            if (window == null) return;
            int y = window.Top + Ui.S(4) + pad;
            if (rows.Count == 0) { Ui.Add(window, Ui.Label("Nothing from your mods for this game.", inner), pad, y); }
            foreach (var r in rows) { Ui.Add(window, r, pad, y); y += r.Height + gap; }
            Ui.Add(window, back, window.Width - pad - back.Width, window.Height - pad - bh);
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var build = typeof(cef).GetMethod("a", Any, null, Type.EmptyTypes, null);
            if (build == null || Row == null) { Log.Write("escape menu: its rows weren't found: no Mods row"); return; }
            harmony.Patch(build, postfix: new HarmonyLib.HarmonyMethod(typeof(EscapeMenuHooks).GetMethod("Built", BindingFlags.Static | BindingFlags.NonPublic)));
        }
    }
}
