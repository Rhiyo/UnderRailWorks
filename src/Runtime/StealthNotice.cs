// UnderRailWorks: how close the player can get to a character before it starts noticing them, by the game's own
// detection rule (cn8, the AI's sensor, each scan: bui.a(target, observer Detection, target Stealth skill, distance,
// observer SightRange, angle from the observer's facing, the target's light, observer darkvision) > 0 means the
// observer is noticing; bui.a(detection, stealth) true means it spots a sneaking target at once). Works calls the
// same functions with the distance changed to find where the rate turns positive. Nothing is changed or rolled.
//   var n = Stealth.Notice(enemy);
//   if (n != null) Log.Info(enemy.Name + (n.Noticing ? " is noticing you" : " notices you within " + n.Within + " tiles"));
using System;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class Notice
    { internal Notice() { }
        public double Distance { get; internal set; }     // tiles from the observer to the player now
        public double SightRange { get; internal set; }   // the observer's
        public bool Instant { get; internal set; }        // its Detection beats the player's Stealth so far it sees them at once
        public double Within { get; internal set; }       // from here in (same light and angle) it gains on the player (0: never)
        public bool Noticing { get { return Instant ? Distance <= SightRange : Distance <= Within; } }
    }

    public static partial class Stealth
    {
        // The observer's view of the player as they stand now (null: no player, or not characters).
        public static Notice Notice(Character observer)
        {
            try
            {
                var player = World.Player;
                if (player == null || observer == null || observer.IsPlayer) return null;
                var obs = observer.Entity as c5x; var me = player.Entity as c5x;
                var o = observer.GameData; var p = player.GameData;
                if (obs == null || me == null || o == null || p == null) return null;
                int detection = o.Detection, stealth = p.Skills.Get<a2n>().g();
                double sight = o.SightRange;
                double distance = bnx.a(d6e.a(obs.aj()), me);
                double angle = bnx.a(d6e.b(obs.Location), obs.VisualDirection, d6e.b(me.Location));
                double light = me.l();
                if (bui.a(detection, stealth)) return new Notice { Distance = distance, SightRange = sight, Instant = true, Within = sight };
                // The rate falls with distance: the largest distance (0.1-tile steps) where it's still positive.
                double within = 0;
                for (double d = sight; d > 0; d -= 0.1)
                    if (bui.a(p, detection, stealth, d, sight, angle, light, o.Darkvision) > 0) { within = Math.Round(d, 1); break; }
                return new Notice { Distance = distance, SightRange = sight, Within = within };
            }
            catch (Exception ex) { UnderRailWorks.Internal.Log.Write("stealth: notice: " + ex.Message); return null; }
        }

        // Whether the observer would gain on the player if the player stood on that tile now, in the light the player
        // stands in now (the game's detection rule with that tile's distance and angle; walls between aren't checked).
        // For drawing where an enemy would notice: one rule check per tile, no search.
        public static bool NoticesAt(Character observer, Point tile)
        {
            try
            {
                var player = World.Player;
                if (player == null || observer == null || observer.IsPlayer) return false;
                var obs = observer.Entity as c5x; var me = player.Entity as c5x;
                var o = observer.GameData; var p = player.GameData;
                if (obs == null || me == null || o == null || p == null) return false;
                int detection = o.Detection, stealth = p.Skills.Get<a2n>().g();
                double sight = o.SightRange;
                double distance = bnx.a(d6e.a(obs.aj()), tile);
                if (distance > sight) return false;
                if (bui.a(detection, stealth)) return true;
                double angle = bnx.a(d6e.b(obs.Location), obs.VisualDirection, new Vector2(tile.X, tile.Y));
                return bui.a(p, detection, stealth, distance, sight, angle, me.l(), o.Darkvision) > 0;
            }
            catch (Exception ex) { UnderRailWorks.Internal.Log.Write("stealth: notices at: " + ex.Message); return false; }
        }
    }
}
