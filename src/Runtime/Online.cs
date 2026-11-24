// UnderRailWorks: playing together through Steam (the only store supported for now: GOG Galaxy's multiplayer
// needs the Galaxy app, which most Linux GOG players don't run, and couldn't be tested).
// Lobbies only your friends can see and join, messages relayed by the store (no addresses, no port
// forwarding), the friends who are hosting, the store's invite dialog, and invites accepted in the store's friends list
// or overlay. The store's own API runs in the 32-bit helper process (UnderrailSteamHost.exe, OnlineHost.cs); this side
// polls it on the game's thread, so every hook here runs on the game's thread.
// Messages: any length (sent in parts of at most 256 KB, joined again on arrival), reliable and in order per player.
// Ids (players, lobbies) are the store's own, as decimal strings.
//   Online.Hosted.Add(this, lobby => ...);            Online.Host(4);
//   Online.Received.Add(this, (from, data) => ...);   Online.Send(id, bytes);
//   foreach (var f in Online.FriendsHosting()) ...    Online.Join(f.Lobby);
// Not in test runs without Steam (Service is null there).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    [Preview("multiplayer: built for Co-op so far")]
    public sealed class OnlinePlayer
    {
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string Lobby { get; internal set; }     // FriendsHosting: the lobby they're hosting or in
        public override string ToString() { return Name + " (" + Id + ")"; }
    }

    [Preview("multiplayer: built for Co-op so far")]
    public static class Online
    {
        const int Part = 256 * 1024;
        static string service, myId, myName;
        static double askTimer, pollTimer;
        static bool pending;     // a Host or Join waiting for its answer
        static readonly Dictionary<string, MemoryStream> partial = new Dictionary<string, MemoryStream>();

        // "Steam" (or "Test store"), or null (not signed in to a store: then nothing here works).
        public static string Service { get { Ask(); return service; } }
        public static bool Available { get { return Service != null; } }
        public static string MyId { get { Ask(); return myId; } }
        public static string MyName { get { Ask(); return myName; } }
        // The lobby this player is in (hosting or joined), or null; and who made it.
        public static string Lobby { get; private set; }
        public static string LobbyOwner { get; private set; }
        public static bool IsHosting { get { return Lobby != null && LobbyOwner == myId; } }

        // A lobby of ours is up (lobby id): friends can now see and join it.
        public static readonly Hook<Action<string>> Hosted = new Hook<Action<string>>("Online.Hosted");
        // We're in someone's lobby (lobby, owner): talk to the owner with Send.
        public static readonly Hook<Action<string, string>> Entered = new Hook<Action<string, string>>("Online.Entered");
        // Host or Join didn't work ("host" / "join", why, for the player).
        public static readonly Hook<Action<string, string>> Failed = new Hook<Action<string, string>>("Online.Failed");
        public static readonly Hook<Action<OnlinePlayer>> MemberJoined = new Hook<Action<OnlinePlayer>>("Online.MemberJoined");
        // A member left the lobby or their connection failed (id). For a joiner: the host's id means the game is gone.
        public static readonly Hook<Action<string>> MemberLeft = new Hook<Action<string>>("Online.MemberLeft");
        public static readonly Hook<Action<string, byte[]>> Received = new Hook<Action<string, byte[]>>("Online.Received");
        // The player accepted an invite or chose Join Game in the store's friends list (lobby id), also when the game
        // was started by it (Steam's "+connect_lobby"; raised once the main menu is up).
        public static readonly Hook<Action<string>> InviteAccepted = new Hook<Action<string>>("Online.InviteAccepted");

        static void Ask()
        {
            if (service != null || askTimer > 0) return;
            askTimer = 10;
            var r = Store.Call("NetInfo");
            var p = (r ?? "").Split(' ');
            if (p.Length == 3 && (p[0] == "steam" || p[0] == "test"))
            {
                service = p[0] == "steam" ? "Steam" : "Test store";
                myId = p[1]; myName = Text(p[2]);
                Log.Write("online: " + service + " as " + myName);
            }
        }

        static string Text(string b64) { try { return Encoding.UTF8.GetString(Convert.FromBase64String(b64)); } catch { return ""; } }

        // Makes a friends-only lobby for up to maxMembers players (you included). Answered by Hosted or Failed.
        public static bool Host(int maxMembers)
        {
            if (!Available) return false;
            Lobby = null; LobbyOwner = null; partial.Clear();
            pending = Store.Call("NetHost " + Math.Max(2, maxMembers)) == "1";
            return pending;
        }
        // Enters a friend's lobby. Answered by Entered or Failed.
        public static bool Join(string lobby)
        {
            if (!Available || string.IsNullOrEmpty(lobby)) return false;
            Lobby = null; LobbyOwner = null; partial.Clear();
            pending = Store.Call("NetJoin " + lobby) == "1";
            return pending;
        }
        public static void Leave()
        {
            if (service == null) return;
            if (Lobby != null || pending) Store.Call("NetLeave");
            Lobby = null; LobbyOwner = null; pending = false; partial.Clear();
        }

        // Reliable, in order. False: not in a lobby, or the helper refused.
        public static bool Send(string to, byte[] data)
        {
            if (Lobby == null || string.IsNullOrEmpty(to) || data == null) return false;
            int at = 0;
            do
            {
                int n = Math.Min(Part, data.Length - at);
                var packet = new byte[n + 1];
                packet[0] = (byte)(at + n >= data.Length ? 1 : 0);     // 1: the message's last part
                Buffer.BlockCopy(data, at, packet, 1, n);
                if (Store.Call("NetSend " + to + " " + Convert.ToBase64String(packet)) != "1") return false;
                at += n;
            } while (at < data.Length);
            return true;
        }

        public static IList<OnlinePlayer> Members() { return Players(Lobby == null ? null : Store.Call("NetMembers")); }
        // Friends hosting (or in) a lobby of this game, with their lobby.
        public static IList<OnlinePlayer> FriendsHosting() { return Players(Available ? Store.Call("NetFriends") : null); }
        // The store's overlay invite dialog for our lobby (if the overlay runs in this game).
        public static bool OpenInviteDialog() { return Lobby != null && Store.Call("NetInvite") == "1"; }
        // The store's overlay with the player's friends (Steam: Join Game on a friend there). False: no store, or it
        // couldn't (the overlay may not run in this game, e.g. under Proton: nothing shows then either).
        public static bool OpenFriends() { return Available && Store.Call("NetOverlay Friends") == "1"; }

        static List<OnlinePlayer> Players(string reply)
        {
            var list = new List<OnlinePlayer>();
            foreach (var e in (reply ?? "").Split(new[] { ' ' }, StringSplitOptions.RemoveEmptyEntries))
            {
                var p = e.Split(',');
                if (p.Length >= 2) list.Add(new OnlinePlayer { Id = p[0], Name = Text(p[1]), Lobby = p.Length > 2 ? p[2] : null });
            }
            return list;
        }

        static string launchLobby;     // "+connect_lobby <id>" on the command line (Steam started the game for an invite)
        static bool launchChecked;

        internal static void Tick(double seconds)
        {
            askTimer -= seconds;
            if (!launchChecked)
            {
                launchChecked = true;
                var args = Environment.GetCommandLineArgs();
                int i = Array.IndexOf(args, "+connect_lobby");
                if (i >= 0 && i + 1 < args.Length) { launchLobby = args[i + 1]; Log.Write("online: started for lobby " + launchLobby); }
            }
            if (launchLobby != null && Hud.Current == null && UiPopups.Screen() is a8k && Available && InviteAccepted.Any)
            { var l = launchLobby; launchLobby = null; InviteAccepted.Run(h => h(l)); }
            if (service == null) { Ask(); if (service == null) return; }     // (asked every 10 s: a store can sign in late)
            // Every frame in a lobby; otherwise once a second (invites).
            if (Lobby == null && !pending && (pollTimer -= seconds) > 0) return;
            pollTimer = 1;
            var r = Store.Call("NetPoll");
            if (string.IsNullOrEmpty(r)) return;
            foreach (var e in r.Split(' '))
            {
                var p = e.Split(',');
                try { Handle(p); } catch (Exception ex) { Log.Write("online: " + p[0] + ": " + ex.Message); }
            }
        }

        static void Handle(string[] p)
        {
            switch (p[0])
            {
                case "hosted":
                    pending = false; Lobby = p[1]; LobbyOwner = myId;
                    Log.Write("online: hosting lobby " + Lobby);
                    if (Hosted.Any) Hosted.Run(h => h(p[1]));
                    break;
                case "entered":
                    pending = false; Lobby = p[1]; LobbyOwner = p[2];
                    Log.Write("online: in lobby " + Lobby + " (host " + LobbyOwner + ")");
                    if (Entered.Any) Entered.Run(h => h(p[1], p[2]));
                    break;
                case "failed":
                {
                    var why = p.Length > 2 ? Text(p[2]) : "";
                    Log.Write("online: " + p[1] + " failed: " + why);
                    if (p[1] == "host" || p[1] == "join") pending = false;
                    if (Failed.Any) Failed.Run(h => h(p[1], why));
                    break;
                }
                case "joined":
                {
                    var who = new OnlinePlayer { Id = p[1], Name = p.Length > 2 ? Text(p[2]) : p[1] };
                    if (who.Id != myId && MemberJoined.Any) MemberJoined.Run(h => h(who));
                    break;
                }
                case "left":
                    partial.Remove(p[1]);
                    if (p[1] != myId && MemberLeft.Any) MemberLeft.Run(h => h(p[1]));
                    break;
                case "data":
                {
                    var bytes = Convert.FromBase64String(p[2]);
                    if (bytes.Length == 0) return;
                    MemoryStream m;
                    if (!partial.TryGetValue(p[1], out m)) partial[p[1]] = m = new MemoryStream();
                    m.Write(bytes, 1, bytes.Length - 1);
                    if (bytes[0] != 1) return;
                    partial.Remove(p[1]);
                    var data = m.ToArray();
                    if (Received.Any) Received.Run(h => h(p[1], data));
                    break;
                }
                case "invite":
                    Log.Write("online: invite to lobby " + p[1]);
                    if (InviteAccepted.Any) InviteAccepted.Run(h => h(p[1]));
                    break;
            }
        }
    }

    // Where Online's commands go: the helper (Steam), or with UNDERRAILWORKS_TEST_STORE=<folder> a pretend store
    // for test runs: games on one computer sharing that folder (lobbies and messages as files), so two test games can
    // play together through Online without two store accounts.
    static class Store
    {
        static readonly string folder = Environment.GetEnvironmentVariable("UNDERRAILWORKS_TEST_STORE");
        internal static string Call(string command) { return string.IsNullOrEmpty(folder) ? SteamBridge.Call(command) : TestStore.Call(folder, command); }
    }

    static class TestStore
    {
        static readonly string me = ((ulong)new Random().Next(1, int.MaxValue) * 1000 + (ulong)System.Diagnostics.Process.GetCurrentProcess().Id).ToString();
        static string myName, lobby, owner;
        static long seq;
        static HashSet<string> known = new HashSet<string>();
        static string B64(string t) { return Convert.ToBase64String(Encoding.UTF8.GetBytes(t ?? "")); }
        static string Lobbies(string root) { return Path.Combine(root, "lobbies"); }
        static string MembersOf(string root, string l) { return Path.Combine(Path.Combine(Lobbies(root), l), "members"); }

        internal static string Call(string root, string line)
        {
            var parts = line.Split(new[] { ' ' }, 2);
            string arg = parts.Length > 1 ? parts[1] : "";
            if (myName == null) myName = Environment.GetEnvironmentVariable("UNDERRAILWORKS_TEST_NAME") ?? ("Tester " + me.Substring(me.Length - 3));
            Directory.CreateDirectory(Lobbies(root));
            switch (parts[0])
            {
                case "NetInfo": return "test " + me + " " + B64(myName);
                case "NetHost":
                    Leave(root);
                    lobby = ((ulong)DateTime.UtcNow.Ticks).ToString(); owner = me;
                    Directory.CreateDirectory(MembersOf(root, lobby));
                    File.WriteAllText(Path.Combine(Path.Combine(Lobbies(root), lobby), "owner"), me);
                    File.WriteAllText(Path.Combine(MembersOf(root, lobby), me), myName);
                    known = new HashSet<string> { me };
                    pendingEvents.Add("hosted," + lobby);
                    return "1";
                case "NetJoin":
                {
                    Leave(root);
                    var ownerFile = Path.Combine(Path.Combine(Lobbies(root), arg), "owner");
                    if (!File.Exists(ownerFile)) { pendingEvents.Add("failed,join," + B64("that game no longer exists")); return "1"; }
                    lobby = arg; owner = File.ReadAllText(ownerFile).Trim();
                    File.WriteAllText(Path.Combine(MembersOf(root, lobby), me), myName);
                    known = new HashSet<string>(Directory.GetFiles(MembersOf(root, lobby)).Select(Path.GetFileName));
                    pendingEvents.Add("entered," + lobby + "," + owner);
                    return "1";
                }
                case "NetLeave": Leave(root); return "";
                case "NetSend":
                {
                    var p = arg.Split(' ');
                    if (p.Length != 2) return "0";
                    var box = Path.Combine(Path.Combine(root, "inbox"), p[0]);
                    Directory.CreateDirectory(box);
                    var name = DateTime.UtcNow.Ticks.ToString("D20") + "-" + (seq++).ToString("D10") + "-" + me;
                    File.WriteAllBytes(Path.Combine(box, name + ".tmp"), Convert.FromBase64String(p[1]));
                    File.Move(Path.Combine(box, name + ".tmp"), Path.Combine(box, name + ".msg"));
                    return "1";
                }
                case "NetPoll": return Poll(root);
                case "NetMembers":
                    if (lobby == null || !Directory.Exists(MembersOf(root, lobby))) return "";
                    return string.Join(" ", Directory.GetFiles(MembersOf(root, lobby)).Select(f => Path.GetFileName(f) + "," + B64(Read(f))).ToArray());
                case "NetFriends":
                {
                    var list = new List<string>();
                    foreach (var d in Directory.GetDirectories(Lobbies(root)))
                    {
                        var o = Read(Path.Combine(d, "owner")).Trim();
                        if (o.Length == 0 || o == me) continue;
                        list.Add(o + "," + B64(Read(Path.Combine(MembersOf(root, Path.GetFileName(d)), o))) + "," + Path.GetFileName(d));
                    }
                    return string.Join(" ", list.ToArray());
                }
                case "NetInvite": return "0";
                case "NetOverlay": return "0";
            }
            return "?";
        }

        static readonly List<string> pendingEvents = new List<string>();
        static string Read(string f) { try { return File.ReadAllText(f); } catch { return ""; } }

        static void Leave(string root)
        {
            if (lobby == null) return;
            try { File.Delete(Path.Combine(MembersOf(root, lobby), me)); } catch { }
            if (owner == me) try { Directory.Delete(Path.Combine(Lobbies(root), lobby), true); } catch { }
            lobby = null; owner = null;
        }

        static string Poll(string root)
        {
            var events = new List<string>(pendingEvents); pendingEvents.Clear();
            if (lobby != null)
            {
                var dir = MembersOf(root, lobby);
                var now = Directory.Exists(dir) ? new HashSet<string>(Directory.GetFiles(dir).Select(Path.GetFileName)) : new HashSet<string>();
                foreach (var m in now.Where(m => !known.Contains(m))) events.Add("joined," + m + "," + B64(Read(Path.Combine(dir, m))));
                foreach (var m in known.Where(m => !now.Contains(m))) events.Add("left," + m);
                known = now;
                if (!now.Contains(owner) && owner != me) { lobby = null; owner = null; }
            }
            var box = Path.Combine(Path.Combine(root, "inbox"), me);
            if (Directory.Exists(box))
                foreach (var f in Directory.GetFiles(box, "*.msg").OrderBy(x => x, StringComparer.Ordinal))
                {
                    byte[] data;
                    try { data = File.ReadAllBytes(f); File.Delete(f); } catch { break; }
                    var name = Path.GetFileNameWithoutExtension(f);
                    events.Add("data," + name.Substring(name.LastIndexOf('-') + 1) + "," + Convert.ToBase64String(data));
                }
            return string.Join(" ", events.ToArray());
        }
    }
}
