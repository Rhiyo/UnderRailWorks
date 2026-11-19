// Pacing: an UnderRailWorks mod. How fast you level and what a level gives: the XP (or Oddity points) a level costs,
// the XP you earn, and extra skill, feat and ability points per level. The XP-rate and point mods every moddable CRPG
// grows. Built from Works' API only (Progression). Points and XP are plain numbers in the save: turning the mod off
// keeps what you have, and the save loads in the stock game.
using System;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class PacingConfig : ModConfig
{
    [Header("Pacing")]
    [Label("XP a level costs (%)"), Range(25, 300), Increment(5), Tooltip("Classic XP or Oddity points needed for each level.")] public int XpNeeded = 100;
    [Label("XP you earn (%)"), Range(25, 300), Increment(5)] public int XpEarned = 100;
    [Label("Extra skill points per level"), Range(0, 40), Increment(5)] public int SkillPoints = 0;
    [Label("Extra feat every N levels (0: none)"), Range(0, 10), Increment(1), Tooltip("The game gives a feat every other level; this adds one more every N levels.")] public int FeatEvery = 0;
    [Label("Extra ability point every N levels (0: none)"), Range(0, 10), Increment(1)] public int AbilityEvery = 0;
}

public sealed class Pacing : WorksMod
{
    PacingConfig config;

    public override void Load()
    {
        config = GetConfig<PacingConfig>();
        Progression.ModifyXpNeeded.Add(this, x => { if (config.XpNeeded != 100) x.Needed = Math.Max(1, (int)Math.Round(x.Needed * config.XpNeeded / 100.0)); });
        Progression.ModifyExperience.Add(this, g => { if (config.XpEarned != 100 && g.Amount > 0) g.Amount = Math.Max(1, (int)Math.Round(g.Amount * config.XpEarned / 100.0)); });
        Progression.ModifyLevelPoints.Add(this, p =>
        {
            p.Skill += config.SkillPoints;
            if (config.FeatEvery > 0 && p.Level % config.FeatEvery == 0) p.Feat++;
            if (config.AbilityEvery > 0 && p.Level % config.AbilityEvery == 0) p.Ability++;
            if (config.SkillPoints > 0 || p.Feat > 0 || p.Ability > 0) Log.Info("level " + p.Level + ": " + p.Skill + " skill, " + p.Feat + " feat, " + p.Ability + " ability points");
        });
    }
}
