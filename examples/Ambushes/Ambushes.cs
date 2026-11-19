// Ambushes: an UnderRailWorks mod. On arriving in a zone visited before (ZoneLoaded, not the first visit), a chance of
// a pack of creatures (World.Spawn with SpawnSide.PlayerOnly: they fight the player and nobody else) a few tiles away.
// Clearing it gives experience. Spawns are never saved (Works takes them out around saves). Works' API only.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.1")]

public sealed class AmbushesConfig : ModConfig
{
    [Header("Ambushes")]
    [Label("Chance on arrival (%)"), Range(0, 100), Increment(5)] public int Chance = 15;
    [Label("Pack size"), Range(1, 6)] public int Pack = 3;
    [Label("Experience for clearing one"), Range(0, 500), Increment(25)] public int Xp = 100;
    [Label("Creatures (comma separated)"), Hidden] public string Creatures = "creatures\\rathound,creatures\\cavehopper,creatures\\dog";
}

public sealed class Ambushes : WorksMod
{
    AmbushesConfig config;
    readonly List<Character> pack = new List<Character>();
    readonly Random dice = new Random();
    double delay = -1;

    public override void Load()
    {
        config = GetConfig<AmbushesConfig>();
        GameEvents.ZoneLoaded.Add(this, (zone, first) =>
        {
            pack.Clear();
            if (!first && dice.Next(100) < config.Chance) delay = 1.5;     // after the zone settles
        });
        GameEvents.Update.Add(this, s =>
        {
            if (delay < 0) return;
            if ((delay -= s) > 0) return;
            delay = -1;
            Spring();
        });
        GameEvents.CharacterDied.Add(this, who =>
        {
            if (pack.Count == 0 || !pack.Contains(who)) return;
            if (pack.All(c => c.IsDead))
            {
                pack.Clear();
                if (config.Xp > 0 && World.Player != null) World.Player.GiveExperience(config.Xp, T("cleared"));
            }
        });
        Commands.Add(this, "ambush", "ambush: spring an ambush now", a => { Spring(); return pack.Count + " creatures"; });
    }

    void Spring()
    {
        var p = World.Player;
        if (p == null || Combat.InCombat) return;
        var at = p.Position;
        // Tiles 5-8 away, so they come at the player rather than appear on top of them.
        var tiles = World.FreeTilesNear((int)at.X, (int)at.Y, 8)
            .Where(t => { var d = Math.Max(Math.Abs(t.X - at.X), Math.Abs(t.Y - at.Y)); return d >= 5; })
            .OrderBy(t => dice.Next()).ToList();
        var kinds = config.Creatures.Split(',').Select(x => x.Trim()).Where(x => x.Length > 0).ToArray();
        if (tiles.Count == 0 || kinds.Length == 0) return;
        var kind = kinds[dice.Next(kinds.Length)];
        // A pack gathers: the first tile, then the free tiles nearest to it.
        var centre = tiles[0];
        foreach (var t in tiles.OrderBy(t => (t.X - centre.X) * (t.X - centre.X) + (t.Y - centre.Y) * (t.Y - centre.Y)).Take(config.Pack))
        {
            var c = World.Spawn(kind, t.X, t.Y, SpawnSide.PlayerOnly);
            if (c != null) pack.Add(c);
        }
        if (pack.Count > 0) { Notify.OverPlayer(T("warning"), new Color(220, 120, 60)); Log.Info("ambush: " + pack.Count + " x " + kind); }
    }
}
