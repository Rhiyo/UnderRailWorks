// Hunger: an UnderRailWorks mod. Satiety (0-100) drops with game time (GameTimeAdvanced, so not while paused or
// in a turn-based fight's waiting), eating the game's food raises it (ItemUsed + Item.Uses contains "Food"), and the
// hungry / starving statuses (ModStatuses with StatBonus penalties) follow it. State is per save (SaveData), the
// meter is a HUD part. Built from Works' API only.
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.1")]

public sealed class HungerConfig : ModConfig
{
    [Header("Hunger")]
    [Label("Game hours from full to starving"), Range(12, 120), Increment(6)] public int Hours = 48;
    [Label("A meal fills (%)"), Range(10, 100), Increment(5)] public int Meal = 40;
    [Label("Show the meter")] public bool Meter = true;
}

public sealed class Hunger : WorksMod
{
    HungerConfig config;
    ModStatus hungry, starving;
    double satiety = 100;
    bool loaded;
    int lastShown = -1;
    UiElement meter;

    public override void Load()
    {
        config = GetConfig<HungerConfig>();
        hungry = ModStatuses.Define(this, "hungry", T("hungry.name"), T("hungry.desc"), "Status.Poison", StatBonus.Ability("Strength", -1), StatBonus.Dodge(-5));
        starving = ModStatuses.Define(this, "starving", T("starving.name"), T("starving.desc"), "Status.Poison",
            StatBonus.Ability("Strength", -2), StatBonus.Ability("Agility", -1), StatBonus.Dodge(-15));
        GameEvents.GameStarted.Add(this, () => { satiety = SaveData.Get<double>("satiety", 100); loaded = true; Apply(false); });     // a new game has no saved value: 100
        GameEvents.Saving.Add(this, name => SaveData.Set("satiety", Math.Round(satiety, 2)));
        GameEvents.GameTimeAdvanced.Add(this, dt =>
        {
            if (!loaded || World.Player == null) return;
            satiety = Math.Max(0, satiety - 100.0 * dt.TotalHours / Math.Max(1, config.Hours));
            Apply(true);
        });
        GameEvents.ItemUsed.Add(this, u =>
        {
            if (u.User == null || !u.User.IsPlayer || u.Item == null || !u.Item.Uses.Contains("Food")) return;
            satiety = Math.Min(100, satiety + config.Meal);
            Notify.OverPlayer(T("ate", (int)satiety), new Color(120, 200, 120));
            Apply(true);
        });
        UiEvents.HudCreated.Add(this, root =>
        {
            meter = Ui.Custom(Ui.S(120), Ui.LineHeight + 2, c =>
            {
                var b = c.Bounds;
                c.Fill(b, new Color(12, 12, 12, 210));
                var fill = satiety >= 30 ? new Color(110, 150, 60) : satiety >= 10 ? new Color(200, 150, 40) : new Color(190, 50, 40);
                c.Fill(new Rectangle(b.X + 1, b.Y + 1, (int)((b.Width - 2) * satiety / 100), b.Height - 2), fill);
                c.Frame(b, new Color(181, 92, 0));
                var t = T("meter", (int)Math.Round(satiety));
                c.Text(t, b.X + (b.Width - Ui.TextWidth(t)) / 2, b.Y + 1, Color.White);
            });
            Hud.AddPart(this, "Hunger meter", meter, HudAnchor.TopLeft, 10, 160);
            Ui.Show(meter, config.Meter);
        });
        Commands.Add(this, "hunger.set", "hunger.set <percent>: how fed you are now", args =>
        {
            double v;
            if (args.Length == 0 || !double.TryParse(args[0], System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v)) return "fed " + Math.Round(satiety) + "%";
            satiety = Math.Max(0, Math.Min(100, v)); Apply(true);
            return "fed " + Math.Round(satiety) + "%";
        });
        config.Changed += () => { if (meter != null) Ui.Show(meter, config.Meter); };
    }

    // The statuses follow satiety: hungry below 30 %, starving below 10 % (kept on with a long duration, re-applied
    // each in-game hour so they never run out).
    void Apply(bool announce)
    {
        var p = World.Player;
        if (p == null) return;
        int band = satiety < 10 ? 2 : satiety < 30 ? 1 : 0;
        if (band != lastShown && announce && band > lastShown && lastShown >= 0) Notify.OverPlayer(T(band == 2 ? "nowStarving" : "nowHungry"), new Color(220, 160, 60));
        bool changed = band != lastShown;
        lastShown = band;
        if (band == 2) { if (hungry.Has(p)) hungry.Remove(p); if (changed || !starving.Has(p)) starving.Apply(p, 720); }
        else if (band == 1) { if (starving.Has(p)) starving.Remove(p); if (changed || !hungry.Has(p)) hungry.Apply(p, 720); }
        else { if (hungry.Has(p)) hungry.Remove(p); if (starving.Has(p)) starving.Remove(p); }
    }
}
