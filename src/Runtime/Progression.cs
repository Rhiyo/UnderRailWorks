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
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class XpNeeded
{ internal XpNeeded() { }
    // The level being reached, whether the game uses the Oddity system (else Classic XP), and the cost.
    public int Level { get; internal set; }
    public bool Oddity { get; internal set; }
    public int Needed { get; set; }
}

public sealed class LevelPoints
{ internal LevelPoints() { }
    // The level just reached, and the points it gives (change them).
    public int Level { get; internal set; }
    public int Ability { get; set; }
    public int Skill { get; set; }
    public int Feat { get; set; }
}

public sealed class XpGain
{ internal XpGain() { }
    // The game uses the Oddity experience system (XP from studying oddities and quests, none for kills).
    public bool Oddity { get; internal set; }
    // Who gets it (the player, or another character with a player sheet), and why, as the game says it ("studying an
    // oddity: ...", a quest's reason, null for kills).
    public Character Who { get; internal set; }
    public string Reason { get; internal set; }
    // For studying an oddity.
    public bool FromOddity { get { return Reason != null && Reason.StartsWith("studying an oddity", StringComparison.OrdinalIgnoreCase); } }
    // What the player is about to get (Classic: before the game's intelligence bonus).
    public int Amount { get; set; }
}

public static partial class Progression
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
        harmony.Patch(typeof(awq).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(int), typeof(string) }, null), prefix: m("Giving"), finalizer: m("Given"));
        harmony.Patch(typeof(amc).GetMethod("a", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(int), typeof(string) }, null), prefix: m("Giving"), finalizer: m("Given"));
    }

    static void Needed(int __0, ExperienceSystem __1, ref int __result)
    { try {
        if (!Progression.ModifyXpNeeded.Any) return;
        var x = new XpNeeded { Level = __0, Oddity = __1 == ExperienceSystem.Oddity, Needed = __result };
        Progression.ModifyXpNeeded.Run(h => h(x));
        __result = Math.Max(1, x.Needed);     // 0 would level forever in c2h.b's loop
    } catch (Exception ex_) { PatchGuard.Failed("Progression.Needed", ex_); }
    }

    static void PointsBefore(e3q __instance, out int[] __state)
    { try {
        __state = Progression.ModifyLevelPoints.Any ? new[] { __instance.g(), __instance.h(), __instance.j() } : null;
    } catch (Exception ex_) { PatchGuard.Failed("Progression.PointsBefore", ex_); __state = default(int[]); }
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

    // The reason the XP is given with (awq.a(int, string) for the player, amc.a(int, string) for any sheet), for e3q.c.
    [ThreadStatic] internal static string XpReason;
    static void Giving(string __1) { try { XpReason = __1; } catch (Exception ex_) { PatchGuard.Failed("Progression.Giving", ex_); }
    }
    static void Given() { XpReason = null; }

    static void Gain(e3q __instance, ref int __0)
    { try {
        if (!Progression.ModifyExperience.Any) return;
        var sheet = __instance as au2; var body = sheet == null ? null : sheet.b() as c5x;
        var g = new XpGain { Oddity = __instance.e() == ExperienceSystem.Oddity, Amount = __0, Who = body == null ? null : Character.Of(body), Reason = XpReason };
        Progression.ModifyExperience.Run(h => h(g));
        __0 = Math.Max(0, g.Amount);
    } catch (Exception ex_) { PatchGuard.Failed("Progression.Gain", ex_); }
    }
}
}
