// Grudges: an UnderRailWorks mod. When a fight ends, hostile characters still alive and hurt (they fled, or the player
// got away) are remembered with their name and the blueprint they were made from (Character.Blueprint). Entering
// another area later, with a chance, one of them is there: spawned hostile a few tiles away, with a warning. Kept in
// the mod's save data (the game's save never holds them). Built from Works' API only.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class GrudgesConfig : ModConfig
{
    [Header("Grudges")]
    [Label("Chance per area entered (%)"), Range(0, 100), Increment(5), Tooltip("When you enter an area other than the one where the grudge started.")]
    public int Chance = 15;
    [Label("Grudges remembered"), Range(1, 20), Increment(1)] public int Remembered = 5;
}

// The API other mods use: Mods.Call("Grudges", "Hold", character) or Mods.Api<GrudgesApi>("Grudges").Hold(character).
public sealed class GrudgesApi
{
    readonly Grudges mod;
    internal GrudgesApi(Grudges mod) { this.mod = mod; }
    public bool Hold(Character c) { return mod.Hold(c); }
}

public sealed class Grudges : WorksMod
{
    GrudgesConfig config;
    static readonly Random random = new Random();

    // "name|blueprint|zone" per grudge, oldest first.
    List<string> List { get { return (SaveData.Of(this).Get<List<object>>("grudges", null) ?? new List<object>()).Select(o => Convert.ToString(o)).ToList(); } }
    void Store(List<string> l) { SaveData.Of(this).Set("grudges", l.Cast<object>().ToList()); }

    public override void Load()
    {
        config = GetConfig<GrudgesConfig>();
        GameEvents.CombatEnded.Add(this, Remember);
        GameEvents.ZoneLoaded.Add(this, (zone, first) => Return(zone));
        Mods.Provide(this, new GrudgesApi(this));     // other mods: Mods.Call("Grudges", "Hold", character)
    }

    // Remembers one character now (as a fight ending with it alive and hurt would). False: no blueprint matches it.
    internal bool Hold(Character c)
    {
        var bp = c == null ? null : c.Blueprint; var zone = World.ZoneId;
        if (bp == null || zone == null) return false;
        var l = List; l.Add(c.Name + "|" + bp + "|" + zone);
        while (l.Count > config.Remembered) l.RemoveAt(0);
        Store(l);
        return true;
    }

    void Remember()
    {
        var zone = World.ZoneId; if (zone == null) return;
        var l = List;
        foreach (var c in World.Characters.Where(c => c.IsHostile && !c.IsDead && !c.IsPlayer && c.Health < c.MaxHealth))
        {
            var bp = c.Blueprint;
            if (bp == null) continue;     // can't be made again
            l.Add(c.Name + "|" + bp + "|" + zone);
            Log.Info(c.Name + " holds a grudge (" + bp + ")");
        }
        while (l.Count > config.Remembered) l.RemoveAt(0);
        Store(l);
    }

    void Return(string zone)
    {
        var l = List; var p = World.Player;
        if (l.Count == 0 || p == null || Combat.InCombat || random.Next(100) >= config.Chance) return;
        int i = l.FindIndex(g => !string.Equals(g.Split('|')[2], zone, StringComparison.OrdinalIgnoreCase));
        if (i < 0) return;
        var parts = l[i].Split('|');
        var at = p.Position;
        var tile = World.FreeTilesNear((int)at.X, (int)at.Y, 7).Where(t => Math.Abs(t.X - at.X) + Math.Abs(t.Y - at.Y) >= 5).OrderBy(t => random.Next()).FirstOrDefault();
        if (tile == Point.Zero) return;
        var who = World.Spawn(parts[1], tile.X, tile.Y, SpawnSide.Hostile);
        if (who == null) return;
        who.Name = parts[0];
        l.RemoveAt(i); Store(l);
        Notify.OverPlayer(parts[0] + " has tracked you down!", new Color(230, 90, 60), true);
        Log.Info(parts[0] + " returned in " + zone);
    }
}
