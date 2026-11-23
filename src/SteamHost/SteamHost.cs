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
                    case "InitSteam": reply = B(OnlineHost.SteamStarted = SteamHelper_InitSteam()); break;
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
                    case "NetInfo": case "NetHost": case "NetJoin": case "NetLeave": case "NetSend": case "NetPoll":
                    case "NetMembers": case "NetFriends": case "NetInvite":
                        reply = OnlineHost.Do(p[0], arg); break;
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

    public static void Process()
    {
        try { Galaxy.Api.GalaxyInstance.ProcessData(); } catch { }
        try { GalaxyNet.Pump(); } catch (Exception ex) { OnlineHost.Event("failed", "pump", OnlineHost.B64(ex.GetType().Name + ": " + ex.Message)); }
    }

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
        if (command.StartsWith("Net")) return GalaxyRunner.initialised && GalaxyRunner.signedIn ? GalaxyNet.Do(command, arg) : (command == "NetInfo" ? "none" : "0");
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

// GOG Galaxy's lobbies and P2P (on GalaxyRunner's thread): the same commands and events as Steam's (OnlineHost.cs).
// The host's lobby id is in its rich presence ("connect": "+connect_lobby <id>"), which is how friends find it and
// what Galaxy's own "join" passes (OnGameJoinRequested).
static class GalaxyNet
{
    static Galaxy.Api.GalaxyID lobby;
    static bool listening;
    static readonly System.Collections.Generic.Queue<System.Collections.Generic.KeyValuePair<ulong, byte[]>> outgoing = new System.Collections.Generic.Queue<System.Collections.Generic.KeyValuePair<ulong, byte[]>>();
    static Created created; static Entered entered; static MemberState members; static JoinRequested joinRequested; static FriendList friendList;

    static Galaxy.Api.IMatchmaking Mm { get { return Galaxy.Api.GalaxyInstance.Matchmaking(); } }
    static Galaxy.Api.GalaxyID Me { get { return Galaxy.Api.GalaxyInstance.User().GetGalaxyID(); } }

    static void Listen()
    {
        if (listening) return;
        listening = true;
        created = new Created(); entered = new Entered(); members = new MemberState(); joinRequested = new JoinRequested(); friendList = new FriendList();
        Galaxy.Api.GalaxyInstance.Friends().RequestFriendList();
    }

    public static string Do(string command, string arg)
    {
        Listen();
        switch (command)
        {
            case "NetInfo": return "gog " + Me.ToUint64() + " " + OnlineHost.B64(Galaxy.Api.GalaxyInstance.Friends().GetPersonaName());
            case "NetHost":
            {
                uint max; if (!uint.TryParse(arg, out max)) max = 4;
                Leave();
                Mm.CreateLobby(Galaxy.Api.LobbyType.LOBBY_TYPE_FRIENDS_ONLY, max, true, Galaxy.Api.LobbyTopologyType.LOBBY_TOPOLOGY_TYPE_STAR);
                return "1";
            }
            case "NetJoin":
            {
                ulong id; if (!ulong.TryParse(arg, out id)) return "0";
                Leave();
                Mm.JoinLobby(new Galaxy.Api.GalaxyID(id));
                return "1";
            }
            case "NetLeave": Leave(); return "";
            case "NetSend":
            {
                var p = arg.Split(' '); ulong to;
                if (p.Length != 2 || !ulong.TryParse(p[0], out to)) return "0";
                outgoing.Enqueue(new System.Collections.Generic.KeyValuePair<ulong, byte[]>(to, Convert.FromBase64String(p[1])));
                return "1";
            }
            case "NetMembers":
            {
                var list = new System.Collections.Generic.List<string>();
                if (lobby != null)
                    for (uint i = 0, n = Mm.GetNumLobbyMembers(lobby); i < n; i++)
                    { var m = Mm.GetLobbyMemberByIndex(lobby, i); list.Add(m.ToUint64() + "," + OnlineHost.B64(NameOf(m))); }
                return string.Join(" ", list.ToArray());
            }
            case "NetFriends":
            {
                var f = Galaxy.Api.GalaxyInstance.Friends();
                var list = new System.Collections.Generic.List<string>();
                for (uint i = 0, n = f.GetFriendCount(); i < n; i++)
                {
                    var friend = f.GetFriendByIndex(i);
                    f.RequestRichPresence(friend);     // answered later: the list shows it from the next time
                    string connect = null;
                    try { connect = f.GetRichPresence("connect", friend); } catch { }
                    ulong l;
                    var parts = (connect ?? "").Split(' ');
                    if (parts.Length == 2 && parts[0] == "+connect_lobby" && ulong.TryParse(parts[1], out l)) list.Add(friend.ToUint64() + "," + OnlineHost.B64(f.GetFriendPersonaName(friend)) + "," + l);
                }
                return string.Join(" ", list.ToArray());
            }
            case "NetInvite":
                if (lobby == null) return "0";
                Galaxy.Api.GalaxyInstance.Friends().ShowOverlayInviteDialog("+connect_lobby " + lobby.ToUint64());
                return "1";
        }
        return "?";
    }

