// UnderRailWorks: characters controlled from code instead of their AI (a co-op player, a possessed NPC, a scripted
// scene). A controlled character's AI only keeps its awareness (what it sees and hears): it doesn't wander, follow,
// flee or fight by itself, and in combat its turns wait (no 15 s limit, unlike an AI turn) until EndTurn.
//   CharacterControl.Take(this, buddy);
//   buddy.MoveTo(x, y);            // in combat: on its turn, spending its movement points then AP, as the player's walk
//   buddy.Attack(rathound);        // its weapon, the game's attack (AP, range, line of fire)
//   buddy.EndTurn();
//   CharacterControl.Release(buddy);        // its AI again
// The game's side: the AI aspect bm6 steps per frame in r7 (awareness a(eds), then the combat step b(eds) or the
// out-of-combat tactic m_c.avh()); b(eds) ends an AI turn after d0u.a (15 s) from its first step (m_ab).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static class CharacterControl
    {
        sealed class Owners : IModOwned
        {
            public void RemoveOwner(string mod) { lock (Taken) foreach (var k in Taken.Where(kv => kv.Value == mod).Select(kv => kv.Key).ToList()) Taken.Remove(k); }
        }
        // Entity -> controlling mod. Entities are new on each zone load (a controlled character is taken again then).
        internal static readonly Dictionary<b0b, string> Taken = new Dictionary<b0b, string>();
        static CharacterControl() { lock (ModScope.Hooks) ModScope.Hooks.Add(new Owners()); }

        // Its AI stops (see the file's header). False: not a character (or the player, who is the player's already).
        public static bool Take(WorksMod mod, Character c)
        {
            if (c == null || c.IsPlayer || !(c.Entity is c5x) || ((c5x)c.Entity).GetAspect<amc>() == null) return false;
            lock (Taken) Taken[c.Entity] = mod.Id;
            return true;
        }
        public static void Release(Character c)
        {
            if (c == null) return;
            lock (Taken) Taken.Remove(c.Entity);
            ControlHooks.ResetTurnClock(c.Entity as c5x);
        }
        public static bool IsControlled(Character c) { lock (Taken) return c != null && Taken.ContainsKey(c.Entity); }
        // The controlled characters in the current zone.
        public static IList<Character> All { get { lock (Taken) return Taken.Keys.Where(e => e.Area != null).Select(e => Character.Of(e)).ToList(); } }
    }

    public sealed partial class Character
    {
        // In combat: this character's turn now.
        public bool IsTakingTurn { get { var c = Combat.Current; return c != null && c.Entity == Entity; } }

        // An attack on target with what it holds, as the player's click (the game checks AP, range and line of fire, and
        // refuses with its own message). A character in the fight only on its turn; one outside it attacks at once, which
        // brings it into the fight (as the player shooting first). False: it can't attack now.
        public bool Attack(Character target)
        {
            var me = Entity as c5x; var them = target == null ? null : target.Entity as c5x;
            var a = me == null ? null : me.GetAspect<amc>(); var b = them == null ? null : them.GetAspect<amc>();
            if (a == null || b == null || IsDead || target.IsDead || (Combat.InCombat && IsInCombat && !IsTakingTurn)) return false;
            return a.b(b) != null;
        }

        // Ends its combat turn (as the player's End Turn: leftover AP carries over up to 10). False: not its turn.
        public bool EndTurn()
        {
            var m = Combat.Manager;
            if (m == null || !m.ahg() || !IsTakingTurn) return false;
            var e = m.ahe();
            if (e == null) return false;
            e.f();
            return true;
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class ControlHooks
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
        static readonly MethodInfo Awareness = typeof(bm6).GetMethod("a", Any, null, new[] { typeof(eds) }, null);
        // m_ab: when an AI turn times out (a field named "ab": the decompile shows m_ab beside a method ab).
        static readonly FieldInfo TurnClock = typeof(bm6).GetField("ab", Any) ?? typeof(bm6).GetField("m_ab", Any);

        // bm6.r7: a controlled character keeps only its awareness.
        static bool Step(bm6 __instance, eds __0)
        {
            if (CharacterControl.Taken.Count == 0) return true;
            var e = __instance.a8();
            bool mine;
            lock (CharacterControl.Taken) mine = e != null && CharacterControl.Taken.ContainsKey(e);
            if (!mine) return true;
            try { if (Awareness != null) Awareness.Invoke(__instance, new object[] { __0 }); }
            catch (Exception ex) { Log.Write("control: awareness: " + ex.Message); }
            return false;
        }

        // Released mid-turn: its AI starts the turn's clock afresh (not timed from when it was first taken).
        internal static void ResetTurnClock(c5x e)
        {
            var ai = e == null ? null : e.GetAspect<bm6>();
            if (ai != null && TurnClock != null) try { TurnClock.SetValue(ai, null); } catch { }
        }

        // Walking past others. A walk step (bpr.fr) stops for good where its body's footprint meets another character's
        // (the collision handler's kq says characters collide) and waits there; the player and the AI get round it by
        // ordering again, a mod's character doesn't. So while a controlled character steps, the player and other
        // controlled characters don't stop it; in a frozen world (positions set from elsewhere) nobody stops anybody.
        [ThreadStatic] static b0b stepping;
        static void StepStarting(bpr __instance) { var m = __instance.l(); stepping = m == null ? null : m.a8(); }
        static void StepDone() { stepping = null; }
        static void Passable(b0b __0, ref bool? __result)
        {
            var m = stepping;
            var other = __0 as c5x;
            if (m == null || other == null || other == m || other.GetAspect<amc>() == null) return;
            if (Simulation.Frozen) { __result = false; return; }
            bool mine, theirs;
            lock (CharacterControl.Taken) { mine = CharacterControl.Taken.ContainsKey(m); theirs = CharacterControl.Taken.ContainsKey(other); }
            if (mine && (other.IsPlayer || theirs)) __result = false;
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var step = typeof(bpr).GetMethod("fr", Any, null, new[] { typeof(TimeSpan), typeof(TimeSpan) }, null);
            if (step != null) harmony.Patch(step, prefix: new HarmonyLib.HarmonyMethod(typeof(ControlHooks).GetMethod("StepStarting", BindingFlags.Static | BindingFlags.NonPublic)), finalizer: new HarmonyLib.HarmonyMethod(typeof(ControlHooks).GetMethod("StepDone", BindingFlags.Static | BindingFlags.NonPublic)));
            foreach (var t in typeof(a7o).Assembly.GetTypes().Where(t => typeof(a7o).IsAssignableFrom(t) && !t.IsInterface && !t.IsAbstract))
            {
                var kq = t.GetMethod("kq", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(b0b) }, null);
                if (kq != null) harmony.Patch(kq, postfix: new HarmonyLib.HarmonyMethod(typeof(ControlHooks).GetMethod("Passable", BindingFlags.Static | BindingFlags.NonPublic)));
            }
            harmony.Patch(typeof(bm6).GetMethod("r7", Any, null, new[] { typeof(eds) }, null), prefix: new HarmonyLib.HarmonyMethod(typeof(ControlHooks).GetMethod("Step", BindingFlags.Static | BindingFlags.NonPublic)));
            Log.Write("control: hooks in place (awareness " + (Awareness != null) + ", turn clock " + (TurnClock != null) + ")");
        }
    }
}
