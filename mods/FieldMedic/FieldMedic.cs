// Field Medic: an UnderRailWorks mod. After a fight, if you're hurt, it treats you with what's in your bag, the way
// the game's own characters do: a bandage when you're above 40 % health (bandages don't work below that), else a health
// hypo. One item at a time, with the game's AP costs and checks. Built from Works' API only (CombatEnded, Items.Use,
// ItemUsed, Assets.Sound).
using System;
using System.Linq;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class FieldMedicConfig : ModConfig
{
    [Header("Field Medic")]
    [Label("Treat yourself after a fight below (% health)"), Range(30, 95), Increment(5)] public int Below = 70;
    [Label("Use health hypos when too hurt for bandages")] public bool Hypos = true;
    [Label("Play a sound when treating")] public bool Sound = true;
}

public sealed class FieldMedic : WorksMod
{
    static readonly string[] Hypos = { "med.healthHypo", "med.healthHypoEnriched", "med.advancedHealthHypo", "med.advancedHealthHypoEnriched", "med.superHealthHypo", "med.superHealthHypoEnriched" };
    FieldMedicConfig config;
    double wait = -1;     // seconds until the treatment (after the fight's dust settles)

    public override void Load()
    {
        config = GetConfig<FieldMedicConfig>();
        GameEvents.CombatEnded.Add(this, () => wait = 1.5);
        GameEvents.GameLoaded.Add(this, () => wait = -1);
        GameEvents.Update.Add(this, seconds =>
        {
            if (wait < 0) return;
            wait -= seconds;
            if (wait <= 0) { wait = -1; Treat(); }
        });
    }

    void Treat()
    {
        var p = World.Player;
        if (p == null || !p.IsCharacter || p.IsDead || Combat.InCombat || p.MaxHealth <= 0) return;
        float share = p.Health / p.MaxHealth;
        if (share * 100 >= config.Below) return;
        var bag = p.Inventory.Items;
        Item use = share > 0.4f ? bag.FirstOrDefault(i => i.Code == "med.bandage") : null;
        if (use == null && config.Hypos) use = Hypos.Select(code => bag.FirstOrDefault(i => i.Code == code)).FirstOrDefault(i => i != null);
        if (use == null) { Notify.OverPlayer("Nothing to patch up with"); return; }
        if (!Items.Use(p, use)) return;
        Notify.OverPlayer("Patching up: " + use.Name);
        if (config.Sound) { var s = Assets.Sound(this, "treat.wav"); if (s != null) s.Play(0.6f, 0f, 0f); }
    }
}
