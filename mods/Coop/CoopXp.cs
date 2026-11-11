// Co-op experience, chosen by the host when they start hosting (Host this game asks):
//   Everyone   whatever anyone earns, everyone gets in full
//   Nearby     the one who earned it and those within 20 tiles of them get it in full
//   Split      it's divided between everyone
//   Earner     only who earned it (a joined character's kill: the game gives kill XP to the host's player, so it goes
//              to the killer instead; a conversation's XP goes to whoever held it, through Works' TalkAs)
// and for oddities (the Oddity system's studying): the same rule, or only the finder.
// Works' Progression.ModifyExperience runs before any player sheet takes XP (who, how much, why).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    enum XpMode { Everyone, Nearby, Split, Earner }
    static readonly string[] XpModeNames = { "Everyone, in full", "Full, if nearby", "Split between players", "Only who earned it" };
    XpMode xpMode;
    bool oddityFinder, sharing;
    const float NearbyTiles = 20;
    Character lastKiller; double killTime, clock;

    void LoadXp()
    {
        int m; xpMode = int.TryParse(Settings.Get("xp.mode"), out m) && m >= 0 && m <= 3 ? (XpMode)m : XpMode.Everyone;
        oddityFinder = Settings.Get("xp.oddity") == "1";
        GameEvents.Update.Add(this, dt => clock += dt);
        GameEvents.CharacterDying.Add(this, (dead, killer) =>
        {
            if (listener != null && killer != null && seats.Values.Concat(orphans).Any(s => s.Character == killer)) { lastKiller = killer; killTime = clock; }
        });
        Progression.ModifyExperience.Add(this, g =>
        {
            if (listener == null || sharing || g.Who == null || g.Amount <= 0 || !Party.Contains(g.Who)) return;
            var members = Party.Members;
            if (members.Count < 2) return;
            var earner = g.Who;
            // Kill XP (no reason) the game gives the host's player right after a joined character's kill: the killer's.
            if (g.Who.IsPlayer && g.Reason == null && lastKiller != null && clock - killTime < 1.5 && lastKiller.Area != null) earner = lastKiller;
            List<Character> to;
            if (g.FromOddity && oddityFinder) to = new List<Character> { earner };
            else switch (xpMode)
            {
                case XpMode.Nearby: to = members.Where(c => c == earner || Vector2.Distance(new Vector2(c.Position.X, c.Position.Y), new Vector2(earner.Position.X, earner.Position.Y)) <= NearbyTiles).ToList(); break;
                case XpMode.Earner: to = new List<Character> { earner }; break;
                default: to = members.ToList(); break;
            }
            int each = xpMode == XpMode.Split && !(g.FromOddity && oddityFinder) ? (int)Math.Ceiling(g.Amount / (double)to.Count) : g.Amount;
            int total = g.Amount;
            g.Amount = to.Contains(g.Who) ? each : 0;
            foreach (var c in to.Where(c => c != g.Who)) Give(c, each);
            Log.Info("XP " + total + (g.Reason != null ? " (" + g.Reason + ")" : "") + " earned by " + earner.Name + ": " + each + " each to " + string.Join(", ", to.Select(c => c.Name).ToArray()) + " [" + XpModeNames[(int)xpMode] + (g.FromOddity && oddityFinder ? ", oddity: finder" : "") + "]");
        });
    }

    void Give(Character c, int xp) { sharing = true; try { c.GiveExperience(xp, "co-op"); } finally { sharing = false; } }

    // Host this game: the experience rules first (remembered for next time), then hosting.
    void AskAndHost()
    {
        Ui.Choose(this, "Experience", "How is experience shared between the players?\n(now: " + XpModeNames[(int)xpMode] + ")", i =>
        {
            if (i < 0 || i > 3) return;
            xpMode = (XpMode)i;
            Ui.Choose(this, "Oddity experience", "Experience from studying oddities (the Oddity system):", j =>
            {
                if (j < 0 || j > 1) return;
                oddityFinder = j == 1;
                Settings.Set("xp.mode", ((int)xpMode).ToString()); Settings.Set("xp.oddity", oddityFinder ? "1" : "0");
                Commands.Print("co-op: " + Host(new string[0]));
            }, "Same as other experience", "Only the finder", "Cancel");
        }, XpModeNames[0], XpModeNames[1], XpModeNames[2], XpModeNames[3], "Cancel");
    }
}
