// UnderRailWorks: moving characters from code (scripted scenes, escorts, summons): walking somewhere the game's way
// (the movement aspect's order, as its AI moves) and placing a character at once (a0l.a, as its scripts teleport).
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed partial class Character
    {
        // Walks to tile x, y in its area (pathfinding, its speed; it may stop short if the way is blocked). False when it
        // can't take orders (no movement, not on a map) or the tile is outside the area. Not in turn-based combat.
        public bool MoveTo(int x, int y)
        {
            var c = Entity as c5x;
            var move = c == null ? null : c.GetAspect<ale>();
            var area = Area;
            if (move == null || area == null || x < 0 || y < 0 || x >= area.Width || y >= area.Height || GameClock.InTurnBasedCombat) return false;
            return move.a(Areas.Tile(area.Game, x, y), 0.0, 1, 0, true) != null;
        }

        // Puts it on tile x, y of its area at once (no walking, as the game's scripts move characters between scenes).
        public bool TeleportTo(int x, int y)
        {
            var c = Entity as c5x;
            var area = Area;
            if (c == null || area == null || x < 0 || y < 0 || x >= area.Width || y >= area.Height) return false;
            a0l.a(c, area.Game, new Vector3(x, y, 0), null, true);
            return true;
        }
    }
}

namespace UnderRailWorks
{
    // The defense values the character sheet's Defense tab shows (what StatBonus.Dodge / Evasion / Fortitude /
    // Resolve change; not the Dodge and Evasion skills): efk.Defense (nw) v() dodge, z() evasion, ab() fortitude,
    // ae() resolve.
    public sealed class DefenseStats
    {
        readonly nw d;
        internal DefenseStats(nw defense) { d = defense; }
        public int Dodge { get { return d.v(); } }
        public int Evasion { get { return d.z(); } }
        public int Fortitude { get { return d.ab(); } }
        public int Resolve { get { return d.ae(); } }
    }

    public sealed partial class Character
    {
        public DefenseStats Defense { get { var g = GameData; return g == null ? null : new DefenseStats(g.Defense); } }
        // Its initiative (efk.Offense ay(): what StatBonus.Initiative changes).
        public int Initiative { get { var g = GameData; return g == null ? 0 : g.Offense.ay(); } }
    }
}
