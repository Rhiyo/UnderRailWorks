// UnderrailSteamHost.exe: playing together through the store the game came from (Works' Online, used by Co-op).
// Steam: the game's own steam_api.dll is from 2014 and has no flat API, so its interfaces are called through their
// vtables, by the versions it was built with (SteamMatchMaking009, SteamNetworking005, SteamFriends014, SteamUser017,
// SteamUtils006; a version string fixes its layout). Lobbies (friends-only), P2P packets relayed by Steam, friends in
// a lobby of this game. Callbacks are read from the Steam client's queue (steamclient's Steam_BGetCallback, as
// steam_api's own RunCallbacks does) on a pump thread; call results (lobby created / entered) are polled through
// ISteamUtils.
// GOG: the same through Galaxy (GalaxyCSharp.dll, on GalaxyRunner's thread): lobbies, P2P, friends' rich presence.
// Commands (one line each, as the rest of the helper):
//   NetInfo                 -> "steam <my id> <name b64>" | "gog <id> <name b64>" | "none"
//   NetHost <max members>   -> "1"/"0"; then the event "hosted <lobby>" or "failed host"
//   NetJoin <lobby>         -> "1"/"0"; then "entered <lobby> <owner>" or "failed join <why>"
//   NetLeave                -> ""
//   NetSend <to> <data b64> -> "1"/"0" (reliable, in order; queued while the store's buffer is full)
//   NetPoll                 -> events, space separated, each "kind,arg,arg...": hosted,<lobby> entered,<lobby>,<owner>
//                              failed,<what>,<why> joined,<id> left,<id> data,<from>,<b64> invite,<lobby>
//   NetMembers              -> "<id>,<name b64> ..." (the lobby's members)
//   NetFriends              -> "<id>,<name b64>,<lobby> ..." (friends in a lobby of this game; GOG: their rich presence)
//   NetInvite               -> "1"/"0": the store's overlay invite dialog for the lobby
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

static class OnlineHost
{
    internal static bool SteamStarted;     // the game's InitSteam succeeded
    internal static readonly object Gate = new object();     // every Steam call
    static readonly List<string> events = new List<string>();

    internal static void Event(params object[] parts) { lock (events) events.Add(string.Join(",", parts.Select(p => Convert.ToString(p, System.Globalization.CultureInfo.InvariantCulture)).ToArray())); }
    // Steps, for finding what failed (UnderrailSteamHost.log next to the exe; only the networking writes it).
    internal static void Trace(string what)
    {
        try { System.IO.File.AppendAllText(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UnderrailSteamHost.log"), DateTime.Now.ToString("HH:mm:ss.fff") + " " + what + Environment.NewLine); } catch { }
    }
    internal static string B64(string s) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(s ?? "")); }

    public static string Do(string command, string arg)
    {
        if (command == "NetPoll") { lock (events) { var r = string.Join(" ", events.ToArray()); events.Clear(); return r; } }
        if (SteamStarted) return SteamNet.Do(command, arg);
        if (GalaxyRunner.signedIn) return GalaxyRunner.Run(command, arg);
        return command == "NetInfo" ? "none" : "0";
    }
}

static class SteamNet
{
    const int ItsMatchmaking = 500, ItsFriends = 300, ItsNetworking = 1200;
    static IntPtr matchmaking, networking, friends, user, utils;
    static Thread pump;
    static ulong lobby, creating, joining;     // the lobby we're in; pending CreateLobby / JoinLobby calls
    static uint appId;
    static readonly Queue<KeyValuePair<ulong, byte[]>> outgoing = new Queue<KeyValuePair<ulong, byte[]>>();
    static Func<int, IntPtr, IntPtr, bool> bGetCallback;     // (pipe, msg, call) -> got one
    static Action<int> freeLastCallback;
    static int pipe;

