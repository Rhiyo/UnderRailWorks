// UnderRailWorks: the character-building rules (points at creation and per level, the ability range at creation).
// The game's numbers are constants (aiy: static readonly, which .NET compiles into the methods using them, so they
// can't be changed in place); Works applies a mod's numbers as differences after the game's own steps:
// - e3q.b(): a new character's points (abilities aiy.t, skills aiy.ak * aiy.ai, feats aiy.al) -> postfix adds
//   (rule - game) to the counters m_c / m_d / m_e (saved as plain numbers: the stock game reads them).
// - e3q.a(): each level up (an ability point every aiy.u levels below aiy.bk, aiy.ai skill points, a feat every
//   aiy.am levels) -> postfix adds the difference.
// - The creation screen's ability rows (c3p): "can raise" stops at aiy.x at level 1 (postfix c3p.a()); the minimum is
//   aiy.y when creating (set by e44.a(e3q): postfix sets the rule's).
//   CharacterRules.SkillPointsPerLevel = 50; CharacterRules.AbilityMaxAtCreation = 12;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static class CharacterRules
    {
        static int G(string field) { var f = typeof(aiy).GetField(field, BindingFlags.Static | BindingFlags.Public); return f == null ? 0 : (int)f.GetValue(null); }
        internal static readonly int GameAbilityPoints = G("t"), GameSkillPoints = G("ak") * G("ai"), GameFeats = G("al"), GameSkillPerLevel = G("ai"),
            GameAbilityEvery = G("u"), GameFeatEvery = G("am"), GameAbilityMin = G("y"), GameAbilityMax = G("x"), AbilityLevelCap = G("bk");

        // Each rule remembers the mod that set it: two mods setting one rule is logged (the later wins), and a mod's
        // rules go back to the game's when it's reloaded or fails to load.
        static readonly Dictionary<string, KeyValuePair<string, int>> set = new Dictionary<string, KeyValuePair<string, int>>();
        static int Get(string rule, int game) { KeyValuePair<string, int> v; return set.TryGetValue(rule, out v) ? v.Value : game; }
        static void Set(string rule, int value)
        {
            var mod = ModScope.Current ?? "";
            KeyValuePair<string, int> had;
            if (set.TryGetValue(rule, out had) && had.Key != mod && had.Key != "") Log.Write("rules: " + mod + " changes CharacterRules." + rule + " that " + had.Key + " set (" + had.Value + " -> " + value + ")");
            set[rule] = new KeyValuePair<string, int>(mod, value);
        }
        internal static void Forget(string mod) { foreach (var k in set.Where(kv => string.Equals(kv.Value.Key, mod, StringComparison.OrdinalIgnoreCase)).Select(kv => kv.Key).ToList()) set.Remove(k); }

        // A new character's unspent points (the game: 5 ability points over abilities starting at 5, 120 skill points, 2 feats).
        public static int AbilityPointsAtCreation { get { return Get("AbilityPointsAtCreation", GameAbilityPoints); } set { Set("AbilityPointsAtCreation", value); } }
        public static int SkillPointsAtCreation { get { return Get("SkillPointsAtCreation", GameSkillPoints); } set { Set("SkillPointsAtCreation", value); } }
        public static int FeatsAtCreation { get { return Get("FeatsAtCreation", GameFeats); } set { Set("FeatsAtCreation", value); } }
        // Each level (the game: 40 skill points, an ability point every 4 levels, a feat every 2).
        public static int SkillPointsPerLevel { get { return Get("SkillPointsPerLevel", GameSkillPerLevel); } set { Set("SkillPointsPerLevel", value); } }
        public static int AbilityPointEveryLevels { get { return Get("AbilityPointEveryLevels", GameAbilityEvery); } set { Set("AbilityPointEveryLevels", value); } }
        public static int FeatEveryLevels { get { return Get("FeatEveryLevels", GameFeatEvery); } set { Set("FeatEveryLevels", value); } }
        // The range of each ability on the creation screen (the game: 3 to 10).
        public static int AbilityMinAtCreation { get { return Get("AbilityMinAtCreation", GameAbilityMin); } set { Set("AbilityMinAtCreation", value); } }
        public static int AbilityMaxAtCreation { get { return Get("AbilityMaxAtCreation", GameAbilityMax); } set { Set("AbilityMaxAtCreation", value); } }
    }
}

namespace UnderRailWorks.Internal
{
    static class CharacterRuleHooks
    {
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        // The decompile's m_c / m_d / m_e (ILSpy adds m_ to names that clash): c, d, e in the game itself, declared on e3q.
        static FieldInfo Field(string name) { const BindingFlags d = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly; return typeof(e3q).GetField("m_" + name, d) ?? typeof(e3q).GetField(name, d); }
        static readonly FieldInfo AbilityPoints = Field("c"), SkillPoints = Field("d"), FeatPoints = Field("e");
        static void Add(FieldInfo f, e3q c, int delta) { if (delta != 0) f.SetValue(c, (int)f.GetValue(c) + delta); }

