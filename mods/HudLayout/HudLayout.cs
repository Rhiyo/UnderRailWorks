// HUD Layout: an UnderRailWorks mod. Press the key (default F12) to arrange the game's HUD: frames show its parts;
// drag one with the left mouse button, right-click one to put it back, Reset all for every part; Done (or the key) to finish.
// Every part is anchored to an edge, a corner or the centre and keeps its distance from it, so a layout survives
// resolution and interface-scale changes: a part keeps its natural anchor (the game's own, Hud.AnchorOf, or the one a
// mod gave) unless it is dropped in the opposite third of the screen. While dragging, a line shows the anchor and
// positions snap to a grid counted from it (a setting, with its size; Shift drags freely). The mouse wheel over a part
// picks its anchor yourself (Auto, then each of the nine); right-click puts the part back and clears that choice. Built from Works' API only (Hud handles, Ui.Custom, Ui.Place/Restore, Input mouse, Settings).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class HudLayoutConfig : ModConfig
{
    [Header("HUD Layout")]
    [Label("Show a Layout button on the HUD"), Tooltip("A small button that starts arranging; it can be moved like the rest.")]
    public bool HudButton = false;
    [Label("Snap to a grid"), Tooltip("Positions snap to the grid, counted from the edge (or the centre) the part is anchored to. Hold Shift while dragging to place freely.")]
    public bool Snap = true;
    [Label("Grid size"), Range(2, 40), Increment(1), Tooltip("In interface units. The game keeps its own parts 10 from the screen edges.")]
    public int Grid = 10;

    public override void OnChanged() { HudLayout.Changed = true; }

}


public sealed class HudLayout : WorksMod
{
    static readonly HudPart[] GameParts = (HudPart[])Enum.GetValues(typeof(HudPart));

    // One movable part: the game's (by HudPart) or a mod's (by its name).
    sealed class Part { public string Name; public UiElement Element; public HudAnchor Natural; }

    internal static bool Changed;
    HudLayoutConfig config;
    UiElement layoutButton;
    Keybind arrangeKey;
    UiElement hud, overlay;
    UiPanel help;
    readonly Dictionary<string, Point> originals = new Dictionary<string, Point>();
    Part dragging, hovered;
    Point dragStart, partStart;

    public override void Load()
    {
        config = GetConfig<HudLayoutConfig>();
        arrangeKey = Keybinds.Register(this, "arrange", "Arrange");     // unbound by default (Options -> Controls)
        UiEvents.HudCreated.Add(this, root =>
        {
            hud = root; overlay = null; help = null; dragging = null; layoutButton = null; originals.Clear();
            foreach (var p in Parts()) { originals[p.Name] = Default(p); Apply(p); }
            ShowButton();
        });
        UiEvents.HudDestroyed.Add(this, () => { hud = null; overlay = null; help = null; dragging = null; layoutButton = null; });     // back to the main menu
        // A mod's part was added (or rebuilt): where the player put it.
        Hud.PartAdded.Add(this, part =>
        {
            if (hud == null) return;
            originals["mod:" + part.Name] = new Point(part.DefaultX, part.DefaultY);
            Apply(ModPart(part));
        });
        GameEvents.Update.Add(this, seconds =>
        {
            if (hud == null) return;
            if (Changed) { Changed = false; ShowButton(); }
            if (arrangeKey.Pressed) { if (overlay == null) Begin(); else End(); }
            if (overlay == null) return;
            var m = Input.Mouse;
            hovered = PartAt(m);
            if (movingBar)
            {
                var to = Ui.KeepInside(help, barStart.X + m.X - dragStart.X, barStart.Y + m.Y - dragStart.Y);
                Ui.Move(help, to.X, to.Y);
                if (!Input.MouseDown(MouseButton.Left)) { movingBar = false; var place = Ui.Place(help); if (place != null) Settings.Set("bar", place); }
                return;
            }
            if (Input.MousePressed(MouseButton.Left) && BarClick(m)) return;
            if (Input.MousePressed(MouseButton.Left) && hovered != null)
            {
                dragging = hovered; dragStart = m; partStart = new Point(hovered.Element.X, hovered.Element.Y);
                help.Visible = false;     // out of the way while a part moves
            }
            if (dragging != null)
            {
                // Mods rebuild their parts (new text, new size): follow the part by name, not the old element.
                var now = Parts().FirstOrDefault(p => p.Name == dragging.Name);
                if (now == null) { dragging = null; if (help != null) help.Visible = true; return; }
                dragging.Element = now.Element;
                var to = Ui.KeepInside(dragging.Element, partStart.X + m.X - dragStart.X, partStart.Y + m.Y - dragStart.Y);
                Ui.Move(dragging.Element, to.X, to.Y);
                if (config.Snap && !Input.Down(Keys.LeftShift) && !Input.Down(Keys.RightShift)) Snap(dragging);
                if (Input.MouseReleased(MouseButton.Left))
                {
                    var place = Ui.Place(dragging.Element, AnchorFor(dragging));
                    if (place != null) Settings.Set(Key(dragging.Name), place);
                    if (help != null) help.Visible = true;
                    dragging = null;
                }
            }
            int wheel = Input.Wheel;
            if (wheel != 0 && hovered != null && dragging == null) CycleAnchor(hovered, wheel > 0 ? -1 : 1);
            if (Input.MousePressed(MouseButton.Right) && hovered != null)
            {
                Settings.Set(AnchorKey(hovered.Name), "");
                Point o;
                if (originals.TryGetValue(hovered.Name, out o)) Ui.Move(hovered.Element, o.X, o.Y);
                Settings.Set(Key(hovered.Name), "");
            }
        });
    }

