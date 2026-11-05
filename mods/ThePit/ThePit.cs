// The Pit: an UnderRailWorks mod. A pit master at South Gate Station runs a gauntlet: pay the entry and waves of
// hostile creatures come at you, each one bigger than the last, each cleared wave paying out. Your best run is kept with
// the save. Built from Works' API only: World.Spawn (temporary hostile characters, never saved), FreeTilesNear,
// CharacterDied / CombatEnded, a Works dialog, an anchored HUD part, WorksMod.Data.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.1.0")]

public sealed class ThePitConfig : ModConfig
{
    [Header("The Pit")]
    [Label("Entry fee (charons)"), Range(0, 200), Increment(10)] public int Fee = 20;
    [Label("Charons per wave cleared (x wave number)"), Range(0, 100), Increment(5)] public int Reward = 15;
    [Label("Seconds between waves"), Range(3, 30), Increment(1)] public int Breather = 6;
}

public sealed class ThePit : WorksMod
{
    // Waves: blueprints, harder as they go; after the last, the last ones again with one more creature each time.
    static readonly string[][] Waves =
    {
        new[] { "creatures\\rathound", "creatures\\rathound" },
        new[] { "creatures\\rathound", "creatures\\rathound", "creatures\\rathound" },
        new[] { "creatures\\rathoundalpha", "creatures\\rathound", "creatures\\rathound" },
        new[] { "creatures\\crawler", "creatures\\crawler" },
        new[] { "creatures\\generichumans\\lurkercutthroat", "creatures\\rathound", "creatures\\rathound" },
        new[] { "creatures\\generichumans\\lurkergunner", "creatures\\generichumans\\lurkercutthroat", "creatures\\rathoundalpha" },
    };

    ThePitConfig config;
    HudText status;
    readonly List<Character> alive = new List<Character>();
    int wave;            // 0: no run
    double countdown;    // > 0: the next wave comes after this many seconds

    public override void Load()
    {
        config = GetConfig<ThePitConfig>();
        Dialogs.Define("pit/master", d =>
        {
            int best = Data.GetInt("best");
            d.Line("start", "The Pit. You pay, I let the beasts out, you try not to die. Every wave you clear pays better than the last."
                    + (best > 0 ? " Your best so far: wave " + best + "." : ""))
                .Option("I'm in. (" + config.Fee + " charons)", "go", Condition.Code(() => wave == 0 && World.Player != null && World.Player.Inventory.Money >= config.Fee), Outcome.Code(StartRun))
                .Option("[Not enough charons] I'm in.", "broke", Condition.Code(() => wave == 0 && World.Player != null && World.Player.Inventory.Money < config.Fee))
                .Option("I've had enough. Call them off.", "stop", Condition.Code(() => wave > 0), Outcome.Code(() => EndRun("You called it off at wave " + wave + ".")))
                .Option("Maybe later.", Dialog.End);
            d.Line("go", "Stand clear of the gate and keep your back to something. Here they come.").Next(Dialog.End);
            d.Line("broke", "No coin, no blood. Come back with " + config.Fee + " charons.").Next(Dialog.End);
            d.Line("stop", "Smart. The beasts that are out stay out, mind.").Next(Dialog.End);
        });
        World.Place(new Placement
        {
            Id = "pit_master", Zone = "LU-SouthGateStation-N", Area = "LU-SouthGateStation-N", X = 12, Y = 26,
            Blueprint = "creatures\\generichumans\\foundryworker", Name = "Pit Master", Dialog = "pit/master",
        });
        status = HudText.Add(this, "The Pit", HudAnchor.Top, 0, 40);
        status.MinWidth = 0;
        status.Background = new Color(30, 8, 4) * 0.85f;
        GameEvents.GameStarted.Add(this, () => Reset());
        GameEvents.ZoneLoaded.Add(this, (zone, first) => { if (wave > 0) EndRun("You left the Pit at wave " + wave + "."); });
        GameEvents.CharacterDied.Add(this, c =>
        {
            if (wave == 0) return;
            if (c.IsPlayer) { EndRun(null); return; }
            if (alive.RemoveAll(a => a == c) == 0) return;
            if (alive.Count > 0) { Notify.OverPlayer(alive.Count + " left"); return; }
            int pay = config.Reward * wave;
            if (pay > 0 && World.Player != null) World.Player.Inventory.AddMoney(pay);
            if (wave > Data.GetInt("best")) Data.Set("best", wave);
            Notify.Log("The Pit: wave " + wave + " cleared" + (pay > 0 ? ", " + pay + " charons" : "") + ". The next comes in " + config.Breather + " seconds.");
            Notify.OverPlayer("Wave " + wave + " cleared!");
            countdown = config.Breather;
        });
        GameEvents.Update.Add(this, seconds =>
        {
            if (wave > 0 && countdown > 0 && !Combat.InCombat)
            {
                int before = (int)Math.Ceiling(countdown);
                countdown -= seconds;
                if (countdown <= 0) { wave++; SpawnWave(); }
                else if ((int)Math.Ceiling(countdown) != before && countdown < 3.5) Notify.OverPlayer(((int)Math.Ceiling(countdown)).ToString());
            }
            ShowStatus();
        });
    }

