// Workbench Planner: an UnderRailWorks mod. A window (key D) with every recipe the player knows, the game's and mods',
// and whether it can be made now from the bag: ready ones first (green), the rest with the components or skill levels
// they still need. Built from Works' API only (Crafting.Plans, Ui.Window, Ui.List, Keybinds).
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class WorkbenchPlanner : WorksMod
{
    Keybind openKey;
    UiWindow window;

    public override void Load()
    {
        openKey = Keybinds.Register(this, "open", T("key"), Keys.D);
        GameEvents.Update.Add(this, s => { if (openKey.Pressed) { if (window != null && window.IsOpen) window.Close(); else Open(); } });
    }

    void Open()
    {
        var player = World.Player;
        if (player == null) return;
        var plans = Crafting.Plans(player).OrderByDescending(p => p.CanCraft).ThenBy(p => p.Missing.Count + p.Lacks.Count).ThenBy(p => p.Name).ToList();
        const int w = 560, h = 440;
        window = Ui.Window(this, "planner", T("title"), w, h);
        if (window == null) return;
        window.Closed += () => window = null;
        Ui.Add(window, Ui.Label(plans.Count == 0 ? T("none") : T("summary", plans.Count(p => p.CanCraft), plans.Count), w - 20), 10, window.Top + 2);
        var list = Ui.List(w - 20, h - 110, 22);
        list.Items = plans.Select(p => p.CanCraft ? T("ready", p.Name) : T("missing", p.Name, string.Join(", ", p.Missing.Concat(p.Lacks.Select(kv => T("skill", kv.Key, kv.Value)))))).ToList();
        list.ItemColors = plans.Select(p => p.CanCraft ? new Color(120, 220, 120) : new Color(200, 170, 120)).ToList();
        Ui.Add(window, list.View, 10, window.Top + 28);
        var detail = Ui.Label(T("pick"), w - 20);
        Ui.Add(window, detail, 10, window.Top + 28 + h - 110 + 6);
        list.Selected += i =>
        {
            if (i < 0 || i >= plans.Count) return;
            var p = plans[i];
            var needs = p.Needs.Count == 0 ? "" : " (" + string.Join(", ", p.Needs.Select(kv => T("skill", kv.Key, kv.Value))) + ")";
            Ui.SetText(detail, p.Name + " [" + p.Category + "]" + needs);
        };
    }
}
