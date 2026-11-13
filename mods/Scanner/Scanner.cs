// Scanner: an UnderRailWorks mod. Point at a creature and a readout shows what the game keeps to itself: its level,
// health, how it feels about you, its damage threshold and resistance per damage type, and how sharp its eyes are
// against your stealth. Works' API only (World.Hovered, Character.Resistances / Detection, an anchored HUD part).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.1.0")]

public sealed class ScannerConfig : ModConfig
{
    public enum When { Always, InCombat, WhileKeyHeld }
    [Header("Scanner")]
    [Label("Show the readout"), Tooltip("While key held: the Scanner key in Options -> Controls (Left Alt by default).")] public When Show = When.Always;
    [Label("Include friendly and neutral characters")] public bool Everyone = false;
}

public sealed class Scanner : WorksMod
{
    ScannerConfig config;
    Keybind holdKey;
    HudText readout;
    double since;

    public override void Load()
    {
        config = GetConfig<ScannerConfig>();
        holdKey = Keybinds.Add(this, "hold", "Scanner (hold)", Keys.LeftAlt);
        readout = HudText.Add(this, "Scanner", HudAnchor.TopRight, 10, 80);
        readout.MinWidth = 220;
        readout.Background = new Color(6, 10, 12) * 0.88f;
        readout.Border = new Color(90, 160, 200);
        GameEvents.Update.Add(this, seconds =>
        {
            if (!Hud.Exists) return;
            since += seconds;
            if (since < 0.15) return;     // the hovered creature, a few times a second
            since = 0;
            var c = Target();
            var text = c == null ? null : Describe(c);
            var lines = new HudLines();
            if (text != null) { var parts = text.Split('\n'); for (int i = 0; i < parts.Length; i++) lines.Add(parts[i], i == 0 ? new Color(140, 210, 240) : new Color(210, 215, 215)); }
            readout.Show(lines);
        });
    }

    Character Target()
    {
        if (config.Show == ScannerConfig.When.InCombat && !Combat.InCombat) return null;
        if (config.Show == ScannerConfig.When.WhileKeyHeld && !holdKey.Down) return null;
        var c = World.Hovered;
        if (c == null || c.IsPlayer) return null;
        if (!config.Everyone && !c.IsHostile) return null;
        return c;
    }

    static string Describe(Character c)
    {
        var lines = new List<string>();
        lines.Add(c.Name + "  (level " + c.Level + ")");
        lines.Add("Health " + (int)c.Health + " / " + (int)c.MaxHealth + "    " + c.AttitudeToPlayer);
        var player = World.Player;
        int stealth = player == null ? 0 : player.Skill("Stealth");
        lines.Add("Detection " + c.Detection + " vs your stealth " + stealth);
        var res = c.Resistances.Where(kv => kv.Value.Threshold != 0 || kv.Value.Percent != 0).ToList();
        if (res.Count == 0) lines.Add("No damage resistance");
        else foreach (var kv in res) lines.Add(kv.Key + ": " + (kv.Value.Threshold != 0 ? kv.Value.Threshold + " threshold" : "") + (kv.Value.Threshold != 0 && kv.Value.Percent != 0 ? ", " : "") + (kv.Value.Percent != 0 ? kv.Value.Percent + "%" : ""));
        return string.Join("\n", lines);
    }
}
