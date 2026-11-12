// Co-op deaths. The host picks the rule when hosting (Host this game...):
//   Get up after the fight   a player at 0 health is downed (incapacitated, 1 health) and gets up at a third of their
//                            health when the fight ends (at once if there's no fight)
//   Others revive them       downed until another player standing next to them revives them (Revive, Co-op window)
//   Dead for good            they die, as in single player; a joined player picks or makes another character
// The host's character follows the same rule. Game over (the game's own) only when every player is down or dead, or
// when the host is down or dead and nobody else is playing (the last one leaving ends it).
// Works: GameEvents.Dying (cancel: left at 1 health), GameEvents.GameOver (cancel: the game goes on), World.EndGame.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    internal int deathRule;     // 0 get up after the fight, 1 others revive, 2 dead for good
    static readonly string[] DeathRules = { "Get up after the fight", "Others revive them", "Dead for good" };
    readonly HashSet<Character> downed = new HashSet<Character>();
    const float ReviveTiles = 2.5f;
    bool ending;

    void LoadDeath()
    {
        int v; deathRule = int.TryParse(Settings.Get("death.rule"), out v) && v >= 0 && v <= 2 ? v : 0;
        AddOption(new HostOption { Mod = Id, Key = "death", Label = "When a player's health runs out", Names = DeathRules, Value = deathRule });

        GameEvents.Dying.Add(this, d =>
        {
            if (listener == null || d.Who == null || !Party.Contains(d.Who)) return;
            if (deathRule == 2) { Notify.Log("Co-op: " + d.Who.Name + " died", Bad); return; }
            d.Cancel = true;
            Down(d.Who);
        });
        GameEvents.CharacterDying.Add(this, (dead, killer) =>
        {
            // Dead for good: a joined player's character is gone; they choose again.
            if (listener == null || deathRule != 2 || dead == null) return;
            var seat = seats.Values.FirstOrDefault(s => s.Character == dead);
            if (seat == null) return;
            if (seat.Record != null) { records.Remove(seat.Record); }
            CharacterControl.Release(dead); Party.Remove(dead);
            seat.Character = null; seat.Record = null; seat.InWorld = false;
            seat.Peer.Send("SAID", dead.Name + " is dead. Choose or make another character.");
            seat.Peer.Send("DIED");
            SendList(seat.Peer);
            CheckGameOver();
        });
        // The game's game over (a death in the player's faction): only when nobody's left playing.
        GameEvents.GameOver.Add(this, g => { if (listener != null && !ending && AnyoneStanding()) g.Cancel = true; });
        GameEvents.CombatEnded.Add(this, () => { if (listener != null && deathRule == 0) foreach (var c in downed.ToList()) Revive(c, null); });
        double outOfFight = 0;
        GameEvents.Update.Add(this, dt =>
        {
            if (listener == null || downed.Count == 0 || deathRule != 0 || Combat.InCombat) { outOfFight = 0; return; }
            if ((outOfFight += dt) < 3) return;     // downed outside a fight (a trap, a fall): up again after a moment
            outOfFight = 0;
            foreach (var c in downed.ToList()) Revive(c, null);
        });
        // Enemies leave a downed player alone: not a candidate when they choose whom to fight (the game's own choice runs
        // on everyone else it has detected).
        Ai.IgnoresTarget.Add(this, (npc, who) => listener != null && who != null && downed.Contains(who));
        // ...and any action aimed at a downed player is called off (the NPC's tree tries something else).
        Ai.Acting.Add(this, a => { var t = a.Target as Character; if (listener != null && t != null && downed.Contains(t)) a.Cancel = true; });
        // A downed joined character's turn passes; the host's own is theirs to end (the game shows it as incapacitated).
        Combat.TurnStarted.Add(this, c => { if (listener != null && c != null && downed.Contains(c) && !c.IsPlayer) c.EndTurn(); });
    }

    void Down(Character c)
    {
        if (!downed.Add(c)) return;
        c.Health = 1;
        StatusEffects.Apply(c, "incapacitated", 0, true);
        // Enemies after them drop them and choose again (IgnoresTarget leaves them out).
        foreach (var e in World.CharactersNear(c.Position, 60).Where(e => e != c && !e.IsDead && Ai.CurrentTarget(e) == c)) Ai.SetCurrentTarget(e, null);
        Notify.Log("Co-op: " + c.Name + " is down" + (deathRule == 1 ? ": another player can revive them (Revive, next to them)" : ": they'll get up when the fight is over"), Bad);
        foreach (var s in seats.Values.Where(x => x.Peer != null)) s.Peer.Send("SAID", (s.Character == c ? "you're" : c.Name + " is") + " down" + (deathRule == 1 ? " (another player can revive " + (s.Character == c ? "you" : "them") + ")" : " until the fight is over"));
        if (c.IsTakingTurn && !c.IsPlayer) c.EndTurn();
        CheckGameOver();
    }

    // By: who revives (null: the rule's own getting up).
    bool Revive(Character c, Character by)
    {
        if (c == null || !downed.Remove(c)) return false;
        foreach (var e in c.StatusEffects.Where(e => e.Code == "incapacitated").ToList()) e.Remove();
        c.Health = Math.Max(1, (int)(c.MaxHealth / 3));
        var text = c.Name + " is back up" + (by != null ? " (" + by.Name + ")" : "");
        Notify.Log("Co-op: " + text, Say);
        foreach (var s in seats.Values.Where(x => x.Peer != null)) s.Peer.Send("SAID", text);
        return true;
    }

    // A player reviving someone downed next to them (Others revive them).
    string ReviveNear(Character by)
    {
        if (deathRule != 1) return "players get up by themselves after the fight in this game";
        if (downed.Contains(by)) return "you're down yourself";
        if (Combat.InCombat && !by.IsTakingTurn) return "not your turn";
        var c = downed.FirstOrDefault(d => d != by && Dist(d, by) <= ReviveTiles);
        if (c == null) return "nobody down next to you";
        Revive(c, by);
        if (Combat.InCombat) by.EndTurn();     // reviving takes the turn
        return "";
    }

    bool AnyoneStanding()
    {
        foreach (var c in Party.Members) if (!c.IsDead && !downed.Contains(c)) return true;
        return false;
    }

    // Everyone down or dead, or the host down/dead with nobody else playing: the game's game over.
    void CheckGameOver()
    {
        if (listener == null || ending) return;
        var host = World.Player;
        bool hostOut = host == null || host.IsDead || downed.Contains(host);
        bool othersPlaying = seats.Values.Any(s => s.Character != null && !s.Character.IsDead);
        if (AnyoneStanding() && !(hostOut && !othersPlaying)) return;
        ending = true;
        Notify.Log("Co-op: everyone is down", Bad);
        World.EndGame();
    }
}
