// Character Builder: an UnderRailWorks mod. The game's character-building numbers as settings (CharacterRules).
// The defaults are the game's own, so switching it on changes nothing until a value is changed.
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class CharacterBuilderConfig : ModConfig
{
    [Header("At creation")]
    [Label("Ability points to spend"), Range(0, 20)] public int AbilityPoints = 5;
    [Label("Lowest ability"), Range(1, 5)] public int AbilityMin = 3;
    [Label("Highest ability"), Range(8, 20)] public int AbilityMax = 10;
    [Label("Skill points"), Range(40, 400), Increment(10)] public int SkillPoints = 120;
    [Label("Feats"), Range(0, 6)] public int Feats = 2;
    [Header("Each level")]
    [Label("Skill points per level"), Range(10, 100), Increment(5)] public int SkillPerLevel = 40;
    [Label("A feat every ... levels"), Range(1, 5)] public int FeatEvery = 2;
    [Label("An ability point every ... levels"), Range(1, 8)] public int AbilityEvery = 4;
}

public sealed class CharacterBuilder : WorksMod
{
    CharacterBuilderConfig config;

    public override void Load()
    {
        config = GetConfig<CharacterBuilderConfig>();
        Apply();
        config.Changed += Apply;
    }

    void Apply()
    {
        CharacterRules.AbilityPointsAtCreation = config.AbilityPoints;
        CharacterRules.AbilityMinAtCreation = config.AbilityMin;
        CharacterRules.AbilityMaxAtCreation = config.AbilityMax;
        CharacterRules.SkillPointsAtCreation = config.SkillPoints;
        CharacterRules.FeatsAtCreation = config.Feats;
        CharacterRules.SkillPointsPerLevel = config.SkillPerLevel;
        CharacterRules.FeatEveryLevels = config.FeatEvery;
        CharacterRules.AbilityPointEveryLevels = config.AbilityEvery;
    }
}
