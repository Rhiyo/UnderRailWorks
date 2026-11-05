// UnderRailWorks: traps. The game has no dice here: disarming (action ds, ds.fr) compares the character's effective
// Traps skill with the trap's disarm difficulty (ed2.d): skill >= difficulty disarms it, >= difficulty + 20 also
// recovers the trap item, <= difficulty - 15 sets it off ("Critical failure!"), anything between fails. Spotting a trap
// uses a detection score (qw.a(efk): max(Detection x 0.75, Detection x 0.5 + Traps x 0.75)) against the trap's
// detection difficulty, every couple of seconds.
//   GameEvents.TrapDisarming.Add(this, d => { if (d.Character.HasFeat(feat.Key)) d.Difficulty -= 10; });
//   GameEvents.TrapDisarmed.Add(this, r => { if (r.Outcome == DisarmOutcome.CriticalFailure) Notify.OverPlayer("Ouch"); });
//   Traps.ModifyDetection.Add(this, (who, score) => who.IsPlayer ? score + 5 : score);
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public enum DisarmOutcome { Disarmed, Recovered, Failed, CriticalFailure }

    // A disarm attempt about to be decided: change Difficulty for this attempt only (the trap keeps its own), or Cancel
    // (nothing happens; the action ends).
    public sealed class TrapDisarm : Cancellable
    {
        public Character Character { get; internal set; }
        public Vector3 Position { get; internal set; }
        public int Skill { get; internal set; }          // the character's effective Traps skill
        public int Difficulty { get; set; }
        public bool OwnTrap { get; internal set; }       // the character's own trap: always disarmed and recovered
    }

    // A trap a character has just spotted (the player sees "Trap detected!").
    public sealed class TrapSpotted
    {
        public Character Character { get; internal set; }
        public Vector3 Position { get; internal set; }
        public int DetectDifficulty { get; internal set; }
        public int DisarmDifficulty { get; internal set; }
        public Trap Trap { get; internal set; }
    }

    public sealed class TrapDisarmResult
    {
        public Character Character { get; internal set; }
        public Vector3 Position { get; internal set; }
        public int Skill { get; internal set; }
        public int Difficulty { get; internal set; }
        public DisarmOutcome Outcome { get; internal set; }
    }

    public static partial class GameEvents
    {
        public static readonly Hook<Action<TrapDisarm>> TrapDisarming = new Hook<Action<TrapDisarm>>("TrapDisarming");
        public static readonly Hook<Action<TrapDisarmResult>> TrapDisarmed = new Hook<Action<TrapDisarmResult>>("TrapDisarmed");
        // A character spotted a trap (its scan every couple of seconds; the player's included).
        public static readonly Hook<Action<TrapSpotted>> TrapDetected = new Hook<Action<TrapSpotted>>("TrapDetected");
    }

    // A trap in the world (laid by a character or placed in the map).
    public sealed class Trap
    {
        internal c5x Entity; internal ed2 Aspect;
        public Vector3 Position { get { return Entity.Location; } }
        public int DetectDifficulty { get { return TrapHooks.Detect(Aspect); } }
        public int DisarmDifficulty { get { return TrapHooks.Get(Aspect); } }
        // Has this character spotted it (or laid it).
        public bool KnownTo(Character c) { return c != null && c.Entity != null && TrapHooks.Spotted(Aspect).Contains(c.Entity.qe()); }
        // The character tries to disarm it, as clicking the trap does: the game's disarm capability (bb3), queued as
        // the character's next action (it walks there first; the game's checks and time). False if it can't be queued.
        public bool Disarm(Character who)
        {
            var c = who == null ? null : who.Entity as c5x;
            var data = c == null ? null : c.GetAspect<amc>();
            var cap = dgk.Get<bb3>();     // what a click on a trap uses (dka: a trap under the cursor -> bb3)
            if (data == null || cap == null) return false;
            var proxy = ecf.a(cap, new eru(new Ouroboros.Playfield.References.EntityAspectReference<amc, amc>(data)), null) as eu7;
            return proxy != null && cp.b(proxy, c, Entity) != null;
        }
    }

    public static class Traps
    {
        // The traps in the current zone (all its areas).
        public static IList<Trap> InZone
        {
            get
            {
                var list = new List<Trap>();
                var zone = Areas.CurrentZone();
                if (zone == null) return list;
                foreach (var area in zone.Areas)
                    foreach (var e in area.Entities)
                    {
                        var c = e as c5x; var t = c == null ? null : c.GetAspect<b4h>() as ed2;
                        if (t != null) list.Add(new Trap { Entity = c, Aspect = t });
                    }
                return list;
            }
        }

        // A character's trap detection score (the player's is then x1.25 against the trap's difficulty): each handler
        // gets the score so far and returns the new one. Also what the character sheet shows.
        public static readonly Hook<Func<Character, int, int>> ModifyDetection = new Hook<Func<Character, int, int>>("Traps.ModifyDetection");
    }
}

