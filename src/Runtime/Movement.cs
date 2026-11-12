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
        // can't take orders (no movement, not on a map) or the tile is outside the area or not floor. In turn-based combat only on its
        // own turn (the whole zone is turn-based: everyone with an AI is in the turn order), spending movement points then AP as the player's walk does (it stops when they run out).
        public bool MoveTo(int x, int y)
        {
            var c = Entity as c5x;
            var move = c == null ? null : c.GetAspect<ale>();
            var area = Area;
            if (move == null || area == null || x < 0 || y < 0 || x >= area.Width || y >= area.Height || (GameClock.InTurnBasedCombat && !IsTakingTurn)) return false;
            if (area.KindAt(x, y, false) != TileKind.Floor) return false;     // a wall or nothing to stand on: the game would quietly not walk
            return move.a(Areas.Tile(area.Game, x, y), 0.0, 1, 0, true) != null;
        }

        // As MoveTo, but it keeps going: a walk the game cuts short (another character in the way: the walk step stops
        // when two bodies' footprints meet, and only the player's clicks or an AI would order it again) is ordered again
        // from where it stopped, a few times, until it's there. For characters a mod drives (CharacterControl).
        public bool WalkTo(int x, int y)
        {
            if (!MoveTo(x, y)) return false;
            lock (Walks) Walks[this] = new Walk { X = x, Y = y };
            return true;
        }
        internal sealed class Walk { public int X, Y, Tries; public double Idle; }
        internal static readonly System.Collections.Generic.Dictionary<Character, Walk> Walks = new System.Collections.Generic.Dictionary<Character, Walk>();
        internal static void WalkTick(double seconds)
        {
            if (Walks.Count == 0) return;
            lock (Walks)
                foreach (var kv in Walks.ToList())
                {
                    var c = kv.Key; var w = kv.Value;
                    var at = c.Position;
                    if (c.IsDead || c.Area == null || (Math.Abs(at.X - w.X) < 0.6f && Math.Abs(at.Y - w.Y) < 0.6f)) { Walks.Remove(c); continue; }
                    if (c.IsWalking) { w.Idle = 0; continue; }
                    if ((w.Idle += seconds) < 0.3) continue;
                    w.Idle = 0;
                    if (++w.Tries > 6 || !c.MoveTo(w.X, w.Y)) Walks.Remove(c);
                }
        }

        // Puts it on tile x, y of its area at once (no walking, as the game's scripts move characters between scenes).
        public bool TeleportTo(int x, int y)
        {
            if (Simulation.Refuses("TeleportTo")) return false;
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
        // In its secondary weapon set now (the game's swap: X). SwitchWeapons() swaps as the key does.
        public bool UsingSecondaryWeapon { get { var g = GameData; return g != null && Equals(Internal.CancelHooks.CurrentWeapon.GetValue(g.Offense), TimelapseVertigo.Rules.Characters.WeaponKey.Secondary); } }
        public void SwitchWeapons() { var g = GameData; if (g != null) g.Offense.k(); }
        // Its initiative (efk.Offense ay(): what StatBonus.Initiative changes).
        public int Initiative { get { var g = GameData; return g == null ? 0 : g.Offense.ay(); } }
    }
}
