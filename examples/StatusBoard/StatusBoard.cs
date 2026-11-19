// Status Board: an UnderRailWorks mod. A small HUD panel (top centre; HUD Layout can move it) spells out what the game
// only shows as icons: your health, your status effects by name with the turns they have left, and in combat your AP
// and MP and whose turn it is. It also says over your head when an effect wears off. Built from Works' API only (Character.StatusEffects/Health, Combat, Hud.AddPart).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.1.0")]

public sealed class StatusBoardConfig : ModConfig
{
    [Header("Status Board")]
    [Label("Show the board")] public bool Show = true;
    [Label("Only in combat")] public bool CombatOnly = false;
    [Label("Say when an effect wears off")] public bool AnnounceEnd = true;
    public override void OnChanged() { StatusBoard.Dirty = true; }
}

public sealed class StatusBoard : WorksMod
{
    internal static bool Dirty;
    StatusBoardConfig config;
    HudText board;
    double since;

    public override void Load()
    {
        config = GetConfig<StatusBoardConfig>();
        board = HudText.Add(this, "Status Board", HudAnchor.Top, 0, 10);
        GameEvents.StatusRemoved.Add(this, s =>
        {
            if (!config.AnnounceEnd || s.Character == null || !s.Character.IsPlayer || string.IsNullOrEmpty(s.Name)) return;
            Notify.OverPlayer(s.Name + " wore off");
            Dirty = true;
        });
        GameEvents.StatusApplied.Add(this, s => Dirty = true);
        GameEvents.Update.Add(this, seconds =>
        {
            since += seconds;
            if (!Hud.Exists || (since < 0.3 && !Dirty)) return;
            since = 0; Dirty = false;
            Refresh();
        });
    }

    void Refresh()
    {
        var p = World.Player;
        var lines = new HudLines();
        bool combat = Combat.InCombat;
        if (p != null && p.IsCharacter && config.Show && (combat || !config.CombatOnly))
        {
            lines.Add("Health " + (int)p.Health + "/" + (int)p.MaxHealth, p.Health < p.MaxHealth * 0.35f ? new Color(230, 110, 90) : HudLines.Body);
            if (combat)
            {
                var turn = Combat.Current;
                lines.Add("AP " + Combat.ActionPoints(p) + "   MP " + Combat.MovementPoints(p) + "   Round " + Combat.Round
                    + (turn == null ? "" : turn.IsPlayer ? "   Your turn" : "   " + turn.Name + "'s turn"), new Color(230, 200, 120));
            }
            foreach (var e in p.StatusEffects.Where(x => !string.IsNullOrEmpty(x.Name)))
                lines.Add(e.Name + (e.Stacks > 1 ? " x" + e.Stacks : "") + (e.Permanent ? "" : "  (" + Text.Count((int)Math.Ceiling(e.TurnsLeft), "turn") + ")"), new Color(160, 200, 230));
        }
        board.Show(lines);
    }
}