    // The game's parts, then the mods' (added later: they draw on top).
    IEnumerable<Part> Parts()
    {
        foreach (var p in GameParts)
        {
            var e = Hud.Get(p);
            if (e != null) yield return new Part { Name = p.ToString(), Element = e, Natural = Hud.AnchorOf(p) };
        }
        foreach (var p in Hud.ModParts) yield return ModPart(p);
    }

    Part ModPart(HudModPart p)
    {
        // A mod's anchor, else the one its default place suggests.
        var natural = p.Anchor ?? Ui.AnchorOfPlace(Ui.Place(p.DefaultX, p.DefaultY, p.Element.Width, p.Element.Height, hud.Width, hud.Height));
        return new Part { Name = "mod:" + p.Name, Element = p.Element, Natural = natural };
    }

    // The anchor a part takes where it is now: per axis its natural one, unless its centre is in the opposite third
    // (or, for a centred axis, in an edge third).
    HudAnchor AnchorFor(Part p)
    {
        HudAnchor chosen;
        if (Enum.TryParse(Settings.Get(AnchorKey(p.Name)) ?? "", out chosen)) return chosen;
        int col = Pick((int)p.Natural % 3, p.Element.X + p.Element.Width / 2, hud.Width);
        int row = Pick((int)p.Natural / 3, p.Element.Y + p.Element.Height / 2, hud.Height);
        return (HudAnchor)(row * 3 + col);
    }

    // Grid lines across a size: from the start, from the end, or out from the middle.
    static IEnumerable<int> Lines(int side, int size, int g)
    {
        if (side == 0) for (int v = 0; v <= size; v += g) yield return v;
        else if (side == 2) for (int v = size; v >= 0; v -= g) yield return v;
        else { yield return size / 2; for (int k = g; k <= size / 2; k += g) { yield return size / 2 - k; yield return size / 2 + k; } }
    }

    static int Pick(int natural, int centre, int room)
    {
        int third = centre < room / 3 ? 0 : centre > room * 2 / 3 ? 2 : 1;
        if (natural == 1) return third;
        return third == 2 - natural ? third : natural;
    }

