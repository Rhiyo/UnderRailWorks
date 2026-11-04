// UnderrailSteamHost.exe: a 32-bit helper for a 64-bit UnderRail (experimental-64bit). It loads the game's own
// 32-bit SteamHelper.dll (and through it steam_api.dll) and runs the game's Steam calls for it: one command per line
// on stdin ("SetAchievement NAME"), one result per line on stdout ("1"/"0", a number, or "" for void). It exits
// when stdin closes, i.e. when the game does.
// GOG: the "Galaxy..." commands run GOG Galaxy (the game's GalaxyCSharp.dll and its 32-bit natives) the way the game's
// own Galaxy manager (axn) does, on one thread that also calls ProcessData every 20 ms (GalaxyRunner). GalaxyInit
// gets the game's client ID and secret from the game (GalaxyBridge reads them from the game's code).
using System;
using System.Runtime.InteropServices;

static class SteamHost
{
    static int Main()
    {
        string line;
        while ((line = Console.In.ReadLine()) != null)
        {
            string[] p = line.Split(new[] { ' ' }, 2);
            string arg = p.Length > 1 ? p[1] : "";
            string reply;
            try
            {
                switch (p[0])
                {
                    case "InitSteam": reply = B(SteamHelper_InitSteam()); break;
                    case "ShutdownSteam": SteamHelper_ShutdownSteam(); reply = ""; break;
                    case "SetAchievement": reply = B(SteamHelper_SetAchievement(arg)); break;
                    case "ClearAchievement": reply = B(SteamHelper_ClearAchievement(arg)); break;
                    case "IncrementStat": reply = SteamHelper_IncrementStat(arg).ToString(); break;
                    case "ResetAllStats": reply = B(SteamHelper_ResetAllStats(arg == "1")); break;
                    case "IsOnSteamDeck": reply = B(SteamHelper_IsOnSteamDeck()); break;
                    case "IsRunningOnWine": reply = B(SteamHelper_IsRunningOnWine()); break;
                    case "GalaxyInit": case "GalaxyShutdown": case "GalaxyReset": case "GalaxySetAchievement":
                    case "GalaxyClearAchievement": case "GalaxyIncrementStat":
                        reply = GalaxyRunner.Run(p[0], arg); break;
                    case "ShowKeyboard":
                        string[] n = arg.Split(' ');
                        reply = B(SteamHelper_ShowKeyboard(int.Parse(n[0]), int.Parse(n[1]), int.Parse(n[2]), int.Parse(n[3])));
                        break;
                    default: reply = "?"; break;
                }
            }
            catch (Exception ex) { reply = "error " + ex.GetType().Name; }
            Console.Out.WriteLine(reply);
            Console.Out.Flush();
        }
        GalaxyRunner.Run("GalaxyShutdown", "");
        return 0;
    }

    static string B(bool v) { return v ? "1" : "0"; }

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

// GOG Galaxy on its own thread: commands are queued to it, and between them it calls ProcessData. Galaxy types are only
// touched on that thread and only after GalaxyInit, so the Steam version never loads GalaxyCSharp.dll.
static class GalaxyRunner
{
    static System.Threading.Thread thread;
    static readonly System.Collections.Generic.Queue<Action> work = new System.Collections.Generic.Queue<Action>();
    internal static bool initialised, signedIn, statsReady;

    public static string Run(string command, string arg)
    {
        if (thread == null)
        {
            if (command == "GalaxyShutdown") return "";
            thread = new System.Threading.Thread(Loop) { IsBackground = true, Name = "Galaxy" };
            thread.Start();
        }
        string reply = null;
        var done = new System.Threading.ManualResetEvent(false);
        lock (work) work.Enqueue(() => { try { reply = GalaxyCalls.Do(command, arg); } catch (Exception ex) { reply = "error " + ex.GetType().Name; } done.Set(); });
        return done.WaitOne(10000) ? reply : "error timeout";
    }

    static void Loop()
    {
        while (true)
        {
            Action next = null;
            lock (work) if (work.Count > 0) next = work.Dequeue();
            if (next != null) { next(); continue; }
            if (initialised) GalaxyCalls.Process();
            System.Threading.Thread.Sleep(20);
        }
    }
}

// The game's axn, step for step: its listeners, the checks before changing anything, Store after each change.
static class GalaxyCalls
{
    static AuthListener auth;
    static StatsListener stats;

    public static void Process() { try { Galaxy.Api.GalaxyInstance.ProcessData(); } catch { } }

    public static string Do(string command, string arg)
    {
        switch (command)
        {
            case "GalaxyInit":     // arg: the game's client ID and secret
                string[] ids = arg.Split(' ');
                if (ids.Length != 2) return "0";
                Galaxy.Api.GalaxyInstance.Init(new Galaxy.Api.InitParams(ids[0], ids[1]));
                if (auth == null) auth = new AuthListener();
                if (stats == null) stats = new StatsListener();
                Galaxy.Api.GalaxyInstance.User().SignInGalaxy();
                GalaxyRunner.initialised = true;
                return "1";
            case "GalaxyShutdown":
                if (GalaxyRunner.initialised) { GalaxyRunner.initialised = false; Galaxy.Api.GalaxyInstance.Shutdown(true); }
                return "";
        }
        if (!(GalaxyRunner.initialised && GalaxyRunner.signedIn && GalaxyRunner.statsReady)) return "0";
        var s = Galaxy.Api.GalaxyInstance.Stats();
        switch (command)
        {
            case "GalaxyReset": s.ResetStatsAndAchievements(); s.StoreStatsAndAchievements(); return "1";
            case "GalaxySetAchievement": s.SetAchievement(arg); s.StoreStatsAndAchievements(); return "1";
            case "GalaxyClearAchievement": s.ClearAchievement(arg); s.StoreStatsAndAchievements(); return "1";
            case "GalaxyIncrementStat":
                int value = s.GetStatInt(arg) + 1;
                s.SetStatInt(arg, value);
                s.StoreStatsAndAchievements();
                return value.ToString();
        }
        return "?";
    }

    sealed class AuthListener : Galaxy.Api.GlobalAuthListener
    {
        public override void OnAuthSuccess() { GalaxyRunner.signedIn = true; Galaxy.Api.GalaxyInstance.Stats().RequestUserStatsAndAchievements(); }
        public override void OnAuthFailure(Galaxy.Api.IAuthListener.FailureReason reason) { GalaxyRunner.signedIn = false; }
        public override void OnAuthLost() { GalaxyRunner.signedIn = false; }
    }

    sealed class StatsListener : Galaxy.Api.GlobalUserStatsAndAchievementsRetrieveListener
    {
        public override void OnUserStatsAndAchievementsRetrieveSuccess(Galaxy.Api.GalaxyID user) { GalaxyRunner.statsReady = true; }
        public override void OnUserStatsAndAchievementsRetrieveFailure(Galaxy.Api.GalaxyID user, Galaxy.Api.IUserStatsAndAchievementsRetrieveListener.FailureReason reason) { }
    }
}
