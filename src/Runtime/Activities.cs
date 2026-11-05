// UnderRailWorks: the game clock and pause, using items, crafting, pickpocketing. The game's side (measured from its
// code; ~/UnderRailWorks-research/specs/clock-items-crafting.md):
// - Time: the game state (cyt) keeps two saved clocks: world time (p(), "Play time": un-paused zone time at the game's
//   speed, running in real time even in turn-based combat; merchant restocks and quest timers use it) and turn time
//   (o(): frozen in turn-based combat but for 5 s at the end of each player turn; out of combat a "turn" is 5 s).
//   d6f.a(object, c4i) runs once per un-paused frame with the frame's game time. ps.ak4(): paused (dialogs, the menu,
//   the character sheet...; not inventory, loot, barter or crafting).
// - Items are used through capabilities: cp.a(proxy, user, target) queues the action (all users, NPCs too); the
//   use is committed in dea.dt() of a consumable action (a62), the stack already taken.
// - Crafting: ahu.i() takes the components and returns the results (cfe: a items, d crafted); the player crafts.
// - Pickpocketing: each item goes through rc.kj (b87.b adds the item's suspicion b87.a(thief, victim, item, k) to
//   the victim; caught at 1.5, no dice); rc.a(item) a success, rc.a() caught.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static class GameClock
    {
        // The game's world clock (the save screen's "Play time"; what restocks and quest timers count).
        public static TimeSpan WorldTime { get { var s = State; return s == null ? TimeSpan.Zero : s.p(); } }
        // Turn time: stands still in turn-based combat except 5 s per player turn; out of combat a turn is 5 s of it.
        public static TimeSpan TurnTime { get { var s = State; return s == null ? TimeSpan.Zero : s.o(); } }
        public static double Turns { get { return TurnTime.TotalSeconds / 5.0; } }
        // The game is paused (a dialog, the menu, the character sheet...); nothing in the world moves.
        public static bool Paused { get { var p = bpo.GetService<ps>(); return p != null && p.ak4(); } }
        public static bool InTurnBasedCombat { get { return dz6.e(); } }
        static cyt State { get { var g = bpo.GetService<cnk>(); return g == null ? null : g.aqu(); } }
    }

    public sealed class ItemUse
    {
        public Character User { get; internal set; }
        public Item Item { get; internal set; }
        // The capability's name ("Use", "Throw", ...).
        public string Action { get; internal set; }
        // What it is used on: a Character, an Item, or null (a tile, itself).
        public object Target { get; internal set; }
        // ItemUsing: true stops the use (nothing happens, no AP spent).
        public bool Cancel { get; set; }
    }

    public static partial class Items
    {
        // A character uses an item from its bag the game's way (the capability its "use" is, e.g. a bandage's treatment;
        // a grenade is thrown at target), as the game's own AI does: queued as the character's next action, with the AP
        // cost, cooldowns and checks of a normal use. target: the character it is used on (null: the user itself).
        // False if the item has no use or the game refuses it now (e.g. a bandage below 40 % health, in combat).
        public static bool Use(Character user, Item item, Character target = null)
        {
            var c = user == null ? null : user.Entity as c5x;
            var data = c == null ? null : c.GetAspect<amc>();
            if (data == null || item == null || item.Game.Definition == null) return false;
            var uses = item.Game.Definition.a2();
            var cap = uses == null || uses.Count == 0 ? null : uses[0].Value;
            if (cap == null) return false;
            var proxy = ecf.a(cap, new eru(new Ouroboros.Playfield.References.EntityAspectReference<amc, amc>(data)), item.Game) as eu7;
            if (proxy == null || !proxy.yw()) return false;     // yw(): usable now
            return cp.b(proxy, c, target == null ? c : target.Entity) != null;
        }
    }

    public sealed class Blueprint
    {
        public string Id { get; internal set; }         // the game's technical name, e.g. "throwingNet"
        public string Name { get; internal set; }       // as the crafting window shows it
        public string Category { get; internal set; }
        internal Type Type;
    }

    public static partial class Crafting
    {
        static List<Blueprint> all;
        // Every blueprint in the game (its blueprint classes).
        public static IList<Blueprint> All
        {
            get
            {
                if (all != null) return all;
                all = new List<Blueprint>();
                foreach (var t in typeof(cwp).Assembly.GetTypes().Where(t => typeof(cwp).IsAssignableFrom(t) && !t.IsAbstract && t.GetConstructor(Type.EmptyTypes) != null))
                {
                    try { var b = (cwp)Activator.CreateInstance(t); all.Add(new Blueprint { Id = b.TechnicalName, Name = b.LocalizedName, Category = b.Category.ToString(), Type = t }); }
                    catch (Exception ex) { Log.Write("crafting: blueprint " + t.Name + ": " + ex.Message); }
                }
                all = all.OrderBy(b => b.Name).ToList();
                return all;
            }
        }

        // The blueprints the player knows (e3q.d()).
        public static IList<Blueprint> Known
        {
            get
            {
                var p = World.Player == null ? null : World.Player.PlayerData;
                if (p == null) return new List<Blueprint>();
                var known = new HashSet<string>(p.d().Select(r => r.ao() == null ? null : r.ao().TechnicalName).Where(n => n != null));
                return All.Where(b => known.Contains(b.Id)).ToList();
            }
        }

        // The player learns a stock blueprint (by id or name), as reading its blueprint item does. Saved with the game
        // (the stock game knows every stock blueprint). False if there's no such blueprint.
        public static bool Learn(string idOrName)
        {
            var p = World.Player == null ? null : World.Player.PlayerData;
            var b = All.FirstOrDefault(x => string.Equals(x.Id, idOrName, StringComparison.OrdinalIgnoreCase) || string.Equals(x.Name, idOrName, StringComparison.OrdinalIgnoreCase));
            if (p == null || b == null) return false;
            p.b(b.Type);
            return true;
        }
    }

    public sealed class Crafted
    {
        public string Blueprint { get; internal set; }       // its name as the game shows it
        public IList<Item> Items { get; internal set; }
    }

    public sealed class Theft
    {
        public Character Thief { get; internal set; }
        public Character Victim { get; internal set; }
        public Item Item { get; internal set; }
        // Stealing.ModifySuspicion: how much suspicion taking it adds (the victim notices at 1.5 in all).
        public double Suspicion { get; set; }
    }

    public static class Stealing
    {
        // thief tries to pickpocket victim the game's way (its Pickpocket ability: the window opens for the player,
        // with the game's checks: in reach, not in combat, the victim's suspicion). False if the game refuses.
        public static bool Pickpocket(Character thief, Character victim)
        {
            var t = thief == null ? null : thief.Entity as c5x;
            var data = t == null ? null : t.GetAspect<amc>();
            if (data == null || victim == null) return false;
            var cap = dgk.Get<eag>();
            var proxy = cap == null ? null : ecf.a(cap, new eru(new Ouroboros.Playfield.References.EntityAspectReference<amc, amc>(data)), null) as eu7;
            if (proxy == null || !proxy.yw()) return false;
            return cp.b(proxy, t, victim.Entity) != null;
        }

        public static readonly Hook<Action<Theft>> ModifySuspicion = new Hook<Action<Theft>>("ModifySuspicion");
    }

    public static partial class GameEvents
    {
        // Game time passed this frame (not while paused): the scaled delta.
        public static readonly Hook<Action<TimeSpan>> GameTimeAdvanced = new Hook<Action<TimeSpan>>("GameTimeAdvanced");
        // An item is about to be used (set Cancel to stop it), was used (consumed: the stack already taken).
        public static readonly Hook<Action<ItemUse>> ItemUsing = new Hook<Action<ItemUse>>("ItemUsing");
        public static readonly Hook<Action<ItemUse>> ItemUsed = new Hook<Action<ItemUse>>("ItemUsed");
        public static readonly Hook<Action<Crafted>> ItemCrafted = new Hook<Action<Crafted>>("ItemCrafted");
        public static readonly Hook<Action<Theft>> ItemStolen = new Hook<Action<Theft>>("ItemStolen");
        public static readonly Hook<Action<Theft>> CaughtStealing = new Hook<Action<Theft>>("CaughtStealing");
    }
}

