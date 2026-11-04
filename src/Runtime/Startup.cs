// Called first thing in the game's Main, before the window exists.
// Run(string) is the one hook every version of the patch calls, so its signature must never change.
using System;
using System.Runtime.InteropServices;

public static class FixStartup
{
    // patched = the FixVersion the patcher installed with this exe.
    public static void Run(string patched)
    {
        Log.Write("UnderRailWorks " + FixVersion.Value + " (.NET " + Environment.Version + ")");
        if (patched != FixVersion.Value)
        {
            // Another version's hooks may not exist in this DLL; stop with a clear message instead of a crash later.
            string message = "UnderRailWorks.dll is version " + FixVersion.Value + ", but the game was patched by version " + patched
                + ".\n\nRun the UnderRailWorks patcher again to update the patch.";
            Log.Always(message.Replace("\n\n", " "));
            try { MessageBox(IntPtr.Zero, message, "UnderRailWorks", 0x10); } catch { }
            Environment.Exit(1);
        }
        try { FnaMode.Startup(); }
        catch (Exception ex) { Log.Write("startup (FNA mode): " + ex.Message); }
        try
        {
            if (FixSettings.DpiAware) DpiAware();
        }
        catch (Exception ex) { Log.Write("startup: " + ex.Message); }
        CoreCompat.Startup();
        try { ModLoader.Init(); }
        catch (Exception ex) { Log.Write("mods: " + ex); }
    }

    // XNA games aren't marked DPI-aware, so with display scaling Windows stretches the window and reports a smaller
    // screen. Per-monitor v2 awareness (Windows 10 1703+), falling back to the older calls. Not needed under Wine,
    // which doesn't virtualise DPI for games, so it's skipped there.
    static void DpiAware()
    {
        if (Environment.OSVersion.Platform != PlatformID.Win32NT || IsWine()) return;
        string how;
        try { how = SetProcessDpiAwarenessContext(new IntPtr(-4)) ? "per-monitor v2" : null; } catch (EntryPointNotFoundException) { how = null; }
        if (how == null) try { how = SetProcessDpiAwareness(2) == 0 ? "per-monitor" : null; } catch (DllNotFoundException) { } catch (EntryPointNotFoundException) { }
        if (how == null) how = SetProcessDPIAware() ? "system" : "unchanged (already set?)";
        Log.Write("DPI awareness: " + how);
    }

    static bool IsWine()
    {
        IntPtr ntdll = GetModuleHandle("ntdll.dll");
        return ntdll != IntPtr.Zero && GetProcAddress(ntdll, "wine_get_version") != IntPtr.Zero;
    }

    [DllImport("user32.dll", CharSet = CharSet.Unicode)] static extern int MessageBox(IntPtr owner, string text, string caption, uint type);
    [DllImport("user32.dll")] static extern bool SetProcessDpiAwarenessContext(IntPtr value);
    [DllImport("shcore.dll")] static extern int SetProcessDpiAwareness(int value);
    [DllImport("user32.dll")] static extern bool SetProcessDPIAware();
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] static extern IntPtr GetModuleHandle(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);
}
