// Difficulty Dial: an UnderRailWorks mod. Tune combat to taste: damage you take, damage you deal, your hit chance,
// your action points per turn. Every value is a setting (Options -> Mods -> Difficulty Dial settings) and applies at
// once; nothing is written to saves. Built from Works' combat API only.
using System;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class DifficultyDialConfig : ModConfig
{
    [Header("Difficulty Dial")]
    [Label("Damage you take (%)"), Range(25, 300), Increment(5), Tooltip("Before your armor, shields and resistances.")] public int DamageTaken = 100;
    [Label("Damage you deal (%)"), Range(25, 300), Increment(5), Tooltip("Before the target's armor, shields and resistances.")] public int DamageDealt = 100;
    [Label("Your hit chance (+/- points)"), Range(-50, 50), Increment(5)] public int HitChance = 0;
    [Label("Your action points per turn (+/-)"), Range(-20, 30), Increment(5)] public int ActionPoints = 0;
    [Label("How fast enemies notice you sneaking (%)"), Range(25, 300), Increment(5), Tooltip("100: the game's rate. Lower: you stay hidden longer.")] public int Detection = 100;
    [Label("Show the dial in the combat log"), Tooltip("A line at the start of each fight with the values in use (when any isn't the default).")] public bool Announce = true;
}

public sealed class DifficultyDial : WorksMod
{
    DifficultyDialConfig config;

    public override void Load()
    {
        Stealth.ModifyDetection.Add(this, d => { if (config.Detection != 100 && d.Target != null && d.Target.IsPlayer && d.Rate > 0) d.Rate *= config.Detection / 100.0; });
        config = GetConfig<DifficultyDialConfig>();
        Combat.ModifyIncomingDamage.Add(this, d =>
        {
            if (d.Target != null && d.Target.IsPlayer) { if (config.DamageTaken != 100) d.Scale(config.DamageTaken / 100f); }
            else if (d.ByPlayer && config.DamageDealt != 100) d.Scale(config.DamageDealt / 100f);
        });
        Combat.ModifyHitChance.Add(this, h => { if (h.Attacker != null && h.Attacker.IsPlayer && config.HitChance != 0) h.Chance += config.HitChance / 100.0; });
        Combat.ModifyTurnPoints.Add(this, t => { if (t.Character != null && t.Character.IsPlayer && config.ActionPoints != 0) t.ActionPoints += config.ActionPoints; });
        // Announced on the player's first turn of a fight (at combat start the game's own "enters combat" lines follow
        // and push it out of the short log).
        bool announced = false;
        GameEvents.CombatStarted.Add(this, () => announced = false);
        Combat.TurnStarted.Add(this, c =>
        {
            if (announced || c == null || !c.IsPlayer) return;
            announced = true;
            if (!config.Announce || (config.DamageTaken == 100 && config.DamageDealt == 100 && config.HitChance == 0 && config.ActionPoints == 0)) return;
            Combat.Log("Difficulty dial: damage taken " + config.DamageTaken + "%, dealt " + config.DamageDealt + "%, hit chance "
                + (config.HitChance >= 0 ? "+" : "") + config.HitChance + ", AP " + (config.ActionPoints >= 0 ? "+" : "") + config.ActionPoints);
        });
    }
}
