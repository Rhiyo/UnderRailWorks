// Spike: a mod adds a new item as a data file, without shipping game code.
// 1. Overlay: the game loads every item definition by path through ez2.b(path, logError), reading
//    data\Rules\Items\<path>.item. A prefix looks in each mod's data\Rules\Items first, so a mod can add or replace items.
// 2. Test only: once the player exists, gives them works\hellothrowingknife (what the game's own "playerGiveItem"
//    console command does: ez2.a(path), then the player's Bag.z3(item)).
using System;
using System.IO;
using HarmonyLib;

public static class HelloItems
{
    static Action<string> log;
    static string mods;
    static bool given;
    static int frame;

    public static void OnLoad(Harmony harmony, Action<string> logger)
    {
        log = logger;
        mods = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");
        harmony.Patch(AccessTools.Method(typeof(ez2), "b", new[] { typeof(string), typeof(bool) }), prefix: new HarmonyMethod(typeof(HelloItems), "LoadItem"));
        harmony.Patch(AccessTools.Method("tb:Draw"), postfix: new HarmonyMethod(typeof(HelloItems), "Drawn"));
    }

    // ez2.b(string path, bool logError): bk3
    static bool LoadItem(string __0, ref bk3 __result)
    {
        foreach (string mod in Directory.GetDirectories(mods))
        {
            string file = Path.Combine(Path.Combine(mod, @"data\Rules\Items"), __0 + ".item");
            if (!File.Exists(file)) continue;
            __result = bk3.LoadFromBinaryFile<bk3>(file);
            log("item " + __0 + " from " + Path.GetFileName(mod) + ": " + __result.az());
            return false;
        }
        return true;
    }

    static void Drawn()
    {
        if (given || ++frame % 60 != 0) return;
        cyt game = bpo.GetService<cnk>()?.aqu();
        au2 player = game?.v();
        if (player == null) return;
        given = true;
        coh item = ez2.a(@"works\hellothrowingknife");
        if (item == null) { log("couldn't make the item"); return; }
        item.Stacks = 5;
        player.Bag.z3(item);
        log("gave the player 5 x " + item.Definition.az());
    }
}
