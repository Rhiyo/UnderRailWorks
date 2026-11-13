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
        openKey = Keybinds.Add(this, "open", T("key"), Keys.D);
        GameEvents.Update.Add(this, s => { if (openKey.Pressed) { if (window != null && window.IsOpen) window.Close(); else Open(); } });
    }

    void Open()
    {
        var player = World.Player;
        if (player == null) return;
        var plans = Crafting.Plans(player).OrderByDescending(p => p.CanCraft).ThenBy(p => p.Missing.Count + p.Lacks.Count).ThenBy(p => p.Name).ToList();
        int w = Ui.S(560), h = Ui.S(440), line = Ui.LineHeight;
        window = Ui.Window(this, "planner", T("title"), w, h);
        if (window == null) return;
        window.Closed += () => window = null;
        w = window.Width;     // at least as wide as its title
        Ui.Add(window, Ui.Label(plans.Count == 0 ? T("none") : T("summary", plans.Count(p => p.CanCraft), plans.Count), w - 20), 10, window.Top + 2);
        int listTop = window.Top + line + 10, listHeight = window.Height - listTop - line - 20;
        var list = Ui.List(w - 20, listHeight);
        list.Items = plans.Select(p => p.CanCraft ? T("ready", p.Name) : T("missing", p.Name, string.Join(", ", p.Missing.Concat(p.Lacks.Select(kv => T("skill", kv.Key, kv.Value)))))).ToList();
        list.ItemColors = plans.Select(p => p.CanCraft ? new Color(120, 220, 120) : new Color(200, 170, 120)).ToList();
        Ui.Add(window, list.View, 10, listTop);
        var detail = Ui.Label(T("pick"), w - 20);
        Ui.Add(window, detail, 10, listTop + listHeight + 6);
        list.Selected += i =>
        {
            if (i < 0 || i >= plans.Count) return;
            var p = plans[i];
            var needs = p.Needs.Count == 0 ? "" : " (" + string.Join(", ", p.Needs.Select(kv => T("skill", kv.Key, kv.Value))) + ")";
            Ui.SetText(detail, p.Name + " [" + p.Category + "]" + needs);
        };
    }
}
