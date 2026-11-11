// Gauntlet: an UnderRailWorks mod. Opt-in challenges, each off at its default value (100%, 0): the damage the player
// takes and deals (Combat.ModifyIncomingDamage), the player's action points each turn (Combat.ModifyTurnPoints),
// merchants' prices (Prices.BuyMarkup) and experience (Progression.ModifyExperience). A HUD note lists the active ones.
// Nothing is saved: change or switch them off at any time. Built from Works' API only.
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class GauntletConfig : ModConfig
{
    [Header("Gauntlet (100% / 0 = off)")]
    [Label("Damage you take (%)"), Range(100, 300), Increment(10)] public int DamageTaken = 100;
    [Label("Damage you deal (%)"), Range(25, 100), Increment(5)] public int DamageDealt = 100;
    [Label("Fewer action points per turn"), Range(0, 20), Increment(1)] public int ApLess = 0;
    [Label("Merchant prices (%)"), Range(100, 300), Increment(10)] public int Prices = 100;
    [Label("Experience (%)"), Range(25, 100), Increment(5)] public int Experience = 100;
    [Label("Show the HUD note")] public bool ShowNote = true;
}

public sealed class Gauntlet : WorksMod
{
    GauntletConfig config;
    HudText note;

    public override void Load()
    {
        config = GetConfig<GauntletConfig>();
        Combat.ModifyIncomingDamage.Add(this, d =>
        {
            if (d.Target != null && d.Target.IsPlayer && config.DamageTaken != 100) d.Scale(config.DamageTaken / 100f);
            else if (d.ByPlayer && config.DamageDealt != 100) d.Scale(config.DamageDealt / 100f);
        });
        Combat.ModifyTurnPoints.Add(this, t => { if (t.Character != null && t.Character.IsPlayer && config.ApLess > 0) t.ActionPoints = Math.Max(1, t.ActionPoints - config.ApLess); });
        Prices.BuyMarkup.Add(this, (merchant, markup) => config.Prices == 100 ? markup : markup * config.Prices / 100f);
        Progression.ModifyExperience.Add(this, x => { if (config.Experience != 100) x.Amount = Math.Max(0, x.Amount * config.Experience / 100); });
        note = HudText.Add(this, "Gauntlet", HudAnchor.TopRight, 12, 110);
        config.Changed += Show;
        UiEvents.HudCreated.Add(this, root => Show());
    }

    void Show()
    {
        var on = new List<string>();
        if (config.DamageTaken != 100) on.Add("Damage taken " + config.DamageTaken + "%");
        if (config.DamageDealt != 100) on.Add("Damage dealt " + config.DamageDealt + "%");
        if (config.ApLess > 0) on.Add("-" + config.ApLess + " AP");
        if (config.Prices != 100) on.Add("Prices " + config.Prices + "%");
        if (config.Experience != 100) on.Add("XP " + config.Experience + "%");
        if (!config.ShowNote || on.Count == 0) { note.Hide(); return; }
        var lines = new HudLines();
        lines.Add("Gauntlet", new Color(230, 120, 60));
        foreach (var o in on) lines.Add(o, new Color(215, 215, 205));
        note.Show(lines);
    }
}
