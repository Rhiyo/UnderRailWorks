// Wall Vision: Occluded characters (ShowCovered.cs) on Works' world-drawing events, with
// its settings in Mods\WallVision\settings.ini and its rows in the Mods tab.
using System;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class WallVisionMod : WorksMod
{
    internal const string Name = "WallVision";
    static ModLog log;
    internal static void Note(string line) { if (log != null) log.Info(line); }

    // Works removes the handlers and outline requests; the GPU memory is the mod's to free (a hot reload would leak it).
    public override void Unload() { try { Keyhole.Release(true); ShowCovered.Release(true); } catch { } }

    public override void Load()
    {
        log = base.Log;
        bool fresh = !System.IO.File.Exists(System.IO.Path.Combine(Info.Folder, "settings.ini"));
        CoveredSettings.Config = GetConfig<WallVisionConfig>();
        // First start: the player's settings from UnderRailFixes 1.x's UnderrailFix.ini, if there is one.
        if (fresh)
        {
            bool any = false;
            // 1.x applied ShowCovered only while its group switch GameplayFixes was on.
            bool group = WorksSettings.Get("GameplayFixes") != "0";
            foreach (string key in new[] { "ShowCovered", "ShowCoveredPlayer", "ShowCoveredHostile", "ShowCoveredOther", "ShowCoveredTransparency", "KeyholeTransparency", "KeyholeSize" })
            {
                string v = key == "ShowCovered" && !group ? "0" : WorksSettings.Get(key);
                if (v != null) { Settings.Set(key, v); any = true; }
            }
            if (any) { CoveredSettings.Config.Reload(); log.Info("settings taken from UnderrailFix.ini"); }
        }
        // Outlines for every creature Tab would outline, while the effect is on (Works hides the extra ones; we draw them
        // on the covered part only).
        Outlines.Request(this, () => ShowCovered.On || CoveredSettings.Config.SelectBehindScenery);
        // Characters behind scenery can be hovered and clicked as if Tab were held.
        Outlines.PickThrough(this, () => CoveredSettings.Config.SelectBehindScenery);
        WorldDraw.LayerStarting.Add(this, ShowCovered.Drawn);
        WorldDraw.DrawLayer.Add(this, ShowCovered.DrawLayer);
        WorldDraw.DrawTileExtras.Add(this, ShowCovered.TileExtras);
        WorldDraw.WorldDone.Add(this, ShowCovered.WorldDone);
        WorldDraw.ShaderPassStarting.Add(this, ShowCovered.BeforePass);
        WorldDraw.ShaderPassDone.Add(this, ShowCovered.AfterPass);
        WorldDraw.InterfaceOffset.Add(this, Keyhole.Into);
        GameEvents.Update.Add(this, seconds => { try { ShowCovered.Tidy(); } catch { } });     // frees the effects' GPU memory once they're off
        log.Info("occluded characters ready (" + (CoveredSettings.On ? "on" : "off") + ")");
    }
}

// The settings (Options -> Works -> Wall Vision, and Mods\WallVision\settings.ini).
public sealed class WallVisionConfig : ModConfig
{
    [Header("Occluded Characters")]
    [Label("Show occluded characters"), Tooltip("Occluded characters: characters hidden behind scenery are shown where they're hidden. 1 = on.\nFollows the game's Character Outlines option: none with \"Never\".")]
    public bool ShowCovered = false;
    [EnabledIf("ShowCovered"), Label("Player"), Tooltip("Effect per kind of character: off, keyhole (a see-through hole in what stands in front), lines,\ndither, fill (the covered part in the colour Tab gives them), outline.")]
    public CoveredEffect ShowCoveredPlayer = CoveredEffect.Keyhole;
    [EnabledIf("ShowCovered"), Label("Hostile")] public CoveredEffect ShowCoveredHostile = CoveredEffect.Lines;
    [EnabledIf("ShowCovered"), Label("Other")] public CoveredEffect ShowCoveredOther = CoveredEffect.Lines;
    [EnabledIf("ShowCovered"), Label("Fill transparency"), Range(0, 100), Increment(5), Tooltip("How see-through the lines, dots, fill and outline are, in percent (0 = solid).")]
    public int ShowCoveredTransparency = 75;
    [EnabledIf("ShowCovered"), Label("Keyhole transparency"), Range(0, 100), Increment(5), Tooltip("How see-through the keyhole is at its centre, in percent.")]
    public int KeyholeTransparency = 75;
    [EnabledIf("ShowCovered"), Label("Keyhole size"), Range(50, 200), Increment(5), Tooltip("The keyhole's size, in percent of the default (about the character's height).")]
    public int KeyholeSize = 100;
    [Label("Select characters behind scenery"), Tooltip("Characters behind walls and other scenery can be hovered and clicked\nwithout holding Tab, as the game allows while Tab is held.\nWorks with the effects off too.")]
    public bool SelectBehindScenery = true;
    [Hidden, Tooltip("Debugging: 1 = log the layers in each hole every few seconds; 2 = also save the keyhole's pictures once (keyhole-*.png).")]
    public int KeyholeDebug = 0;
}

public enum CoveredEffect
{
    Off,
    Keyhole,
    [Label("Line fill")] Lines,
    [Label("Dithered fill")] Dither,
    Fill,
    Outline,
}

static class CoveredSettings
{
    internal static WallVisionConfig Config;
    internal static int Changes { get { return Config.Changes; } }
    internal static bool On { get { return Config.ShowCovered; } }
    internal static CoveredEffect Player { get { return Config.ShowCoveredPlayer; } }
    internal static CoveredEffect Hostile { get { return Config.ShowCoveredHostile; } }
    internal static CoveredEffect Other { get { return Config.ShowCoveredOther; } }
    internal static int FillTransparency { get { return Math.Max(0, Math.Min(100, Config.ShowCoveredTransparency)); } }
    internal static int KeyholeTransparency { get { return Math.Max(0, Math.Min(100, Config.KeyholeTransparency)); } }
    internal static int KeyholeSize { get { return Math.Max(25, Math.Min(300, Config.KeyholeSize)); } }
    internal static bool KeyholeDebug { get { return Config.KeyholeDebug >= 1; } }
    internal static bool KeyholeDumps { get { return Config.KeyholeDebug >= 2; } }
}