    // --- vtable calls (MSVC thiscall; a CSteamID returned through a hidden pointer) --------------------------------
    // Wine Mono can't call a thiscall function pointer through a delegate (the helper died on the first one), so each
    // method gets a stub: called as stdcall with "this" first, it moves "this" into ECX and jumps to the method, which
    // then pops the rest as stdcall would (thiscall is callee-cleaned too). Bytes: pop eax; pop ecx; push eax;
    // mov eax, target; jmp eax.
    static readonly Dictionary<IntPtr, IntPtr> stubs = new Dictionary<IntPtr, IntPtr>();
    static readonly Dictionary<KeyValuePair<IntPtr, Type>, Delegate> delegates = new Dictionary<KeyValuePair<IntPtr, Type>, Delegate>();
    [DllImport("kernel32.dll")] static extern IntPtr VirtualAlloc(IntPtr at, UIntPtr size, uint type, uint protect);
    static IntPtr Stub(IntPtr target)
    {
        IntPtr stub;
        if (stubs.TryGetValue(target, out stub)) return stub;
        stub = VirtualAlloc(IntPtr.Zero, (UIntPtr)16, 0x3000, 0x40);     // MEM_COMMIT | MEM_RESERVE, PAGE_EXECUTE_READWRITE
        var t = BitConverter.GetBytes(target.ToInt32());
        var code = new byte[] { 0x58, 0x59, 0x50, 0xB8, t[0], t[1], t[2], t[3], 0xFF, 0xE0 };
        Marshal.Copy(code, 0, stub, code.Length);
        stubs[target] = stub;
        return stub;
    }
    static T F<T>(IntPtr iface, int index) where T : class
    {
        IntPtr target = Marshal.ReadIntPtr(Marshal.ReadIntPtr(iface), index * IntPtr.Size);
        var key = new KeyValuePair<IntPtr, Type>(target, typeof(T));
        Delegate d;
        if (!delegates.TryGetValue(key, out d)) delegates[key] = d = Marshal.GetDelegateForFunctionPointer(Stub(target), typeof(T));
        return d as T;
    }
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate ulong CallIntInt(IntPtr self, int a, int b);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate ulong CallId(IntPtr self, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate void VoidId(IntPtr self, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int IntId(IntPtr self, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr IdOut(IntPtr self, out ulong ret);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr IdOutIdInt(IntPtr self, out ulong ret, ulong id, int i);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr IdOutIntInt(IntPtr self, out ulong ret, int i, int flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.I1)] delegate bool BoolIdStrStr(IntPtr self, ulong id, [MarshalAs(UnmanagedType.LPStr)] string k, [MarshalAs(UnmanagedType.LPStr)] string v);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.I1)] delegate bool BoolId(IntPtr self, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.I1)] delegate bool SendPacket(IntPtr self, ulong to, byte[] data, uint size, int type, int channel);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.I1)] delegate bool PacketAvailable(IntPtr self, out uint size, int channel);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.I1)] delegate bool ReadPacket(IntPtr self, byte[] dest, uint size, out uint got, out ulong from, int channel);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr Str(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr StrId(IntPtr self, ulong id);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate int IntInt(IntPtr self, int flags);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.I1)] delegate bool GamePlayed(IntPtr self, ulong id, byte[] info);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate uint UInt(IntPtr self);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.I1)] delegate bool CallDone(IntPtr self, ulong call, [MarshalAs(UnmanagedType.I1)] out bool failed);
    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.I1)] delegate bool CallResult(IntPtr self, ulong call, byte[] result, int size, int expected, [MarshalAs(UnmanagedType.I1)] out bool failed);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] [return: MarshalAs(UnmanagedType.I1)] delegate bool BGetCallback(int pipe, IntPtr msg, IntPtr call);
    [UnmanagedFunctionPointer(CallingConvention.Cdecl)] delegate void FreeLastCallback(int pipe);

    [DllImport("steam_api.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamMatchmaking();
    [DllImport("steam_api.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamNetworking();
    [DllImport("steam_api.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamFriends();
    [DllImport("steam_api.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamUser();
    [DllImport("steam_api.dll", CallingConvention = CallingConvention.Cdecl)] static extern IntPtr SteamUtils();
    [DllImport("steam_api.dll", CallingConvention = CallingConvention.Cdecl)] static extern int SteamAPI_GetHSteamPipe();
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetModuleHandleA(string name);
    [DllImport("kernel32.dll", CharSet = CharSet.Ansi)] static extern IntPtr GetProcAddress(IntPtr module, string name);

    // Vtable indexes (from the interface headers of those versions).
    const int MmCreateLobby = 13, MmJoinLobby = 14, MmLeaveLobby = 15, MmNumMembers = 17, MmMemberByIndex = 18,
        MmSetLobbyData = 20, MmSetJoinable = 34, MmLobbyOwner = 35;
    const int NetSendP2P = 0, NetAvailable = 1, NetRead = 2, NetAccept = 3, NetClose = 4, NetAllowRelay = 7;
    const int FrPersonaName = 0, FrFriendCount = 3, FrFriendByIndex = 4, FrFriendName = 7, FrGamePlayed = 8, FrInviteDialog = 27;
    const int UsSteamId = 2;
    const int UtAppId = 9, UtCallDone = 11, UtCallResult = 13;

    static bool Ready()
    {
        if (matchmaking != IntPtr.Zero) return true;
        OnlineHost.Trace("interfaces...");
        matchmaking = SteamMatchmaking(); networking = SteamNetworking(); friends = SteamFriends(); user = SteamUser(); utils = SteamUtils();
        OnlineHost.Trace("interfaces " + matchmaking + " " + networking + " " + friends + " " + user + " " + utils);
        if (matchmaking == IntPtr.Zero || networking == IntPtr.Zero || friends == IntPtr.Zero || user == IntPtr.Zero || utils == IntPtr.Zero) { matchmaking = IntPtr.Zero; return false; }
        appId = F<UInt>(utils, UtAppId)(utils);
        OnlineHost.Trace("app " + appId);
        AllowRelay();
        OnlineHost.Trace("relay allowed");
        // The Steam client's callback queue (steamclient.dll, loaded by steam_api).
        var client = GetModuleHandleA("steamclient.dll");
        IntPtr get = client == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(client, "Steam_BGetCallback");
        IntPtr free = client == IntPtr.Zero ? IntPtr.Zero : GetProcAddress(client, "Steam_FreeLastCallback");
        if (get != IntPtr.Zero && free != IntPtr.Zero)
        {
            var g = (BGetCallback)Marshal.GetDelegateForFunctionPointer(get, typeof(BGetCallback));
            var f = (FreeLastCallback)Marshal.GetDelegateForFunctionPointer(free, typeof(FreeLastCallback));
            bGetCallback = (p, m, c) => g(p, m, c);
            freeLastCallback = p => f(p);
            pipe = SteamAPI_GetHSteamPipe();
        }
        OnlineHost.Trace("callback queue " + (bGetCallback != null) + ", pipe " + pipe);
        pump = new Thread(Pump) { IsBackground = true, Name = "Steam networking" };
        pump.Start();
        return true;
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] [return: MarshalAs(UnmanagedType.I1)] delegate bool BoolBool(IntPtr self, [MarshalAs(UnmanagedType.I1)] bool b);
    static void AllowRelay() { F<BoolBool>(networking, NetAllowRelay)(networking, true); }

    static ulong MyId() { ulong id; F<IdOut>(user, UsSteamId)(user, out id); return id; }
    static string Utf8(IntPtr p)
    {
        if (p == IntPtr.Zero) return "";
        int n = 0; while (Marshal.ReadByte(p, n) != 0) n++;
        var b = new byte[n]; Marshal.Copy(p, b, 0, n);
        return Encoding.UTF8.GetString(b);
    }
    static string NameOf(ulong id) { return id == MyId() ? Utf8(F<Str>(friends, FrPersonaName)(friends)) : Utf8(F<StrId>(friends, FrFriendName)(friends, id)); }
    static List<ulong> Members()
    {
        var r = new List<ulong>();
        if (lobby == 0) return r;
        int n = F<IntId>(matchmaking, MmNumMembers)(matchmaking, lobby);
        var at = F<IdOutIdInt>(matchmaking, MmMemberByIndex);
        for (int i = 0; i < n; i++) { ulong m; at(matchmaking, out m, lobby, i); r.Add(m); }
        return r;
    }

    public static string Do(string command, string arg)
    {
        lock (OnlineHost.Gate)
        {
            if (!Ready()) return command == "NetInfo" ? "none" : "0";
            switch (command)
            {
                case "NetInfo":
                {
                    var id = MyId(); OnlineHost.Trace("my id " + id);
                    var name = NameOf(id); OnlineHost.Trace("my name " + name);
                    return "steam " + id + " " + OnlineHost.B64(name);
                }
                case "NetHost":
                {
                    int max; if (!int.TryParse(arg, out max)) max = 4;
                    Leave();
                    creating = F<CallIntInt>(matchmaking, MmCreateLobby)(matchmaking, 1, max);     // k_ELobbyTypeFriendsOnly
                    return creating != 0 ? "1" : "0";
                }
                case "NetJoin":
                {
                    ulong id; if (!ulong.TryParse(arg, out id)) return "0";
                    Leave();
                    joining = F<CallId>(matchmaking, MmJoinLobby)(matchmaking, id);
                    return joining != 0 ? "1" : "0";
                }
                case "NetLeave": Leave(); return "";
                case "NetSend":
                {
                    var p = arg.Split(' '); ulong to;
                    if (p.Length != 2 || !ulong.TryParse(p[0], out to)) return "0";
                    lock (outgoing) outgoing.Enqueue(new KeyValuePair<ulong, byte[]>(to, Convert.FromBase64String(p[1])));
                    return "1";
                }
                case "NetMembers": return string.Join(" ", Members().Select(m => m + "," + OnlineHost.B64(NameOf(m))).ToArray());
                case "NetFriends":
                {
                    var list = new List<string>();
                    const int Immediate = 4;
                    int n = F<IntInt>(friends, FrFriendCount)(friends, Immediate);
                    var at = F<IdOutIntInt>(friends, FrFriendByIndex);
                    var played = F<GamePlayed>(friends, FrGamePlayed);
                    for (int i = 0; i < n; i++)
                    {
                        ulong f; at(friends, out f, i, Immediate);
                        var info = new byte[24];     // FriendGameInfo_t: game id 0, ip 8, ports 12/14, lobby 16
                        if (!played(friends, f, info)) continue;
                        ulong game = BitConverter.ToUInt64(info, 0), lob = BitConverter.ToUInt64(info, 16);
                        if ((uint)(game & 0xFFFFFF) != appId || lob == 0) continue;
                        list.Add(f + "," + OnlineHost.B64(NameOf(f)) + "," + lob);
                    }
                    return string.Join(" ", list.ToArray());
                }
                case "NetInvite":
                    if (lobby == 0) return "0";
                    F<VoidId>(friends, FrInviteDialog)(friends, lobby);
                    return "1";
            }
            return "?";
        }
    }

    static void Leave()
    {
        if (lobby == 0) return;
        foreach (var m in Members()) if (m != MyId()) F<BoolId>(networking, NetClose)(networking, m);
        F<VoidId>(matchmaking, MmLeaveLobby)(matchmaking, lobby);
        lobby = 0;
    }

    // Every 20 ms: the call results we wait for, the Steam client's callbacks, packets in and out.
    static void Pump()
    {
        var msg = Marshal.AllocHGlobal(16);     // CallbackMsg_t: user, id, data pointer, size
        var call = Marshal.AllocHGlobal(8);
        var buffer = new byte[1 << 20];
        while (true)
        {
            try
            {
                lock (OnlineHost.Gate)
                {
                    PollCalls();
                    if (bGetCallback != null)
                        while (bGetCallback(pipe, msg, call))
                        {
                            try { Callback(Marshal.ReadInt32(msg, 4), Marshal.ReadIntPtr(msg, 8), Marshal.ReadInt32(msg, 12)); }
                            finally { freeLastCallback(pipe); }
                        }
                    // Accept sessions from the lobby's members (also covers a missed session request).
                    if (lobby != 0) foreach (var m in Members()) if (m != MyId()) F<BoolId>(networking, NetAccept)(networking, m);
                    uint size; ulong from;
                    var available = F<PacketAvailable>(networking, NetAvailable); var read = F<ReadPacket>(networking, NetRead);
                    while (available(networking, out size, 0))
                    {
                        if (size > buffer.Length) buffer = new byte[size];
                        uint got;
                        if (!read(networking, buffer, (uint)buffer.Length, out got, out from, 0)) break;
                        OnlineHost.Event("data", from, Convert.ToBase64String(buffer, 0, (int)got));
                    }
                    var send = F<SendPacket>(networking, NetSendP2P);
                    lock (outgoing)
                        while (outgoing.Count > 0)
                        {
                            var o = outgoing.Peek();
                            if (!send(networking, o.Key, o.Value, (uint)o.Value.Length, 2, 0)) break;     // k_EP2PSendReliable; full: later
                            outgoing.Dequeue();
                        }
                }
            }
            catch (Exception ex) { OnlineHost.Event("failed", "pump", OnlineHost.B64(ex.GetType().Name + ": " + ex.Message)); Thread.Sleep(1000); }
            Thread.Sleep(20);
        }
    }

    static void PollCalls()
    {
        bool failed;
        var done = F<CallDone>(utils, UtCallDone); var result = F<CallResult>(utils, UtCallResult);
        if (creating != 0 && done(utils, creating, out failed))
        {
            var r = new byte[16];     // LobbyCreated_t: result 0, lobby 8
            bool ok = result(utils, creating, r, r.Length, ItsMatchmaking + 13, out failed) && !failed && BitConverter.ToInt32(r, 0) == 1;
            creating = 0;
            if (ok)
            {
                lobby = BitConverter.ToUInt64(r, 8);
                F<BoolIdStrStr>(matchmaking, MmSetLobbyData)(matchmaking, lobby, "game", "underrailworks");
                OnlineHost.Event("hosted", lobby);
            }
            else OnlineHost.Event("failed", "host", OnlineHost.B64("Steam couldn't make the lobby (result " + BitConverter.ToInt32(r, 0) + ")"));
        }
        if (joining != 0 && done(utils, joining, out failed))
        {
            var r = new byte[24];     // LobbyEnter_t: lobby 0, permissions 8, locked 12, response 16
            bool ok = result(utils, joining, r, r.Length, ItsMatchmaking + 4, out failed) && !failed;
            joining = 0;
            int response = BitConverter.ToInt32(r, 16);
            if (ok && response == 1)     // k_EChatRoomEnterResponseSuccess
            {
                lobby = BitConverter.ToUInt64(r, 0);
                ulong owner = Owner();
                F<BoolId>(networking, NetAccept)(networking, owner);
                OnlineHost.Event("entered", lobby, owner);
            }
            else OnlineHost.Event("failed", "join", OnlineHost.B64(response == 2 ? "that game no longer exists" : response == 4 ? "that game is full" : "Steam couldn't enter the lobby (" + response + ")"));
        }
    }

    [UnmanagedFunctionPointer(CallingConvention.StdCall)] delegate IntPtr OwnerOf(IntPtr self, out ulong ret, ulong lobby);
    static ulong Owner() { ulong o; F<OwnerOf>(matchmaking, MmLobbyOwner)(matchmaking, out o, lobby); return o; }

    static void Callback(int id, IntPtr data, int size)
    {
        switch (id)
        {
            case ItsMatchmaking + 6:     // LobbyChatUpdate_t: lobby 0, changed 8, by 16, state 24
            {
                if (size < 28 || (ulong)Marshal.ReadInt64(data, 0) != lobby) return;
                ulong who = (ulong)Marshal.ReadInt64(data, 8);
                int state = Marshal.ReadInt32(data, 24);
                if ((state & 1) != 0) { F<BoolId>(networking, NetAccept)(networking, who); OnlineHost.Event("joined", who, OnlineHost.B64(NameOf(who))); }
                if ((state & 0x1E) != 0) OnlineHost.Event("left", who);
                return;
            }
            case ItsFriends + 33:     // GameLobbyJoinRequested_t: lobby 0, friend 8 (the player accepted an invite / Join Game)
                if (size >= 8) OnlineHost.Event("invite", (ulong)Marshal.ReadInt64(data, 0));
                return;
            case ItsFriends + 37:     // GameRichPresenceJoinRequested_t: friend 0, connect string 8
            {
                if (size < 9) return;
                var connect = Marshal.PtrToStringAnsi(new IntPtr(data.ToInt64() + 8)) ?? "";
                var parts = connect.Split(' ');
                ulong l;
                if (parts.Length == 2 && parts[0] == "+connect_lobby" && ulong.TryParse(parts[1], out l)) OnlineHost.Event("invite", l);
                return;
            }
            case ItsNetworking + 2:     // P2PSessionRequest_t: remote 0 (accepted only from the lobby's members)
            {
                if (size < 8) return;
                ulong who = (ulong)Marshal.ReadInt64(data, 0);
                if (Members().Contains(who)) F<BoolId>(networking, NetAccept)(networking, who);
                return;
            }
            case ItsNetworking + 3:     // P2PSessionConnectFail_t: remote 0, error 8
                if (size >= 9) OnlineHost.Event("left", (ulong)Marshal.ReadInt64(data, 0));
                return;
        }
    }
}
