// Gauntlet: an UnderRailWorks mod. Challenges chosen per game, as the game's own difficulty is: on the new-game screen
// (Game Rules -> Mods tab, NewGameOptions) pick Off, Tough, Brutal or Custom (the values in Options -> Mods ->
// Gauntlet, taken when the game starts). The choice is kept with that game (SaveData), so it can't be switched off
// mid-fight; games started before Gauntlet are Off. Effects: damage the player takes and deals (Combat.ModifyIncomingDamage), action points per turn
// (Combat.ModifyTurnPoints), merchants' prices (Prices.ModifyBuying), experience (Progression.ModifyExperience). A HUD
// note lists what's on. Built from Works' API only.
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.1.0")]

public sealed class GauntletConfig : ModConfig
{
    [Header("Gauntlet: the Custom challenge (chosen when a game starts)")]
    [Label("Damage you take (%)"), Range(100, 300), Increment(10)] public int DamageTaken = 150;
    [Label("Damage you deal (%)"), Range(25, 100), Increment(5)] public int DamageDealt = 100;
    [Label("Fewer action points per turn"), Range(0, 20), Increment(1)] public int ApLess = 0;
    [Label("Merchant prices (%)"), Range(100, 300), Increment(10)] public int Prices = 100;
    [Label("Experience (%)"), Range(25, 100), Increment(5)] public int Experience = 100;
    [Label("Show the HUD note")] public bool ShowNote = true;
}

public sealed class Gauntlet : WorksMod
{
    sealed class Challenge { public string Name; public int Taken = 100, Dealt = 100, ApLess, Prices = 100, Xp = 100; }

    static readonly Challenge Off = new Challenge { Name = "Off" };
    static readonly Challenge Tough = new Challenge { Name = "Tough", Taken = 150, Prices = 125 };
    static readonly Challenge Brutal = new Challenge { Name = "Brutal", Taken = 200, Dealt = 75, ApLess = 2, Prices = 150, Xp = 75 };

    GauntletConfig config;
    HudText note;
    Challenge now = Off;

    public override void Load()
    {
        config = GetConfig<GauntletConfig>();
        Combat.ModifyIncomingDamage.Add(this, d =>
        {
            if (d.Target != null && d.Target.IsPlayer && now.Taken != 100) d.Scale(now.Taken / 100f);
            else if (d.ByPlayer && now.Dealt != 100) d.Scale(now.Dealt / 100f);
        });
        Combat.ModifyTurnPoints.Add(this, t => { if (t.Character != null && t.Character.IsPlayer && now.ApLess > 0) t.ActionPoints = Math.Max(1, t.ActionPoints - now.ApLess); });
        Prices.ModifyBuying.Add(this, p => { if (now.Prices != 100) p.Markup *= now.Prices / 100f; });
        Progression.ModifyExperience.Add(this, x => { if (now.Xp != 100) x.Amount = Math.Max(0, x.Amount * now.Xp / 100); });
        note = HudText.Add(this, "Gauntlet", HudAnchor.TopRight, 12, 110);
        config.Changed += Show;
        UiEvents.HudCreated.Add(this, root => Show());
        NewGameOptions.AddChoice(this, "challenge", "Gauntlet", new[] { "Off", "Tough", "Brutal", "Custom" }, 0);
        // The player accepted the new-game screen: this game's challenge (Custom: today's Options values, kept).
        NewGameOptions.Applied.Add(this, () =>
        {
            int pick = NewGameOptions.Get(this, "challenge", 0);
            var custom = new Challenge { Name = "Custom", Taken = config.DamageTaken, Dealt = config.DamageDealt, ApLess = config.ApLess, Prices = config.Prices, Xp = config.Experience };
            Choose(new[] { Off, Tough, Brutal, custom }[Math.Max(0, Math.Min(3, pick))]);
        });
        GameEvents.GameStarted.Add(this, () => { now = Read(); Show(); });
    }

    Challenge Read()
    {
        var d = SaveData.Of(this);
        if (!d.Has("challenge")) return Off;
        return new Challenge { Name = d.Get("challenge", "Off"), Taken = d.Get("taken", 100), Dealt = d.Get("dealt", 100), ApLess = d.Get("apLess", 0), Prices = d.Get("prices", 100), Xp = d.Get("xp", 100) };
    }

    void Choose(Challenge c)
    {
        var d = SaveData.Of(this);
        d.Set("challenge", c.Name); d.Set("taken", c.Taken); d.Set("dealt", c.Dealt); d.Set("apLess", c.ApLess); d.Set("prices", c.Prices); d.Set("xp", c.Xp);
        now = c;
        Log.Info("challenge for this game: " + Describe(c));
        Show();
    }

    static string Describe(Challenge c)
    {
        var on = new List<string>();
        if (c.Taken != 100) on.Add("damage taken " + c.Taken + "%");
        if (c.Dealt != 100) on.Add("damage dealt " + c.Dealt + "%");
        if (c.ApLess > 0) on.Add("-" + c.ApLess + " AP");
        if (c.Prices != 100) on.Add("prices " + c.Prices + "%");
        if (c.Xp != 100) on.Add("XP " + c.Xp + "%");
        return on.Count == 0 ? "no changes" : string.Join(", ", on);
    }

    void Show()
    {
        if (note == null) return;
        var d = Describe(now);
        if (!config.ShowNote || now.Name == "Off" || d == "no changes") { note.Hide(); return; }
        var lines = new HudLines();
        lines.Add("Gauntlet: " + now.Name, new Color(230, 120, 60));
        foreach (var part in d.Split(new[] { ", " }, StringSplitOptions.None)) lines.Add(part, new Color(215, 215, 205));
        note.Show(lines);
    }
}
