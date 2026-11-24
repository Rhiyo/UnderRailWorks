// UnderRailWorks: buttons mods add to the main menu's column (New Game, Resume, Load Game, Options, Credits, Exit),
// in the game's own button style, above Exit, in the order mods add them:
//   MainMenu.AddButton(this, "Join a game", () => ...);
// Call it from Load: the game builds the main menu anew each time it shows it, and the buttons are added each time.
// A page: one button on the main menu that opens a column of its own in the same place and style, with Back at the
// bottom (Escape too), for a mod with several entries (Co-op: Host New Game, Host Saved Game, Join a Game):
//   var page = MainMenu.AddPage(this, "Co-op");
//   page.AddButton("Host New Game", HostNew);  page.Opened += () => ...;  page.Closed += () => ...;
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

        // A page of buttons opened from the main menu (see the top of this file). Adding it again returns the same page.
        public static MainMenuPage AddPage(WorksMod mod, string text)
        {
            if (mod == null) throw new ArgumentNullException("mod");
            MainMenuPage page;
            lock (pages)
            {
                page = pages.FirstOrDefault(x => x.Mod == mod.Id && x.Text == text);
                if (page == null) pages.Add(page = new MainMenuPage(mod.Id, text));
            }
            AddButton(mod, text, () => page.Open());
            return page;
        }
        static readonly List<MainMenuPage> pages = new List<MainMenuPage>();
        // The main menu is the screen showing (not a game, not a load or character creation in between).
        public static bool IsShowing { get { return UiPopups.Screen() is a8k; } }
        // The page showing now (null: the main menu's own column).
        public static MainMenuPage OpenPage { get { return MainMenuPage.Current; } }

        internal static void Forget(string mod) { lock (entries) entries.RemoveAll(e => e.Mod == mod); lock (pages) pages.RemoveAll(p => p.Mod == mod); if (MainMenuPage.Current != null && MainMenuPage.Current.Mod == mod) MainMenuPage.Current.Close(); }
        internal static bool HasButtons(string mod) { lock (entries) return entries.Any(e => string.Equals(e.Mod, mod, StringComparison.OrdinalIgnoreCase)); }

        // The main menu built anew (as the game does when it returns to it: a new a8k with the same controller), e.g.
        // after mods that change it were switched on or off: its buttons, its background and what mods add to it.
        static readonly FieldInfo Controller = typeof(a8k).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).FirstOrDefault(f => f.FieldType == typeof(azl));
        internal static bool Rebuild()
        {
            var menu = UiPopups.Screen() as a8k;
            var controller = menu == null || Controller == null ? null : Controller.GetValue(menu) as azl;
            if (controller == null) return false;
            AssetStore.Apply();
            bjn.b().aav(new a8k(controller));
            Log.Write("main menu: built again (mods that change it were switched)");
            return true;
        }

        const BindingFlags Fields = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        static readonly MethodInfo Style = typeof(a8k).GetMethod("a", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(zt) }, null);
        static readonly FieldInfo[] Colours = new[] { "b", "c", "h" }.Select(n => typeof(zt).GetField(n, Fields)).Where(f => f != null).ToArray();
        static readonly PropertyInfo Alignment = typeof(zt).GetProperty("VerticalAlignment");

        // After a8k.c(): the mods' buttons go where Exit was, Exit below them, the panel grows upwards.
        // The main menu's button column (the panel holding New Game ... Exit), shown or hidden (the Mods screen hides it
        // while it's open, as the game's own screens replace the menu).
        internal static void ShowButtons(bool visible)
        {
            var menu = UiPopups.Screen() as a8k;
            if (menu == null) return;
            var button = typeof(a8k).GetFields(Fields).Where(f => f.FieldType == typeof(zt)).Select(f => f.GetValue(menu) as zt).FirstOrDefault(b => b != null);
            var panel = button == null ? null : button.Parent;
            if (panel != null) panel.Visible = visible;
        }

        // A button in the menu's style (as Exit: its colours, size, alignment, sound), running click as mod.
        internal static zt MakeButton(a8k menu, zt like, string text, string mod, Action click)
        {
            var b = new zt();
            if (Style != null) Style.Invoke(menu, new object[] { b });
            foreach (var f in Colours) f.SetValue(b, f.GetValue(like));
            b.HorizontalAlignment = like.HorizontalAlignment;
            if (Alignment != null) Alignment.SetValue(b, Alignment.GetValue(like, null), null);
            b.Text = text;
            b.Size = like.Size;
            e4z.j(b);
            b.c(new EventHandler<b25>((s, a) => { try { ModScope.As(mod, click, click); } catch (Exception ex) { ModErrors.Report(mod, "a main menu button", ex); } }));
            return b;
        }

        // The menu's column: its panel and its buttons, top to bottom.
        internal static bool Column(a8k menu, out euz panel, out List<zt> buttons)
        {
            panel = null; buttons = null;
            var mine = typeof(a8k).GetFields(Fields).Where(f => f.FieldType == typeof(zt)).Select(f => f.GetValue(menu) as zt).FirstOrDefault(b => b != null);
            if (mine == null || mine.Parent == null) return false;
            panel = mine.Parent;
            buttons = panel.Controls.Cast<euz>().OfType<zt>().OrderBy(b => UnderrailDropdownScroll.Y(UnderrailDropdownScroll.LocationOf(b))).ToList();
            return buttons.Count > 0;
        }

        internal static void Built(a8k menu)
        {
            MainMenuPage.Current = null;     // a new menu shows its own column
            World.StartAs = null; World.CreationOnly = false;     // back on the menu: a New Game left before creation
            List<Entry> list;
            lock (entries) list = entries.ToList();
            // Works' own first: the Mods screen (switching mods on and off, their order, profiles).
            list.Insert(0, new Entry { Mod = "UnderRailWorks", Text = "Mods", Click = ModManager.Open });
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
                    panel.aox(MakeButton(menu, exit, e.Text, e.Mod, e.Click));
                    UnderrailDropdownScroll.Move(panel.Controls.Cast<euz>().Last(), 0, y);
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

    // A page of main menu buttons (MainMenu.AddPage): shown in the menu's column in place of its buttons, top-aligned,
    // with Back last; the column's panel shrinks to fit (its bottom stays) and grows back when the page closes.
    public sealed class MainMenuPage
    {
        sealed class Entry { public string Text; public Action Click; }
        internal readonly string Mod;
        public string Text { get; private set; }
        readonly List<Entry> entries = new List<Entry>();
        internal static MainMenuPage Current;
        // While open: the column as it was, to put back.
        a8k menu; euz panel; List<zt> hidden; List<zt> shown; esj panelSize; object panelAt;

        internal MainMenuPage(string mod, string text) { Mod = mod; Text = text; }
        // A button on the page, in the order added (again with the same text: replaced).
        public void AddButton(string text, Action onClick)
        {
            lock (entries) { entries.RemoveAll(e => e.Text == text); entries.Add(new Entry { Text = text, Click = onClick }); }
        }
        public bool IsOpen { get { return Current == this && menu != null && UiPopups.Screen() == menu; } }
        // After it opened / after it closed (Back, Escape, Close, or the menu going away).
        public event Action Opened, Closed;

        public void Open()
        {
            var m = UiPopups.Screen() as a8k;
            if (m == null) return;
            if (Current != null && Current != this) Current.Close();
            if (Current == this) return;
            euz p; List<zt> column;
            if (!MainMenu.Column(m, out p, out column)) { Log.Write("main menu: the menu's buttons weren't found; page " + Text + " not opened"); return; }
            List<Entry> list; lock (entries) list = entries.ToList();
            menu = m; panel = p; panelSize = p.Size; panelAt = UnderrailDropdownScroll.LocationOf(p);
            hidden = column.Where(b => b.Visible).ToList();
            var ys = column.Select(b => UnderrailDropdownScroll.Y(UnderrailDropdownScroll.LocationOf(b))).ToList();
            int step = ys.Count > 1 ? ys[1] - ys[0] : UnderrailDropdownScroll.H(column[0].Size) * 3 / 2;
            var like = column[column.Count - 1];
            foreach (var b in hidden) b.Visible = false;
            shown = new List<zt>();
            int y = ys[0], i = 0;
            foreach (var e in list.Concat(new[] { new Entry { Text = "Back", Click = Close } }))
            {
                var click = e.Click; var isBack = i == list.Count;
                var b = MainMenu.MakeButton(m, like, e.Text, isBack ? "UnderRailWorks" : Mod, click);
                p.aox(b); UnderrailDropdownScroll.Move(b, UnderrailDropdownScroll.X(UnderrailDropdownScroll.LocationOf(like)), y);
                shown.Add(b);
                y += step; i++;
            }
            // Shrink the panel to the page (bottom fixed): what's left below the last button stays as it was.
            int lastY = ys[ys.Count - 1], pageLast = y - step, cut = Math.Max(0, lastY - pageLast);
            if (cut > 0)
            {
                p.Size = new esj(UnderrailDropdownScroll.W(panelSize), UnderrailDropdownScroll.H(panelSize) - cut);
                UnderrailDropdownScroll.Move(p, UnderrailDropdownScroll.X(panelAt), UnderrailDropdownScroll.Y(panelAt) + cut);
            }
            Current = this;
            Log.Write("main menu: page " + Text + " (" + Mod + ")");
            var h = Opened; if (h != null) try { ModScope.As(Mod, h, h); } catch (Exception ex) { ModErrors.Report(Mod, "a main menu page's Opened", ex); }
        }

        public void Close()
        {
            if (Current != this) return;
            Current = null;
            try
            {
                if (menu != null && panel != null)
                {
                    foreach (var b in shown ?? new List<zt>()) UnderrailDropdownScroll.Remove(panel, b);
                    foreach (var b in hidden ?? new List<zt>()) b.Visible = true;
                    panel.Size = panelSize;
                    UnderrailDropdownScroll.Move(panel, UnderrailDropdownScroll.X(panelAt), UnderrailDropdownScroll.Y(panelAt));
                }
            }
            catch (Exception ex) { Log.Write("main menu: closing page " + Text + ": " + ex.Message); }
            menu = null; panel = null; hidden = null; shown = null;
            var h = Closed; if (h != null) try { ModScope.As(Mod, h, h); } catch (Exception ex) { ModErrors.Report(Mod, "a main menu page's Closed", ex); }
        }

        // Every frame: the menu went away (a game started, the menu was built anew): the page with it.
        internal static void Tick()
        {
            var c = Current;
            if (c != null && UiPopups.Screen() != c.menu) { c.menu = null; c.Close(); }
        }
    }
}
