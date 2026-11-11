// Stealth Lens: an UnderRailWorks mod. While the player sneaks, a label over each hostile character in view says how
// close they can get before it starts noticing them (Stealth.Notice: the game's own detection rule, for the light the
// player stands in and the way it faces now): red "noticing you" when it's gaining on them already, amber when they're
// within 2 tiles of that line, green beyond. Built from Works' API only (Stealth.Notice, World.Visible,
// Character.ScreenBounds, Ui.Overlay).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class StealthLens : WorksMod
{
    sealed class Mark { public Character Who; public string Text; public Color Colour; }
    readonly List<Mark> marks = new List<Mark>();
    UiElement overlay;
    double sinceUpdate;

    public override void Load()
    {
        UiEvents.HudCreated.Add(this, root => overlay = null);
        GameEvents.Update.Add(this, Tick);
    }

    void Tick(double seconds)
    {
        if (!Hud.Exists) return;
        if (overlay == null) overlay = Ui.Overlay(Draw);
        sinceUpdate += seconds;
        if (sinceUpdate < 0.25) return;
        sinceUpdate = 0;
        marks.Clear();
        var player = World.Player;
        if (player == null || !player.HasStatusEffect("stealthMode")) return;
        foreach (var c in World.Visible.Where(c => c.IsHostile && !c.IsDead))
        {
            var n = Stealth.Notice(c);
            if (n == null || n.Distance > n.SightRange + 4) continue;
            if (n.Noticing) marks.Add(new Mark { Who = c, Text = T("sees"), Colour = new Color(240, 80, 70) });
            else if (n.Within <= 0) marks.Add(new Mark { Who = c, Text = T("never"), Colour = new Color(110, 200, 110) });
            else marks.Add(new Mark { Who = c, Text = T("within", n.Within.ToString("0.#")), Colour = n.Distance - n.Within <= 2 ? new Color(240, 180, 70) : new Color(110, 200, 110) });
        }
    }

    void Draw(UiCanvas c)
    {
        foreach (var m in marks)
        {
            var b = m.Who.ScreenBounds;
            if (b == null) continue;
            var r = b.Value;
            int lh = Ui.LineHeight, w = Ui.TextWidth(m.Text) + 10, x = r.Center.X - w / 2, y = r.Top - lh - 4;
            c.Fill(new Rectangle(x, y, w, lh), new Color(8, 8, 8, 200));
            c.Text(m.Text, x + 5, y + 1, m.Colour);
        }
    }
}
