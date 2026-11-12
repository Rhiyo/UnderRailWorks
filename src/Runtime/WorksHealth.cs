// What didn't fit in this game version: hook groups that failed to install and game members not found (GameNames).
// Normally nothing. After a game update some may: the player is told once, on the main menu (which parts, that mods
// using them may not work, to look for a Works update); mods read WorksApi.Unavailable. Details in UnderRailWorks.log.
using System;
using System.Collections.Generic;
using System.Linq;
using UnderRailWorks;

namespace UnderRailWorks.Internal
{
static class WorksHealth
{
    static readonly List<string> failed = new List<string>();
    internal static readonly IReadOnlyList<string> Failed = failed.AsReadOnly();
    internal static int MissingNames;
    static bool told;

    internal static void Fail(string what) { lock (failed) if (!failed.Contains(what)) failed.Add(what); }

    // On the main menu: when it's built no screen is up yet for a popup, so it's tried each frame (Tick) until shown.
    static bool pending;
    internal static void Tell() { if (!told && (failed.Count > 0 || MissingNames > 0)) pending = true; }
    internal static void Tick()
    {
        if (!pending || told || UiPopups.Screen() == null) return;
        pending = false; told = true;
        try
        {
            string game = WorksApi.GameVersion, made = WorksApi.MadeForGameVersion;
            string text = (game != made ? "This game version (" + game + ") isn't the one this UnderRailWorks was made for (" + made + "). " : "")
                + (failed.Count > 0 ? "These parts of UnderRailWorks couldn't start: " + string.Join(", ", failed.ToArray()) + ". " : "")
                + (MissingNames > 0 ? MissingNames + " parts of the game it uses weren't found. " : "")
                + "Mods that use them may not work" + (game != made ? ": look for an UnderRailWorks update." : "; details in UnderRailWorks.log.");
            if (!Ui.Choose(null, "UnderRailWorks", text, null, "OK")) { told = false; pending = true; return; }
            Log.Always("UnderRailWorks: told the player: " + text);
        }
        catch (Exception ex) { Log.Write("health: " + ex.Message); }
    }
}
}