namespace UnderRailWorks.Internal
{
    static class TrapHooks
    {
        sealed class State { public ed2 Trap; public int Difficulty; public TrapDisarm Attempt; }
        // ed2.d: the disarm difficulty (a field and a method share the name).
        static readonly FieldInfo DisarmDifficulty = typeof(ed2).GetField("d", BindingFlags.Instance | BindingFlags.Public);
        internal static int Get(ed2 t) { return (int)DisarmDifficulty.GetValue(t); }
        internal static int Detect(ed2 t) { return (int)DetectField.GetValue(t); }
        internal static HashSet<Guid> Spotted(ed2 t) { return (HashSet<Guid>)SpottedBy.GetValue(t); }
        static void Set(ed2 t, int v) { DisarmDifficulty.SetValue(t, v); }

        static bool Disarming(ds __instance, ref dc4 __result, ref State __state)
        {
            __state = null;
            if (!GameEvents.TrapDisarming.Any && !GameEvents.TrapDisarmed.Any) return true;
            try
            {
                var ctx = __instance.vg();
                var target = ctx == null ? null : ctx.h() as c5x;
                var actor = ctx == null ? null : ctx.q();
                var trap = target == null ? null : target.GetAspect<b4h>() as ed2;
                var who = actor == null ? null : actor.GetAspect<amc>();
                if (trap == null || who == null) return true;
                var data = who.aa();
                int difficulty = Get(trap);
                var e = new TrapDisarm { Character = Character.Of(actor), Position = Character.Of(target).Position, Skill = data.Skills.Get<c0b>().g(), Difficulty = difficulty, OwnTrap = trap.a() == who };
                __state = new State { Trap = trap, Difficulty = difficulty, Attempt = e };
                if (GameEvents.TrapDisarming.Any) GameEvents.TrapDisarming.Run(h => h(e));
                if (e.Cancel) { __state = null; __result = dc4.c; return false; }
                if (e.Difficulty != difficulty) Set(trap, e.Difficulty);     // for this attempt; put back in the finalizer
            }
            catch (Exception ex) { Log.Write("traps: " + ex.Message); }
            return true;
        }

        static Exception Disarmed(State __state, Exception __exception)
        {
            if (__state == null) return __exception;
            var e = __state.Attempt;
            try
            {
                Set(__state.Trap, __state.Difficulty);
                if (__exception == null && GameEvents.TrapDisarmed.Any)
                {
                    var outcome = e.OwnTrap || e.Skill >= e.Difficulty + 20 ? DisarmOutcome.Recovered : e.Skill >= e.Difficulty ? DisarmOutcome.Disarmed
                        : e.Skill <= e.Difficulty - 15 ? DisarmOutcome.CriticalFailure : DisarmOutcome.Failed;
                    var r = new TrapDisarmResult { Character = e.Character, Position = e.Position, Skill = e.Skill, Difficulty = e.Difficulty, Outcome = outcome };
                    GameEvents.TrapDisarmed.Run(h => h(r));
                }
            }
            catch (Exception ex) { Log.Write("traps: " + ex.Message); }
            return __exception;
        }