        static void Created(e3q __instance)
        {
            try
            {
                Add(AbilityPoints, __instance, CharacterRules.AbilityPointsAtCreation - CharacterRules.GameAbilityPoints);
                Add(SkillPoints, __instance, CharacterRules.SkillPointsAtCreation - CharacterRules.GameSkillPoints);
                Add(FeatPoints, __instance, CharacterRules.FeatsAtCreation - CharacterRules.GameFeats);
            }
            catch (Exception ex) { Log.Write("rules: creation points: " + ex); }
        }

        static void LevelledUp(e3q __instance)
        {
            try { LevelPoints(__instance); } catch (Exception ex) { Log.Write("rules: level points: " + ex); }
        }
        static void LevelPoints(e3q __instance)
        {
            int level = __instance.Level;
            Func<int, bool> every = n => n > 0 && level % n == 0;
            int gameAbility = level < CharacterRules.AbilityLevelCap && every(CharacterRules.GameAbilityEvery) ? 1 : 0;
            int ruleAbility = level < CharacterRules.AbilityLevelCap && every(CharacterRules.AbilityPointEveryLevels) ? 1 : 0;
            Add(AbilityPoints, __instance, ruleAbility - gameAbility);
            Add(SkillPoints, __instance, CharacterRules.SkillPointsPerLevel - CharacterRules.GameSkillPerLevel);
            Add(FeatPoints, __instance, (every(CharacterRules.FeatEveryLevels) ? 1 : 0) - (every(CharacterRules.GameFeatEvery) ? 1 : 0));
        }

        // c3p.a(): may this ability be raised now (level 1: up to the creation maximum, while points are left).
        static void CanRaise(c3p __instance, ref bool __result)
        {
            if (CharacterRules.AbilityMaxAtCreation == CharacterRules.GameAbilityMax) return;
            try
            {
                var stat = __instance.j();
                var c = stat == null || stat.e() == null ? null : stat.e().re() as e3q;
                if (c == null || c.Level != 1) return;
                if (stat is av8 && c.b(d8o.t)) return;     // the game's exception (Intelligence for some origins), as it decides
                __result = stat.f() < CharacterRules.AbilityMaxAtCreation && c.g() > 0;
            }
            catch (Exception ex) { Log.Write("rules: " + ex.Message); }
        }

        static readonly Type AbilityTab = typeof(c3p).Assembly.GetType("e44");     // internal to the game: by name
        static readonly FieldInfo Rows = AbilityTab.GetField("m_a", Inst) ?? AbilityTab.GetField("a", Inst);
        static readonly MethodInfo Mode = AbilityTab.GetMethods(Inst).FirstOrDefault(m => m.Name == "d" && m.GetParameters().Length == 0 && m.ReturnType == typeof(a9m));
        // e44.a(e3q) built the ability rows: the creation minimum.
        static void RowsBuilt(object __instance)
        {
            if (CharacterRules.AbilityMinAtCreation == CharacterRules.GameAbilityMin || Mode == null) return;
            try
            {
                if ((a9m)Mode.Invoke(__instance, null) != a9m.c) return;
                var rows = Rows.GetValue(__instance) as List<c3p>;
                if (rows != null) foreach (var r in rows) r.e(CharacterRules.AbilityMinAtCreation);
            }
            catch (Exception ex) { Log.Write("rules: " + ex.Message); }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(CharacterRuleHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(e3q).GetMethod("b", Inst, null, Type.EmptyTypes, null), postfix: m("Created"));
            harmony.Patch(typeof(e3q).GetMethod("a", Inst, null, Type.EmptyTypes, null), postfix: m("LevelledUp"));
            harmony.Patch(typeof(c3p).GetMethod("a", Inst, null, Type.EmptyTypes, null), postfix: m("CanRaise"));
            harmony.Patch(AbilityTab.GetMethod("a", Inst, null, new[] { typeof(e3q) }, null), postfix: m("RowsBuilt"));
            Log.Write("rules: the game's: abilities " + CharacterRules.GameAbilityPoints + " points, " + CharacterRules.GameAbilityMin + "-" + CharacterRules.GameAbilityMax
                + "; skills " + CharacterRules.GameSkillPoints + " + " + CharacterRules.GameSkillPerLevel + "/level; feats " + CharacterRules.GameFeats + ", every " + CharacterRules.GameFeatEvery
                + " levels; an ability point every " + CharacterRules.GameAbilityEvery + " levels");
        }
    }
}