    static string NameOf(Galaxy.Api.GalaxyID id)
    {
        var f = Galaxy.Api.GalaxyInstance.Friends();
        if (id.ToUint64() == Me.ToUint64()) return f.GetPersonaName();
        try { return f.GetFriendPersonaName(id); } catch { return id.ToUint64().ToString(); }
    }

    static void Leave()
    {
        if (lobby == null) return;
        try { Mm.LeaveLobby(lobby); } catch { }
        try { Galaxy.Api.GalaxyInstance.Friends().DeleteRichPresence("connect"); } catch { }
        lobby = null;
    }

    public static void Pump()
    {
        if (!listening) return;
        var net = Galaxy.Api.GalaxyInstance.Networking();
        uint size = 0;
        var buffer = new byte[1 << 20];
        while (net.IsP2PPacketAvailable(ref size))
        {
            if (size > buffer.Length) buffer = new byte[size];
            uint got = 0; var from = new Galaxy.Api.GalaxyID();
            if (!net.ReadP2PPacket(buffer, (uint)buffer.Length, ref got, ref from)) break;
            OnlineHost.Event("data", from.ToUint64(), Convert.ToBase64String(buffer, 0, (int)got));
        }
        while (outgoing.Count > 0)
        {
            var o = outgoing.Peek();
            if (!net.SendP2PPacket(new Galaxy.Api.GalaxyID(o.Key), o.Value, (uint)o.Value.Length, Galaxy.Api.P2PSendType.P2P_SEND_RELIABLE)) break;
            outgoing.Dequeue();
        }
    }

    sealed class Created : Galaxy.Api.GlobalLobbyCreatedListener
    {
        public override void OnLobbyCreated(Galaxy.Api.GalaxyID id, Galaxy.Api.LobbyCreateResult result)
        {
            if (result != Galaxy.Api.LobbyCreateResult.LOBBY_CREATE_RESULT_SUCCESS) { OnlineHost.Event("failed", "host", OnlineHost.B64("GOG couldn't make the lobby")); return; }
            lobby = id;
            Mm.SetLobbyData(id, "game", "underrailworks");
            Galaxy.Api.GalaxyInstance.Friends().SetRichPresence("connect", "+connect_lobby " + id.ToUint64());
            OnlineHost.Event("hosted", id.ToUint64());
        }
    }
    sealed class Entered : Galaxy.Api.GlobalLobbyEnteredListener
    {
        public override void OnLobbyEntered(Galaxy.Api.GalaxyID id, Galaxy.Api.LobbyEnterResult result)
        {
            if (lobby != null && id.ToUint64() == lobby.ToUint64()) return;     // our own lobby, just made
            if (result != Galaxy.Api.LobbyEnterResult.LOBBY_ENTER_RESULT_SUCCESS)
            { OnlineHost.Event("failed", "join", OnlineHost.B64(result == Galaxy.Api.LobbyEnterResult.LOBBY_ENTER_RESULT_LOBBY_DOES_NOT_EXIST ? "that game no longer exists" : result == Galaxy.Api.LobbyEnterResult.LOBBY_ENTER_RESULT_LOBBY_IS_FULL ? "that game is full" : "GOG couldn't enter the lobby")); return; }
            lobby = id;
            OnlineHost.Event("entered", id.ToUint64(), Mm.GetLobbyOwner(id).ToUint64());
        }
    }
    sealed class MemberState : Galaxy.Api.GlobalLobbyMemberStateListener
    {
        public override void OnLobbyMemberStateChanged(Galaxy.Api.GalaxyID id, Galaxy.Api.GalaxyID member, Galaxy.Api.LobbyMemberStateChange change)
        {
            if (lobby == null || id.ToUint64() != lobby.ToUint64()) return;
            if (change == Galaxy.Api.LobbyMemberStateChange.LOBBY_MEMBER_STATE_CHANGED_ENTERED) OnlineHost.Event("joined", member.ToUint64(), OnlineHost.B64(NameOf(member)));
            else OnlineHost.Event("left", member.ToUint64());
        }
    }
    sealed class JoinRequested : Galaxy.Api.GlobalGameJoinRequestedListener
    {
        public override void OnGameJoinRequested(Galaxy.Api.GalaxyID user, string connection)
        {
            var parts = (connection ?? "").Split(' '); ulong l;
            if (parts.Length == 2 && parts[0] == "+connect_lobby" && ulong.TryParse(parts[1], out l)) OnlineHost.Event("invite", l);
        }
    }
    sealed class FriendList : Galaxy.Api.GlobalFriendListListener
    {
        public override void OnFriendListRetrieveSuccess() { }
        public override void OnFriendListRetrieveFailure(Galaxy.Api.IFriendListListener.FailureReason reason) { }
    }
}
