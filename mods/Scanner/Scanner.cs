// Scanner: an UnderRailWorks mod. Point at a creature and a readout shows what the game keeps to itself: its level,
// health, how it feels about you, its damage threshold and resistance per damage type, and how sharp its eyes are
// against your stealth. Works' API only (World.Hovered, Character.Resistances / Detection, an anchored HUD part).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

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
    UiElement hud;
    UiPanel panel;
    Character shownFor;
    string shownText;
    double since;

    public override void Load()
    {
        config = GetConfig<ScannerConfig>();
        holdKey = Keybinds.Register(this, "hold", "Scanner (hold)", Keys.LeftAlt);
        UiEvents.HudCreated.Add(this, root => { hud = root; panel = null; shownFor = null; shownText = null; });
        UiEvents.HudDestroyed.Add(this, () => hud = null);
        GameEvents.Update.Add(this, seconds =>
        {
            if (hud == null) return;
            since += seconds;
            if (since < 0.15) return;     // the hovered creature, a few times a second
            since = 0;
            var c = Target();
            var text = c == null ? null : Describe(c);
            if (c == shownFor && text == shownText) return;
            shownFor = c; shownText = text;
            if (panel != null) Ui.Remove(panel);
            panel = null;
            if (text == null) return;
            var lines = text.Split('\n');
            int w = Math.Max(220, lines.Max(l => Ui.TextWidth(l)) + 24), h = 10 + lines.Length * 18;
            panel = Ui.Panel(w, h);
            panel.Background = new Color(6, 10, 12) * 0.88f;
            panel.Border = new Color(90, 160, 200);
            for (int i = 0; i < lines.Length; i++)
            {
                var l = Ui.Label(lines[i], w - 16);
                Ui.SetColor(l, i == 0 ? new Color(140, 210, 240) : new Color(210, 215, 215));
                Ui.Add(panel, l, 8, 4 + i * 18);
            }
            Hud.AddPart(this, "Scanner", panel, HudAnchor.TopRight, 10, 80);
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
