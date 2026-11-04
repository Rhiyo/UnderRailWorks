// HUD Layout: an UnderRailWorks mod. Press the key (default F12) to arrange the game's HUD: frames show its parts;
// drag one with the left mouse button, right-click one to put it back; press the key again to finish. Positions are
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
    [Label("Key that starts and ends arranging"), Tooltip("While arranging: drag a part with the left mouse button, right-click it to put it back.")]
    public LayoutKey Key = LayoutKey.F12;
}

public enum LayoutKey { F12, F11, F6 }

public sealed class HudLayout : WorksMod
{
    static readonly HudPart[] Parts = (HudPart[])Enum.GetValues(typeof(HudPart));
    HudLayoutConfig config;
    UiElement hud, overlay;
    UiPanel help;
    readonly Dictionary<HudPart, Point> original = new Dictionary<HudPart, Point>();
    HudPart? dragging;
    Point dragStart, partStart;
    HudPart? hovered;

    public override void Load()
    {
        config = GetConfig<HudLayoutConfig>();
        UiEvents.HudCreated.Add(this, root =>
        {
            hud = root; overlay = null; help = null; dragging = null; original.Clear();
            foreach (var p in Parts)
            {
                var e = Hud.Get(p);
                if (e == null) continue;
                original[p] = new Point(e.X, e.Y);
                var saved = Settings.Get(Key(p));
                Point at;
                if (saved != null && TryParse(saved, out at)) Ui.Move(e, at.X, at.Y);
            }
        });
        GameEvents.Update.Add(this, seconds =>
        {
            if (hud == null) return;
            if (Input.Pressed(KeyOf(config.Key))) { if (overlay == null) Begin(); else End(); }
            if (overlay == null) return;
            var m = Input.Mouse;
            hovered = PartAt(m);
            if (Input.MousePressed(MouseButton.Left) && hovered != null)
            {
                var e = Hud.Get(hovered.Value);
                dragging = hovered; dragStart = m; partStart = new Point(e.X, e.Y);
            }
            if (dragging != null)
            {
                var e = Hud.Get(dragging.Value);
                Ui.Move(e, partStart.X + m.X - dragStart.X, partStart.Y + m.Y - dragStart.Y);
                if (Input.MouseReleased(MouseButton.Left))
                {
                    Settings.Set(Key(dragging.Value), e.X + "," + e.Y);
                    dragging = null;
                }
            }
            if (Input.MousePressed(MouseButton.Right) && hovered != null)
            {
                Point o;
                if (original.TryGetValue(hovered.Value, out o)) Ui.Move(Hud.Get(hovered.Value), o.X, o.Y);
                Settings.Set(Key(hovered.Value), "");
            }
        });
    }

    void Begin()
    {
        overlay = Ui.Custom(hud.Width, hud.Height, Draw);     // covers the screen: clicks go to it, not the game
        Ui.Add(hud, overlay, 0, 0);
        var message = "Arranging the HUD: drag a part, right-click to put it back, " + config.Key + " to finish.";
        int w = Ui.TextWidth(message) + 30;
        help = Ui.Panel(w, 30);
        var text = Ui.Label(message, w - 20);
        Ui.SetColor(text, new Color(230, 200, 120));
        Ui.Add(help, text, 10, 4);
        Ui.Add(hud, help, (hud.Width - w) / 2, hud.Height / 2 - 15);
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
        foreach (var p in Parts)
        {
            var e = Hud.Get(p);
            if (e == null || !e.Visible || e.Width <= 0) continue;
            var r = e.Bounds;
            bool hot = p == hovered || p == dragging;
            if (hot) canvas.Fill(r, new Color(230, 160, 60, 60));
            canvas.Frame(r, hot ? new Color(255, 200, 90) : new Color(181, 92, 0));
        }
    }

    HudPart? PartAt(Point m)
    {
        foreach (var p in Parts.Reverse())     // later parts draw on top
        {
            var e = Hud.Get(p);
            if (e != null && e.Visible && e.Bounds.Contains(m)) return p;
        }
        return null;
    }

    string Key(HudPart p) { return p + "@" + hud.Width + "x" + hud.Height; }
    static bool TryParse(string s, out Point p)
    {
        p = Point.Zero;
        var parts = s.Split(',');
        int x, y;
        if (parts.Length != 2 || !int.TryParse(parts[0], out x) || !int.TryParse(parts[1], out y)) return false;
        p = new Point(x, y); return true;
    }
    static Keys KeyOf(LayoutKey k) { return k == LayoutKey.F11 ? Keys.F11 : k == LayoutKey.F6 ? Keys.F6 : Keys.F12; }
}
