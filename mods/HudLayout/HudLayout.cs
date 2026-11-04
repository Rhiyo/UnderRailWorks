// HUD Layout: an UnderRailWorks mod. Press the key (default F12) to arrange the game's HUD: frames show its parts;
// drag one with the left mouse button, right-click one to put it back; Done (or the key) to finish. Positions are
// saved per HUD size in the mod's settings.ini and applied whenever the game builds its HUD. Built from Works' API only
// (Hud handles, Ui.Custom, Input mouse, Settings).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class HudLayoutConfig : ModConfig
{
    [Header("HUD Layout")]
    [Label("Key that starts and ends arranging"), Tooltip("None: use the Arrange button here (or set a key).")]
    public LayoutKey Key = LayoutKey.None;

    // Closes Options and the game menu, then arranging starts: drag parts, right-click to put one back, Done to finish.
    [Label("Arrange the HUD"), Button("Arrange now", NeedsGame = true)]
    public void Arrange() { HudLayout.Requested = true; }
}

public enum LayoutKey { [Label("(none)")] None, F12, F11, F6 }

public sealed class HudLayout : WorksMod
{
    static readonly HudPart[] GameParts = (HudPart[])Enum.GetValues(typeof(HudPart));

    // One movable part: the game's (by HudPart) or a mod's (by its name).
    sealed class Part { public string Name; public UiElement Element; public Point Original; }

    internal static bool Requested;
    int startIn = -1;     // frames until arranging starts after the settings button (Options and the menu close first)
    HudLayoutConfig config;
    UiElement hud, overlay;
    UiPanel help;
    readonly Dictionary<string, Point> originals = new Dictionary<string, Point>();
    Part dragging, hovered;
    Point dragStart, partStart;

    public override void Load()
    {
        config = GetConfig<HudLayoutConfig>();
        UiEvents.HudCreated.Add(this, root =>
        {
            hud = root; overlay = null; help = null; dragging = null; originals.Clear();
            foreach (var p in Parts()) { originals[p.Name] = new Point(p.Element.X, p.Element.Y); Apply(p); }
        });
        // A mod's part was added (or rebuilt): where the player put it.
        Hud.PartAdded.Add(this, part =>
        {
            if (hud == null) return;
            originals["mod:" + part.Name] = new Point(part.DefaultX, part.DefaultY);
            Apply(new Part { Name = "mod:" + part.Name, Element = part.Element });
        });
        GameEvents.Update.Add(this, seconds =>
        {
            if (hud == null) return;
            if (Requested)
            {
                Requested = false;
                Windows.CloseOptions(true);      // as Accept: what else was changed is kept
                Windows.Close(Window.GameMenu);
                startIn = 2;                     // the HUD may be rebuilt by the Accept: start on a later frame
            }
            if (startIn >= 0 && startIn-- == 0 && overlay == null) Begin();
            if (config.Key != LayoutKey.None && Input.Pressed(KeyOf(config.Key))) { if (overlay == null) Begin(); else End(); }
            if (overlay == null) return;
            var m = Input.Mouse;
            hovered = PartAt(m);
            if (Input.MousePressed(MouseButton.Left) && hovered != null)
            {
                dragging = hovered; dragStart = m; partStart = new Point(hovered.Element.X, hovered.Element.Y);
            }
            if (dragging != null)
            {
                Ui.Move(dragging.Element, partStart.X + m.X - dragStart.X, partStart.Y + m.Y - dragStart.Y);
                if (Input.MouseReleased(MouseButton.Left))
                {
                    Settings.Set(Key(dragging.Name), dragging.Element.X + "," + dragging.Element.Y);
                    dragging = null;
                }
            }
            if (Input.MousePressed(MouseButton.Right) && hovered != null)
            {
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
            if (e != null) yield return new Part { Name = p.ToString(), Element = e };
        }
        foreach (var p in Hud.ModParts) yield return new Part { Name = "mod:" + p.Name, Element = p.Element };
    }

    void Apply(Part p)
    {
        var saved = Settings.Get(Key(p.Name));
        Point at;
        if (saved != null && TryParse(saved, out at)) Ui.Move(p.Element, at.X, at.Y);
    }

    void Begin()
    {
        overlay = Ui.Custom(hud.Width, hud.Height, Draw);     // covers the screen: clicks go to it, not the game
        Ui.Add(hud, overlay, 0, 0);
        var message = "Arranging the HUD: drag a part, right-click to put it back." + (config.Key != LayoutKey.None ? " " + config.Key + " or" : "");
        var done = Ui.Button("Done", End);
        int w = Ui.TextWidth(message) + done.Width + 40;
        help = Ui.Panel(w, 40);
        var text = Ui.Label(message, w - done.Width - 30);
        Ui.SetColor(text, new Color(230, 200, 120));
        Ui.Add(help, text, 10, 9);
        Ui.Add(help, done, w - done.Width - 8, 5);     // above the overlay: it gets the click
        Ui.Add(hud, help, (hud.Width - w) / 2, hud.Height / 2 - 20);
    }

    void End()
    {
        if (overlay != null) Ui.Remove(overlay);
        if (help != null) Ui.Remove(help);
        overlay = null; help = null; dragging = null;
    }

    void Draw(UiCanvas canvas)
    {
        canvas.Fill(canvas.Bounds, new Color(0, 0, 0, 70));
        foreach (var p in Parts())
        {
            if (!p.Element.Visible || p.Element.Width <= 0) continue;
            var r = p.Element.Bounds;
            bool hot = Same(p, hovered) || Same(p, dragging);
            if (hot) canvas.Fill(r, new Color(230, 160, 60, 60));
            canvas.Frame(r, hot ? new Color(255, 200, 90) : p.Name.StartsWith("mod:") ? new Color(120, 170, 230) : new Color(181, 92, 0));
        }
    }

    static bool Same(Part a, Part b) { return a != null && b != null && a.Name == b.Name; }

    Part PartAt(Point m)
    {
        Part found = null;
        foreach (var p in Parts()) if (p.Element.Visible && p.Element.Bounds.Contains(m)) found = p;     // the last one drawn on top
        return found;
    }

    string Key(string part) { return part + "@" + hud.Width + "x" + hud.Height; }
    static bool TryParse(string s, out Point p)
    {
        p = Point.Zero;
        var parts = s.Split(',');
        int x, y;
        if (parts.Length != 2 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y)) return false;
        p = new Point(x, y); return true;
    }
    static Keys KeyOf(LayoutKey k) { return k == LayoutKey.F11 ? Keys.F11 : k == LayoutKey.F6 ? Keys.F6 : k == LayoutKey.F12 ? Keys.F12 : Keys.None; }
}