        // amc.a(List<ak1>): a character's scan of the tiles around it. Traps not yet spotted by it before, spotted after.
        static readonly FieldInfo SpottedBy = typeof(ed2).GetField("a", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);
        static readonly FieldInfo DetectField = typeof(ed2).GetField("c", BindingFlags.Instance | BindingFlags.Public);
        static void Scanning(amc __instance, List<ak1> __0, ref List<KeyValuePair<c5x, ed2>> __state)
        {
            __state = null;
            if (!GameEvents.TrapDetected.Any || __0 == null) return;
            try
            {
                var id = ((b0b)__instance.a8()).qe();
                foreach (var tile in __0)
                    foreach (var e in tile.Entities)
                    {
                        var c = e as c5x; var trap = c == null ? null : c.GetAspect<b4h>() as ed2;
                        if (trap == null || ((HashSet<Guid>)SpottedBy.GetValue(trap)).Contains(id)) continue;
                        (__state ?? (__state = new List<KeyValuePair<c5x, ed2>>())).Add(new KeyValuePair<c5x, ed2>(c, trap));
                    }
            }
            catch (Exception ex) { Log.Write("traps: " + ex.Message); }
        }
        static void Scanned(amc __instance, List<KeyValuePair<c5x, ed2>> __state)
        {
            if (__state == null) return;
            try
            {
                var id = ((b0b)__instance.a8()).qe();
                Character who = null;
                foreach (var kv in __state)
                {
                    if (!((HashSet<Guid>)SpottedBy.GetValue(kv.Value)).Contains(id)) continue;
                    if (who == null) who = Character.Of((b0b)__instance.a8());
                    var e = new TrapSpotted { Character = who, Position = Character.Of(kv.Key).Position, DetectDifficulty = (int)DetectField.GetValue(kv.Value), DisarmDifficulty = Get(kv.Value), Trap = new Trap { Entity = kv.Key, Aspect = kv.Value } };
                    GameEvents.TrapDetected.Run(h => h(e));
                }
            }
            catch (Exception ex) { Log.Write("traps: " + ex.Message); }
        }

        // The detection score qw.a(efk) is tiny, so the JIT inlines it into its callers: a postfix on it never ran for
        // the real scan (measured: the player's score stayed 25 against a 50 trap), and invoking it by reflection runs
        // the original. So its two callers (the scan amc.a(List<ak1>) and the character sheet dm1.b()) are rewritten to
        // call Score, which takes the game's value and applies Traps.ModifyDetection once.
        static readonly MethodInfo ScoreMethod = typeof(qw).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(efk) }, null);
        public static int Score(efk data)
        {
            int v = (int)ScoreMethod.Invoke(null, new object[] { data });
            if (!Traps.ModifyDetection.Any) return v;
            try
            {
                var who = Combat.OfData(data);
                if (who == null) return v;
                Traps.ModifyDetection.Run(h => v = h(who, v));
                return Math.Max(0, v);
            }
            catch (Exception ex) { Log.Write("traps: " + ex.Message); return v; }
        }
        static IEnumerable<HarmonyLib.CodeInstruction> UseScore(IEnumerable<HarmonyLib.CodeInstruction> code, MethodBase __originalMethod)
        {
            var score = typeof(TrapHooks).GetMethod("Score", BindingFlags.Static | BindingFlags.Public);
            int n = 0;
            foreach (var i in code)
            {
                if (i.opcode == System.Reflection.Emit.OpCodes.Call && Equals(i.operand, ScoreMethod)) { i.operand = score; n++; }
                yield return i;
            }
            if (n != 1) Log.Write("traps: " + __originalMethod.DeclaringType.Name + "." + __originalMethod.Name + ": the detection score call found " + n + " times");
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(TrapHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(ds).GetMethod("fr", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(TimeSpan), typeof(TimeSpan) }, null), prefix: m("Disarming"), finalizer: m("Disarmed"));
            harmony.Patch(typeof(dm1).GetMethod("b", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null), transpiler: m("UseScore"));
            harmony.Patch(typeof(amc).GetMethod("a", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(List<ak1>) }, null), prefix: m("Scanning"), postfix: m("Scanned"), transpiler: m("UseScore"));
        }
    }
}
