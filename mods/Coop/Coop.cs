// Co-op: an UnderRailWorks mod (in development; design and open questions: docs/Multiplayer.md in the Works repo).
// Step 1, no network: a second character in the host's game, controlled from the console instead of its AI
// (CharacterControl), to see what the game does with it: walking, combat turns, attacks, enemies' targets.
//   coop.spawn [blueprint]   an ally next to you, controlled (default a Junkyard gunslinger)
//   coop.go x y              it walks there (in combat: on its turn, with its movement points)
//   coop.attack <name>       it attacks the nearest living character whose name contains <name>
//   coop.end                 ends its combat turn
//   coop.status              where it is, health, AP/MP, whose turn
//   coop.attitudes           how everyone around stands towards it
//   coop.release / coop.take its AI back / controlled again
// Networking (host, join, the character list) lives in this mod, not in Works.
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("0.1.0")]

public sealed class Coop : WorksMod
{
    Character partner;

    public override void Load()
    {
        Commands.Add(this, "coop.spawn", "coop.spawn [blueprint]: a controlled ally next to you", Spawn);
        Commands.Add(this, "coop.go", "coop.go x y: the partner walks to tile x, y", args =>
        {
            int x, y;
            if (Missing() || args.Length < 2 || !int.TryParse(args[0], out x) || !int.TryParse(args[1], out y)) return Missing() ? "no partner" : "coop.go x y";
            bool ok = partner.MoveTo(x, y);
            Log.Info("go " + x + "," + y + ": " + (ok ? "walking" : "refused") + Points());
            return ok ? "walking" : "can't walk there now" + (Combat.InCombat && !partner.IsTakingTurn ? " (not its turn)" : "");
        });
        Commands.Add(this, "coop.attack", "coop.attack <name>: the partner attacks the nearest character with that name", args =>
        {
            if (Missing()) return "no partner";
            var name = string.Join(" ", args);
            var target = World.CharactersNear(partner.Position, 40).FirstOrDefault(c => c != partner && !c.IsPlayer && !c.IsDead && c.Name != null && c.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
            if (target == null) return "nobody called " + name;
            bool ok = partner.Attack(target);
            Log.Info("attack " + target.Name + " (" + target.Health + " HP): " + (ok ? "attacking" : "refused") + Points());
            return ok ? "attacking " + target.Name : "can't attack now";
        });
        Commands.Add(this, "coop.end", "coop.end: ends the partner's combat turn", args =>
        {
            if (Missing()) return "no partner";
            bool ok = partner.EndTurn();
            Log.Info("end turn: " + (ok ? "ended" : "not its turn"));
            return ok ? "turn ended" : "not its turn";
        });
        Commands.Add(this, "coop.status", "coop.status: the partner's state", args => { var s = Status(); Log.Info(s); return s; });
        Commands.Add(this, "coop.attitudes", "coop.attitudes: how the characters around stand towards the partner (and it towards them)", args =>
        {
            if (Missing()) return "no partner";
            var s = string.Join("; ", World.CharactersNear(partner.Position, 30).Where(c => c != partner).Select(c => c.Name + " -> partner " + c.AttitudeTo(partner) + ", partner -> " + partner.AttitudeTo(c)).ToArray());
            Log.Info("attitudes: " + s);
            return s;
        });
        Commands.Add(this, "coop.release", "coop.release: the partner's own AI again", args => { if (Missing()) return "no partner"; CharacterControl.Release(partner); return "released"; });
        Commands.Add(this, "coop.take", "coop.take: control the partner again", args => { if (Missing()) return "no partner"; return CharacterControl.Take(this, partner) ? "controlled" : "can't"; });

        // Observing what the game does with it.
        Combat.TurnStarted.Add(this, c => { if (partner != null && c == partner) Log.Info("partner's turn" + Points()); });
        Combat.AttackResolved.Add(this, o =>
        {
            if (partner == null) return;
            if (o.AttackerName == partner.Name || o.Target == partner)
                Log.Info(o.AttackerName + " -> " + (o.Target == null ? "?" : o.Target.Name) + ": " + (o.Hit ? "hit" : "miss") + (o.Critical ? " (critical)" : "") + ", " + o.HealthLost + " HP" + (o.Killed ? ", killed" : ""));
        });
        GameEvents.CharacterDying.Add(this, (who, killer) => { if (who == partner) Log.Info("partner dying (killed by " + (killer == null ? "?" : killer.Name) + ")"); });
        GameEvents.ZoneLoaded.Add(this, (zone, first) => { if (partner != null && partner.Area == null) { Log.Info("partner left behind (zone changed)"); partner = null; } });
    }

    string Spawn(string[] args)
    {
        var p = World.Player;
        if (p == null) return "no game";
        var blueprint = args.Length > 0 ? args[0] : "creatures\\generichumans\\jkkgunslinger";
        var at = p.Position;
        var tile = World.FreeTilesNear((int)at.X, (int)at.Y, 4).Where(t => Math.Abs(t.X - at.X) + Math.Abs(t.Y - at.Y) >= 2).OrderBy(t => Math.Abs(t.X - at.X) + Math.Abs(t.Y - at.Y)).FirstOrDefault();
        if (tile == Point.Zero) return "no room next to you";
        var c = World.Spawn(blueprint, tile.X, tile.Y, SpawnSide.Ally);
        if (c == null) return "couldn't spawn " + blueprint;
        c.Name = "Partner";
        // The player's own faction: everyone treats it as they treat the player. (Not playerNativeAlly, the spawn's
        // ally faction: that one is for force fields and turrets, which enemies only target when nothing else is near.)
        c.Faction = "player";
        partner = c;
        CharacterControl.Take(this, c);
        Log.Info("partner spawned: " + blueprint + " at " + tile.X + "," + tile.Y + ", faction " + c.Faction + ", controlled " + CharacterControl.IsControlled(c));
        return "partner at " + tile.X + "," + tile.Y;
    }

    bool Missing() { if (partner != null && (partner.IsDead || partner.Area == null)) partner = null; return partner == null; }

    string Points() { return Combat.InCombat ? " (AP " + Combat.ActionPoints(partner) + ", MP " + Combat.MovementPoints(partner) + ")" : ""; }

    string Status()
    {
        if (Missing()) return "no partner";
        var cur = Combat.Current;
        return "partner at " + partner.Position.X + "," + partner.Position.Y + ", " + partner.Health + " HP, controlled " + CharacterControl.IsControlled(partner)
            + (Combat.InCombat ? ", turn: " + (cur == null ? "?" : cur.Name) + Points() : ", no combat");
    }
}
