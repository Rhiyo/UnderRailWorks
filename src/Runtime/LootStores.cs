// UnderRailWorks: what merchants sell and what loot rolls, as data. The game's side (measured from its code;
// ~/UnderRailWorks-research/specs/items-stores.md):
// - A merchant (dvp) reads its store file afresh on every (re)stock (c8j.a(name) -> p9: shipments d6w). Works appends
//   its own shipments there: items delivered at each restock, or once per game ("unique": kept through restocks).
//   Only the delivered items (stock types) and the shipment's name (a string) are saved; the stock game ignores
//   a name it doesn't know.
// - Loot rolls once per container or character (ery.mx -> the loot table's c9y.c() list). Works appends its own items
//   to that list, so the game still applies durability and equips NPCs; the table itself is never touched.
//   Stores.AddRestock(this, "bigbret", "consumables\\medicine\\bandage", 5);
//   Loot.Add(this, (who, box) => who != null && who.Name.Contains("Rathound"), "components\\scraps\\fabricscraps", 0.5, 2);
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TimelapseVertigo.Generators.Items;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static class Stores
    {
        // Delivered at every restock of the store (its file name under data/rules/stores, e.g. "bigbret").
        public static void AddRestock(WorksMod mod, string store, string itemPath, int count = 1) { StoreHooks.Add(mod.Id, store, itemPath, count, false); }
        // Delivered once per game, kept through restocks.
        public static void AddOnce(WorksMod mod, string store, string itemPath, int count = 1) { StoreHooks.Add(mod.Id, store, itemPath, count, true); }
    }

    public static class Loot
    {
        // When loot is rolled for a character or a container (once each, at its first appearance) and where holds,
        // add count of the item with chance (0..1). where gets the character (null for containers) and the container
        // (null for characters). Rolled with Works' own random numbers: the game's rolls stay as they were.
        public static void Add(WorksMod mod, Func<Character, Container, bool> where, string itemPath, double chance, int count = 1) { LootHooks.Rules.Add(new LootHooks.Rule { Mod = mod.Id, Where = where, Path = itemPath, Chance = chance, Count = count }); }
    }
}

namespace UnderRailWorks.Internal
{
    static class StoreHooks
    {
        sealed class Entry { public string Mod, Path; public int Count; public bool Once; }
        static readonly Dictionary<string, List<Entry>> entries = new Dictionary<string, List<Entry>>(StringComparer.OrdinalIgnoreCase);

        internal static void Add(string mod, string store, string path, int count, bool once)
        {
            List<Entry> list;
            if (!entries.TryGetValue(store, out list)) entries[store] = list = new List<Entry>();
            list.Add(new Entry { Mod = mod, Path = path, Count = Math.Max(1, count), Once = once });
        }

        // c8j.a(store name) -> p9: the store definition read for this (re)stock.
        static void Read(string __0, p9 __result)
        {
            List<Entry> list;
            if (__result == null || __0 == null || !entries.TryGetValue(__0, out list)) return;
            foreach (var e in list)
            {
                try
                {
                    var item = ez2.a(e.Path);
                    if (item == null) { Log.Write("stores: " + e.Mod + ": no item " + e.Path); continue; }
                    item.Stacks = e.Count;
                    var box = new dcw();
                    box.z3(item, false);
                    var s = new d6w();
                    s.a("works:" + e.Mod + ":" + e.Path + (e.Once ? ":once" : ""));     // the shipment's name: saved as delivered
                    s.a(box);
                    s.a(e.Once);     // unique: kept through restocks, never delivered again
                    __result.b().Add(s);
                }
                catch (Exception ex) { ModErrors.Report(e.Mod, "store " + __0, ex); }
            }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(typeof(c8j).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null),
                postfix: new HarmonyLib.HarmonyMethod(typeof(StoreHooks).GetMethod("Read", BindingFlags.Static | BindingFlags.NonPublic)));
        }
    }

    static class LootHooks
    {
        internal sealed class Rule { public string Mod, Path; public Func<Character, Container, bool> Where; public double Chance; public int Count; }
        internal static readonly List<Rule> Rules = new List<Rule>();
        [ThreadStatic] static ery rolling;

        static void Before(ery __instance) { rolling = Rules.Count > 0 && !__instance.a() ? __instance : null; }
        static void After() { rolling = null; }

        // c9y.c(): a loot table rolled. Only while ery.mx rolls an entity's loot (not stores or scripts).
        static void Rolled(List<bmd> __result)
        {
            var g = rolling;
            if (g == null || __result == null) return;
            rolling = null;     // once per entity (a table can roll nested tables)
            var e = g.a8();
            if (e == null) return;
            var amc = e.GetAspect<amc>();
            var who = amc != null && amc.aa() != null ? Character.Of(e) : null;
            var c7 = e.GetAspect<bim>() as c7j;
            var box = c7 == null ? null : new Container(c7);
            var rng = new Random(e.GetHashCode() ^ Environment.TickCount);
            foreach (var r in Rules)
            {
                try
                {
                    if (!r.Where(who, box) || rng.NextDouble() >= r.Chance) continue;
                    var item = ez2.a(r.Path);
                    if (item == null) { Log.Write("loot: " + r.Mod + ": no item " + r.Path); continue; }
                    item.Stacks = r.Count;
                    item.Lootable = true; item.Stealable = true;
                    __result.Add(new bmd(item, AutoEquipMode.None));
                }
                catch (Exception ex) { ModErrors.Report(r.Mod, "loot " + r.Path, ex); }
            }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(LootHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(ery).GetMethod("mx", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null), prefix: m("Before"), finalizer: m("After"));
            harmony.Patch(typeof(c9y).GetMethod("c", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null), postfix: m("Rolled"));
        }
    }
}
