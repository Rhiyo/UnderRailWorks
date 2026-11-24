// UnderRailWorks: a standard window for mods: the game's own titled window (a9t, as its notes and inventory windows:
// the interface size's frame, title strip, font and close button), dragged by its title bar, closed by the button or
// Escape (the topmost one first; the game's menu doesn't open meanwhile), its place
// remembered per mod and kept relative to the screen edges (Ui.Place), so it survives resolution changes.
//   var w = Ui.Window(this, "ledger", "Merchant ledger", 560, 360);
//   Ui.Add(w, Ui.Label("..."), 10, w.Top + 8);      // content below the title bar
//   w.Closed += () => { ... };
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class UiWindow : UiPanel
{
    internal WorksMod Mod;
    internal string Id;
    internal UiWindow(a9t window) : base(window) { }
    // The title bar's height at this interface size.
    public int TitleHeight { get { var w = Control as a9t; return w == null ? 22 : (int)UiWindows.TitleField.GetValue(w); } }
    // Where content starts (below the title bar).
    public int Top { get { return TitleHeight + 4; } }
    public bool IsOpen { get { return Control.Parent != null; } }
    // On the main menu: true keeps the menu's buttons showing while this window is open (by default they're hidden, as
    // for the game's own screens). Set it right after opening; it can change any time.
    public bool KeepMainMenu { get; set; }
    // Runs once when the window closes (button, Escape, Close, or the HUD going away).
    public event Action Closed;
    public void Close()
    {
        if (!UiWindows.Open.Remove(this)) return;
        Ui.Remove(this);
        var c = Closed; Closed = null;
        if (c != null) try { c(); } catch (Exception ex) { Log.Write("mods: " + (Mod == null ? "UnderRailWorks" : Mod.Id) + ": a window's Closed failed: " + ex.Message); }
    }
}

public static partial class Ui
{
    // A window on the HUD, or on the main menu when it is showing (null otherwise; there the menu's buttons are hidden
    // while it's open, as for the game's own screens). id: unique within the mod, for remembering its place. Opening a
    // window with an id already open returns that one. For a panel that stays open (a ledger, a list). To ask the player
    // something, use the game's own popups instead: Ui.Ask (text), Ui.AskNumber, Ui.Confirm (yes/no), Ui.Choose
    // (buttons). height counts a 22-high title bar; the real one follows the interface size (place content from Top).
    public static UiWindow Window(WorksMod mod, string id, string title, int width, int height)
    {
        if (mod == null) throw new ArgumentNullException("mod");
        return OpenWindow(mod, id, title, width, height);
    }

    // Buttons along the bottom of a window (or any panel), from the left, wrapping onto more rows when they don't fit
    // its width. Returns the y where the buttons start (lay the rest of the window out above it).
    //   int bottom = Ui.AddButtons(w, new[] { ok, cancel });
    public static int AddButtons(UiElement window, IList<UiElement> buttons, int pad = -1, int gap = -1)
    {
        if (window == null || buttons == null || buttons.Count == 0) return window == null ? 0 : window.Height;
        if (pad < 0) pad = S(10);
        if (gap < 0) gap = S(6);
        int width = window.Width - 2 * pad, bh = buttons.Max(b => b.Height);
        var rows = new List<List<UiElement>> { new List<UiElement>() };
        int used = 0;
        foreach (var b in buttons)
        {
            if (used > 0 && used + b.Width > width) { rows.Add(new List<UiElement>()); used = 0; }
            rows[rows.Count - 1].Add(b); used += b.Width + gap;
        }
        int top = window.Height - pad - rows.Count * (bh + gap) + gap, y = top;
        foreach (var row in rows)
        {
            int x = pad;
            foreach (var b in row) { Add(window, b, x, y); x += b.Width + gap; }
            y += bh + gap;
        }
        return top;
    }
    // The interface area windows go in (the HUD, or the main menu), in layout units: 0 when neither is showing.
    public static int ScreenWidth { get { var r = Hud.Root ?? (UiPopups.Screen() is a8k ? UiElement.Of(UiPopups.Screen()) : null); return r == null ? 0 : r.Width; } }
    public static int ScreenHeight { get { var r = Hud.Root ?? (UiPopups.Screen() is a8k ? UiElement.Of(UiPopups.Screen()) : null); return r == null ? 0 : r.Height; } }

