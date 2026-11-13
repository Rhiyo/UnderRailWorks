// UnderRailWorks: buttons mods add to the main menu's column (New Game, Resume, Load Game, Options, Credits, Exit),
// in the game's own button style, above Exit, in the order mods add them:
//   MainMenu.AddButton(this, "Join a game", () => ...);
// Call it from Load: the game builds the main menu anew each time it shows it, and the buttons are added each time.
// The game's side: a8k.c() builds the column (an ec9 panel) with one zt per entry, 1.5 button heights apart (55 px in
// the Classic interface), the panel's bottom fixed above the screen's; a8k.a(zt) styles a button for the menu theme.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static partial class MainMenu
    {
        sealed class Entry { public string Mod, Text; public Action Click; }
        static readonly List<Entry> entries = new List<Entry>();

        public static void AddButton(WorksMod mod, string text, Action onClick)
        {
            // Once per mod and text (a mod adding it from MainMenuCreated would otherwise get one more each visit).
            lock (entries) { entries.RemoveAll(e => e.Mod == mod.Id && e.Text == text); entries.Add(new Entry { Mod = mod.Id, Text = text, Click = onClick }); }
        }

        internal static void Forget(string mod) { lock (entries) entries.RemoveAll(e => e.Mod == mod); }

        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        static readonly MethodInfo Style = typeof(a8k).GetMethod("a", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(zt) }, null);
        static readonly FieldInfo[] Colours = new[] { "b", "c", "h" }.Select(n => typeof(zt).GetField(n, Fields)).Where(f => f != null).ToArray();
        static readonly PropertyInfo Alignment = typeof(zt).GetProperty("VerticalAlignment");

        // After a8k.c(): the mods' buttons go where Exit was, Exit below them, the panel grows upwards.
        internal static void Built(a8k menu)
        {
            List<Entry> list;
            lock (entries) list = entries.ToList();
            if (list.Count == 0) return;
            try
            {
                var buttons = typeof(a8k).GetFields(Fields).Where(f => f.FieldType == typeof(zt)).Select(f => (zt)f.GetValue(menu)).Where(b => b != null).ToList();
                if (buttons.Count < 2) { Log.Write("main menu: its buttons weren't found; mods' buttons not added"); return; }
                zt exit = buttons[buttons.Count - 1], before = buttons[buttons.Count - 2];
                var panel = exit.Parent;
                if (panel == null) return;
                int exitY = UnderrailDropdownScroll.Y(UnderrailDropdownScroll.LocationOf(exit));
                int step = exitY - UnderrailDropdownScroll.Y(UnderrailDropdownScroll.LocationOf(before));
                // The panel grows upwards: never past the top of the screen (many mods at 1280x720): closer rows instead,
                // but never closer than a button's height.
                int room = UnderrailDropdownScroll.Y(UnderrailDropdownScroll.LocationOf(panel));
                if (list.Count * step > room) step = Math.Max(UnderrailDropdownScroll.H(exit.Size) + 1, room / Math.Max(1, list.Count));
                int y = exitY;
                foreach (var e in list)
                {
                    var b = new zt();
                    if (Style != null) Style.Invoke(menu, new object[] { b });
                    foreach (var f in Colours) f.SetValue(b, f.GetValue(exit));
                    b.HorizontalAlignment = exit.HorizontalAlignment;
                    if (Alignment != null) Alignment.SetValue(b, Alignment.GetValue(exit, null), null);
                    b.Text = e.Text;
                    b.Size = exit.Size;
                    e4z.j(b);
                    var click = e.Click; var mod = e.Mod;
                    b.c(new EventHandler<b25>((s, a) => { try { ModScope.As(mod, click, click); } catch (Exception ex) { ModErrors.Report(mod, "a main menu button", ex); } }));
                    panel.aox(b);
                    UnderrailDropdownScroll.Move(b, 0, y);
                    y += step;
                }
                int grow = y - exitY;
                UnderrailDropdownScroll.Move(exit, 0, y);
                var at = UnderrailDropdownScroll.LocationOf(panel);
                panel.Size = new esj(UnderrailDropdownScroll.W(panel.Size), UnderrailDropdownScroll.H(panel.Size) + grow);
                UnderrailDropdownScroll.Move(panel, UnderrailDropdownScroll.X(at), Math.Max(0, UnderrailDropdownScroll.Y(at) - grow));
            }
            catch (Exception ex) { Log.Write("main menu: mods' buttons: " + ex); }
        }
    }
}