namespace UnderRailWorks.Internal
{
    static class ActivityHooks
    {
        internal static void Install(HarmonyLib.Harmony harmony)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(ActivityHooks).GetMethod(n, any));
            var d6f = typeof(cnk).Assembly.GetType("d6f");     // internal: by name
            harmony.Patch(d6f.GetMethod("a", any, null, new[] { typeof(object), typeof(c4i) }, null), postfix: m("Time"));
            harmony.Patch(typeof(cp).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(eu7), typeof(c5x), typeof(object) }, null), prefix: m("Using"));
            harmony.Patch(typeof(dea).GetMethod("dt", any, null, Type.EmptyTypes, null), postfix: m("Used"));
            harmony.Patch(typeof(ahu).GetMethod("i", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null), postfix: m("Craft"));
            harmony.Patch(typeof(b87).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(efk), typeof(efk), typeof(coh), typeof(double) }, null), postfix: m("Suspicion"));
            harmony.Patch(typeof(rc).GetMethod("a", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(coh) }, null), postfix: m("Stolen"));
            harmony.Patch(typeof(rc).GetMethod("a", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null), prefix: m("Caught"));
        }

        static void Time(c4i __1)
        {
            if (!GameEvents.GameTimeAdvanced.Any || __1 == null) return;
            var d = __1.a;
            GameEvents.GameTimeAdvanced.Run(h => h(d));
        }

        static Character Of(object entity) { return entity is c5x ? Character.Of((c5x)entity) : null; }
        static Character OfData(efk data) { return Combat.OfData(data); }

        static ItemUse Use(av proxy, object user, object target)
        {
            var item = proxy == null ? null : proxy.b9() as coh;
            if (item == null) return null;     // not an item's capability (skills, psi, attacks)
            var cap = proxy.p();
            return new ItemUse
            {
                User = Of(user), Item = new Item(item), Action = cap == null ? null : cap.Name,
                Target = target is coh ? (object)new Item((coh)target) : Of(target),
            };
        }

        static bool Using(eu7 __0, c5x __1, object __2, ref ad5 __result)
        {
            if (!GameEvents.ItemUsing.Any) return true;
            try
            {
                var u = Use(__0, __1, __2);
                if (u == null) return true;
                GameEvents.ItemUsing.Run(h => h(u));
                if (u.Cancel) { __result = null; return false; }     // nothing queued (the game's callers handle null)
            }
            catch (Exception ex) { Log.Write("items: " + ex.Message); }
            return true;
        }

        static void Used(dea __instance)
        {
            if (!GameEvents.ItemUsed.Any) return;
            try
            {
                var action = __instance.vg() as a62;     // a consumable's action
                if (action == null) return;
                var u = Use(action.e(), action.q(), action.h());
                if (u != null) GameEvents.ItemUsed.Run(h => h(u));
            }
            catch (Exception ex) { Log.Write("items: " + ex.Message); }
        }

        static readonly FieldInfo Blueprint = typeof(ahu).GetField("m_b", BindingFlags.Instance | BindingFlags.NonPublic) ?? typeof(ahu).GetField("b", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo CraftedItems = typeof(cfe).GetField("a");
        static void Craft(ahu __instance, cfe __result)
        {
            var items = __result == null ? null : CraftedItems.GetValue(__result) as List<coh>;     // cfe.a: a field/method name clash
            if (!GameEvents.ItemCrafted.Any || __result == null || !__result.d || items == null) return;
            try
            {
                var bp = Blueprint.GetValue(__instance) as cwp;
                var c = new Crafted { Blueprint = bp == null ? null : bp.LocalizedName, Items = items.Select(i => new Item(i)).ToList() };
                GameEvents.ItemCrafted.Run(h => h(c));
            }
            catch (Exception ex) { Log.Write("crafting: " + ex.Message); }
        }

        static void Suspicion(efk __0, efk __1, coh __2, ref double __result)
        {
            if (!Stealing.ModifySuspicion.Any) return;
            try
            {
                var t = new Theft { Thief = OfData(__0), Victim = OfData(__1), Item = __2 == null ? null : new Item(__2), Suspicion = __result };
                Stealing.ModifySuspicion.Run(h => h(t));
                __result = Math.Max(0, t.Suspicion);
            }
            catch (Exception ex) { Log.Write("stealing: " + ex.Message); }
        }

        static Theft TheftOf(rc action, coh item)
        {
            return new Theft { Thief = action.d() == null ? null : Of(action.d().a8()), Victim = action.c() == null ? null : Of(action.c().a8()), Item = item == null ? null : new Item(item) };
        }
        static void Stolen(rc __instance, coh __0)
        {
            if (!GameEvents.ItemStolen.Any) return;
            try { var t = TheftOf(__instance, __0); GameEvents.ItemStolen.Run(h => h(t)); } catch (Exception ex) { Log.Write("stealing: " + ex.Message); }
        }
        static void Caught(rc __instance)
        {
            if (!GameEvents.CaughtStealing.Any) return;
            try { var t = TheftOf(__instance, null); GameEvents.CaughtStealing.Run(h => h(t)); } catch (Exception ex) { Log.Write("stealing: " + ex.Message); }
        }
    }
}
