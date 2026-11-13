// UnderRailWorks: NPC decisions in combat (sfall's FINDTARGET / BESTWEAPON hooks, RimWorld's think-tree patches). The
// game's side (measured from its code; ~/UnderRailWorks-research/specs/music-console-feats-ai.md section 4): an NPC's
// combat turn walks a tree of nodes, one per frame. Target selection is the node bq1 (it stores "PrimaryTarget" in
// the NPC's AI properties); every special attack, psi ability, item use and consumable goes through cp.b(proxy, user,
// target); the whole turn is stepped by e1k.avi() (false: still busy; true: the turn is over).
//   Ai.ChooseTarget.Add(this, c => c.Npc.Name.Contains("Rathound") ? World.Player : null);
//   Ai.Acting.Add(this, a => { if (a.Action == "Frag Grenade") a.Cancel = true; });
//   Ai.TakingTurn.Add(this, t => { if (frozen.Contains(t.Npc.Key)) t.EndTurn = true; });
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class TargetChoice
    { internal TargetChoice() { }
        public Character Npc { get; internal set; }
        // What the game chose (null: nothing to fight).
        public Character Chosen { get; internal set; }
    }

    public sealed class NpcAction : Cancellable
    { internal NpcAction() { }
        public Character Npc { get; internal set; }
        public string Action { get; internal set; }     // the capability's name (a special attack, psi ability, item use...)
        public Item Item { get; internal set; }         // for item uses
        public object Target { get; internal set; }     // a Character, an Item or null
        // Cancel (Cancellable): the NPC doesn't do it (its tree tries something else)
    }

    public sealed class NpcFlight
    { internal NpcFlight() { }
        public Character Npc { get; internal set; }
        public bool Fleeing { get; internal set; }     // the game's choice
    }

    public sealed class NpcTurn
    { internal NpcTurn() { }
        public Character Npc { get; internal set; }
        // True: the NPC's turn ends now, before it acts further.
        public bool EndTurn { get; set; }
    }

    public static class Ai
    {
        // A handler may return another character to fight (null: keep the game's choice). The lowest mod in the load order that answers wins.
        public static readonly Hook<Func<TargetChoice, Character>> ChooseTarget = new Hook<Func<TargetChoice, Character>>("Ai.ChooseTarget");
        public static readonly Hook<Action<NpcAction>> Acting = new Hook<Action<NpcAction>>("Ai.Acting");
        public static readonly Hook<Action<NpcTurn>> TakingTurn = new Hook<Action<NpcTurn>>("Ai.TakingTurn");
        // Whether an NPC flees this step (the game's reasons: fear, civilians in danger, Scrambled Cortex). A handler
        // returns true (flee), false (don't: it fights or acts normally) or null (the game's choice). The lowest mod in the load order that answers wins.
        public static readonly Hook<Func<NpcFlight, bool?>> ShouldFlee = new Hook<Func<NpcFlight, bool?>>("Ai.ShouldFlee");

        // Characters an NPC leaves out when it chooses whom to fight (handler(npc, candidate) true: not a candidate).
        // The game's own choice then runs on the rest: detected, hostile, nearest, in sight first. None left: it does
        // what it does with no enemy. Example: a co-op mod leaves out players who are down.
        [Preview("new")] public static readonly Hook<Func<Character, Character, bool>> IgnoresTarget = new Hook<Func<Character, Character, bool>>("Ai.IgnoresTarget");

        // The character an NPC's AI is after now (its memory's current target), or null. Set null: it chooses again.
        [Preview("new")] public static Character CurrentTarget(Character npc)
        {
            var ai = npc == null ? null : (npc.Entity as c5x)?.GetAspect<bm6>();
            var t = ai == null ? null : new bzv(ai.y6()).g();
            return t == null ? null : Character.Of(t.a8());
        }
        [Preview("new")] public static void SetCurrentTarget(Character npc, Character target)
        {
            var ai = npc == null ? null : (npc.Entity as c5x)?.GetAspect<bm6>();
            if (ai == null) return;
            new bzv(ai.y6()).a(target == null ? null : (target.Entity as c5x)?.GetAspect<amc>());
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class AiHooks
    {
        static readonly FieldInfo Found = typeof(bq1).GetField("b", BindingFlags.Instance | BindingFlags.Public | BindingFlags.DeclaredOnly);

        // Ai.IgnoresTarget: the left-out candidates count as passive (the game skips passive ones) during bq1.f7 only.
        static void Hide(bq1 __instance, out List<efk> __state)
        {
            __state = null;
            if (!Ai.IgnoresTarget.Any) return;
            try
            {
                var tactic = __instance.b();
                var npc = Character.Of(tactic.g().a8());
                foreach (var seen in new bzv(tactic.c().y6()).a().Values)
                {
                    var data = seen == null || seen.e() == null ? null : seen.e().aa();
                    if (data == null || data.Passive) continue;
                    var who = Character.Of(seen.e().a8());
                    bool ignore = false;
                    Ai.IgnoresTarget.RunUntil(h => ignore = h(npc, who));
                    if (!ignore) continue;
                    data.Passive = true;
                    (__state ?? (__state = new List<efk>())).Add(data);
                }
            }
            catch (Exception ex) { Log.Write("ai: IgnoresTarget: " + ex.Message); }
        }
        static Exception Unhide(Exception __exception, List<efk> __state)
        {
            if (__state != null) foreach (var d in __state) d.Passive = false;
            return __exception;
        }

        static void Target(bq1 __instance, ref abg __result)
        {
            if (!Ai.ChooseTarget.Any) return;
            try
            {
                var tactic = __instance.b();
                var self = tactic.g();
                var props = new bzv(tactic.c().y6());
                var current = props.g();
                var choice = new TargetChoice { Npc = Character.Of(self.a8()), Chosen = current == null ? null : Character.Of(current.a8()) };
                Character pick = null;
                Ai.ChooseTarget.RunUntil(h => (pick = h(choice)) != null);
                if (pick == null || pick == choice.Chosen) return;
                var amc = (pick.Entity as c5x)?.GetAspect<amc>();
                if (amc == null || World.IsPlaced(pick.Entity)) return;     // Works-placed characters vanish around saves: never a saved target
                props.a(amc);
                __result = (abg)Found.GetValue(__instance);
            }
            catch (Exception ex) { Log.Write("ai: " + ex.Message); }
        }

        static readonly FieldInfo Branches = typeof(fh).GetField("a", BindingFlags.Instance | BindingFlags.Public);
        static readonly FieldInfo Root = typeof(or).GetField("j", BindingFlags.Instance | BindingFlags.NonPublic), Flee = typeof(or).GetField("k", BindingFlags.Instance | BindingFlags.NonPublic);

        // The root selector of the game's combat tactic (or.j, a fh) picks branch or.k to flee.
        static void Choose(fh __instance, ref abg __result)
        {
            if (!Ai.ShouldFlee.Any) return;
            try
            {
                var tactic = __instance.b() as or;
                if (tactic == null || Root.GetValue(tactic) != __instance) return;
                var flee = (abg)Flee.GetValue(tactic);
                var self = tactic.g();
                if (self == null || flee == null) return;
                var q = new NpcFlight { Npc = Character.Of(self.a8()), Fleeing = __result == flee };
                bool? answer = null;
                Ai.ShouldFlee.RunUntil(h => (answer = h(q)) != null);
                if (answer == null || answer.Value == q.Fleeing) return;
                if (answer.Value) { __result = flee; return; }
                // Not fleeing: the selector's next branch whose condition holds.
                foreach (var item in (List<arr>)Branches.GetValue(__instance))
                    if (item.a != flee && (item.b == null || item.b(tactic))) { __result = item.a; return; }
            }
            catch (Exception ex) { Log.Write("ai: " + ex.Message); }
        }

        static bool Act(eu7 __0, c5x __1, object __2, ref ad5 __result)
        {
            if (!Ai.Acting.Any || __1 == null || World.Player == null || __1 == World.Player.Entity) return true;
            try
            {
                var cap = __0.p();
                var a = new NpcAction
                {
                    Npc = Character.Of(__1), Action = cap == null ? null : cap.Name, Item = __0.b9() is coh ? new Item((coh)__0.b9()) : null,
                    Target = __2 is coh ? (object)new Item((coh)__2) : __2 is c5x ? Character.Of((c5x)__2) : null,
                };
                Ai.Acting.Run(h => h(a));
                if (a.Cancel) { __result = null; return false; }
            }
            catch (Exception ex) { Log.Write("ai: " + ex.Message); }
            return true;
        }

        static bool Turn(e1k __instance, ref bool __result)
        {
            if (!Ai.TakingTurn.Any) return true;
            try
            {
                var self = __instance.g();
                if (self == null) return true;
                var t = new NpcTurn { Npc = Character.Of(self.a8()) };
                Ai.TakingTurn.Run(h => h(t));
                if (t.EndTurn) { __result = true; return false; }
            }
            catch (Exception ex) { Log.Write("ai: " + ex.Message); }
            return true;
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(AiHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(bq1).GetMethod("f7"), prefix: m("Hide"), postfix: m("Target"), finalizer: m("Unhide"));
            harmony.Patch(typeof(fh).GetMethod("f7"), postfix: m("Choose"));
            harmony.Patch(typeof(cp).GetMethod("b", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(eu7), typeof(c5x), typeof(object) }, null), prefix: m("Act"));
            harmony.Patch(typeof(e1k).GetMethod("avi", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic), prefix: m("Turn"));
        }
    }
}