    // Rounds the part's distance from its anchor to the grid.
    void Snap(Part p)
    {
        var a = AnchorFor(p);
        var e = p.Element;
        int g = Math.Max(1, config.Grid), col = (int)a % 3, row = (int)a / 3;
        Func<int, int> round = v => (int)Math.Round(v / (double)g) * g;
        int x = col == 0 ? round(e.X) : col == 2 ? hud.Width - e.Width - round(hud.Width - e.Width - e.X) : (hud.Width - e.Width) / 2 + round(e.X - (hud.Width - e.Width) / 2);
        int y = row == 0 ? round(e.Y) : row == 2 ? hud.Height - e.Height - round(hud.Height - e.Height - e.Y) : (hud.Height - e.Height) / 2 + round(e.Y - (hud.Height - e.Height) / 2);
        var to = Ui.KeepInside(e, x, y);
        Ui.Move(e, to.X, to.Y);
    }

    void Apply(Part p)
    {
        var saved = Settings.Get(Key(p.Name));
        if (string.IsNullOrEmpty(saved)) saved = Legacy(p);
        if (!string.IsNullOrEmpty(saved)) Ui.Restore(p.Element, saved);
    }

    // Version 1.0 saved pixels per HUD size ("Part@2560x720=x,y"): use the entry for this size, else the newest
    // other size's, converted to an edge-relative place (and written in the new form).
    string Legacy(Part p)
    {
        string best = null;
        foreach (var k in Settings.Keys.Where(k => k.StartsWith(p.Name + "@")))
        {
            var v = Settings.Get(k);
            Point at; int w, h;
            if (string.IsNullOrEmpty(v) || !TryParse(v, out at) || !Size(k.Substring(p.Name.Length + 1), out w, out h)) continue;
            var place = Ui.Place(at.X, at.Y, p.Element.Width, p.Element.Height, w, h);
            best = place;
            if (w == hud.Width && h == hud.Height) break;
        }
        if (best != null) Settings.Set(Key(p.Name), best);
        return best;
    }

    static bool Size(string s, out int w, out int h)
    {
        w = h = 0;
        var parts = s.Split('x');
        return parts.Length == 2 && int.TryParse(parts[0], out w) && int.TryParse(parts[1], out h);
    }

    // The Layout button on the HUD (a part of its own, so it can be moved too), while the setting is on.
    void ShowButton()
    {
        if (hud == null) return;
        if (!config.HudButton) { if (layoutButton != null) Ui.Remove(layoutButton); layoutButton = null; return; }
        if (layoutButton != null) return;
        layoutButton = Ui.Button("Layout", () => { if (overlay == null) Begin(); });
        Hud.AddPart(this, "Layout button", layoutButton, HudAnchor.TopRight, 70, 62);
    }

    // Every part back where it was, and every saved place cleared (all HUD sizes).
    void ResetAll()
    {
        foreach (var k in Settings.Keys.ToList()) if (k.Contains("@") || k.StartsWith("at.") || k.StartsWith("anchor.")) Settings.Set(k, "");
        foreach (var p in Parts())
        {
            Point o;
            if (originals.TryGetValue(p.Name, out o)) Ui.Move(p.Element, o.X, o.Y);
            Settings.Set(Key(p.Name), "");
        }
    }

    // The bar while arranging: what to do, the snap switch and grid size, Reset all, Done. Drag it by its text; it hides
    // while a part is being dragged (it would be in the way).
    UiElement snapBox, gridMinus, gridPlus, resetButton, doneButton;
    bool movingBar;
    Point barStart;

