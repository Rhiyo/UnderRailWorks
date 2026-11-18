// UnderRailWorks example: a mod built on an API mod (examples/GameText) without referencing its DLL. mod.json requires
// "GameText >= 1.0" (it loads first; without it this mod isn't loaded, with the reason in the Mods tab). The interface
// below lists the members used, with the same names and types as GameText's API: Works makes an object implementing it.
using System;
using UnderRailWorks;

namespace GameTextUserExample
{
    public interface IGameText
    {
        Hook<Func<string, string, string>> Shown { get; }
        bool Working { get; }
    }

    public sealed class GameTextUserMod : WorksMod
    {
        bool told;

        // PostLoad: every mod's Load has run, so GameText's API is there.
        public override void PostLoad()
        {
            Hook(false);
            // GameText hot-reloaded (HotReload=1): its old hook went with its old code; add the handler to the new one.
            Mods.Reloaded.Add(this, id => { if (id == "GameText") Hook(true); });
        }

        void Hook(bool again)
        {
            var text = Mods.Api<IGameText>("GameText");
            if (text == null) { Log.Info("GameText's API doesn't fit IGameText"); return; }
            if (!text.Working) Log.Info("GameText can't change text in this game version");
            text.Shown.Add(this, Shown);
            Log.Info("hooked GameText.Shown" + (again ? " again (GameText was reloaded)" : ""));
        }

        string Shown(string key, string text)
        {
            if (key != "ui/MainMenu_Version") return text;
            if (!told) { told = true; Log.Info("changed the main menu's version text"); }
            return "Game Text example | " + text;
        }
    }
}
