// UnderRailWorks: deaths that don't have to happen, and game over on the mod's terms (co-op downed players, a second
// chance, a respawn).
//   GameEvents.Dying.Add(this, d => { if (d.Who.IsPlayer) d.Cancel = true; });   // left at 1 health instead
//   GameEvents.GameOver.Add(this, g => g.Cancel = true);                          // the game goes on
//   World.EndGame();                                                              // the game's own game over, now
// The game's side: every death goes through efk.a(cwt, cru, bool) (blows and scripts: Dead set, the death event, the
// dying action d3q, a corpse); the game over is dgz.cr(f8), which calls dka.y() for any character of the player's
// faction (a co-op partner's death would end the game).
using System;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    [Preview("multiplayer: built for Co-op so far")] public sealed class DeathAttempt : Cancellable { public Character Who { get; internal set; } }
    [Preview("multiplayer: built for Co-op so far")] public sealed class GameOverAttempt : Cancellable { public Character Who { get; internal set; } }

    public static partial class GameEvents
    {
        // A character is about to die (any cause). Cancel: it doesn't; it's left at 1 health.
        [Preview("multiplayer: built for Co-op so far")] public static readonly Hook<Action<DeathAttempt>> Dying = new Hook<Action<DeathAttempt>>("Dying");
        // The game is about to end (a death in the player's faction: Who). Cancel: it goes on.
        [Preview("multiplayer: built for Co-op so far")] public static readonly Hook<Action<GameOverAttempt>> GameOver = new Hook<Action<GameOverAttempt>>("GameOver");
    }

    public static partial class World
    {
        // The game's own game over (its death screen: Resume, Load, Quit), now.
        [Preview("multiplayer: built for Co-op so far")] public static bool EndGame()
        {
            var screen = bpo.GetService<ps>() as dka;
            if (screen == null || DeathHooks.End == null) return false;
            DeathHooks.End.Invoke(screen, null);
            return true;
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class DeathHooks
    {
        internal static readonly MethodInfo End = typeof(dka).GetMethod("y", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public, null, Type.EmptyTypes, null);

        static bool Dies(efk __instance)
        {
            if (!GameEvents.Dying.Any || __instance.Dead) return true;
            try
            {
                var who = System.Linq.Enumerable.FirstOrDefault(World.Characters, c => c.GameData == __instance);
                if (who == null) return true;
                var d = new DeathAttempt { Who = who };
                GameEvents.Dying.Run(h => h(d));
                if (!d.Cancel) return true;
                __instance.Defense.q().zp(1);
                return false;
            }
            catch (Exception ex) { Log.Write("death: " + ex.Message); return true; }
        }

        static bool Over(f8 __0)
        {
            if (!GameEvents.GameOver.Any) return true;
            try
            {
                var g = new GameOverAttempt { Who = null };
                GameEvents.GameOver.Run(h => h(g));
                return !g.Cancel;
            }
            catch (Exception ex) { Log.Write("game over: " + ex.Message); return true; }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(DeathHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(efk).GetMethod("a", Any, null, new[] { typeof(cwt), typeof(cru), typeof(bool) }, null), prefix: m("Dies"));
            harmony.Patch(typeof(dgz).GetMethod("cr", Any, null, new[] { typeof(f8) }, null), prefix: m("Over"));
            Log.Write("death: hooks in place (end " + (End != null) + ")");
        }
    }
}
