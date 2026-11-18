// UnderRailWorks example: an API mod. Works has Text.Set (replace a text for good), but nothing to change the game's
// text as it is looked up. This mod adds that with one Harmony patch and offers it to other mods as a hook, so they get
// what Works' own hooks give (their handlers owned, ordered, removed when they unload, a failing one switched off) and
// only this mod touches the game's scrambled code. examples/GameTextUser uses it without referencing this DLL.
//
// The API mod's rules (docs/ModdingGuide.md, "Making an API mod"):
// - its own namespace and id, never UnderRailWorks';
// - the API in one public class, with .NET and Works types only, so other mods can describe it in an interface of
//   their own; only added to within a major version (mod.json "version"; dependents require "GameText >= 1.0");
// - every game lookup checked: if the game changed, the feature is off with one log line, and the dependents carry on.
using System;
using HarmonyLib;
using UnderRailWorks;

namespace GameTextExample
{
    public sealed class GameTextMod : WorksMod
    {
        internal static GameTextApi Api;

        public override void Load()
        {
            Api = new GameTextApi(this);
            Mods.Provide(this, Api);
            // qv.b(string): the game's text lookup ("category/key" -> the text in the player's language).
            var lookup = AccessTools.Method("qv:b", new[] { typeof(string) });
            if (lookup == null || lookup.ReturnType != typeof(string)) { Log.Info("the game's text lookup wasn't found: Shown is off"); return; }
            new Harmony("GameText").Patch(lookup, postfix: new HarmonyMethod(typeof(GameTextMod), nameof(Looked)));
            Api.Working = true;
            Log.Info("ready");
        }

        static void Looked(string __0, ref string __result)
        {
            var api = Api;
            if (api == null || !api.Shown.Any || __0 == null) return;
            string text = __result;
            api.Shown.Run(api.Mod, h => text = h(__0, text) ?? text);
            __result = text;
        }
    }

    // What other mods see (Mods.Api<T>("GameText")).
    public sealed class GameTextApi
    {
        internal readonly WorksMod Mod;
        internal GameTextApi(WorksMod mod) { Mod = mod; Shown = new Hook<Func<string, string, string>>(mod, "GameText.Shown"); }

        // Every lookup of the game's text: (key, text) -> the text to show (return text unchanged for keys you don't
        // change). Handlers run in load order, each getting the text the one before returned.
        public Hook<Func<string, string, string>> Shown { get; private set; }
        // False when the game's lookup wasn't found (a game update): Shown then never runs.
        public bool Working { get; internal set; }
    }
}
