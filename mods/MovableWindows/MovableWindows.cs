// Movable Windows: an UnderRailWorks mod. The game's windows with a title bar (inventory, notes, crafting, map,
// oddities, psi, combat stats, barter, containers) can be dragged by their title bar, and clicking a window brings it
// to the front. Windows open where the game puts them. The game closes the windows on one side of the screen when
// another opens there (they'd overlap); a window you moved is out of the way, so it stays open (OpenWindow.StaysOpen)
// until you close it. Right-click a title bar to put the window back: it then closes with its side again. Built from
// Works' API only (Windows.OpenWindows, Input mouse, Ui).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.2.1")]

public sealed class MovableWindows : WorksMod
{
    readonly HashSet<UiElement> seen = new HashSet<UiElement>();
    readonly Dictionary<UiElement, Point> gamePlace = new Dictionary<UiElement, Point>();
    OpenWindow dragging;
    UiDrag drag;

    public override void Load()
    {
        UiEvents.HudCreated.Add(this, root => { seen.Clear(); gamePlace.Clear(); dragging = null; drag = null; });
        GameEvents.Update.Add(this, seconds =>
        {
            if (!Hud.Exists) return;
            var hud = Hud.Root;
            var open = Windows.OpenWindows;
            // Newly opened windows: where the game put them (for right-click).
            seen.RemoveWhere(e => !open.Any(w => w.Element == e));
            foreach (var w in open)
                if (seen.Add(w.Element)) gamePlace[w.Element] = new Point(w.Element.X, w.Element.Y);
            foreach (var e in gamePlace.Keys.Where(e => !seen.Contains(e)).ToList()) gamePlace.Remove(e);
            var m = Input.Mouse;
            // A click anywhere on a window brings it in front of the others.
            if (Input.MousePressed(MouseButton.Left) || Input.MousePressed(MouseButton.Right))
            {
                var hit = WindowAt(open, m);
                if (hit != null && hit.Element != open[open.Count - 1].Element) { Ui.ToFront(hit.Element); open = Windows.OpenWindows; }
            }
            if (dragging == null && Input.MousePressed(MouseButton.Left))
            {
                var w = TitleAt(open, m);
                if (w != null) { dragging = w; drag = UiDrag.Begin(w.Element); drag.Keep = (e, at) => Reachable(w, at, hud); }
            }
            if (dragging != null)
            {
                if (!open.Any(w => w.Element == dragging.Element)) { dragging = null; drag = null; return; }     // closed meanwhile
                bool held = drag.Update();
                Point o;     // moved away from the game's place: it stays open when another window opens on its side
                if (gamePlace.TryGetValue(dragging.Element, out o) && (dragging.Element.X != o.X || dragging.Element.Y != o.Y)) dragging.StaysOpen = true;
                if (!held) { dragging = null; drag = null; }
            }
            if (Input.MousePressed(MouseButton.Right))
            {
                var w = TitleAt(open, m);
                Point o;
                if (w != null && gamePlace.TryGetValue(w.Element, out o)) { Ui.Move(w.Element, o.X, o.Y); w.StaysOpen = false; }
            }
        });
    }

    // The topmost window under the mouse.
    static OpenWindow WindowAt(IList<OpenWindow> open, Point m)
    {
        for (int i = open.Count - 1; i >= 0; i--) if (open[i].Element.Bounds.Contains(m)) return open[i];
        return null;
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

    // Windows may go partly off screen, but at least 60 px of the title bar stays reachable.
    const int Reach = 60;
    static Point Reachable(OpenWindow w, Point at, UiElement hud)
    {
        return new Point(Math.Max(Reach - w.Element.Width, Math.Min(hud.Width - Reach, at.X)), Math.Max(0, Math.Min(hud.Height - w.TitleHeight, at.Y)));
    }
}
