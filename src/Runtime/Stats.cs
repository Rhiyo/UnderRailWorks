// UnderRailWorks: changing how the game computes characters' stats.
// - The game rebuilds every stat in efk.bl(): it gathers static effects (d3e) from feats (ai8.cy) and from the
//   character's list z (gear, status effects; not saved) and applies them in phases (abilities, skills, the rest).
//   Stats.Modify handlers add StatBonuses for a character during that rebuild: Works puts them into z for the
//   rebuild and takes them out again, so they are never saved. A handler sees the stats as they were before this
//   rebuild (an ability-based bonus follows a changed ability at the next rebuild).
// - A skill's value is its points x (1 + the best of its related abilities' weight x the ability's factor), plus
//   synergies from other skills; each skill class names those in fi() / fj(). Skills.SetAbilities / SetSynergies
//   replace them for a skill.
//   Stats.Modify.Add(this, s => { if (s.Character.Ability("Strength") >= 8) s.Add(StatBonus.CarryCapacity(20)); });
//   Skills.SetAbilities(this, "Throwing", new Dictionary<string, float> { { "Dexterity", 1f }, { "Strength", 0.5f } });
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TimelapseVertigo.Rules.Characters;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class StatRebuild
    {
        public Character Character { get; internal set; }
        internal readonly List<StatBonus> Added = new List<StatBonus>();
        public void Add(StatBonus bonus) { if (bonus != null) Added.Add(bonus); }
    }

    public static class Stats
    {
        public static readonly Hook<Action<StatRebuild>> Modify = new Hook<Action<StatRebuild>>("Stats.Modify");
        // Rebuilds a character's stats now (after a mod's rule changed what its handler would add).
        public static void Refresh(Character c) { var d = c == null ? null : c.GameData; if (d != null) d.bl(); }
    }

    public static class Skills
    {
        internal static readonly Dictionary<string, Dictionary<string, float>> Abilities = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, Dictionary<string, float>> Synergies = new Dictionary<string, Dictionary<string, float>>(StringComparer.OrdinalIgnoreCase);
        // Which abilities raise a skill, and how much (the game's weights are 1 or less; the best one counts).
        public static void SetAbilities(WorksMod mod, string skill, IDictionary<string, float> weights) { Abilities[skill] = new Dictionary<string, float>(weights, StringComparer.OrdinalIgnoreCase); Log.Write("stats: " + mod.Id + " sets " + skill + "'s abilities"); }
        // Which other skills add to it (a share of their points, up to the level's cap).
        public static void SetSynergies(WorksMod mod, string skill, IDictionary<string, float> weights) { Synergies[skill] = new Dictionary<string, float>(weights, StringComparer.OrdinalIgnoreCase); Log.Write("stats: " + mod.Id + " sets " + skill + "'s synergies"); }
    }
}

namespace UnderRailWorks.Internal
{
    static class StatHooks
    {
        static readonly FieldInfo Effects = typeof(efk).GetField("z", BindingFlags.Instance | BindingFlags.NonPublic);
        [ThreadStatic] static List<RequirementRestrictedElementContainer<d3e>> added;

        static void Rebuilding(efk __instance)
        {
            if (!Stats.Modify.Any) return;
            try
            {
                var c = Combat.OfData(__instance);
                if (c == null) return;
                var r = new StatRebuild { Character = c };
                Stats.Modify.Run(h => h(r));
                if (r.Added.Count == 0) return;
                var list = (List<RequirementRestrictedElementContainer<d3e>>)Effects.GetValue(__instance);
                added = r.Added.Select(b => new RequirementRestrictedElementContainer<d3e>(b.Make())).ToList();
                list.AddRange(added);
            }
            catch (Exception ex) { Log.Write("stats: " + ex.Message); }
        }

        static Exception Rebuilt(efk __instance, Exception __exception)
        {
            var a = added; added = null;
            if (a != null) try { var list = (List<RequirementRestrictedElementContainer<d3e>>)Effects.GetValue(__instance); foreach (var x in a) list.Remove(x); } catch (Exception ex) { Log.Write("stats: " + ex.Message); }
            return __exception;
        }

        // The seven abilities' classes (bn2's properties: Strength d3m, Dexterity cwu, Agility dqg, Perception eav,
        // Constitution axa, Intelligence av8, Will axr).
        static readonly Dictionary<string, Type> AbilityTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase)
        {
            { "Strength", typeof(d3m) }, { "Dexterity", typeof(cwu) }, { "Agility", typeof(dqg) }, { "Perception", typeof(eav) },
            { "Constitution", typeof(axa) }, { "Intelligence", typeof(av8) }, { "Will", typeof(axr) },
        };
        static readonly Dictionary<string, Type> SkillTypes = new Dictionary<string, Type>(StringComparer.OrdinalIgnoreCase);

        static void AbilityBonuses(csn __instance, ref Dictionary<Type, float> __result)
        {
            Dictionary<string, float> w;
            if (Skills.Abilities.Count == 0 || !Skills.Abilities.TryGetValue(__instance.Name, out w)) return;
            var d = new Dictionary<Type, float>();
            foreach (var kv in w) { Type t; if (AbilityTypes.TryGetValue(kv.Key, out t)) d[t] = kv.Value; }
            __result = d;
        }

        static void SkillSynergies(csn __instance, ref Dictionary<Type, float> __result)
        {
            Dictionary<string, float> w;
            if (Skills.Synergies.Count == 0 || !Skills.Synergies.TryGetValue(__instance.Name, out w)) return;
            if (SkillTypes.Count == 0 && __instance.Skills != null) foreach (var sk in __instance.Skills) SkillTypes[sk.Name] = sk.GetType();     // names from a real character's list
            var d = new Dictionary<Type, float>();
            foreach (var kv in w) { Type t; if (SkillTypes.TryGetValue(kv.Key, out t)) d[t] = kv.Value; }
            __result = d;
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            const BindingFlags inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(StatHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(efk).GetMethod("bl", inst, null, Type.EmptyTypes, null), prefix: m("Rebuilding"), finalizer: m("Rebuilt"));
            // fi() / fj() are virtual: patch the base and every skill class that overrides them.
            foreach (var t in typeof(csn).Assembly.GetTypes().Where(t => typeof(csn).IsAssignableFrom(t)))
            {
                var fi = t.GetMethod("fi", inst | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                var fj = t.GetMethod("fj", inst | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                if (fi != null && !fi.IsAbstract) harmony.Patch(fi, postfix: m("AbilityBonuses"));
                if (fj != null && !fj.IsAbstract) harmony.Patch(fj, postfix: m("SkillSynergies"));
            }

        }
    }
}
