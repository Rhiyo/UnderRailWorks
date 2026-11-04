// UnderRailFixes as an UnderRailWorks mod: Occluded characters (ShowCovered.cs) on Works' world-drawing events, with
// its settings in Mods\UnderRailFixes\settings.ini and its rows in the Mods tab.
using System;
using Microsoft.Xna.Framework;

[assembly: System.Reflection.AssemblyVersion("2.0.0")]

public sealed class UnderRailFixesMod : WorksMod
{
    internal const string Name = "UnderRailFixes";
    static ModLog log;
    internal static void Note(string line) { if (log != null) log.Info(line); }

    public override void Load()
    {
        log = base.Log;
        CoveredSettings.Load();
        // Outlines for every creature Tab would outline, while the effect is on (Works hides the extra ones; we draw them
        // on the covered part only).
        Outlines.Request(Name, () => ShowCovered.On);
        WorldDraw.LayerStarting.Add(this, ShowCovered.Drawn);
        WorldDraw.DrawLayer.Add(this, ShowCovered.DrawLayer);
        WorldDraw.DrawTileExtras.Add(this, ShowCovered.TileExtras);
        WorldDraw.WorldDone.Add(this, ShowCovered.WorldDone);
        WorldDraw.ShaderPassStarting.Add(this, ShowCovered.BeforePass);
        WorldDraw.ShaderPassDone.Add(this, ShowCovered.AfterPass);
        WorldDraw.InterfaceOffset.Add(this, Keyhole.Into);
        ModOptions.Register(Name, Page);
        log.Info("occluded characters ready (" + (CoveredSettings.On ? "on" : "off") + ")");
    }

    static readonly string[] EffectTexts = { "Off", "Keyhole", "Line fill", "Dithered fill", "Fill", "Outline" };
    static readonly string[] EffectValues = { "off", "keyhole", "lines", "dither", "fill", "outline" };

    static void Page(ModPage page)
    {
        page.Subsection("Occluded Characters");
        page.Toggle("ShowCovered", "Show occluded characters", false);
        page.Choice("ShowCoveredPlayer", "Player", EffectTexts, EffectValues, "keyhole");
        page.Choice("ShowCoveredHostile", "Hostile", EffectTexts, EffectValues, "lines");
        page.Choice("ShowCoveredOther", "Other", EffectTexts, EffectValues, "lines");
        page.Slider("ShowCoveredTransparency", "Fill transparency", 0, 100, 5, 75);
        page.Slider("KeyholeTransparency", "Keyhole transparency", 0, 100, 5, 75);
        page.Slider("KeyholeSize", "Keyhole size", 50, 200, 5, 100);
        // Back to the defaults (applied with Accept, like everything here; the on/off switch stays).
        page.Button("Recommended settings", "Reset", p =>
        {
            p.Set("ShowCoveredPlayer", "keyhole"); p.Set("ShowCoveredHostile", "lines"); p.Set("ShowCoveredOther", "lines");
            p.Set("ShowCoveredTransparency", "75"); p.Set("KeyholeTransparency", "75"); p.Set("KeyholeSize", "100");
        });
    }
}

enum CoveredEffect { Off, Keyhole, Lines, Dither, Fill, Outline }

static class CoveredSettings
{
    const string Defaults = @"; UnderRailFixes (an UnderRailWorks mod) settings. The Mods tab in Options changes these.

; Occluded characters: characters hidden behind scenery are shown where they're hidden. 1 = on.
ShowCovered=0
; Effect per kind of character: off, keyhole, lines, dither, fill, outline.
ShowCoveredPlayer=keyhole
ShowCoveredHostile=lines
ShowCoveredOther=lines
; Transparency of the fills (0-100), the keyhole (0-100), and the keyhole's size in % (50-200).
ShowCoveredTransparency=75
KeyholeTransparency=75
KeyholeSize=100
";
    static ModSettings s;
    static readonly string[] Carried = { "ShowCovered", "ShowCoveredPlayer", "ShowCoveredHostile", "ShowCoveredOther",
        "ShowCoveredTransparency", "KeyholeTransparency", "KeyholeSize", "KeyholeTiles" };

    internal static void Load()
    {
        bool fresh = !System.IO.File.Exists(System.IO.Path.Combine(System.IO.Path.Combine(ModLoader.ModsFolder, UnderRailFixesMod.Name), "settings.ini"));
        s = ModSettings.For(UnderRailFixesMod.Name, Defaults);
        // First start: take the player's settings from UnderrailFix.ini (1.x kept them there).
        if (fresh)
            foreach (string key in Carried)
            {
                string v = FixSettings.Raw(key);
                if (v != null) s.Set(key, v);
            }
    }

    internal static int Changes { get { return s.Changes; } }
    internal static bool On { get { return s.Bool("ShowCovered", false); } }
    internal static CoveredEffect Player { get { return Effect("ShowCoveredPlayer", CoveredEffect.Keyhole); } }
    internal static CoveredEffect Hostile { get { return Effect("ShowCoveredHostile", CoveredEffect.Lines); } }
    internal static CoveredEffect Other { get { return Effect("ShowCoveredOther", CoveredEffect.Lines); } }
    internal static int FillTransparency { get { return s.Int("ShowCoveredTransparency", 75, 0, 100); } }
    internal static int KeyholeTransparency { get { return s.Int("KeyholeTransparency", 75, 0, 100); } }
    internal static int KeyholeSize { get { return s.Int("KeyholeSize", 100, 25, 300); } }
    internal static bool KeyholeTiles { get { return s.Get("KeyholeTiles") == "1"; } }
    internal static bool KeyholeDebug { get { var v = s.Get("KeyholeDebug"); return v == "1" || v == "2"; } }
    internal static bool KeyholeDumps { get { return s.Get("KeyholeDebug") == "2"; } }

    static CoveredEffect Effect(string key, CoveredEffect fallback)
    {
        switch ((s.Get(key) ?? "").Trim().ToLowerInvariant())
        {
            case "off": case "0": return CoveredEffect.Off;
            case "keyhole": return CoveredEffect.Keyhole;
            case "lines": return CoveredEffect.Lines;
            case "dither": return CoveredEffect.Dither;
            case "fill": return CoveredEffect.Fill;
            case "outline": return CoveredEffect.Outline;
            default: return fallback;
        }
    }
}
