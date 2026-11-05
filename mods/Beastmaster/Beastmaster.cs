// Beastmaster: an UnderRailWorks mod. An ability calls an allied creature (World.Spawn with SpawnSide.Ally: the game's
// own "playerNativeAlly" faction) that follows the player (Character.Follow) and fights at their side for some turns.
// A small HUD part shows it. Built from Works' API only.
using System;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.2")]

public sealed class BeastmasterConfig : ModConfig
{
    [Header("Beastmaster")]
    [Label("Companion"), Hidden] public string Blueprint = "creatures\\rathound";
    [Label("Turns it stays"), Range(5, 60), Increment(5)] public int Turns = 20;
    [Label("Cooldown (turns)"), Range(0, 60), Increment(5)] public int Cooldown = 30;
    [Label("Call cost (AP)"), Range(0, 50), Increment(5)] public int ApCost = 20;
}

public sealed class Beastmaster : WorksMod
{
    BeastmasterConfig config;
    ModAbility call;
    Character companion;
    double leaveAt;
    UiElement panel;

    public override void Load()
    {
        config = GetConfig<BeastmasterConfig>();
        call = Abilities.Add(this, "call", T("ability.name"), T("ability.desc", config.Turns), "art/paw.png", apCost: config.ApCost, cooldown: config.Cooldown, key: Keys.J);
        call.Available = p => companion == null;
        call.Used += (user, target) => Call(user);
        config.Changed += () => { call.ApCost = config.ApCost; call.Cooldown = config.Cooldown; };
        GameEvents.Update.Add(this, s => Tick());
        GameEvents.FollowerArrived.Add(this, (now, old) => { if (old == companion) companion = now; });
        GameEvents.GameStarted.Add(this, () => { companion = null; leaveAt = 0; });     // spawns are never saved
        GameEvents.CharacterDying.Add(this, (who, killer) => { if (who == companion) { Notify.OverPlayer(T("fell")); companion = null; } });
        UiEvents.HudCreated.Add(this, root =>
        {
            panel = Ui.Custom(220, 22, c =>
            {
                var pet = companion;
                if (pet == null) return;
                c.Fill(c.Bounds, new Color(12, 12, 12, 210));
                c.Frame(c.Bounds, new Color(181, 92, 0));
                int left = Math.Max(0, (int)Math.Ceiling(leaveAt - GameClock.Turns));
                c.Text(T("status", pet.Name, (int)pet.Health, (int)pet.MaxHealth, left), c.Bounds.X + 6, c.Bounds.Y + 3, new Color(215, 215, 205));
            });
            Hud.AddPart(this, "Companion", panel, HudAnchor.Left, 10, 0);
            Ui.Show(panel, false);
        });
    }

    void Call(Character user)
    {
        var at = user.Position;
        var tile = World.FreeTilesNear((int)at.X, (int)at.Y, 3).Cast<Point?>().FirstOrDefault();     // nearest first; the player's own tile is taken
        if (tile == null) { Notify.OverPlayer(T("noRoom")); return; }
        companion = World.Spawn(config.Blueprint, tile.Value.X, tile.Value.Y, SpawnSide.Ally);
        if (companion == null) { Notify.OverPlayer(T("noRoom")); return; }
        companion.Follow(user, 2, true);     // comes along to other zones
        leaveAt = GameClock.Turns + config.Turns;
        Notify.Over(companion, T("called"), new Color(230, 160, 60));
        Log.Info("called " + companion.Name + " at " + tile.Value);
    }

    void Tick()
    {
        if (panel != null) Ui.Show(panel, companion != null);
        if (companion == null) return;
        if (companion.IsDead) { companion = null; return; }
        if (GameClock.Turns >= leaveAt && !Combat.InCombat)
        {
            Notify.Over(companion, T("gone"));
            World.Despawn(companion);
            companion = null;
        }
    }
}
