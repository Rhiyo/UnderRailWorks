// UnderRailWorks: a mod's own settings, in Mods\<mod>\settings.ini (readable text, like UnderRailWorks.ini).
// The mod passes its defaults as ini text (with comments) on first use; the file is written from them when missing,
// and later edits keep the file's lines and comments.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class ModSettings
{
    static readonly Dictionary<string, ModSettings> All = new Dictionary<string, ModSettings>(StringComparer.OrdinalIgnoreCase);

    public readonly string Mod, Path;
    readonly Dictionary<string, string> values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // Bumped on every change, so code reading settings per frame can keep its own copy.
    public int Changes { get; private set; }

    ModSettings(string mod, string defaults)
    {
        Mod = mod;
        Path = System.IO.Path.Combine(System.IO.Path.Combine(ModLoader.ModsFolder, mod), "settings.ini");
        try
        {
            if (!File.Exists(Path) && defaults != null) File.WriteAllText(Path, defaults.Replace("\r\n", "\n"));
            if (File.Exists(Path)) Parse(File.ReadAllLines(Path));
        }
        catch (Exception ex) { Log.Write("mods: " + mod + ": settings: " + ex.Message); }
    }

    // The mod's settings (made on first use).
    internal static ModSettings For(string mod, string defaults = null)
    {
        ModSettings s;
        if (!All.TryGetValue(mod, out s)) All[mod] = s = new ModSettings(mod, defaults);
        return s;
    }

    // Reads the file again (works.reload: the player edited it). True when it was read.
    internal bool ReadAgain()
    {
        try
        {
            if (!File.Exists(Path)) return false;
            var lines = File.ReadAllLines(Path);
            values.Clear();
            Parse(lines);
            Changes++;
            return true;
        }
        catch (Exception ex) { Log.Write("mods: " + Mod + ": settings: " + ex.Message); return false; }
    }
    internal static IEnumerable<ModSettings> Every { get { return All.Values.ToList(); } }

    void Parse(IEnumerable<string> lines)
    {
        foreach (string raw in lines)
        {
            string line = raw.Trim();
            int eq = line.IndexOf('=');
            if (line.Length == 0 || line[0] == ';' || line[0] == '#' || line[0] == '[' || eq < 0) continue;
            values[line.Substring(0, eq).Trim()] = line.Substring(eq + 1).Trim();
        }
    }

    public string Get(string key) { string v; return values.TryGetValue(key, out v) ? v : null; }
    // Every key in the file (e.g. to clear a mod's own saved entries).
    public IList<string> Keys { get { return values.Keys.ToList(); } }

    public bool Bool(string key, bool fallback)
    {
        string v = (Get(key) ?? "").Trim().ToLowerInvariant();
        if (v == "1" || v == "on" || v == "true") return true;
        if (v == "0" || v == "off" || v == "false") return false;
        return fallback;
    }

    public int Int(string key, int fallback, int min, int max)
    {
        int n;
        return int.TryParse((Get(key) ?? "").TrimEnd('%', ' '), out n) ? Math.Max(min, Math.Min(max, n)) : fallback;
    }

    // Changes the value now and in the file.
    public void Set(string key, string value)
    {
        if (string.Equals(Get(key), value, StringComparison.OrdinalIgnoreCase)) return;
        values[key] = value;
        Changes++;
        try
        {
            var lines = File.Exists(Path) ? File.ReadAllLines(Path).ToList() : new List<string>();
            int i = lines.FindIndex(l => l.TrimStart().StartsWith(key + "=", StringComparison.OrdinalIgnoreCase));
            if (i >= 0) lines[i] = key + "=" + value;
            else lines.Add(key + "=" + value);
            string temp = Path + ".tmp";
            File.WriteAllText(temp, string.Join("\n", lines) + "\n");
            if (File.Exists(Path)) File.Delete(Path);
            File.Move(temp, Path);
        }
        catch (Exception ex) { Log.Write("mods: " + Mod + ": could not write settings: " + ex.Message); }
    }
}
}
