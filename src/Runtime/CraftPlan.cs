// UnderRailWorks: what a character can craft now. For each of a blueprint's required component slots (cwp.Slots: label
// -> cgo: the requirement cb6, optional b, count d) a fitting item from the bag (cb6.ahb(item), enough stacks); the skills
// those components need, as the crafting window computes them (cwp's private a(components): skill -> value, the same
// numbers its window shows); and whether the character's skills meet them. Nothing is crafted or moved.
//   foreach (var plan in Crafting.Plans(World.Player)) Log.Info(plan.Name + (plan.CanCraft ? ": ready" : ": missing " + string.Join(", ", plan.Missing)));
// Components are picked first-fit (the first stack in the bag that fits each slot); the crafting window lets the player
// pick others, which may need different skills (better parts often need more).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class CraftPlan
    {
        public string Name { get; internal set; }
        public string Category { get; internal set; }
        public bool FromMod { get; internal set; }
        // Labels of required slots nothing in the bag fills.
        public IList<string> Missing { get; internal set; }
        // The skills the picked components need (skill -> value) and the ones the character lacks (skill -> needed).
        public IDictionary<string, int> Needs { get; internal set; }
        public IDictionary<string, int> Lacks { get; internal set; }
        // Every required slot filled and the skills enough.
        public bool CanCraft { get { return Missing.Count == 0 && Lacks.Count == 0; } }
    }

    public static partial class Crafting
    {
        static readonly MethodInfo Requirements = typeof(cwp).GetMethods(BindingFlags.Instance | BindingFlags.NonPublic)
            .FirstOrDefault(m => m.Name == "a" && m.ReturnType == typeof(Dictionary<string, int>) && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType == typeof(Dictionary<string, coh>));

        // Plans for every blueprint the character knows (the game's and mods' recipes), sorted by name.
        public static IList<CraftPlan> Plans(Character crafter)
        {
            var result = new List<CraftPlan>();
            if (crafter == null) return result;
            foreach (var b in Known) { try { result.Add(Plan((cwp)Activator.CreateInstance(b.Type), b.Name, b.Category, false, crafter)); } catch (Exception ex) { Log.Write("crafting: plan " + b.Id + ": " + ex.Message); } }
            foreach (var r in ModRecipes.Where(r => r.Known && r.Blueprint is cwp)) { try { result.Add(Plan((cwp)r.Blueprint, r.Name, r.Category, true, crafter)); } catch (Exception ex) { Log.Write("crafting: plan " + r.Id + ": " + ex.Message); } }
            return result.OrderBy(p => p.Name).ToList();
        }

        static CraftPlan Plan(cwp bp, string name, string category, bool fromMod, Character crafter)
        {
            var bag = crafter.Inventory.Items.Select(i => i.Game).ToList();
            var used = new Dictionary<coh, int>();     // stacks already picked for another slot
            var picked = new Dictionary<string, coh>();
            var missing = new List<string>();
            foreach (var slot in bp.Slots)
            {
                var need = slot.Value;
                var item = bag.FirstOrDefault(i =>
                {
                    int taken; used.TryGetValue(i, out taken);
                    return i.Stacks - taken >= Math.Max(1, need.d) && need.a != null && need.a.ahb(i);
                });
                if (item == null) { if (!need.b) missing.Add(slot.Key); continue; }
                int already; used.TryGetValue(item, out already); used[item] = already + Math.Max(1, need.d);
                picked[slot.Key] = item;
            }
            var needs = Requirements == null ? new Dictionary<string, int>() : (Dictionary<string, int>)Requirements.Invoke(bp, new object[] { picked });
            var lacks = new Dictionary<string, int>();
            foreach (var kv in needs)
            {
                var skill = crafter.SkillNames.FirstOrDefault(n => string.Equals(n, kv.Key, StringComparison.OrdinalIgnoreCase)) ?? kv.Key;
                int have; try { have = crafter.Skill(skill); } catch { have = 0; }
                if (have < kv.Value) lacks[skill] = kv.Value;
            }
            return new CraftPlan { Name = name, Category = category, FromMod = fromMod, Missing = missing, Needs = needs, Lacks = lacks };
        }
    }
}
