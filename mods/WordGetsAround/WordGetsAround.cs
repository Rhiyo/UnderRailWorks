// Word Gets Around: an UnderRailWorks mod. Friendly characters near the player now and then say a line about what
// the player has done: quests finished, bodies left behind, money, wounds, experience. Lines are rules over game state
// (Works' Quests, Variables, Character); other mods add their own with WordGetsAround.AddLine (a library API: add
// "WordGetsAround" to your mod.json "requires"). Built from Works' API only.
using System;
using System.Collections.Generic;
using System.Linq;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class WordGetsAroundConfig : ModConfig
{
    [Header("Word Gets Around")]
    [Label("Characters comment on your deeds")] public bool Enabled = true;
    [Label("Seconds between remarks"), Range(5, 120), Increment(5), Tooltip("At least this long between two remarks anywhere.")] public int Gap = 20;
    [Label("Distance (tiles)"), Range(2, 12)] public int Distance = 6;
}

public static class WordGetsAround
{
    sealed class Line { public Func<bool> When; public string Text; public int Weight; }
    static readonly List<Line> lines = new List<Line>();

    // A remark characters may make while when() holds; weight: how often it's picked against others (1 = normal).
    public static void AddLine(Func<bool> when, string text, int weight = 1) { lines.Add(new Line { When = when, Text = text, Weight = Math.Max(1, weight) }); }

    internal static string Pick(Random random)
    {
        var ok = lines.Where(l => { try { return l.When(); } catch { return false; } }).ToList();
        int total = ok.Sum(l => l.Weight);
        if (total == 0) return null;
        int r = random.Next(total);
        foreach (var l in ok) { if (r < l.Weight) return l.Text; r -= l.Weight; }
        return null;
    }
}

public sealed class WordGetsAroundMod : WorksMod
{
    const string Kills = "wga_kills";
    WordGetsAroundConfig config;
    readonly Random random = new Random();
    readonly Dictionary<Character, DateTime> lastSpoke = new Dictionary<Character, DateTime>();
    DateTime lastAny = DateTime.MinValue;
    double sinceCheck;

    public override void Load()
    {
        config = GetConfig<WordGetsAroundConfig>();
        // Kills by the player's side: counted while the game runs, kept in a plain variable (saved, ignored by the stock game).
        GameEvents.CharacterDied.Add(this, c =>
        {
            if (c != null && c.IsCharacter && !c.IsPlayer && c.AttitudeToPlayer == Attitude.Hostile && Variables.Global.Available)
                Variables.Global.Set(Kills, Variables.Global.Get(Kills, 0) + 1);
        });
        GameEvents.ZoneLoaded.Add(this, (zone, first) => lastSpoke.Clear());

        var p = (Func<Character>)(() => World.Player);
        WordGetsAround.AddLine(() => Quests.Completed.Count >= 3, "Heard you've been busy. People talk, you know.");
        WordGetsAround.AddLine(() => Quests.Completed.Count >= 10, "You're the one sorting out everybody's problems, huh?", 2);
        WordGetsAround.AddLine(() => Variables.Global.Get(Kills, 0) >= 5, "Word is you've left a trail of bodies out there.");
        WordGetsAround.AddLine(() => Variables.Global.Get(Kills, 0) >= 25, "Folks say you're more dangerous than what's in the tunnels.", 2);
        WordGetsAround.AddLine(() => p().Health < p().MaxHealth * 0.35f, "You look like hell. Get yourself patched up.", 3);
        WordGetsAround.AddLine(() => p().Inventory.Money >= 500, "Heavy purse you've got there. Careful who sees it.");
        WordGetsAround.AddLine(() => p().Inventory.Money < 5, "Broke again? Join the club.");
        WordGetsAround.AddLine(() => p().Level >= 8, "Hard to believe you were the new face around here once.");
        WordGetsAround.AddLine(() => Variables.Global.Get("stranger_paid", false), "Saw you pay off that weirdo by the gate. Soft touch.");
        WordGetsAround.AddLine(() => Quests.Active.Count >= 5, "Got a lot on your plate, by the look of it.");

        GameEvents.Update.Add(this, seconds =>
        {
            sinceCheck += seconds;
            if (sinceCheck < 1 || !config.Enabled) return;
            sinceCheck = 0;
            var player = World.Player;
            if (player == null || player.IsInCombat || (DateTime.Now - lastAny).TotalSeconds < config.Gap) return;
            var near = World.Visible.Where(c => c.CanTalk && !c.IsInCombat && c.AttitudeToPlayer != Attitude.Hostile && c.Area == player.Area
                    && (c.Position - player.Position).Length() <= config.Distance && !Recently(c)).ToList();
            if (near.Count == 0 || random.Next(4) != 0) return;     // not every chance: remarks stay rare
            var text = WordGetsAround.Pick(random);
            if (text == null) return;
            var speaker = near[random.Next(near.Count)];
            if (speaker.Say(text, false)) { lastSpoke[speaker] = lastAny = DateTime.Now; Log.Info(speaker.Name + ": " + text); }
        });
    }

    bool Recently(Character c) { DateTime t; return lastSpoke.TryGetValue(c, out t) && (DateTime.Now - t).TotalSeconds < 120; }
}
