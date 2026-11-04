// UnderrailFix.ini next to the game. Read once at launch; Width/Height are written back when the in-game menu changes them.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

public static class FixSettings
{
    public const string FileName = "UnderrailFix.ini";
    public const int DefaultZoomMin = 75, DefaultZoom = 150, DefaultZoomMax = 200;

    // Bump when a setting's meaning changes, and list it in Reset: older files get those keys reset to the new
    // defaults instead of keeping a value that now means something else.
    const int Version = 3;
    static readonly Dictionary<string, int> Reset = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase)
    {
        { "ZoomMin", 3 }, { "ZoomMax", 3 }, { "ZoomDefault", 3 },     // 3: 1080p-equivalent zoom (2: percent of 1080p's default view)
    };

    const string Default =
@"; UnderrailFix settings. Changes apply on the next launch.
[UnderrailFix]
SettingsVersion=3

; 1 = resolution list built from the modes the monitor reports, long dropdowns scroll, and the resolution is
; kept below instead of in config.dat (config.dat is left untouched). 0 = the game's own list and config.dat.
ResolutionFix=1

; Used when ResolutionFix=1: native = the desktop's width/height at launch, or a number of pixels.
; Picking a resolution in the game's options writes these.
Width=native
Height=native

; With ResolutionFix=1, save screenshots on screens bigger than 2048 pixels (the game only fits those in its
; picture by cropping: it keeps just the top-left 2048 pixels of the screen):
; 1 = what a 1920x1080 screen would show, the 16:9 area at the centre of the screen at 1920x1080; 0 = the game's own.
SaveScreenshots=1

; 1 = zoom as a 1080p screen would need it for the same view, on every screen: the world looks the same size at
;     any resolution, and the game's zoom indicator shows the same numbers as on a 1920x1080 screen.
; 0 = the game's own zoom, which depends on the resolution (1x to 2x, or 3x on screens wider than 1920).
ZoomFix=1

; With ZoomFix=1, in percent as the game shows it: the starting zoom (also used with Allow Zoom off), and how far
; Allow Zoom (SHIFT+wheel) can go. The game's own numbers at 1080p: starts at 150, goes from 100 to 200.
; Below 100 zooms out further than the game ever did: more of the map on screen, so more to draw.
ZoomDefault=150
ZoomMin=75
ZoomMax=200

; 1 = interface scale, insets and menu backgrounds below; 0 = the game's own interface (UiScale, the insets and BackgroundFit are ignored).
UiScaleFix=1

; With UiScaleFix=1: interface scale on top of the game's Interface Size option. auto = whole multiples by the screen's shorter side:
; 100% below 4320p, 200% from 4320p (8K), 300% from 6480p... Or a percentage, e.g. 150.
; The game's Interface Size choices follow the scaled screen: at 150% a 1440p screen offers sizes for 960p.
; Only whole multiples (100, 200, 300) stay crisp; values in between make the pixel art and text look uneven.
UiScale=auto

; With UiScaleFix=1, insets: space kept clear at the screen's edges, so the interface (HUD, menus) sits in a centred area while the
; game world still fills the screen. Each is the total taken off, split evenly between the two sides:
;   a decimal or % is a share of the screen (0.1 or 10% = 5% on each side), a whole number or px is screen pixels
;   (256 or 256px = 128 px on each side). 0 = none.
UiInsetX=0
UiInsetY=0

; With UiScaleFix=1, menu backgrounds (main menu, options...): 0 = fill the screen, cropping the art (the game's own);
; 1 = show the whole image, as large as fits, never stretched. Either way they ignore the insets.
BackgroundFit=0

; 1 = the gameplay fixes below; 0 = the game's own behaviour for all of them (and no Gameplay section in
; Options -> Fixes).
GameplayFixes=1

