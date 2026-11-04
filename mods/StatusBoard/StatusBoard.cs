// Status Board: an UnderRailWorks mod. A small HUD panel (top centre; HUD Layout can move it) spells out what the game
// only shows as icons: your health, your status effects by name with the turns they have left, and in combat your AP
// and MP and whose turn it is. It also says over your head when an effect wears off. Built from Works' API only (Character.StatusEffects/Health, Combat, Hud.AddPart).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

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
    UiElement hud;
    UiPanel panel;
    string shown = "";
    double since;

    public override void Load()
    {
        config = GetConfig<StatusBoardConfig>();
        UiEvents.HudCreated.Add(this, root => { hud = root; panel = null; shown = ""; });
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
            if (hud == null || (since < 0.3 && !Dirty)) return;
            since = 0; Dirty = false;
            Refresh();
        });
    }

    void Refresh()
    {
        var p = World.Player;
        var lines = new List<KeyValuePair<string, Color>>();
        bool combat = Combat.InCombat;
        if (p != null && p.IsCharacter && config.Show && (combat || !config.CombatOnly))
        {
            lines.Add(new KeyValuePair<string, Color>("Health " + (int)p.Health + "/" + (int)p.MaxHealth, p.Health < p.MaxHealth * 0.35f ? new Color(230, 110, 90) : new Color(215, 215, 205)));
            if (combat)
            {
                var turn = Combat.Current;
                lines.Add(new KeyValuePair<string, Color>("AP " + Combat.ActionPoints(p) + "   MP " + Combat.MovementPoints(p) + "   Round " + Combat.Round
                    + (turn == null ? "" : turn.IsPlayer ? "   Your turn" : "   " + turn.Name + "'s turn"), new Color(230, 200, 120)));
            }
            foreach (var e in p.StatusEffects.Where(x => !string.IsNullOrEmpty(x.Name)))
                lines.Add(new KeyValuePair<string, Color>(e.Name + (e.Stacks > 1 ? " x" + e.Stacks : "") + (e.Permanent ? "" : "  (" + Math.Ceiling(e.TurnsLeft) + " turn" + (Math.Ceiling(e.TurnsLeft) == 1 ? "" : "s") + ")"), new Color(160, 200, 230)));
        }
        var key = string.Join("\n", lines.Select(l => l.Key));
        if (key == shown && (panel != null || lines.Count == 0)) return;
        shown = key;
        if (panel != null) Ui.Remove(panel);
        panel = null;
        if (lines.Count == 0) return;
        int w = Math.Max(160, lines.Max(l => Ui.TextWidth(l.Key)) + 24), h = 10 + lines.Count * 18;
        panel = Ui.Panel(w, h);
        panel.Background = new Color(8, 8, 8) * 0.8f;
        for (int i = 0; i < lines.Count; i++)
        {
            var label = Ui.Label(lines[i].Key, w - 16);
            Ui.SetColor(label, lines[i].Value);
            Ui.Add(panel, label, 8, 4 + i * 18);
        }
        Hud.AddPart(this, "Status Board", panel, (hud.Width - w) / 2, 10);
    }
}