    // How tall AddButtons' rows are in a window this wide (to size the window before adding them).
    public static int ButtonsHeight(IList<UiElement> buttons, int windowWidth, int pad = -1, int gap = -1)
    {
        if (buttons == null || buttons.Count == 0) return 0;
        if (pad < 0) pad = S(10);
        if (gap < 0) gap = S(6);
        int width = windowWidth - 2 * pad, bh = buttons.Max(b => b.Height), rows = 1, used = 0;
        foreach (var b in buttons) { if (used > 0 && used + b.Width > width) { rows++; used = 0; } used += b.Width + gap; }
        return rows * (bh + gap) - gap;
    }
    // How wide a window must be for these buttons on one row (with AddButtons' default spacing).
    public static int ButtonsWidth(IList<UiElement> buttons, int pad = -1, int gap = -1)
    {
        if (pad < 0) pad = S(10);
        if (gap < 0) gap = S(6);
        return buttons == null || buttons.Count == 0 ? 2 * pad : buttons.Sum(b => b.Width) + gap * (buttons.Count - 1) + 2 * pad;
    }

    // mod null: Works' own window (the Mods screen): opened in the middle each time, its place not remembered.
    internal static UiWindow OpenWindow(WorksMod mod, string id, string title, int width, int height)
    {
        if (PlayerScreens.ActingIsRemote) { Log.Write("mods: " + (mod == null ? "UnderRailWorks" : mod.Id) + ": a window (" + title + ") for a player on another machine: not opened here"); return null; }
        var hud = Hud.Root;
        if (hud == null) { var screen = UiPopups.Screen(); if (screen is a8k) hud = UiElement.Of(screen); }     // on the main menu: over it (closed when a game starts)
        if (hud == null) return null;
        var open = UiWindows.Open.FirstOrDefault(o => o.Mod == mod && o.Id == id && o.IsOpen);
        if (open != null) return open;
        var game = new a9t();
        game.Text = title;
        var w = new UiWindow(game) { Mod = mod, Id = id };
        // height: as if the title bar were 22 high (the content keeps its room at every interface size)
        // at least as wide as its title in the title font (a9t's title label), plus the close button
        var titleLabel = game.Controls.Cast<euz>().OfType<avo>().FirstOrDefault();
        var titleFont = titleLabel == null || dr3.a() == null ? null : dr3.a().GetResource<Microsoft.Xna.Framework.Graphics.SpriteFont>(titleLabel.g());
        if (titleFont != null) width = Math.Max(width, (int)Math.Ceiling(titleFont.MeasureString(title ?? "").X) + titleLabel.Location.X + 2 * w.TitleHeight + 8);
        // never shorter than the frame's picture (wj.b7, 96 high): below that its middle gets a negative height and draws
        // above the window
        var frame = UiWindows.FrameField == null ? (object)null : UiWindows.FrameField.GetValue(game);
        int minHeight = frame is esj ? UnderrailDropdownScroll.H((esj)frame) + 4 : 100;
        game.Size = new esj(width, Math.Max(minHeight, height - 22 + w.TitleHeight));
        game.a((EventHandler<el5>)((sender, e) => w.Close()));     // its own close button
        Add(hud, w, (hud.Width - UnderrailDropdownScroll.W(game.Size)) / 2, Math.Max(10, hud.Height / 6));
        { var inside = Ui.KeepInside(w, w.X, w.Y); if (inside.X != w.X || inside.Y != w.Y) Ui.Move(w, inside.X, inside.Y); }     // a tall or wide window: on screen from the start
        var place = mod == null ? null : mod.Settings.Get("window." + id);
        if (!string.IsNullOrEmpty(place)) Restore(w, place);
        UiWindows.Open.Add(w);
        return w;
    }
}

