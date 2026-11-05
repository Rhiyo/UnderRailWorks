// Loadouts: an UnderRailWorks mod. Three named gear sets per game: save what you wear and wield, put a set back on with
// one click (the items must be in your bag). Like the equipment-set mods of other CRPGs. Built from Works' API only
// (Character.Equipment, WorksMod.Data: the sets are saved with the game as plain values, Keybinds, Ui).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.1.0")]

public sealed class Loadouts : WorksMod
{
    const int Sets = 3;
    Keybind openKey;
    UiElement hud;
    UiWindow panel;
    UiRows rows;
    readonly Dictionary<int, Keybind> wearKeys = new Dictionary<int, Keybind>();

    public override void Load()
    {
        openKey = Keybinds.Register(this, "open", "Loadouts", Keys.K);     // the game doesn't use K
        for (int i = 1; i <= Sets; i++) wearKeys[i] = Keybinds.Register(this, "wear" + i, "Wear loadout " + i);
        UiEvents.HudCreated.Add(this, root => { hud = root; panel = null; rows = null; });
        UiEvents.HudDestroyed.Add(this, () => hud = null);     // back to the main menu
        GameEvents.Update.Add(this, seconds =>
        {
            if (hud == null) return;
            if (openKey.Pressed) { if (panel == null) Open(); else Close(); }
            foreach (var kv in wearKeys) if (kv.Value.Pressed) Wear(kv.Key);
        });
    }

    // A set: "Slot=code;Slot=code".
    Dictionary<EquipSlot, string> Get(int set)
    {
        var result = new Dictionary<EquipSlot, string>();
        foreach (var part in (Data.Get("set" + set) ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = part.Split('='); EquipSlot slot;
            if (kv.Length == 2 && Enum.TryParse(kv[0], out slot)) result[slot] = kv[1];
        }
        return result;
    }

    void Save(int set)
    {
        var p = World.Player;
        if (p == null || !p.IsCharacter) return;
        var all = p.Equipment.All;
        Data.Set("set" + set, string.Join(";", all.Select(kv => kv.Key + "=" + kv.Value.Code)));
        Data.Set("name" + set, string.Join(", ", all.Values.Select(i => i.Name)));
        Notify.Log("Loadout " + set + " saved: " + (all.Count == 0 ? "nothing worn" : Data.Get("name" + set)) + ".");
        Refresh();
    }

    void Wear(int set)
    {
        var p = World.Player;
        if (p == null || !p.IsCharacter) return;
        var wanted = Get(set);
        if (wanted.Count == 0) { Notify.Log("Loadout " + set + " is empty: save one first."); return; }
        if (Combat.InCombat) { Notify.Log("Not during combat: change gear from the inventory (it costs AP)."); return; }
        var missing = new List<string>();
        int changed = 0;
        foreach (var kv in wanted)
        {
            var now = p.Equipment[kv.Key];
            if (now != null && now.Code == kv.Value) continue;
            var item = p.Inventory.Items.FirstOrDefault(i => i.Code == kv.Value);
            if (item == null) { missing.Add(Name(kv.Value)); continue; }
            if (p.Equipment.Equip(item, kv.Key == EquipSlot.SecondaryWeapon)) changed++;
            else missing.Add(item.Name);
        }
        Notify.Log("Loadout " + set + ": " + (changed == 0 && missing.Count == 0 ? "already worn" : changed + " changed") + (missing.Count > 0 ? "; not in your bag: " + string.Join(", ", missing) : "") + ".");
        Refresh();
    }

    // An item code's name, from a stored name when the item isn't around.
    static string Name(string code) { var dot = code.LastIndexOf('.'); return dot >= 0 ? code.Substring(dot + 1) : code; }

    void Open()
    {
        const int w = 620, h = 36 + Sets * 50;
        panel = Ui.Window(this, "loadouts", "Loadouts: save what you wear, put it back on in one click", w, h);
        if (panel == null) return;
        panel.Closed += () => { panel = null; rows = null; };
        Refresh();
    }

    void Refresh()
    {
        if (panel == null) return;
        if (rows == null) rows = UiRows.In(panel, 10, panel.Top + 6, 600, 50);
        rows.Clear();
        for (int i = 1; i <= Sets; i++)
        {
            int set = i;
            var desc = Data.Get("name" + set);
            rows.Add(set + ".  " + (string.IsNullOrEmpty(desc) ? "(empty)" : desc), null, UiRows.Button("Save", () => Save(set)), UiRows.Button("Wear", () => Wear(set)));
        }
    }

    void Close() { if (panel != null) panel.Close(); }
}
