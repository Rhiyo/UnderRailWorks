// HUD Layout: an UnderRailWorks mod. Press the key (default F12) to arrange the game's HUD: frames show its parts;
// drag one with the left mouse button, right-click one to put it back, Reset all for every part; Done (or the key) to finish. Each part's
// place is kept relative to the nearest screen edge or the centre (Ui.Place), so a layout survives resolution and
// interface-scale changes and stays on screen; it is applied whenever the game builds its HUD (also after a resolution
// change). Built from Works' API only (Hud handles, Ui.Custom, Ui.Place/Restore, Input mouse, Settings).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class HudLayoutConfig : ModConfig
{
    [Header("HUD Layout")]
    [Label("Show a Layout button on the HUD"), Tooltip("A small button that starts arranging; it can be moved like the rest.")]
    public bool HudButton = false;

    public override void OnChanged() { HudLayout.Changed = true; }

}


public sealed class HudLayout : WorksMod
{
    static readonly HudPart[] GameParts = (HudPart[])Enum.GetValues(typeof(HudPart));

    // One movable part: the game's (by HudPart) or a mod's (by its name).
    sealed class Part { public string Name; public UiElement Element; public Point Original; }

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
            foreach (var p in Parts()) { originals[p.Name] = new Point(p.Element.X, p.Element.Y); Apply(p); }
            ShowButton();
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
            if (Changed) { Changed = false; ShowButton(); }
            if (arrangeKey.Pressed) { if (overlay == null) Begin(); else End(); }
            if (overlay == null) return;
            var m = Input.Mouse;
            hovered = PartAt(m);
            if (Input.MousePressed(MouseButton.Left) && hovered != null)
            {
                dragging = hovered; dragStart = m; partStart = new Point(hovered.Element.X, hovered.Element.Y);
            }
            if (dragging != null)
            {
                var to = Ui.KeepInside(dragging.Element, partStart.X + m.X - dragStart.X, partStart.Y + m.Y - dragStart.Y);
                Ui.Move(dragging.Element, to.X, to.Y);
                if (Input.MouseReleased(MouseButton.Left))
                {
                    Settings.Set(Key(dragging.Name), Ui.Place(dragging.Element));
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
        Hud.AddPart(this, "Layout button", layoutButton, hud.Width - layoutButton.Width - 70, 62);
    }

    // Every part back where it was, and every saved place cleared (all HUD sizes).
    void ResetAll()
    {
        foreach (var k in Settings.Keys) if (k.Contains("@") || k.StartsWith("at.")) Settings.Set(k, "");
        foreach (var p in Parts())
        {
            Point o;
            if (originals.TryGetValue(p.Name, out o)) Ui.Move(p.Element, o.X, o.Y);
            Settings.Set(Key(p.Name), "");
        }
    }

    void Begin()
    {
        overlay = Ui.Custom(hud.Width, hud.Height, Draw);     // covers the screen: clicks go to it, not the game
        Ui.Add(hud, overlay, 0, 0);
        var message = "Arranging the HUD: drag a part, right-click it to put it back.";
        var reset = Ui.Button("Reset all", ResetAll);
        var done = Ui.Button("Done", End);
        int w = Ui.TextWidth(message) + reset.Width + done.Width + 50;
        help = Ui.Panel(w, 40);
        var text = Ui.Label(message, w - reset.Width - done.Width - 40);
        Ui.SetColor(text, new Color(230, 200, 120));
        Ui.Add(help, text, 10, 9);
        Ui.Add(help, reset, w - done.Width - reset.Width - 14, 5);     // above the overlay: they get the clicks
        Ui.Add(help, done, w - done.Width - 8, 5);
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

    static string Key(string part) { return "at." + part; }
    static bool TryParse(string s, out Point p)
    {
        p = Point.Zero;
        var parts = s.Split(',');
        int x, y;
        if (parts.Length != 2 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y)) return false;
        p = new Point(x, y); return true;
    }
}
