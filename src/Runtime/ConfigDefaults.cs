// UnderRailWorks: defaults for a new config.dat (none yet: a fresh install, or deleted).
// - Sprite Target Mask (VS:M) on. Off, the world finds what is under the mouse by reading each layer's pixel back from
//   the GPU (a7m's hit test: Texture2D.GetData), which on FNA stalls until the GPU catches up: 10-30 ms per update while
//   the cursor rests over the world or a window above it. At 120 updates a second (FrameRateFix) the game then never
//   catches up (a frame every 0.5 s) and key presses shorter than that are lost. The game itself turns the mask on for
//   new configs under Wine (ls.a: cxc.a()), but its check (SteamHelper_IsRunningOnWine) isn't in the game's
//   SteamHelper.dll, so it never fires. VS:M is the game's own setting: config.dat stays readable by the stock game, and
//   the player can still turn it off in Options.
using System;
using System.IO;
using System.Reflection;

namespace UnderRailWorks.Internal
{
    static class ConfigDefaults
    {
        static void Loading(ref bool __state)
        {
            try { __state = !File.Exists(d5x.f()); } catch { __state = false; }
        }

        static void Loaded(ls __result, bool __state)
        {
            if (!__state || __result == null) return;
            try
            {
                var video = __result.g();
                if (video != null && !video.e) { video.e = true; Log.Write("config: new config.dat, Sprite Target Mask on"); }
            }
            catch (Exception ex) { Log.Write("config: " + ex.Message); }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(ConfigDefaults).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(ls).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, Type.EmptyTypes, null), prefix: m("Loading"), postfix: m("Loaded"));
        }
    }
}
