// Shot Breakdown: an UnderRailWorks mod. While the mouse rests on a hostile character, a small note by the cursor shows
// what range and darkness take off the player's hit chance, computed with the game's own formula (Combat.ShotBreakdown:
// the same shot from 3 tiles, and at a fully lit target). Nothing shows when neither costs anything. Built from Works'
// API only.
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class ShotBreakdown : WorksMod
{
    UiElement note;
    readonly List<string> lines = new List<string>();
    double sinceUpdate;

    public override void Load()
    {
        UiEvents.HudCreated.Add(this, root => note = null);
        GameEvents.Update.Add(this, Tick);
    }

    void Tick(double seconds)
    {
        if (!Hud.Exists) return;
        if (note == null)
        {
            note = Ui.Custom(260, 44, c =>
            {
                if (lines.Count == 0) return;
                var b = c.Bounds;
                c.Fill(new Rectangle(b.X, b.Y, b.Width, 6 + 18 * lines.Count), new Color(8, 8, 8, 215));
                for (int i = 0; i < lines.Count; i++) c.Text(lines[i], b.X + 6, b.Y + 3 + 18 * i, new Color(230, 190, 120));
            });
            Ui.Add(Hud.Root, note, 0, 0);
        }
        sinceUpdate += seconds;
        if (sinceUpdate >= 0.2)
        {
            sinceUpdate = 0;
            lines.Clear();
            var target = World.Hovered;
            var shot = target != null && target.IsHostile ? Combat.ShotBreakdown(target) : null;
            if (shot != null && shot.Ranged)
            {
                int now = Pct(shot.Chance), close = Pct(shot.IfClose), lit = Pct(shot.IfLit);
                if (close > now) lines.Add(T("range", now - close, close));
                if (lit > now) lines.Add(T("dark", now - lit, lit));
            }
            int w = 12;
            foreach (var l in lines) w = Math.Max(w, Ui.TextWidth(l) + 12);
            Ui.Resize(note, w, 6 + 18 * Math.Max(1, lines.Count));
        }
        var m = Input.Mouse;
        Ui.Move(note, m.X + 24, m.Y + 34);     // below the game's own "xx%" by the cursor
        Ui.ToFront(note);
    }

    static int Pct(double chance) { return (int)Math.Round(chance * 100); }
}
