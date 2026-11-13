// Survey Pins: an UnderRailWorks mod. Notes pinned to the map: point at a spot and press the key (Insert), type a note
// in the game's own popup, and a pin shows on the Minimap (its label when the mouse rests on it). The key on a pin asks
// to remove it. Optionally, locked containers the player has discovered get a pin with their lock's kind and difficulty,
// until they're open. Pins are markers (Works keeps them in the save's mod data, never in the game's files).
// Built from Works' API only (World.HoveredTile, Ui.Ask/Confirm, Markers, World.Containers, Keybinds).
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class SurveyPinsConfig : ModConfig
{
    [Header("Survey Pins")]
    [Label("Pin locked containers"), Tooltip("Locked containers you've discovered get a pin with their lock, until you open them.")]
    public bool LockedContainers = true;
}

public sealed class SurveyPins : WorksMod
{
    SurveyPinsConfig config;
    Keybind pinKey;
    double sinceScan;
    static readonly Color NoteColour = new Color(120, 200, 255), LockColour = new Color(240, 150, 60);

    public override void Load()
    {
        config = GetConfig<SurveyPinsConfig>();
        pinKey = Keybinds.Add(this, "pin", T("key"), Keys.Insert);
        GameEvents.ZoneLoaded.Add(this, (zone, first) => sinceScan = 99);
        GameEvents.Update.Add(this, Tick);
    }

    void Tick(double seconds)
    {
        if (pinKey.Pressed) PinHere();
        sinceScan += seconds;
        if (sinceScan >= 3) { sinceScan = 0; ScanLocks(); }
    }

    void PinHere()
    {
        var tile = World.HoveredTile; var zone = World.ZoneId; var player = World.Player;
        if (tile == null || zone == null || player == null || player.Area == null) { Notify.OverPlayer(T("noSpot")); return; }
        var t = tile.Value;
        // A pin of ours within a tile: remove it (after asking).
        var near = Markers.Of(this).FirstOrDefault(m => m.Id.StartsWith("note:") && m.Zone == zone && Math.Abs(m.X - t.X) <= 1 && Math.Abs(m.Y - t.Y) <= 1);
        if (near != null) { var id = near.Id; Ui.Confirm(this, T("removeTitle"), T("removeText", near.Label), () => { Markers.Remove(this, id); Notify.OverPlayer(T("removed")); }); return; }
        var area = player.Area.Name;
        Ui.Ask(this, T("ask"), "", note =>
        {
            Markers.Add(this, "note:" + zone + ":" + t.X + "," + t.Y, zone, t.X, t.Y, note, NoteColour, area);
            Notify.OverPlayer(T("pinned"));
        });
    }

    // Locked containers on discovered tiles get a pin; open (or gone) ones lose theirs.
    void ScanLocks()
    {
        var zone = World.ZoneId; var player = World.Player;
        if (zone == null || player == null || player.Area == null) return;
        var keep = new System.Collections.Generic.HashSet<string>();
        if (config.LockedContainers)
            foreach (var c in World.Containers)
            {
                var l = c.Lock;
                if (l == null || !l.Locked || c.Area == null || c.Area.Name != player.Area.Name) continue;
                int x = (int)c.Position.X, y = (int)c.Position.Y;
                if (c.Area.KindAt(x, y) == TileKind.Nothing) continue;     // not discovered yet
                var id = "lock:" + zone + ":" + x + "," + y;
                keep.Add(id);
                if (!Markers.Of(this).Any(m => m.Id == id)) Markers.Add(this, id, zone, x, y, T("locked", c.Name, l.Difficulty), LockColour, c.Area.Name);
            }
        foreach (var m in Markers.Of(this).Where(m => m.Id.StartsWith("lock:") && m.Zone == zone && !keep.Contains(m.Id)).ToList()) Markers.Remove(this, m.Id);
    }
}
