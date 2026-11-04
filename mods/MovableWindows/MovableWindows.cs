// Movable Windows: an UnderRailWorks mod. The game's windows with a title bar (inventory, notes, crafting, map,
// oddities, psi, combat stats, barter, containers) can be dragged by their title bar. Each opens where you last left
// it (a setting), per HUD size; right-click a title bar to put that window back where the game places it. Built from
// Works' API only (Windows.OpenWindows, Input mouse, Ui, Settings).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class MovableWindowsConfig : ModConfig
{
    [Header("Movable Windows")]
    [Label("Windows open where you left them"), Tooltip("Off: they open where the game puts them (you can still drag them while open).")]
    public bool Remember = true;
}

public sealed class MovableWindows : WorksMod
{
    MovableWindowsConfig config;
    readonly HashSet<UiElement> seen = new HashSet<UiElement>();
    readonly Dictionary<UiElement, Point> gamePlace = new Dictionary<UiElement, Point>();
    OpenWindow dragging;
    Point dragStart, windowStart;

    public override void Load()
    {
        config = GetConfig<MovableWindowsConfig>();
        UiEvents.HudCreated.Add(this, root => { seen.Clear(); gamePlace.Clear(); dragging = null; });
        GameEvents.Update.Add(this, seconds =>
        {
            if (!Hud.Exists) return;
            var hud = Hud.Root;
            var open = Windows.OpenWindows;
            // Newly opened windows: where the game put them (for right-click), then where the player left this kind.
            seen.RemoveWhere(e => !open.Any(w => w.Element == e));
            foreach (var w in open)
            {
                if (!seen.Add(w.Element)) continue;
                gamePlace[w.Element] = new Point(w.Element.X, w.Element.Y);
                Point at;
                if (config.Remember && TryParse(Settings.Get(Key(w, hud)), out at)) Place(w, at, hud);
            }
            var m = Input.Mouse;
            if (dragging == null && Input.MousePressed(MouseButton.Left))
            {
                var w = TitleAt(open, m);
                if (w != null) { dragging = w; dragStart = m; windowStart = new Point(w.Element.X, w.Element.Y); }
            }
            if (dragging != null)
            {
                if (!open.Any(w => w.Element == dragging.Element)) { dragging = null; return; }     // closed meanwhile
                Place(dragging, new Point(windowStart.X + m.X - dragStart.X, windowStart.Y + m.Y - dragStart.Y), hud);
                if (!Input.MouseDown(MouseButton.Left))
                {
                    Settings.Set(Key(dragging, hud), dragging.Element.X + "," + dragging.Element.Y);
                    dragging = null;
                }
            }
            if (Input.MousePressed(MouseButton.Right))
            {
                var w = TitleAt(open, m);
                Point o;
                if (w != null && gamePlace.TryGetValue(w.Element, out o)) { Ui.Move(w.Element, o.X, o.Y); Settings.Set(Key(w, hud), ""); }
            }
        });
    }

    // The topmost window whose title bar (not its close button) is under the mouse.
    static OpenWindow TitleAt(IList<OpenWindow> open, Point m)
    {
        for (int i = open.Count - 1; i >= 0; i--)
        {
            var b = open[i].Element.Bounds;
            var title = new Rectangle(b.X, b.Y, b.Width - open[i].TitleHeight, open[i].TitleHeight);
            if (title.Contains(m)) return open[i];
            if (b.Contains(m)) return null;     // over a window in front: not the one behind
        }
        return null;
    }

    // Moved, but kept on screen: at least part of the title bar stays reachable.
    static void Place(OpenWindow w, Point at, UiElement hud)
    {
        int x = Math.Max(60 - w.Element.Width, Math.Min(hud.Width - 60, at.X));
        int y = Math.Max(0, Math.Min(hud.Height - w.TitleHeight, at.Y));
        Ui.Move(w.Element, x, y);
    }

    static string Key(OpenWindow w, UiElement hud) { return w.Kind + "@" + hud.Width + "x" + hud.Height; }
    static bool TryParse(string s, out Point p)
    {
        p = Point.Zero;
        var parts = (s ?? "").Split(',');
        int x, y;
        if (parts.Length != 2 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y)) return false;
        p = new Point(x, y); return true;
    }
}
