// UnderRailWorks example mod: two Harmony patches on the obfuscated game, with no game code copied.
// Built against underrail.exe and 0Harmony.dll only, so the same DLL runs in the 32-bit (XNA) and 64-bit (FNA) modes.
using System;
using System.Diagnostics;
using HarmonyLib;

public static class HelloWorks
{
    static Action<string> log;
    static int frames;
    static readonly Stopwatch clock = Stopwatch.StartNew();
    static int reported;

    public static void OnLoad(Harmony harmony, Action<string> logger)
    {
        log = logger;
        // qv.b(string): the game's localized text lookup. The main menu shows qv.b("ui/MainMenu_Version") + ": 1.3.1.2".
        harmony.Patch(AccessTools.Method("qv:b", new[] { typeof(string) }), postfix: new HarmonyMethod(typeof(HelloWorks), "Text"));
        // tb.Draw(GameTime): the game's Draw. GameTime is an XNA type; the postfix takes no parameters, so it needn't name it.
        harmony.Patch(AccessTools.Method("tb:Draw"), postfix: new HarmonyMethod(typeof(HelloWorks), "Drawn"));
        log("hello from HelloWorks");
    }

    static void Text(string __0, ref string __result)
    {
        if (__0 == "ui/MainMenu_Version") __result = "UnderRailWorks | " + __result;
    }

    static void Drawn()
    {
        frames++;
        int second = (int)clock.Elapsed.TotalSeconds;
        if (second >= 5 && second % 5 == 0 && second != reported)
        {
            reported = second;
            log(frames + " frames drawn in " + second + " s");
        }
    }
}