    void Reset() { alive.Clear(); wave = 0; countdown = 0; }

    void StartRun()
    {
        var p = World.Player;
        if (p == null || !p.Inventory.TakeMoney(config.Fee)) return;
        wave = 1; countdown = 0;
        SpawnWave();
    }

    void EndRun(string message)
    {
        foreach (var c in alive.Where(c => !c.IsDead).ToList()) World.Despawn(c);
        if (message != null) Notify.Log("The Pit: " + message);
        int reached = Math.Max(0, wave - 1);
        if (reached > Data.GetInt("best")) Data.Set("best", reached);
        Reset();
    }

    void SpawnWave()
    {
        var p = World.Player;
        if (p == null) { Reset(); return; }
        var list = Waves[Math.Min(wave, Waves.Length) - 1].ToList();
        for (int extra = Waves.Length; extra < wave; extra++) list.Add(Waves[Waves.Length - 1][extra % Waves[Waves.Length - 1].Length]);
        int px = (int)Math.Round(p.Position.X), py = (int)Math.Round(p.Position.Y);
        // A ring around the player: not on top of them, not too far to find them.
        var tiles = World.FreeTilesNear(px, py, 9).Where(t => (t.X - px) * (t.X - px) + (t.Y - py) * (t.Y - py) >= 16).ToList();
        var rng = new Random(wave * 7919 + px * 31 + py);
        alive.Clear();
        foreach (var blueprint in list)
        {
            if (tiles.Count == 0) break;
            var t = tiles[rng.Next(tiles.Count)];
            tiles.RemoveAll(x => Math.Abs(x.X - t.X) <= 1 && Math.Abs(x.Y - t.Y) <= 1);
            var c = World.Spawn(blueprint, t.X, t.Y, SpawnSide.PlayerOnly);     // nobody else joins in
            if (c != null) alive.Add(c);
        }
        if (alive.Count == 0) { EndRun("The beasts wouldn't come out (no room here)."); return; }
        Notify.OverPlayer("Wave " + wave + "!");
        Notify.Log("The Pit: wave " + wave + ": " + string.Join(", ", alive.Select(c => c.Name)) + ".");
    }

    // A small part at the top of the HUD while a run is on.
    void ShowStatus()
    {
        string text = wave == 0 ? null
            : countdown > 0 ? "The Pit: wave " + wave + " cleared, wave " + (wave + 1) + " in " + Math.Ceiling(countdown) + " s"
            : "The Pit: wave " + wave + ", " + alive.Count(c => !c.IsDead) + " left";
        status.Show(new HudLines().Add(text, new Color(240, 170, 90)));
    }
}