; Occluded characters. With GameplayFixes=1: 1 = characters behind walls or other scenery are shown where they're hidden, as chosen
; below for the player, hostile characters (hostile and unfriendly) and every other character. Follows the game's
; Character Outlines option: none with ""Never"". Off by default.
ShowCovered=0
; For each: off; keyhole = a round see-through hole in whatever stands in front of them (as Neverwinter Nights: EE's
; keyholing); lines = the covered part in the colour holding Tab gives them, as horizontal lines; dither = the same as
; spaced-out dots; fill = the same, solid; outline = the outline holding Tab draws, on the covered part.
ShowCoveredPlayer=keyhole
ShowCoveredHostile=lines
ShowCoveredOther=lines
; How see-through the lines, dots, fill and outline are, in percent (0 = solid, 100 = invisible).
ShowCoveredTransparency=75
; How see-through the keyhole is at its centre, in percent, and its size in percent of the default (about the
; character's height).
KeyholeTransparency=75
KeyholeSize=100

; Windows only: 1 = tell Windows the game handles high-DPI screens itself, so display scaling (e.g. 150% or 200%
; in Windows' display settings) doesn't stretch it or hide part of the resolution. 0 = Windows' default behaviour.
DpiAware=1

; 1 = give the game window the game's icon explicitly. Under Proton (especially with Wine's Wayland driver) the window
; otherwise has none and the task bar shows a generic icon; on Windows it already has it, so nothing changes there.
WindowIcon=1

; 1 = no 60 FPS cap; 0 = the game's fixed 60 updates and frames per second.
; Off by default: this hasn't been tested thoroughly yet. The game plays at normal speed at 240 FPS, but not every
; part of the game has been tried at other frame rates.
FrameRateFix=0

; With FrameRateFix=1: 0 = no cap, or a frame rate, e.g. 120. With the game's VSync on, no cap means the monitor's
; refresh rate (the smoothest); a cap that divides it evenly (e.g. 120 or 60 on 240 Hz) paces best.
MaxFps=0

; 1 = write UnderrailFix.log in the game folder (what the fixes decided and any errors), e.g. for a bug report.
Log=0
";

    static Dictionary<string, string> values;

    public static string Path => System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, FileName);

    static Dictionary<string, string> Values
    {
        get
        {
            if (values != null) return values;
            values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            try
            {
                if (File.Exists(Path)) values = Parse(File.ReadAllLines(Path));
                var defaults = Parse(Default.Split('\n'));
                int version;
                if (!values.ContainsKey("SettingsVersion") || !int.TryParse(values["SettingsVersion"], out version)) version = 1;
                if (!File.Exists(Path) || version < Version || defaults.Keys.Any(k => !values.ContainsKey(k)))
                {
                    // Write the current file (comments and all), keeping the user's values that still mean the same.
                    foreach (var r in Reset) if (version < r.Value && values.Remove(r.Key)) Log.Write("settings: " + r.Key + " reset to the new default (its meaning changed)");
                    values["SettingsVersion"] = Version.ToString();
                    var lines = Default.Replace("\r\n", "\n").TrimEnd('\n').Split('\n').Select(l =>
                    {
                        int eq = l.IndexOf('=');
                        if (eq <= 0 || l.StartsWith(";")) return l;
                        string key = l.Substring(0, eq).Trim(), v;
                        return values.TryGetValue(key, out v) ? key + "=" + v : l;
                    });
                    File.WriteAllText(Path, string.Join("\n", lines) + "\n");
                    values = Parse(File.ReadAllLines(Path));
                }
            }
            catch (Exception ex) { Log.Write("could not read " + FileName + ": " + ex.Message); }
            return values;
        }
    }

    static Dictionary<string, string> Parse(IEnumerable<string> lines)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[' || eq < 0) continue;
            result[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
        return result;
    }

    static string Get(string key) { string v; return Values.TryGetValue(key, out v) ? v : null; }

    static bool Flag(string key)
    {
        string v = Get(key);
        return v == null || !(v == "0" || v.Equals("off", StringComparison.OrdinalIgnoreCase) || v.Equals("false", StringComparison.OrdinalIgnoreCase));
    }

    // A positive whole number, else null (native, empty, missing or invalid).
    static int? Number(string key)
    {
        int n;
        return int.TryParse((Get(key) ?? "").TrimEnd('%', ' '), out n) && n > 0 ? n : (int?)null;
    }

    public static bool ResolutionFix => Flag("ResolutionFix");
    public static bool ZoomFix => Flag("ZoomFix");
    public static bool SaveScreenshots => Flag("SaveScreenshots");
    public static int? Width => Number("Width");
    public static int? Height => Number("Height");
    // 25 % at least: far below it the game would draw so many tiles it would nearly freeze.
    public static int ZoomMin => Zoom("ZoomMin", DefaultZoomMin);
    public static int ZoomMax => Zoom("ZoomMax", DefaultZoomMax);
    public static int ZoomDefault => Zoom("ZoomDefault", DefaultZoom);
    static int Zoom(string key, int fallback) { var n = Number(key); return n.HasValue ? Math.Max(25, Math.Min(1000, n.Value)) : fallback; }
    public static int? UiScale => Number("UiScale");     // null = auto
    public static bool UiScaleFix => Flag("UiScaleFix");
    public static string UiInsetX => Get("UiInsetX");
    public static string UiInsetY => Get("UiInsetY");
    public static bool DpiAware => Flag("DpiAware");
    public static bool GameplayFixes => Flag("GameplayFixes");
    public static bool ShowCovered => GameplayFixes && Get("ShowCovered") == "1";     // off unless set
    public enum CoveredEffect { Off, Keyhole, Lines, Dither, Fill, Outline }
    public static CoveredEffect CoveredPlayer => Effect("ShowCoveredPlayer", CoveredEffect.Keyhole);
    public static CoveredEffect CoveredHostile => Effect("ShowCoveredHostile", CoveredEffect.Lines);
    public static CoveredEffect CoveredOther => Effect("ShowCoveredOther", CoveredEffect.Lines);
    public static bool AnyKeyhole => CoveredPlayer == CoveredEffect.Keyhole || CoveredHostile == CoveredEffect.Keyhole || CoveredOther == CoveredEffect.Keyhole;
    static CoveredEffect Effect(string key, CoveredEffect fallback)
    {
        switch ((Get(key) ?? "").Trim().ToLowerInvariant())
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
    static int Percent(string key, int fallback, int min, int max) { int n; return int.TryParse((Get(key) ?? "").TrimEnd('%', ' '), out n) ? Math.Max(min, Math.Min(max, n)) : fallback; }
    public static int KeyholeTransparency => Percent("KeyholeTransparency", 75, 0, 100);
    public static int KeyholeSize => Percent("KeyholeSize", 100, 25, 300);
    // Not in the ini or the Fixes tab: 1 = draw the keyhole's front picture in tiles of at most 2048 even where one
    // picture works (for testing the tiled path that Microsoft XNA needs above 2048).
    public static bool KeyholeTiles => Get("KeyholeTiles") == "1";
    public static int ShowCoveredTransparency => Percent("ShowCoveredTransparency", 75, 0, 100);
    public static bool WindowIcon => Flag("WindowIcon");
    public static bool FrameRateFix { get { string v = Get("FrameRateFix"); return v == "1" || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase); } }     // off unless set (not tested thoroughly)
    public static int MaxFps => Number("MaxFps") ?? 0;
    public static bool BackgroundFit => UiScaleFix && Get("BackgroundFit") == "1";     // off unless set; part of the Interface group
    public static bool LogEnabled { get { string v = Get("Log"); return v == "1" || string.Equals(v, "on", StringComparison.OrdinalIgnoreCase) || string.Equals(v, "true", StringComparison.OrdinalIgnoreCase); } }     // off unless set

    // Called when the in-game menu picks a resolution; null = native.
    public static void SetSize(int? w, int? h)
    {
        Set("Width", w.HasValue ? w.Value.ToString() : "native");
        Set("Height", h.HasValue ? h.Value.ToString() : "native");
    }

    // The value as written in the file (null if missing), e.g. to put it back after a revert.
    public static string Raw(string key) { return Get(key); }

    // From the in-game options: changes the value now (settings read every frame, like the zoom limits, follow at once)
    // and in the file.
    public static void SetValue(string key, string value) { Set(key, value); }

    // Bumped on every change, so code reading settings per frame can keep its own copy (ShowCovered).
    public static int Changes { get; private set; }

    static void Set(string key, string value)
    {
        if (string.Equals(Get(key), value, StringComparison.OrdinalIgnoreCase)) return;
        Values[key] = value;
        Changes++;
        try
        {
            var lines = File.Exists(Path) ? File.ReadAllLines(Path).ToList() : new List<string>();
            int i = lines.FindIndex(l => l.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            if (i >= 0) lines[i] = key + "=" + value;
            else lines.Add(key + "=" + value);
            File.WriteAllText(Path, string.Join("\n", lines) + "\n");
        }
        catch (Exception ex) { Log.Write("could not write " + FileName + ": " + ex.Message); }
    }
}

// The UI scale rule, kept free of XNA types so the resolution code (and tests) can use it without loading XNA.
public static class ScaleRules
{
    const double TallestBuiltIn = 2160;

    // Where the interface goes on a screen: scale k, and its area (left, top, width, height) in screen pixels.
    public struct Area { public float K; public int Left, Top, Width, Height; }

    public static Area Layout(int width, int height)
    {
        if (!FixSettings.UiScaleFix) return new Area { K = 1f, Width = width, Height = height };     // the game's own interface
        int ix = Inset(FixSettings.UiInsetX, width), iy = Inset(FixSettings.UiInsetY, height);
        // Never squeeze the interface below a quarter of the screen's width or height.
        ix = Math.Max(0, Math.Min(ix, width * 3 / 4)); iy = Math.Max(0, Math.Min(iy, height * 3 / 4));
        return new Area { K = Ui(width, height), Left = ix / 2, Top = iy / 2, Width = width - ix, Height = height - iy };
    }

    // "0.1" or "10%" -> that share of the length; "256" or "256px" -> pixels. The total, split between both sides.
    static int Inset(string text, int length)
    {
        text = (text ?? "").Trim().ToLowerInvariant();
        if (text.Length == 0) return 0;
        double v;
        var inv = System.Globalization.CultureInfo.InvariantCulture;
        if (text.EndsWith("%")) return double.TryParse(text.TrimEnd('%').Trim(), System.Globalization.NumberStyles.Float, inv, out v) ? (int)Math.Round(length * v / 100) : 0;
        if (text.EndsWith("px")) text = text.Substring(0, text.Length - 2).Trim();
        else if (text.Contains(".") || text.Contains(","))
            return double.TryParse(text.Replace(',', '.'), System.Globalization.NumberStyles.Float, inv, out v) ? (int)Math.Round(length * v) : 0;
        int px;
        return int.TryParse(text, out px) ? px : 0;
    }

    // A set percentage, or UiScale=auto -> how many whole 2160p screens fit in the screen's shorter side (1 up to
    // 4319, 2 from 4320, 3 from 6480 ...). Whole multiples keep the pixel-art interface crisp, so auto never picks
    // an in-between scale; the shorter side makes portrait screens work like landscape ones.
    public static float Ui(int width, int height)
    {
        if (!FixSettings.UiScaleFix) return 1f;
        int? percent = FixSettings.UiScale;
        if (percent.HasValue) return Math.Max(0.5f, Math.Min(8f, percent.Value / 100f));
        return (float)Math.Max(1.0, Math.Floor(Math.Min(width, height) / TallestBuiltIn));
    }
}

public static class Log
{
    static readonly object Gate = new object();
    static bool started;

    // Only with Log=1 in the ini.
    public static void Write(string line)
    {
        if (FixSettings.LogEnabled) Always(line);
    }

    // Written whatever Log says: for messages the player must be able to find (the game closes after them).
    public static void Always(string line)
    {
        lock (Gate)
        {
            try
            {
                string path = System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UnderrailFix.log");
                if (!started) { File.WriteAllText(path, ""); started = true; }
                File.AppendAllText(path, DateTime.Now.ToString("HH:mm:ss ") + line + Environment.NewLine);
            }
            catch { }
        }
    }
}