    void Begin()
    {
        overlay = Ui.Custom(hud.Width, hud.Height, Draw);     // covers the screen: clicks go to it, not the game
        Ui.Add(hud, overlay, 0, 0);
        var message = "Arranging the HUD: drag a part, wheel over it for its anchor, right-click to put it back.";
        var reset = resetButton = Ui.Button("Reset all", ResetAll);
        var done = doneButton = Ui.Button("Done", End);
        const int controls = 190;     // snap switch + grid size
        int w = Ui.TextWidth(message) + controls + reset.Width + done.Width + 60;
        int h = Math.Max(40, Math.Max(reset.Height, done.Height) + 10), mid = h / 2;     // the game's buttons grow with the interface tier
        help = Ui.Panel(w, h);
        var text = Ui.Label(message, Ui.TextWidth(message) + 4);
        Ui.SetColor(text, new Color(230, 200, 120));
        Ui.Add(help, text, 10, mid - 9);
        int x = 10 + Ui.TextWidth(message) + 16;
        snapBox = Ui.Custom(86, 22, c => Box(c, config.Snap ? "Snap: on" : "Snap: off", config.Snap));
        Ui.Add(help, snapBox, x, mid - 11);
        gridMinus = Ui.Custom(20, 22, c => Box(c, "-", false));
        Ui.Add(help, gridMinus, x + 92, mid - 11);
        var size = Ui.Custom(46, 22, c => c.Text(config.Grid.ToString(), c.Bounds.X + (c.Bounds.Width - Ui.TextWidth(config.Grid.ToString())) / 2, c.Bounds.Y + 3, config.Snap ? new Color(230, 200, 120) : new Color(120, 110, 90)));
        Ui.Add(help, size, x + 114, mid - 11);
        gridPlus = Ui.Custom(20, 22, c => Box(c, "+", false));
        Ui.Add(help, gridPlus, x + 162, mid - 11);
        Ui.Add(help, reset, w - done.Width - reset.Width - 14, mid - reset.Height / 2);     // above the overlay: they get the clicks
        Ui.Add(help, done, w - done.Width - 8, mid - done.Height / 2);
        Ui.Add(hud, help, (hud.Width - w) / 2, hud.Height / 2 - h / 2);
        var at = Settings.Get("bar");
        if (!string.IsNullOrEmpty(at)) Ui.Restore(help, at);
    }

    static void Box(UiCanvas c, string label, bool lit)
    {
        var r = c.Bounds; bool hot = r.Contains(Input.Mouse);
        c.Fill(r, hot ? new Color(90, 50, 10) : lit ? new Color(60, 34, 8) : new Color(24, 16, 8));
        c.Frame(r, new Color(181, 92, 0));
        c.Text(label, r.X + (r.Width - Ui.TextWidth(label)) / 2, r.Y + 3, new Color(230, 200, 120));
    }

    // Clicks on the bar: its controls, else the start of moving it. True if the click was the bar's.
    bool BarClick(Point m)
    {
        if (help == null || !help.Visible || !help.Bounds.Contains(m)) return false;
        if (snapBox.Bounds.Contains(m)) { config.Snap = !config.Snap; Settings.Set("Snap", config.Snap ? "1" : "0"); return true; }
        if (gridMinus.Bounds.Contains(m) || gridPlus.Bounds.Contains(m))
        {
            int step = config.Grid >= 10 ? 2 : 1;
            config.Grid = Math.Max(2, Math.Min(40, config.Grid + (gridPlus.Bounds.Contains(m) ? step : -step)));
            Settings.Set("Grid", config.Grid.ToString());
            return true;
        }
        if (resetButton.Bounds.Contains(m) || doneButton.Bounds.Contains(m)) return true;     // the buttons' own clicks
        movingBar = true; dragStart = m; barStart = new Point(help.X, help.Y);     // (the game's buttons take their own clicks)
        return true;
    }

    void End()
    {
        if (overlay != null) Ui.Remove(overlay);
        if (help != null) Ui.Remove(help);
        overlay = null; help = null; dragging = null; movingBar = false;
    }

    static readonly string[] AnchorNames = { "top left", "top", "top right", "left", "centre", "right", "bottom left", "bottom", "bottom right" };

