// UnderRailWorks: levelling rules, changeable by mods (ToME's and sfall's progression hooks, in small).
// The game's side (measured from its code):
// - XP for the next level: c2h.a(level, system, intelligence) gives what reaching `level` costs (Classic XP or Oddity
//   points); the level-up check (c2h.b) and the deduction on level-up (e3q.a) both use it, so one change moves both.
// - Points per level: e3q's private a() adds the level's ability, skill and feat points (and specialisation points);
//   level 1 (character creation) goes through b() instead and is left alone.
// - XP gained: e3q.c(amount) (Classic scales it by intelligence there); Oddity points arrive the same way.
// Points and XP are plain numbers in the save, so a save made with a rule changed still loads in the stock game.
//   Progression.ModifyXpNeeded.Add(this, x => x.Needed = x.Needed * 3 / 4);         // levels come faster
//   Progression.ModifyLevelPoints.Add(this, p => p.Skill += 5);                    // 5 more skill points a level
using System;
using System.Reflection;
using TimelapseVertigo.Rules.Characters;

public sealed class XpNeeded
{
    // The level being reached, whether the game uses the Oddity system (else Classic XP), and the cost.
    public int Level { get; internal set; }
    public bool Oddity { get; internal set; }
    public int Needed;
}

public sealed class LevelPoints
{
    // The level just reached, and the points it gives (change them).
    public int Level { get; internal set; }
    public int Ability, Skill, Feat;
}

public sealed class XpGain
{
    public bool Oddity { get; internal set; }
    // What the player is about to get (Classic: before the game's intelligence bonus).
    public int Amount;
}

public static class Progression
{
    public static readonly Hook<Action<XpNeeded>> ModifyXpNeeded = new Hook<Action<XpNeeded>>("ModifyXpNeeded");
    public static readonly Hook<Action<LevelPoints>> ModifyLevelPoints = new Hook<Action<LevelPoints>>("ModifyLevelPoints");
    public static readonly Hook<Action<XpGain>> ModifyExperience = new Hook<Action<XpGain>>("ModifyExperience");

    // What reaching a level costs now (with every mod's change), for the player's system.
    public static int XpFor(int level)
    {
        var p = World.Player;
        var data = p == null ? null : p.PlayerData;
        return data == null ? 0 : c2h.a(level, data.e(), data.BaseAbilities.Get<av8>().f());
    }
    // The level cap of this game (the base game's, or the expansion's / custom difficulty's): c2h.a().
    public static int MaxLevel { get { return c2h.a(); } }
    // The player can still level up (points freed now can be spent at a next level-up).
    public static bool CanLevelUp { get { var p = World.Player; return p != null && p.IsCharacter && p.Level < c2h.a(); } }
    public static bool Oddity { get { var p = World.Player; var d = p == null ? null : p.PlayerData; return d != null && d.e() == ExperienceSystem.Oddity; } }
}

static class ProgressionHooks
{
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(ProgressionHooks).GetMethod(n, any));
        harmony.Patch(typeof(c2h).GetMethod("a", any, null, new[] { typeof(int), typeof(ExperienceSystem), typeof(int) }, null), postfix: m("Needed"));
        harmony.Patch(typeof(e3q).GetMethod("a", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null), prefix: m("PointsBefore"), postfix: m("PointsAfter"));
        harmony.Patch(typeof(e3q).GetMethod("c", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int) }, null), prefix: m("Gain"));
    }

    static void Needed(int __0, ExperienceSystem __1, ref int __result)
    {
        if (!Progression.ModifyXpNeeded.Any) return;
        var x = new XpNeeded { Level = __0, Oddity = __1 == ExperienceSystem.Oddity, Needed = __result };
        Progression.ModifyXpNeeded.Run(h => h(x));
        __result = Math.Max(1, x.Needed);     // 0 would level forever in c2h.b's loop
    }

    static void PointsBefore(e3q __instance, out int[] __state)
    {
        __state = Progression.ModifyLevelPoints.Any ? new[] { __instance.g(), __instance.h(), __instance.j() } : null;
    }

    static void PointsAfter(e3q __instance, int[] __state)
    {
        if (__state == null) return;
        try
        {
            var p = new LevelPoints { Level = __instance.Level, Ability = __instance.g() - __state[0], Skill = __instance.h() - __state[1], Feat = __instance.j() - __state[2] };
            int a = p.Ability, s = p.Skill, f = p.Feat;
            Progression.ModifyLevelPoints.Run(h => h(p));
            if (p.Ability != a) __instance.b(Math.Max(0, __state[0] + p.Ability));
            if (p.Skill != s) __instance.d(Math.Max(0, __state[1] + p.Skill));
            if (p.Feat != f) __instance.f(Math.Max(0, __state[2] + p.Feat));
        }
        catch (Exception ex) { Log.Write("progression: " + ex.Message); }
    }

    static void Gain(e3q __instance, ref int __0)
    {
        if (!Progression.ModifyExperience.Any) return;
        var g = new XpGain { Oddity = __instance.e() == ExperienceSystem.Oddity, Amount = __0 };
        Progression.ModifyExperience.Run(h => h(g));
        __0 = Math.Max(0, g.Amount);
    }
}
