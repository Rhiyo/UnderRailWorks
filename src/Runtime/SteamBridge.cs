// The game's Steam calls (efl's SteamHelper_* P/Invokes into Styg's 32-bit SteamHelper.dll), redirected here by the
// experimental-64bit patch. 32-bit process: straight to SteamHelper.dll, as before. 64-bit: SteamHelper.dll and
// steam_api.dll are 32-bit and can't load, and Valve's steam_api64.dll may not be redistributed, so the calls go to
// UnderrailSteamHost.exe, a 32-bit helper process that loads the game's own SteamHelper.dll, over its stdin/stdout:
// one command per line ("Name arg..."), one result per line.
using System;
using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using UnderRailWorks;

namespace UnderRailWorks.Internal
{

public static class SteamBridge
{
    static bool Native => IntPtr.Size == 4;
    static Process host;
    static StreamWriter input;
    static StreamReader output;
    static readonly object Gate = new object();
    static bool shutDown;                        // after ShutdownSteam (the game exiting): no new helper
    static bool? onWine, onDeck;                 // fixed for the session; asked repeatedly

    internal static string Call(string command) { return Call(command, command); }

    // Also used by GalaxyBridge (GOG Galaxy goes through the same helper). shown: what the log records instead of the
    // command (GalaxyInit carries the game's GOG credentials).
    internal static string Call(string command, string shown)
    {
        lock (Gate)
        {
            if (shutDown) return null;
            try
            {
                if (host == null || host.HasExited)
                {
                    var start = new ProcessStartInfo(Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UnderrailSteamHost.exe"))
                    {
                        UseShellExecute = false, RedirectStandardInput = true, RedirectStandardOutput = true, CreateNoWindow = true,
                        WorkingDirectory = AppDomain.CurrentDomain.BaseDirectory,
                    };
                    host = Process.Start(start);
                    input = host.StandardInput; input.AutoFlush = true;
                    output = host.StandardOutput;
                    Log.Write("helper: started process " + host.Id);
                }
                input.WriteLine(command);
                string reply = output.ReadLine();
                Log.Write("helper: " + shown + " -> " + reply);
                return reply;
            }
            catch (Exception ex) { Log.Write("helper: " + shown + " failed: " + ex.Message); return null; }
        }
    }

    static bool Bool(string command) { return Call(command) == "1"; }

    public static bool InitSteam() { return Native ? SteamHelper_InitSteam() : Bool("InitSteam"); }
    public static void ShutdownSteam()
    {
        if (Native) { SteamHelper_ShutdownSteam(); return; }
        Call("ShutdownSteam");
        lock (Gate) { shutDown = true; try { input?.Close(); host?.WaitForExit(2000); } catch { } host = null; }
    }
    public static bool SetAchievement(string name) { return Native ? SteamHelper_SetAchievement(name) : Bool("SetAchievement " + name); }
    public static bool ClearAchievement(string name) { return Native ? SteamHelper_ClearAchievement(name) : Bool("ClearAchievement " + name); }
    public static int IncrementStat(string name)
    {
        if (Native) return SteamHelper_IncrementStat(name);
        int v; return int.TryParse(Call("IncrementStat " + name), out v) ? v : 0;
    }
    public static bool ResetAllStats(bool achievements) { return Native ? SteamHelper_ResetAllStats(achievements) : Bool("ResetAllStats " + (achievements ? 1 : 0)); }
    // The game's SteamHelper.dll doesn't have these two (EntryPointNotFound, which the game treats as false); the helper
    // answers "error ...", i.e. false here too.
    public static bool IsOnSteamDeck() { return Native ? SteamHelper_IsOnSteamDeck() : (onDeck ?? (onDeck = Bool("IsOnSteamDeck")).Value); }
    public static bool IsRunningOnWine() { return Native ? SteamHelper_IsRunningOnWine() : (onWine ?? (onWine = Bool("IsRunningOnWine")).Value); }
    public static bool ShowKeyboard(int a, int b, int c, int d) { return Native ? SteamHelper_ShowKeyboard(a, b, c, d) : Bool("ShowKeyboard " + a + " " + b + " " + c + " " + d); }

    // The same declarations as the game's (efl).
    [DllImport("SteamHelper.dll", CallingConvention = CallingConvention.Cdecl)] static extern bool SteamHelper_InitSteam();
    [DllImport("SteamHelper.dll", CallingConvention = CallingConvention.Cdecl)] static extern void SteamHelper_ShutdownSteam();
    [DllImport("SteamHelper.dll", CallingConvention = CallingConvention.Cdecl)] static extern bool SteamHelper_SetAchievement(string name);
    [DllImport("SteamHelper.dll", CallingConvention = CallingConvention.Cdecl)] static extern bool SteamHelper_ClearAchievement(string name);
    [DllImport("SteamHelper.dll", CallingConvention = CallingConvention.Cdecl)] static extern int SteamHelper_IncrementStat(string name);
    [DllImport("SteamHelper.dll", CallingConvention = CallingConvention.Cdecl)] static extern bool SteamHelper_ResetAllStats(bool achievements);
    [DllImport("SteamHelper.dll", CallingConvention = CallingConvention.Cdecl)] static extern bool SteamHelper_IsOnSteamDeck();
    [DllImport("SteamHelper.dll", CallingConvention = CallingConvention.Cdecl)] static extern bool SteamHelper_IsRunningOnWine();
    [DllImport("SteamHelper.dll", CallingConvention = CallingConvention.Cdecl)] static extern bool SteamHelper_ShowKeyboard(int a, int b, int c, int d);
}
}
