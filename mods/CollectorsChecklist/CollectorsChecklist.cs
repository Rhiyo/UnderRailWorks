// Collector's Checklist: an UnderRailWorks mod. A window (Home) with every oddity in the game's data (Items.Paths under
// "oddities"): studied ones in green with how often (Progression.StudiedOddities, the Oddities window's numbers), ones
// in the bag not studied yet in amber, the rest grey; studied first. Built from Works' API only.
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class CollectorsChecklist : WorksMod
{
    Keybind openKey;
    UiWindow window;

    public override void Load()
    {
        openKey = Keybinds.Register(this, "open", "Oddity checklist", Keys.Home);
        GameEvents.Update.Add(this, s => { if (openKey.Pressed) { if (window != null && window.IsOpen) window.Close(); else Open(); } });
    }

    void Open()
    {
        var player = World.Player;
        if (player == null) return;
        var studied = Progression.StudiedOddities;
        var bag = player.Inventory.Items.Select(i => i.Code).Where(c => c != null).ToList();
        var all = Items.Paths.Where(p => p.StartsWith("oddities\\", StringComparison.OrdinalIgnoreCase))
            .Select(p => new { Code = Items.Code(p), Name = Items.Name(p) ?? p })
            .Where(o => o.Code != null).ToList();
        var rows = all.Select(o =>
        {
            int times; studied.TryGetValue(o.Code, out times);
            bool carried = bag.Contains(o.Code);
            return new { o.Name, Times = times, Carried = carried, Rank = times > 0 ? 0 : carried ? 1 : 2 };
        }).OrderBy(r => r.Rank).ThenBy(r => r.Name).ToList();
        int done = rows.Count(r => r.Times > 0), w = Ui.S(460), h = Ui.S(460);
        window = Ui.Window(this, "checklist", "Oddities: " + done + " of " + rows.Count + " studied", w, h);
        if (window == null) return;
        window.Closed += () => window = null;
        var list = Ui.List(window.Width - 20, window.Height - window.Top - 12);
        list.Items = rows.Select(r => r.Times > 0 ? r.Name + (r.Times > 1 ? "  x" + r.Times : "") : r.Carried ? r.Name + "  (in your bag)" : r.Name).ToList();
        list.ItemColors = rows.Select(r => r.Times > 0 ? new Color(120, 220, 120) : r.Carried ? new Color(240, 180, 70) : new Color(130, 130, 125)).ToList();
        Ui.Add(window, list.View, 10, window.Top + 2);
    }
}
