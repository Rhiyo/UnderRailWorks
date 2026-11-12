// UnderRailWorks: what makes up the player's chance to hit a target, by the game's own formula. The game previews the
// chance while the mouse is on a character (dka: euf.a with the distance, the target's light, darkvision, the loaded
// ammo, burst, attack modifiers awq.a). Works calls the same function again with one input changed: from 3 tiles away
// (no range penalty) and with the target fully lit. The differences say what range and darkness cost. Nothing else
// changes and nothing is rolled.
//   var s = Combat.ShotBreakdown(World.Hovered);
//   if (s != null) Log.Info((int)(s.Chance * 100) + "%, " + (int)(s.IfClose * 100) + "% from close, " + (int)(s.IfLit * 100) + "% if lit");
using System;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class Shot
    { internal Shot() { }
        public double Chance { get; internal set; }      // 0..1, as the game previews it
        public bool Ranged { get; internal set; }        // a gun, crossbow...: the range and light parts apply
        public double Distance { get; internal set; }    // tiles
        public double Light { get; internal set; }       // the target's light, 0..1
        public double IfClose { get; internal set; }     // the same shot from 3 tiles (no range penalty)
        public double IfLit { get; internal set; }       // the same shot at a fully lit target
    }

    public static partial class Combat
    {
        static readonly MethodInfo LightOf = typeof(c5x).GetMethod("l", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);

        // The player's plain attack on target with the weapon in hand (no special attack), or null (no target, no weapon).
        public static Shot ShotBreakdown(Character target)
        {
            try
            {
                var player = World.Player;
                if (player == null || target == null || target.IsPlayer || !(target.Entity is c5x) || !(player.Entity is c5x)) return null;
                var att = player.GameData; var tgt = target.GameData;
                var me = ((c5x)player.Entity).GetAspect<amc>(); var them = ((c5x)target.Entity).GetAspect<amc>();
                var weapon = att == null ? null : att.Offense.u();
                if (weapon == null || tgt == null || me == null || them == null) return null;
                var def = weapon.TypedDefinition;
                if (euf.b(def))     // melee: no range or light in the formula
                {
                    var melee = euf.a(att, tgt, awq.b(me, them, null), 1.0, (cd1)null);
                    return new Shot { Chance = melee.a, IfClose = melee.a, IfLit = melee.a };
                }
                double dist = aw1.b(player.Entity, target.Entity);
                double light = LightOf == null ? 1.0 : Convert.ToDouble(LightOf.Invoke(target.Entity, null));
                bool burst = awq.f(me);
                Func<double, double, double> chance = (d, l) => euf.a(att, tgt, Math.Min(d, def.ad()), l, att.Darkvision, weapon.CurrentBullet, burst, 1.0, awq.a(me, them, (cd1)null, (bool?)null)).a;
                return new Shot { Ranged = true, Distance = dist, Light = light, Chance = chance(dist, light), IfClose = chance(Math.Min(dist, 3), light), IfLit = chance(dist, 1.0) };
            }
            catch (Exception ex) { UnderRailWorks.Internal.Log.Write("combat: shot breakdown: " + ex.Message); return null; }
        }
    }
}
