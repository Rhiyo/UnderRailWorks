// GOG Galaxy for the 64-bit process (experimental-64bit patch). The game's Galaxy manager axn (created in enu when
// Galaxy.dll is next to the exe, i.e. the GOG version) uses GalaxyCSharp.dll, whose native Galaxy.dll and
// GalaxyCSharpGlue.dll are 32-bit only. The patch creates this class instead; it does what axn does through the
// 32-bit helper (UnderrailSteamHost.exe), which runs Galaxy itself, including ProcessData on its own thread, so
// nothing here waits on the helper per frame. Same rules as axn: changes only once signed in and the stats are
// retrieved (the helper answers 0 otherwise), and init is retried every 5 minutes after a failure.
// The game's Galaxy client ID and secret are read from axn.sv's code at run time (the first two strings it loads),
// so they're never copied into this project, and never written to the log.
using System;
using System.Collections.Generic;
using System.Reflection;

public sealed class GalaxyBridge : acv
{
    bool started;
    DateTime tried;

    public bool su() { return started; }

    public bool sv()
    {
        tried = DateTime.Now;
        var credentials = GameStrings(typeof(axn).GetMethod("sv"), 2);
        if (credentials.Count < 2) { Log.Write("galaxy: the game's Galaxy credentials weren't found"); return false; }
        started = SteamBridge.Call("GalaxyInit " + credentials[0] + " " + credentials[1], "GalaxyInit") == "1";
        return started;
    }

    // Every frame (enu): only the retry; the helper keeps Galaxy's ProcessData going.
    public void sw()
    {
        if (!started && DateTime.Now > tried + TimeSpan.FromMinutes(5.0)) sv();
    }

    public void sx() { if (started) SteamBridge.Call("GalaxyShutdown"); }

    public bool sy(bool achievements) { return started && SteamBridge.Call("GalaxyReset") == "1"; }

    public bool sz(string achievement) { return started && SteamBridge.Call("GalaxySetAchievement " + achievement) == "1"; }

    public bool s0(string achievement) { return started && SteamBridge.Call("GalaxyClearAchievement " + achievement) == "1"; }

    // As axn: increment the stat, then unlock the achievement tied to it once it reaches the threshold (eil's table).
    public bool s1(string stat)
    {
        int value;
        if (!started || !int.TryParse(SteamBridge.Call("GalaxyIncrementStat " + stat), out value) || value < 0) return false;
        var stats = bpo.GetService<eil>();
        dh2 rule;
        if (stats != null && stats.aq4() != null && stats.aq4().TryGetValue(stat, out rule) && rule != null && value >= rule.a()) sz(rule.b());
        return true;
    }

    // The first strings a method loads (ldstr, opcode 0x72 + string token), in order.
    static List<string> GameStrings(MethodInfo method, int count)
    {
        var found = new List<string>();
        try
        {
            byte[] il = method.GetMethodBody().GetILAsByteArray();
            for (int i = 0; i + 4 < il.Length && found.Count < count; i++)
                if (il[i] == 0x72 && il[i + 4] == 0x70)     // ldstr; user-string tokens are 0x70xxxxxx
                {
                    found.Add(method.Module.ResolveString(BitConverter.ToInt32(il, i + 1)));
                    i += 4;
                }
        }
        catch (Exception ex) { Log.Write("galaxy: reading the game's Galaxy setup failed: " + ex.Message); }
        return found;
    }
}