    void Draw(UiCanvas canvas)
    {
        canvas.Fill(canvas.Bounds, new Color(0, 0, 0, 70));
        var b = canvas.Bounds;
        if (config.Snap && dragging != null && config.Grid >= 4)
        {
            // The grid the dragged part snaps to: counted from the edge (or the centre) it is anchored to on each axis.
            var a = AnchorFor(dragging);
            int g = config.Grid, col = (int)a % 3, row = (int)a / 3;
            var dot = new Color(255, 255, 255, 40);
            var ys = Lines(row, b.Height, g).ToList();
            foreach (var x in Lines(col, b.Width, g))
                foreach (var y in ys) canvas.Fill(new Rectangle(b.X + x, b.Y + y, 1, 1), dot);
        }
        foreach (var p in Parts())
        {
            if (!p.Element.Visible || p.Element.Width <= 0) continue;
            var r = p.Element.Bounds;
            bool hot = Same(p, hovered) || Same(p, dragging);
            if (hot) canvas.Fill(r, new Color(230, 160, 60, 60));
            canvas.Frame(r, hot ? new Color(255, 200, 90) : p.Name.StartsWith("mod:") ? new Color(120, 170, 230) : new Color(181, 92, 0));
        }
        var shown = dragging ?? hovered;
        if (shown != null && shown.Element.Visible)
        {
            // The anchor: a line from the part's anchored side to the screen's.
            var a = AnchorFor(shown);
            var r = shown.Element.Bounds;
            int col = (int)a % 3, row = (int)a / 3;
            var line = new Color(255, 200, 90, 200);
            int px = col == 0 ? r.Left : col == 2 ? r.Right - 1 : r.Center.X, py = row == 0 ? r.Top : row == 2 ? r.Bottom - 1 : r.Center.Y;
            int sx = col == 0 ? b.Left : col == 2 ? b.Right - 1 : b.Center.X, sy = row == 0 ? b.Top : row == 2 ? b.Bottom - 1 : b.Center.Y;
            canvas.Fill(new Rectangle(Math.Min(px, sx), py, Math.Abs(px - sx) + 1, 1), line);
            canvas.Fill(new Rectangle(sx, Math.Min(py, sy), 1, Math.Abs(py - sy) + 1), line);
            canvas.Fill(new Rectangle(sx - 3, sy - 3, 7, 7), line);
            canvas.Text("Anchored " + AnchorNames[(int)a] + (Chosen(shown) ? " (your choice)" : a == shown.Natural ? "" : " (moved from " + AnchorNames[(int)shown.Natural] + ")") + "   wheel: change", r.X, Math.Max(b.Y, r.Y - 16), new Color(255, 220, 140));
        }
    }

    static bool Same(Part a, Part b) { return a != null && b != null && a.Name == b.Name; }

    Part PartAt(Point m)
    {
        Part found = null;
        if (help != null && help.Bounds.Contains(m)) return null;     // Reset all / Done, not a part behind them
        foreach (var p in Parts()) if (p.Element.Visible && p.Element.Bounds.Contains(m)) found = p;     // the last one drawn on top
        return found;
    }

    static string Key(string part) { return "at." + part; }
    static string AnchorKey(string part) { return "anchor." + part; }
    bool Chosen(Part p) { HudAnchor a; return Enum.TryParse(Settings.Get(AnchorKey(p.Name)) ?? "", out a); }

    // Auto -> TopLeft -> ... -> BottomRight -> Auto. The part stays where it is; its place is saved from the new anchor.
    void CycleAnchor(Part p, int step)
    {
        HudAnchor a;
        int i = Enum.TryParse(Settings.Get(AnchorKey(p.Name)) ?? "", out a) ? (int)a + 1 : 0;     // 0 = Auto
        i = ((i + step) % 10 + 10) % 10;
        Settings.Set(AnchorKey(p.Name), i == 0 ? "" : ((HudAnchor)(i - 1)).ToString());
        var place = Ui.Place(p.Element, AnchorFor(p));
        if (place != null) Settings.Set(Key(p.Name), place);
    }

    // Where a part goes back to: the game's own place (read before any change), or the mod's default.
    Point Default(Part p)
    {
        var mod = p.Name.StartsWith("mod:") ? Hud.ModParts.FirstOrDefault(m => "mod:" + m.Name == p.Name) : null;
        return mod != null ? new Point(mod.DefaultX, mod.DefaultY) : new Point(p.Element.X, p.Element.Y);
    }
    static bool TryParse(string s, out Point p)
    {
        p = Point.Zero;
        var parts = s.Split(',');
        int x, y;
        if (parts.Length != 2 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y)) return false;
        p = new Point(x, y); return true;
    }
}