static class UiWindows
{
    internal static readonly System.Reflection.FieldInfo FrameField = typeof(a9t).GetField("f", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);     // a9t.f: the frame picture's size
    internal static readonly System.Reflection.FieldInfo TitleField = typeof(a9t).GetField("i", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);     // a9t.i: also a method name
    internal static readonly List<UiWindow> Open = new List<UiWindow>();
    static UiWindow dragging;
    static Point dragStart, windowStart;

    internal static bool HoldEscape { get { return Open.Any(w => w.IsOpen); } }

    // dka.a(sender, key event): the in-game key handler; Escape (ane.a == 27) closes the topmost game screen or opens
    // the game menu (dka.e()). With a mod window open, Escape closes that window instead and the game never sees it
    // (not while typing in a text box: the game's own fields keep Escape).
    static readonly System.Reflection.FieldInfo KeyField = typeof(ane).GetField("a");
    internal static bool KeyPressed(ane __1)
    {
        try
        {
            if (Open.Count == 0 || __1 == null || Input.Typing || Convert.ToInt32(KeyField.GetValue(__1)) != 27) return true;
            Open.RemoveAll(w => !w.IsOpen);
            if (Open.Count == 0) return true;
            Open[Open.Count - 1].Close();
            return false;
        }
        catch (Exception ex) { Log.Write("windows: " + ex.Message); return true; }
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var handler = typeof(dka).GetMethods(any).First(m => m.Name == "a" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(object) && m.GetParameters()[1].ParameterType == typeof(ane));
        harmony.Patch(handler, prefix: new HarmonyLib.HarmonyMethod(typeof(UiWindows).GetMethod("KeyPressed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
    }

    // On the main menu, the menu's buttons are hidden while a mod window is open over it (unless it has KeepMainMenu), as the game's own screens
    // (Load Game, Options...) replace them; back when the last one closes. Checked every frame (cheap: only a change
    // touches the menu), so a window closed any way, or a menu built anew, is followed.
    static object buttonsMenu; static bool buttonsHidden;
    static void MenuButtons()
    {
        var screen = Hud.Exists ? null : UiPopups.Screen() as a8k;
        if (screen == null) { buttonsMenu = null; return; }
        bool hide = Open.Any(w => !w.KeepMainMenu);
        if (screen == buttonsMenu && hide == buttonsHidden) return;
        buttonsMenu = screen; buttonsHidden = hide;
        MainMenu.ShowButtons(!hide);
    }

    // A window's contents checked once after it opened (and after it moved to fit): anything outside the window or the
    // window outside the screen is logged ("ui: ... doesn't fit"), so a layout that breaks at some resolution or
    // interface size shows in the log instead of only on that screen.
    static readonly HashSet<UiWindow> Checked = new HashSet<UiWindow>();
    static void CheckFit(UiWindow w)
    {
        if (!Checked.Add(w)) return;
        var game = w.Control;
        int ww = UnderrailDropdownScroll.W(game.Size), wh = UnderrailDropdownScroll.H(game.Size);
        var bad = new List<string>();
        foreach (var c in game.Controls.Cast<euz>())
        {
            if (!c.Visible) continue;
            var at = UnderrailDropdownScroll.LocationOf(c);
            int x = UnderrailDropdownScroll.X(at), y = UnderrailDropdownScroll.Y(at), cw = UnderrailDropdownScroll.W(c.Size), ch = UnderrailDropdownScroll.H(c.Size);
            if (x < 0 || y < 0 || x + cw > ww || y + ch > wh)
                bad.Add((string.IsNullOrEmpty(c.Text) ? c.GetType().Name : "\"" + c.Text + "\"") + " at " + x + "," + y + " " + cw + "x" + ch);
            else if (c is avo && !string.IsNullOrEmpty(c.Text) && Ui.TextWidth(c.Text) > cw + 1)
                bad.Add("\"" + c.Text + "\" is wider than its label (" + Ui.TextWidth(c.Text) + " > " + cw + ")");
        }
        var screen = Hud.Root ?? (UiPopups.Screen() == null ? null : UiElement.Of(UiPopups.Screen()));
        bool offScreen = screen != null && (ww > screen.Width || wh > screen.Height);
        string title = (game as a9t) == null ? "" : ((a9t)game).Text;
        if (bad.Count > 0) Log.Write("ui: window \"" + title + "\" (" + (w.Mod == null ? "UnderRailWorks" : w.Mod.Id) + ", " + ww + "x" + wh + "): " + bad.Count + " part(s) don't fit inside it: " + string.Join("; ", bad.Take(6).ToArray()));
        if (offScreen) Log.Write("ui: window \"" + title + "\" (" + (w.Mod == null ? "UnderRailWorks" : w.Mod.Id) + ") is " + ww + "x" + wh + ", bigger than the screen (" + screen.Width + "x" + screen.Height + ")");
        if (bad.Count == 0 && !offScreen) Log.Write("ui: window \"" + title + "\" (" + (w.Mod == null ? "UnderRailWorks" : w.Mod.Id) + ", " + ww + "x" + wh + ") fits");
    }

    internal static void Tick()
    {
        UiPopups.Tick();
        Open.RemoveAll(w => !w.IsOpen);
        Checked.RemoveWhere(w => !w.IsOpen);
        foreach (var w in Open.ToList()) try { CheckFit(w); } catch (Exception ex) { Log.Write("ui: checking a window: " + ex.Message); }
        try { MenuButtons(); } catch (Exception ex) { Log.Write("windows: the main menu's buttons: " + ex.Message); }
        try { MainMenuPage.Tick(); } catch (Exception ex) { Log.Write("main menu: page: " + ex.Message); }
        try { Camera.Tick(); } catch (Exception ex) { Log.Write("camera: " + ex.Message); }
        Saves.ProbeTick();
        if (Open.Count == 0)
        {
            dragging = null;
            // Escape on a main menu page (no window, no game popup over it): back to the menu's own column.
            var page = MainMenuPage.Current;
            if (page != null && !Hud.Exists && !Input.Typing && Input.Pressed(Keys.Escape))
            {
                var screen = UiPopups.Screen();
                if (screen is a8k && !screen.Controls.Cast<euz>().Any(c => c is bnr || c is e4u || c is tg)) page.Close();
            }
            return;
        }
        // On the main menu the in-game key handler (KeyPressed) doesn't run: Escape closes the topmost window here,
        // unless a game popup (a question) is open over it, which takes Escape itself.
        if (!Hud.Exists && !Input.Typing && Input.Pressed(Keys.Escape))
        {
            var screen = UiPopups.Screen();
            if (screen is a8k && !screen.Controls.Cast<euz>().Any(c => c is bnr || c is e4u || c is tg)) { Open[Open.Count - 1].Close(); return; }
        }
        var m = Input.Mouse;
        if (dragging == null && Input.MousePressed(MouseButton.Left))
        {
            for (int i = Open.Count - 1; i >= 0; i--)
            {
                var b = Open[i].Bounds;
                if (!b.Contains(m)) continue;
                int title = Open[i].TitleHeight;
                if (m.Y < b.Y + title && m.X < b.Right - title) { dragging = Open[i]; dragStart = m; windowStart = new Point(dragging.X, dragging.Y); }
                break;
            }
        }
        if (dragging == null) return;
        if (!dragging.IsOpen) { dragging = null; return; }
        var to = Ui.KeepInside(dragging, windowStart.X + m.X - dragStart.X, windowStart.Y + m.Y - dragStart.Y);
        Ui.Move(dragging, to.X, to.Y);
        if (!Input.MouseDown(MouseButton.Left))
        {
            var place = Ui.Place(dragging);
            if (place != null && dragging.Mod != null) dragging.Mod.Settings.Set("window." + dragging.Id, place);
            dragging = null;
        }
    }

    // The HUD went away: its windows with it.
    internal static void HudGone() { foreach (var w in Open.ToList()) w.Close(); Open.Clear(); dragging = null; }
}
}
