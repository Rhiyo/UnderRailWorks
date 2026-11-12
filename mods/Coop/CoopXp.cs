// Co-op experience: Shared XP, the host's option when hosting (0-100 %, default 100). Whoever earns XP gets it in
// full; every other player gets that percentage of it. A joined character's kill: the game gives kill XP to the
// host's player, so it's moved to the killer (who then shares it). A conversation's XP goes to whoever held it
// (Works' TalkAs swaps the sheet in). Other mods can build other rules on Works' Progression.ModifyExperience.
using System;
using System.Collections.Generic;
using System.Linq;
using UnderRailWorks;

public sealed partial class Coop
{
    internal int xpShare = 100;
    bool sharing;
    Character lastKiller; double killTime, clock;

    void LoadXp()
    {
        int v; xpShare = int.TryParse(Settings.Get("xp.share"), out v) ? Math.Max(0, Math.Min(100, v)) : 100;
        GameEvents.Update.Add(this, dt => clock += dt);
        GameEvents.CharacterDying.Add(this, (dead, killer) =>
        {
            if (listener != null && killer != null && seats.Values.Concat(orphans).Any(s => s.Character == killer)) { lastKiller = killer; killTime = clock; }
        });
        Progression.ModifyExperience.Add(this, g =>
        {
            if (listener == null || sharing || !sharesXp || g.Who == null || g.Amount <= 0 || !Party.Contains(g.Who)) return;
            var members = Party.Members;
            if (members.Count < 2) return;
            var earner = g.Who;
            // Kill XP (no reason) the game gives the host's player right after a joined character's kill: the killer's.
            if (g.Who.IsPlayer && g.Reason == null && lastKiller != null && clock - killTime < 1.5 && lastKiller.Area != null) earner = lastKiller;
            int full = g.Amount, share = (int)Math.Round(full * xpShare / 100.0);
            if (earner != g.Who) { g.Amount = share; Give(earner, full); }
            foreach (var c in members.Where(c => c != earner && c != g.Who)) Give(c, share);
            Log.Info("XP " + full + (g.Reason != null ? " (" + g.Reason + ")" : "") + " earned by " + earner.Name + ", " + share + " to each other player (" + xpShare + "%)");
        });
    }

    void Give(Character c, int xp) { if (xp <= 0) return; sharing = true; try { c.GiveExperience(xp, "co-op"); } finally { sharing = false; } }
}
