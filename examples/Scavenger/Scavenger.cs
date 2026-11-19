// Scavenger: an UnderRailWorks mod. A mod skill (CustomStats.AddSkill: the Other tab of the character screen, the
// game's skill points) whose value raises the chance of extra salvage in loot (Loot.Add with a chance per roll), and a
// bonus above a threshold (Stats.Modify). Its points travel with exported characters (CharacterData). Works' API only.
using System;
using System.Collections.Generic;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class ScavengerConfig : ModConfig
{
    [Header("Scavenger")]
    [Label("Chance per 10 skill (%)"), Range(1, 10)] public int PerTen = 3;
    [Label("Highest chance (%)"), Range(10, 80), Increment(5)] public int Max = 40;
    [Label("Mechanics bonus from (skill)"), Range(10, 100), Increment(5)] public int BonusFrom = 40;
}

public sealed class Scavenger : WorksMod
{
    ScavengerConfig config;
    ModSkill skill;

    static readonly string[] Salvage =
    {
        "components\\scraps\\metalscraps", "components\\scraps\\electronicscraps", "components\\scraps\\fabricscraps",
        "expendables\\energysources\\fusioncell", "devices\\electronicrepairkit1",
    };

    public override void Load()
    {
        config = GetConfig<ScavengerConfig>();
        skill = CustomStats.AddSkill(this, "scavenging", T("skill.name"), T("skill.desc"),
            new Dictionary<string, float> { { "Perception", 1f }, { "Intelligence", 0.5f } });
        // One rule per kind of salvage, each with the skill's chance (worked out when the loot is rolled).
        foreach (var path in Salvage)
            Loot.Add(this, (dead, box) => box != null || (dead != null && !dead.IsPlayer), path, (dead, box) => Chance() / Salvage.Length, 1);
        Stats.Modify.Add(this, r => { if (r.Character.IsPlayer && skill.Value >= config.BonusFrom) r.Add(StatBonus.Skill("Mechanics", 10)); });
        CustomStats.Changed.Add(this, () => { var p = World.Player; if (p != null) Stats.Refresh(p); });
        Commands.Add(this, "scavenger", "scavenger: the Scavenging skill and its loot chance", a => T("skill.name") + " " + skill.Base + " [" + skill.Value + "], salvage chance " + Math.Round(Chance() * 100) + "%");
    }

    double Chance() { return Math.Min(config.Max, skill.Value / 10.0 * config.PerTen) / 100.0; }
}
