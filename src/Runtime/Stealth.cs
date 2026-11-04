// UnderRailWorks: stealth detection. The game's side (measured from its code): each NPC's vision scans 2-3 times a
// second; for a sneaking character in view, bui.a(target, detection, stealth, distance, sight range, angle from the
// observer's facing, light, darkvision) gives how fast the observer's awareness rises (positive) or falls (negative);
// awareness passes unaware -> suspicious -> detected in the observer's own state (cn8). A non-sneaking character is
// seen at once, and so is one whose stealth is far below the detection (bui.a(detection, stealth): 15 points lower
// and at most 0.4 of it). Nothing here is saved.
//   Stealth.ModifyDetection.Add(this, d => { if (d.Target.IsPlayer) d.Rate *= 0.5; });     // half as quick to notice
using System;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class Detection
{
    // The sneaking character, the observer's detection and the target's stealth (as the game counts them), the
    // distance and the observer's sight range in tiles, the angle off the observer's facing (degrees), the light on the
    // target (0 dark .. 1), whether the observer has darkvision.
    public Character Target { get; internal set; }
    public int ObserverDetection { get; internal set; }
    public int TargetStealth { get; internal set; }
    public double Distance { get; internal set; }
    public double SightRange { get; internal set; }
    public double Angle { get; internal set; }
    public double Light { get; internal set; }
    public bool Darkvision { get; internal set; }
    // Awareness gained per scan step (negative: losing track). Change it.
    public double Rate { get; set; }
}

public sealed class InstantDetection
{
    public int ObserverDetection { get; internal set; }
    public int TargetStealth { get; internal set; }
    // The game's verdict: the stealth is hopeless against this observer. Change it.
    public bool Instant { get; set; }
}

public static class Stealth
{
    public static readonly Hook<Action<Detection>> ModifyDetection = new Hook<Action<Detection>>("ModifyDetection");
    public static readonly Hook<Action<InstantDetection>> ModifyInstant = new Hook<Action<InstantDetection>>("ModifyInstant");
}

static class StealthHooks
{
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        const BindingFlags st = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(StealthHooks).GetMethod(n, st));
        harmony.Patch(typeof(bui).GetMethod("a", st, null, new[] { typeof(efk), typeof(int), typeof(int), typeof(double), typeof(double), typeof(double), typeof(double), typeof(bool) }, null), postfix: m("Rate"));
        harmony.Patch(typeof(bui).GetMethod("a", st, null, new[] { typeof(int), typeof(int) }, null), postfix: m("Instant"));
    }

    static void Rate(efk __0, int __1, int __2, double __3, double __4, double __5, double __6, bool __7, ref double __result)
    {
        if (!Stealth.ModifyDetection.Any) return;
        try
        {
            var d = new Detection { Target = Combat.OfData(__0), ObserverDetection = __1, TargetStealth = __2, Distance = __3, SightRange = __4, Angle = __5, Light = __6, Darkvision = __7, Rate = __result };
            Stealth.ModifyDetection.Run(h => h(d));
            __result = d.Rate;
        }
        catch (Exception ex) { Log.Write("stealth: " + ex.Message); }
    }

    static void Instant(int __0, int __1, ref bool __result)
    {
        if (!Stealth.ModifyInstant.Any) return;
        var d = new InstantDetection { ObserverDetection = __0, TargetStealth = __1, Instant = __result };
        Stealth.ModifyInstant.Run(h => h(d));
        __result = d.Instant;
    }
}
}
