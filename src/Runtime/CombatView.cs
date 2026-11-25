// UnderRailWorks: a fight as the game's HUD shows it, taken from one game and shown in a copy (a co-op joiner's).
//   host:   var view = Combat.View();  send(view.Fighters.Select(f => key(f.Who) + " " + f.ActionPoints + " " + f.MovementPoints) ...)
//   copy:   Simulation.ShowCombat(new CombatView { Active = true, Current = 1, Fighters = { ... } });
// The game's side: the combat manager (crp) keeps the turn order (d4n entries: fighter, AP, MP), the index of whose
// turn it is, its state (azz: a no fight, b running, c starting) and phase (bh3: b someone acting). The HUD reads only
// those: the turn panel (AP, MP, whose turn), the AP bar, the End Turn button; ps.am5 / am6 switch the HUD into and out
// of combat mode. In a copy nothing ticks the manager (Simulation.Frozen), so the view just sits in it until the next.
// Ending a turn there (End Turn button, its key, the game's automatic end at 0 AP and MP) is a PlayerOrder (EndTurn)
// for the mod to send on; starting or ending a fight there is refused (the other game decides).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    [Preview("multiplayer: built for Co-op so far")] public sealed class CombatFighter
    {
        public Character Who; public int ActionPoints; public int MovementPoints;
    }

    [Preview("multiplayer: built for Co-op so far")] public sealed class CombatView
    {
        public bool Active;
        public List<CombatFighter> Fighters = new List<CombatFighter>();     // in turn order
        public int Current = -1;                                             // index in Fighters of whose turn it is
        public int Round;                                                    // Combat.Round
    }

    public static partial class Combat
    {
        // The fight as the HUD shows it now (Active false: no fight).
        [Preview("multiplayer: built for Co-op so far")] public static CombatView View()
        {
            var v = new CombatView();
            var m = Manager;
            if (m == null || !m.ahg()) return v;
            v.Active = true;
            v.Round = Combat.Round;
            var cur = m.ahe();
            foreach (var e in CombatViewHooks.Order(m))
            {
                var f = Fighter(e);
                var c = f == null ? null : Character.Of(f.a8());
                if (c == null) continue;
                if (e == cur) v.Current = v.Fighters.Count;
                v.Fighters.Add(new CombatFighter { Who = c, ActionPoints = (int)Ap(e), MovementPoints = (int)Mp(e) });
            }
            return v;
        }
    }

    public static partial class Simulation
    {
        // In a copy (IsMirror): the HUD shows this fight (turn panel, AP bar, End Turn) as the other game has it.
        // Fighters not in this game are left out. Inactive: out of combat mode.
        public static void ShowCombat(CombatView view)
        {
            var m = Combat.Manager;
            if (m == null || view == null) return;
            try { CombatViewHooks.Show(m, view); }
            catch (Exception ex) { Log.Write("combat view: " + ex.Message); }
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class CombatViewHooks
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly FieldInfo OrderField = typeof(crp).GetFields(Any).First(f => f.FieldType == typeof(List<d4n>));
        static readonly FieldInfo ByFighter = typeof(crp).GetFields(Any).First(f => f.FieldType == typeof(Dictionary<efk, d4n>));
        static readonly FieldInfo State = typeof(crp).GetFields(Any).First(f => f.FieldType == typeof(azz));
        static readonly FieldInfo Phase = typeof(crp).GetFields(Any).First(f => f.FieldType == typeof(bh3));
        static readonly FieldInfo Index = typeof(crp).GetFields(Any).First(f => f.FieldType == typeof(int));     // the first int: whose turn (measured: f)
        static readonly FieldInfo Ended = typeof(crp).GetFields(Any).Where(f => f.FieldType == typeof(bool)).Skip(1).First();     // i: the current turn ended, waiting to pass
        static readonly FieldInfo FighterField = typeof(d4n).GetField("b");
        static readonly ConstructorInfo EntryCtor = typeof(d4n).GetConstructor(new[] { typeof(crp), typeof(amc) });

        internal static List<d4n> Order(crp m) { return (List<d4n>)OrderField.GetValue(m); }

        [ThreadStatic] static bool showing;
        internal static void Show(crp m, CombatView view)
        {
            var ps = bpo.GetService<ps>();
            bool was = m.ahg();
            showing = true;
            try
            {
                if (!view.Active)
                {
                    if (!was) return;
                    State.SetValue(m, azz.a); Index.SetValue(m, -1);
                    Order(m).Clear(); ((Dictionary<efk, d4n>)ByFighter.GetValue(m)).Clear();
                    if (ps != null) ps.am6();
                    return;
                }
                var map = (Dictionary<efk, d4n>)ByFighter.GetValue(m);
                var order = new List<d4n>();
                int current = -1;
                for (int i = 0; i < view.Fighters.Count; i++)
                {
                    var f = view.Fighters[i];
                    var e = f.Who == null ? null : f.Who.Entity as c5x;
                    var a = e == null ? null : e.GetAspect<amc>();
                    var data = a == null ? null : a.aa();
                    if (data == null) continue;
                    d4n entry;
                    if (!map.TryGetValue(data, out entry) || FighterField.GetValue(entry) != a) entry = (d4n)EntryCtor.Invoke(new object[] { m, a });
                    Combat.SetAp(entry, f.ActionPoints); Combat.SetMp(entry, f.MovementPoints);
                    if (i == view.Current) current = order.Count;
                    order.Add(entry);
                }
                map.Clear();
                foreach (var e in order) map[((amc)FighterField.GetValue(e)).aa()] = e;
                var list = Order(m); list.Clear(); list.AddRange(order);
                Index.SetValue(m, current);
                Combat.Round = view.Round;     // a copy's manager never ends a round itself
                Phase.SetValue(m, bh3.b);
                Ended.SetValue(m, false);
                State.SetValue(m, azz.b);
                if (!was && ps != null) ps.am5();
            }
            finally { showing = false; }
        }

        // On a copy: ending a turn is an order for the other game; starting or ending a fight is refused.
        static bool EndingTurn(crp __instance, d4n __0, bool __1, bool __5)
        {
            if (!Simulation.IsMirror || Simulation.Applying || showing) return true;
            try
            {
                var me = World.Player;
                var f = __0 == null ? null : FighterField.GetValue(__0) as amc;
                if (me != null && __1 && __5 && f != null && Character.Of(f.a8()) == me && __0 == __instance.ahe())
                {
                    var o = new PlayerOrder { Kind = PlayerOrderKind.EndTurn };
                    GameEvents.PlayerOrdering.Run(h => h(o));
                }
            }
            catch (Exception ex) { Log.Write("combat view: end turn: " + ex.Message); }
            return false;
        }
        static bool Starting() { return !Simulation.IsMirror || Simulation.Applying; }
        static bool Ending(ref bool __result) { if (!Simulation.IsMirror || Simulation.Applying) return true; __result = false; return false; }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Func<string, HarmonyLib.HarmonyMethod> h = n => new HarmonyLib.HarmonyMethod(typeof(CombatViewHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(crp).GetMethod("ahq", Any), prefix: h("EndingTurn"));
            harmony.Patch(typeof(crp).GetMethod("ahn", Any), prefix: h("Starting"));
            harmony.Patch(typeof(crp).GetMethod("ahs", Any), prefix: h("Ending"));
        }
    }
}
