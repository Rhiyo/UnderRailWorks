// Neural Reallocation: an UnderRailWorks mod. A neurotechnician at the north gate of South Gate Station unwires what
// you've learned for charons: your skills (all skill points back), or your base abilities (everything above 3 back).
// The points are the game's own unspent points, placed on its level-up screen at your next level-up. Built from Works'
// API only (World.Place, Dialogs, Character skills/abilities/points, Inventory).
using System;
using System.Linq;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class NeuralReallocationConfig : ModConfig
{
    [Header("Neural Reallocation")]
    [Label("Price for skills (charons)"), Range(0, 2000), Increment(50)] public int SkillPrice = 100;
    [Label("Price for abilities (charons)"), Range(0, 5000), Increment(50)] public int AbilityPrice = 300;
}

public sealed class NeuralReallocation : WorksMod
{
    static readonly string[] Abilities = { "Strength", "Dexterity", "Agility", "Constitution", "Perception", "Will", "Intelligence" };
    const int AbilityFloor = 3;
    static NeuralReallocationConfig config;

    public override void Load()
    {
        config = GetConfig<NeuralReallocationConfig>();
        Dialogs.Define("neural/technician", d =>
        {
            int skills = SpentSkills(), abilities = SpentAbilities();
            d.Line("start", "Neural reallocation. I unwire what you've learned; you wire it back however you like at your next level-up. Skills: "
                    + config.SkillPrice + " charons. Abilities: " + config.AbilityPrice + " charons.")
                .Option("Reallocate my skills (" + skills + " points, " + config.SkillPrice + " charons)", "skills", Condition.Code(() => skills > 0 && CanPay(config.SkillPrice)), Outcome.Code(RespecSkills))
                .Option("Reallocate my abilities (" + abilities + " points, " + config.AbilityPrice + " charons)", "abilities", Condition.Code(() => abilities > 0 && CanPay(config.AbilityPrice)), Outcome.Code(RespecAbilities))
                .Option("How does it work?", "how")
                .Option("Not today.", Dialog.End);
            d.Line("how", "The points stay in your head, loose. Next time you level up, you place them with the new ones. Until then you're... less, shall we say.").Next("start");
            d.Line("skills", "Done. Your skill points are loose: place them at your next level-up. Try not to forget your own name.").Next(Dialog.End);
            d.Line("abilities", "Done. Your abilities are back to the bare minimum; place the points at your next level-up. Walk slowly for a while.").Next(Dialog.End);
        });
        World.Place(new Placement
        {
            Id = "neural_technician", Zone = "LU-SouthGateStation-N", Area = "LU-SouthGateStation-N", X = 16, Y = 21,
            Blueprint = "creatures\\generichumans\\coretechrunner", Name = "Neurotechnician", Dialog = "neural/technician",
        });
    }

    static bool CanPay(int price) { var p = World.Player; return p != null && p.Inventory.Money >= price; }

    static int SpentSkills() { var p = World.Player; return p == null ? 0 : p.SkillNames.Sum(n => p.BaseSkill(n)); }
    static int SpentAbilities() { var p = World.Player; return p == null ? 0 : Abilities.Sum(a => Math.Max(0, p.BaseAbility(a) - AbilityFloor)); }

    static void RespecSkills()
    {
        var p = World.Player;
        if (p == null || !p.Inventory.TakeMoney(config.SkillPrice)) return;
        int refund = 0;
        foreach (var n in p.SkillNames) { int v = p.BaseSkill(n); if (v > 0) { refund += v; p.SetBaseSkill(n, 0); } }
        p.SkillPoints += refund;
        Notify.Log("Neural reallocation: " + refund + " skill points to place at your next level-up (" + p.SkillPoints + " in all).");
    }

    static void RespecAbilities()
    {
        var p = World.Player;
        if (p == null || !p.Inventory.TakeMoney(config.AbilityPrice)) return;
        int refund = 0;
        foreach (var a in Abilities) { int v = p.BaseAbility(a); if (v > AbilityFloor) { refund += v - AbilityFloor; p.SetBaseAbility(a, AbilityFloor); } }
        p.AbilityPoints += refund;
        Notify.Log("Neural reallocation: " + refund + " ability points to place at your next level-up (" + p.AbilityPoints + " in all).");
    }
}
