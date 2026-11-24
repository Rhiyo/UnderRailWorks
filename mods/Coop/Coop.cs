// Co-op: an UnderRailWorks mod (design, results and open questions: docs/Multiplayer.md in the Works repo).
// The host's game is the only real one. Players gather in the host's lobby before the game starts (CoopLobby.cs: Host
// New Game / Host Saved Game on the main menu's Co-op page; nobody joins a game under way). Each joiner takes a place: a co-op
// character saved with that game, or a new one made in the game's own creation. The host starts (a new game: with
// whoever joined; a save: when every saved character's player is there), and each character appears in the host's game (where it was saved, else next to the host's character),
// driven by its player instead of an AI (Works' CharacterControl).
// The joiner's game loads the host's save and mirrors it (Mirror.cs); their clicks become commands here; conversations
// (CoopTalk.cs) and looting (CoopLoot.cs) are relayed. The group travels together (every player near the exit), and
// everyone gets all the XP anyone earns (CoopXp.cs).
// The Co-op button on the HUD opens the Co-op window (CoopUi.cs); the console commands do the same.
// Host (console, ` key, main menu):   coop.hostnew <players> [port]   coop.hostsave <save> [new players] [port]
//                                     coop.start   coop.who   coop.stop
// Joiner (main menu):      coop.join <address[:port]> [your name]   then coop.pick <number> or coop.new <name>
// Playing (joiner):        coop.go x y   coop.attack <name>   coop.end   coop.status   coop.leave
// Without a connection the same commands drive a local test partner (coop.spawn).
// Co-op characters are kept in the mod's save data next to the save (never in the game's save): name, blueprint,
// zone, tile and sheet. They follow the host's character to other zones (spawned again next to it).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.12.0")]

public sealed partial class Coop : WorksMod
{
    // What host and joiner compare when connecting: the release version and the message protocol. The protocol goes up
    // whenever the messages change, also between releases (dev builds with different protocols can't play together;
    // the release version itself only changes at a GitHub release).
    const int Protocol = 15;
    string Handshake { get { return Info.VersionText + "/" + Protocol; } }

    const int DefaultPort = 27650;
    const float TogetherTiles = 10;     // every player within this of whoever takes an exit, or nobody goes
    const string DefaultBlueprint = "creatures\\generichumans\\jkkgunslinger";
    static readonly Color Say = new Color(120, 200, 255);

    // --- Saved co-op characters ---------------------------------------------------------------------------------
    sealed class Record { public string Name, Blueprint, Zone, Sheet; public int X, Y; }     // Sheet: the character's .char (base64), made in the game's creation
    List<Record> records = new List<Record>();

    void ReadRecords() { records = ParseRecords(SaveData.Of(this).Get("characters")); }

    void WriteRecords()
    {
        foreach (var seat in seats.Values) Remember(seat);
        foreach (var o in orphans) Remember(o);
        if (listener != null) SaveData.Of(this).Set("options", OptionsToSave());
        SaveData.Of(this).Set("characters", records.Select(r => (object)new Dictionary<string, object> { { "name", r.Name }, { "blueprint", r.Blueprint }, { "zone", r.Zone }, { "x", r.X }, { "y", r.Y }, { "sheet", r.Sheet } }).ToList());
    }

    void Remember(Seat seat)
    {
        var c = seat.Character;
        if (seat.Record == null || c == null || c.IsDead || c.Area == null) return;
        seat.Record.Zone = World.ZoneId; seat.Record.X = (int)Math.Round(c.Position.X); seat.Record.Y = (int)Math.Round(c.Position.Y);
        var sheet = SheetOf(c);
        if (sheet != null) seat.Record.Sheet = sheet;
    }

    // A character's sheet as a .char, base64 (null: it has none, or the export failed).
    string SheetOf(Character c)
    {
        if (c == null || !c.HasPlayerSheet) return null;
        var path = SheetPath("export");
        try { return c.ExportTo(path, new DateTime(2026, 1, 1)) ? Convert.ToBase64String(System.IO.File.ReadAllBytes(path)) : null; }
        catch (Exception ex) { Log.Warn("can't export " + c.Name + ": " + ex); return null; }
    }
    string SheetPath(string name) { return System.IO.Path.Combine(Info.Folder, "sheets", new string(name.Where(char.IsLetterOrDigit).ToArray()) + ".char"); }
    string WriteSheet(string name, string base64) { var path = SheetPath(name); System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)); System.IO.File.WriteAllBytes(path, Convert.FromBase64String(base64)); return path; }

    // --- Host ---------------------------------------------------------------------------------------------------
    sealed class Seat { public Peer Peer; public Record Record; public Character Character; public string LastState; public string LastSign; public bool WorldPending, WorldWaiting, InWorld, Announced; public string[] Owns; public int SheetVersion; public string PlayerName; public readonly Dictionary<string, string> Sent = new Dictionary<string, string>(), Walks = new Dictionary<string, string>(), Doors = new Dictionary<string, string>(), Statuses = new Dictionary<string, string>(); }
    Listener listener;
    static float Dist(Character a, Character b) { return Vector2.Distance(new Vector2(a.Position.X, a.Position.Y), new Vector2(b.Position.X, b.Position.Y)); }
    static bool Sees(Character who, Character other) { return who.CanSee(other) && Dist(who, other) <= who.SightRange; }
    readonly Inbox inbox = new Inbox();
    readonly Dictionary<Peer, Seat> seats = new Dictionary<Peer, Seat>();
    readonly List<Seat> orphans = new List<Seat>();     // joiners who left: their character stays, passing its turns
    bool reloaded;     // a game was loaded while hosting: place players from its records

    // Listening for players (the lobby, CoopLobby.cs). Null, or why not.
    string Listen(int port)
    {
        if (listener != null) return "already hosting on port " + listener.Port;
        try { listener = new Listener(port, tcp => new TcpPeer(tcp, inbox.Add, p => inbox.Add(p, new[] { "_closed" }))); }
        catch (Exception ex) { listener = null; return "can't host on port " + port + ": " + ex.Message; }
        // The group travels together: an exit is taken only with every player near the one taking it (Works' Party).
        ApplyTravelRule();
        // The map's player-only triggers (story, quests, barks, dialog areas) fire for any player stepping in.
        Party.TriggersCountMembers = true;
        // The faction-armour conditions: met if any player wears it.
        Party.FactionArmorCountsAnyMember = true;
        // NPCs react to any player's position (stats stay the host's).
        Party.ReactionsCountMembers = true;
        // Traps work for every player; the game's scripts and windows that only know one player don't hurt the others.
        Party.TrapsCountMembers = true;
        Party.Safeguards = true;
        Log.Info("hosting on port " + port);
        Notify.Log("Co-op: hosting on port " + port + ". Others join with Co-op, Join a Game and your address" + (port == DefaultPort ? "" : ":" + port), Say);
        return null;
    }

    void StopHosting()
    {
        if (listener == null) return;
        WriteRecords();
        foreach (var p in seats.Keys.ToList()) { p.Send("BYE", "the host stopped"); p.Close(); }
        onlinePeers.Clear();
        if (onlineHosting) { Online.Leave(); onlineHosting = false; }
        foreach (var s in seats.Values.Concat(orphans)) if (s.Character != null) CharacterControl.Release(s.Character);
        seats.Clear(); orphans.Clear();
        slots.Clear(); lobby = null; starting = false;
        listener.Stop(); listener = null;
        Party.DontNeedTogether(this, PartyNeed.EnterZone, null);
        Party.TriggersCountMembers = false;
        Party.FactionArmorCountsAnyMember = false;
        Party.ReactionsCountMembers = false;
        Party.TrapsCountMembers = false;
        Party.Safeguards = false;
        Log.Info("stopped hosting");
        Changed();
    }

    void HostMessage(Peer p, string[] w)
    {
        Seat seat;
        seats.TryGetValue(p, out seat);
        switch (w[0])
        {
            case "_closed":
                if (seat == null) return;
                if (talking == seat) { TalkAs.Close(); talking = null; }     // left mid-conversation: it ends
                seats.Remove(p);
                if (seat.Character == null) LeftLobby(seat);
                if (Api != null) Api.RaiseLeft(p.Name);
                CheckGameOver();
                if (seat.Character != null) { Remember(seat); seat.Peer = null; orphans.Add(seat); }
                Log.Info(p.Name + " left" + (seat.Character != null ? " (" + seat.Character.Name + " stays, passing its turns)" : ""));
                Notify.Log("Co-op: " + p.Name + " left", Say);
                Changed(false);
                return;
            case "HELLO":
                p.Name = w.Length > 2 && w[2].Trim().Length > 0 ? w[2].Trim() : "Player";     // no name to go by (no store): "Player"
                if (w.Length < 2 || w[1] != Handshake) { p.Send("BYE", "the host has Co-op " + Handshake + ", you have " + (w.Length > 1 ? w[1] : "?") + " (both need the same)"); p.Close(); return; }
                {
                    var owns = ParseExpansions(w.Length > 4 ? w[4] : "");
                    // After the start only a player coming back to their own character (left behind, still alive).
                    var back = lobby == null ? orphans.FirstOrDefault(o => o.PlayerName != null && o.PlayerName.Equals(p.Name, StringComparison.OrdinalIgnoreCase) && o.Character != null && !o.Character.IsDead && o.Character.Area != null) : null;
                    var missingExp = Expansions.Active.Where(x => !owns.Contains(x)).ToList();
                    var refused = back != null ? (missingExp.Count > 0 ? "this game uses " + Names(missingExp) + ", which you don't have" : null) : RefuseJoin() ?? RefuseForExpansions(owns);
                    if (refused != null)
                    {
                        Log.Info(p.Name + " refused: " + refused);
                        Notify.Log("Co-op: " + p.Name + " can't join: " + refused, Say);
                        p.Send("BYE", refused); p.Close(); return;
                    }
                    // Everyone needs the same mods (those whose mod.json doesn't say "multiplayer": "client"), same versions.
                    var theirs = w.Length > 3 ? w[3].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries) : new string[0];
                    var diff = Mods.Differences(theirs, Mods.Running(true), "you have", "the host has");
                    if (diff.Length > 0)
                    {
                        Log.Info(p.Name + " refused: different mods: " + string.Join("; ", diff));
                        Notify.Log("Co-op: " + p.Name + " can't join: their mods differ (" + string.Join("; ", diff) + ")", Say);
                        // The host's mods that must match, each with its name and homepage: the joiner can match them (CoopMatch.cs).
                        p.Send(new object[] { "HOSTMODS" }.Concat(Mods.Running(true).Select(x => { int at = x.LastIndexOf('@'); var m = ModLoader.Find(x.Substring(0, at)); return (object)(x + "|" + (m == null ? x.Substring(0, at) : m.Name) + "|" + (m == null ? "" : m.Homepage ?? "")); })).ToArray());
                        p.Send("BYE", "your mods differ from the host's: " + string.Join("; ", diff) + ". Mods marked \"client\" may differ."); p.Close(); return;
                    }
                }
                {
                    var back = lobby == null ? orphans.FirstOrDefault(o => o.PlayerName != null && o.PlayerName.Equals(p.Name, StringComparison.OrdinalIgnoreCase) && o.Character != null && !o.Character.IsDead && o.Character.Area != null) : null;
                    if (back != null) { Rejoin(p, back); return; }
                }
                var joined = seats[p] = new Seat { Peer = p, PlayerName = p.Name, Owns = ParseExpansions(w.Length > 4 ? w[4] : "") };
                // A new game's lobby: a place of their own straight away (they make the character for it); a save's: they
                // pick which of its characters is theirs.
                var place = lobby == "new" ? slots.FirstOrDefault(s => s.Seat == null) : null;
                if (place != null) place.Seat = joined;
                p.Name = joined.PlayerName = UniqueName(p.Name, p);
                Log.Info(p.Name + " connected from " + p.Address);
                Changed(false);
                Log.Info(p.Name + (place != null ? " has a place; making a character" : " is choosing a character") + "; expansions they have: " + Names(joined.Owns));
                Notify.Log("Co-op: " + p.Name + " joined" + (lobby == "new" ? "; expansions they have: " + Names(joined.Owns) : ""), Say);
                LobbyChanged();     // everyone: the places, and the expansions the game can use now
                return;
            case "BYE": p.Close(); return;
        }
        if (seat == null) { p.Send("BYE", "say HELLO first"); p.Close(); return; }
        if (w[0] == "MYSHEET" && seat.Character == null && seat.Record != null) { if (w.Length > 1) seat.Record.Sheet = w[1]; return; }     // apart: kept with the record
        if (w[0] == "LOADED") { HostMirrorMessage(seat, w); return; }
        if (ModMessage(p.Name, w)) return;
        if (w[0] == "POPUPANS") { PopupAnswered(w); return; }
        if (w[0] == "KEY") { KeyFrom(seat, w); return; }
        if (w[0] == "BUTTON") { ButtonFrom(seat, w); return; }
        if (w[0] == "PICK" || w[0] == "NEW")
        {
            if (lobby != null) LobbyChoice(seat, w);
            else p.Send("SAID", seat.Character != null ? "you're already playing " + seat.Character.Name : "the game has started: characters are chosen in the lobby");
            return;
        }
        if (seat.Character == null || seat.Character.IsDead || seat.Character.Area == null) { p.Send("SAID", seat.Character != null && seat.Character.IsDead ? "your character is dead" : "pick a character first"); return; }
        if (downed.Contains(seat.Character) && w[0] != "STATUS" && w[0] != "DLGSUGGEST") { p.Send("SAID", "you're down"); return; }
        var said = Act(seat.Character, w);
        if (!string.IsNullOrEmpty(said)) p.Send("SAID", said);
    }

    // The host's key for a character in this (joiner's) copy: a mirror's key, else the game's id (kept in the save).
    string KeyHere(Character c) { return spawned.Where(kv => kv.Value == c).Select(kv => kv.Key).FirstOrDefault() ?? c.EntityId; }

    Character SpawnFor(Record r, bool saved)
    {
        var p = World.Player;
        if (p == null) return null;
        Point tile = Point.Zero;
        if (saved && r.Zone == World.ZoneId)
            tile = World.FreeTilesNear(r.X, r.Y, 2).OrderBy(t => Math.Abs(t.X - r.X) + Math.Abs(t.Y - r.Y)).FirstOrDefault();
        if (tile == Point.Zero)
        {
            var at = p.Position;
            tile = World.FreeTilesNear((int)at.X, (int)at.Y, 4).Where(t => Math.Abs(t.X - at.X) + Math.Abs(t.Y - at.Y) >= 1).OrderBy(t => Math.Abs(t.X - at.X) + Math.Abs(t.Y - at.Y)).FirstOrDefault();
        }
        if (tile == Point.Zero) return null;
        Character c;
        if (r.Sheet != null)
        {
            // Made in the game's creation: built as the player is, with its own sheet.
            try { c = World.SpawnFromCharacterFile(WriteSheet(r.Name, r.Sheet), tile.X, tile.Y); if (c != null) c.Name = r.Name; }
            catch (Exception ex) { Log.Error("can't build " + r.Name + ": " + ex); c = null; }
        }
        else c = World.Spawn(r.Blueprint ?? DefaultBlueprint, tile.X, tile.Y, SpawnSide.Ally);
        if (c == null) return null;
        c.Name = r.Name;
        // The player's own faction: everyone treats it as they treat the player (playerNativeAlly is the force
        // field/turret faction, which enemies only target when nothing else is near).
        c.Faction = "player";
        return c;
    }

    // --- Joiner -------------------------------------------------------------------------------------------------
    Peer host;
    bool playing;
    string hostName;
    string readyAs;     // the lobby gave us this character: waiting for the host to start
    readonly List<KeyValuePair<string, bool>> choices = new List<KeyValuePair<string, bool>>();     // name, taken
    readonly List<KeyValuePair<string, string>> choiceTakers = new List<KeyValuePair<string, string>>();     // per place: player, their character
    readonly List<KeyValuePair<string, string>> lobbySettings = new List<KeyValuePair<string, string>>();     // the host's game settings
    string lobbyKindSeen;     // "new" / "saved"
    string hostCharacterSeen;     // the host's character in a new game's lobby (made there)
    string myLobbyName;       // the name the host knows us by ("Player" without a store name; "(2)" added to a name already there)
    readonly List<string> messages = new List<string>();
    string[] state;     // the host's last STATE for our character
    string[] party;     // the host's last PARTY: name, health, max, dead... for every player

    string Join(string[] args)
    {
        if (listener != null) return "you're hosting";
        if (host != null && !host.Closed) return "already connected (coop.leave first)";
        if (args.Length < 1) return "coop.join <address[:port]> [your name]";
        string name = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()) : Online.Available && !string.IsNullOrEmpty(Online.MyName) ? Online.MyName : "";     // none: the host calls them "Player"
        if (args[0] == "friend")     // the first friend hosting (or the one named)
        {
            if (!Online.Available) return "not signed in to Steam: join by address instead";
            var f = Online.FriendsHosting().FirstOrDefault(x => args.Length < 2 || x.Name.Equals(args[1], StringComparison.OrdinalIgnoreCase));
            if (f == null) return "no friend hosting on " + Online.Service + (args.Length > 1 ? " called " + args[1] : "");
            var rest = new List<string> { "online:" + f.Lobby };
            for (int i = 2; i < args.Length; i++) rest.Add(args[i]);     // (Skip past the end, then Concat, threw on .NET 10)
            args = rest.ToArray();
        }
        if (args[0].StartsWith("online:"))     // a friend's lobby on Steam
        {
            if (!Online.Available) return "not signed in to Steam: join by address instead";
            if (args.Length < 2 && Online.MyName != null) name = Online.MyName;
            onlineJoining = name;
            playing = false; readyAs = null; state = null; choices.Clear(); hostMods = null; hostName = null;
            lastJoin = args;
            if (!Online.Join(args[0].Substring(7))) { onlineJoining = null; return "couldn't join through " + Online.Service; }
            Changed();
            return "joining through " + Online.Service + "...";
        }
        var parts = args[0].Split(':');
        int port = DefaultPort;
        if (parts.Length > 1 && !int.TryParse(parts[1], out port)) return "coop.join <address[:port]> [your name]";
        try
        {
            var tcp = new System.Net.Sockets.TcpClient();
            tcp.Connect(parts[0], port);
            host = new TcpPeer(tcp, inbox.Add, p => inbox.Add(p, new[] { "_closed" }));
        }
        catch (Exception ex) { host = null; return "can't reach " + args[0] + ": " + ex.Message; }
        playing = false; readyAs = null; state = null; choices.Clear(); hostMods = null;
        lastJoin = args;
        Hello(args[0], name);
        return "connected to " + args[0] + ", waiting for the character list";
    }

    // A player's name as everyone sees it: the first one keeps it, the same name again (two "Player"s without a store,
    // or two Steam accounts called the same, the host's included) gets a number: Player, Player (2), Player (3).
    string UniqueName(string name, Peer who)
    {
        var others = seats.Keys.Where(o => o != who).Select(o => o.Name).ToList();
        if (Online.Available && !string.IsNullOrEmpty(Online.MyName)) others.Add(Online.MyName);     // the host
        Func<string, bool> taken = n => others.Any(o => o.Equals(n, StringComparison.OrdinalIgnoreCase));
        if (!taken(name)) return name;
        int n2 = 2;
        while (taken(name + " (" + n2 + ")")) n2++;
        return name + " (" + n2 + ")";
    }

    void Hello(string to, string name)
    {
        host.Send("HELLO", Handshake, name, string.Join(",", Mods.Running(true)), string.Join(",", Expansions.Installed));
        Log.Info("connected to " + to + " as " + name);
    }

    // --- Through the store (Works' Online): the host's lobby beside its port; joiners by lobby --------------------
    readonly Dictionary<string, OnlinePeer> onlinePeers = new Dictionary<string, OnlinePeer>();     // host: by store id
    string onlineJoining;     // joiner: our name while the store enters the lobby
    bool onlineHosting;       // host: a lobby asked for (or up)

    OnlinePeer OnlinePeerFor(string id, string name)
    {
        OnlinePeer p;
        if (onlinePeers.TryGetValue(id, out p) && !p.Closed) return p;
        p = new OnlinePeer(id, Online.Service);
        onlinePeers[id] = p;
        Log.Info((name ?? id) + " came in through " + Online.Service);
        return p;
    }

    void HostOnline(int players)
    {
        if (!Online.Available) return;
        onlineHosting = true;
        if (!Online.Host(players)) Log.Info("couldn't make a " + Online.Service + " lobby (joining by address still works)");
    }

    void LoadOnline()
    {
        // A frame's messages to each store peer, as one packet.
        GameEvents.Update.Add(this, dt =>
        {
            foreach (var p in onlinePeers.Values) p.Flush();
            var hp = host as OnlinePeer; if (hp != null) hp.Flush();
        }, HookOrder.Late);
        Online.Hosted.Add(this, lobbyId =>
        {
            if (!onlineHosting) return;     // not Co-op's
            if (listener == null) { Online.Leave(); onlineHosting = false; return; }
            Notify.Log("Co-op: " + Online.Service + " friends can join you: Co-op, Join a Game, or Join Game in their friends list", Say);
            Changed();
        });
        Online.MemberJoined.Add(this, m => { if (listener != null) OnlinePeerFor(m.Id, m.Name); });
        Online.Received.Add(this, (from, data) =>
        {
            var lines = System.Text.Encoding.UTF8.GetString(data).Split('\n');
            Peer to = null;
            if (listener != null) to = OnlinePeerFor(from, null);
            else { var op = host as OnlinePeer; if (op != null && op.Remote == from) to = op; }
            if (to == null || to.Closed) return;
            foreach (var l in lines) if (l.Length > 0) inbox.Add(to, l.Split('\t'));
        });
        Online.MemberLeft.Add(this, id =>
        {
            OnlinePeer p;
            if (listener != null && onlinePeers.TryGetValue(id, out p)) { onlinePeers.Remove(id); p.Close(); inbox.Add(p, new[] { "_closed" }); return; }
            var op = host as OnlinePeer;
            if (op != null && op.Remote == id) { op.Close(); inbox.Add(op, new[] { "_closed" }); }
        });
        Online.Entered.Add(this, (lobbyId, owner) =>
        {
            if (onlineJoining == null) return;     // not Co-op's
            var name = onlineJoining; onlineJoining = null;
            host = new OnlinePeer(owner, Online.Service);
            Hello(Online.Service + " lobby " + lobbyId, name);
            Changed();
        });
        Online.Failed.Add(this, (what, why) =>
        {
            if (what == "join" && onlineJoining != null) { onlineJoining = null; Tell("can't join: " + why); Changed(); }
            else if (what == "host" && listener != null) Tell(Online.Service + " friends can't join (" + why + "); joining by address still works");
        });
        Online.InviteAccepted.Add(this, lobbyId =>
        {
            if (listener != null || (host != null && !host.Closed)) { Tell("leave this co-op game first to join another"); return; }
            if (World.Player != null) { Tell("a friend's invite: go to the main menu (Escape -> main menu) and accept it again, or use Co-op, Join a Game"); return; }
            Tell(Join(new[] { "online:" + lobbyId }));
            if (window == null || !window.IsOpen) Build();
        });
    }

    void JoinerMessage(string[] w)
    {
        switch (w[0])
        {
            case "_closed": Tell("disconnected from the host"); host = null; playing = false; readyAs = null; state = null; LeftWorld(); Changed(); return;
            case "BYE": Tell("the host closed the connection: " + (w.Length > 1 ? w[1] : "")); if (hostMods != null) Changed(); return;
            case "HOSTMODS": HostModsArrived(w); return;
            case "EXPANSIONS": ExpansionsArrived(w); return;
            case "LIST": hostName = w.Length > 1 ? w[1] : "?"; lobbyKindSeen = w.Length > 2 ? w[2] : null; if (w.Length > 3 && w[3].Length > 0) myLobbyName = w[3]; hostCharacterSeen = w.Length > 4 && w[4].Length > 0 ? w[4] : null; choices.Clear(); choiceTakers.Clear(); lobbySettings.Clear(); return;
            case "CHAR":
                choices.Add(new KeyValuePair<string, bool>(w[2], w.Length > 3 && w[3] == "taken"));
                choiceTakers.Add(new KeyValuePair<string, string>(w.Length > 4 ? w[4] : "", w.Length > 5 ? w[5] : ""));
                return;
            case "SETTING": if (w.Length > 2) lobbySettings.Add(new KeyValuePair<string, string>(w[1], w[2])); return;
            case "LISTEND":
                Log.Info("characters: " + hostName + " (host), " + string.Join(", ", choices.Select((c, i) => (i + 1) + ". " + c.Key + (c.Value ? " (taken)" : "")).ToArray()));
                Commands.Print("co-op: characters: " + hostName + " (host), " + string.Join(", ", choices.Select((c, i) => (i + 1) + ". " + c.Key + (c.Value ? " (taken)" : "")).ToArray()) + ". coop.pick <number> or coop.new <name>");
                Changed(); return;
            case "REJOIN": readyAs = w.Length > 1 ? w[1] : "?"; Tell("You're back as " + readyAs + ": the host's world is on its way."); Changed(); return;
            case "READY": readyAs = w.Length > 1 ? w[1] : "?"; Tell("You'll play " + readyAs + ". Waiting for the host to start the game."); Changed(); return;
            case "PLAYING": playing = true; readyAs = null; Tell("You play " + w[1] + " (at " + w[2] + "," + w[3] + ")"); Changed(); return;
            case "STATE": state = w; Changed(false); return;
            case "TURN":
                Tell("Your turn (AP " + w[1] + ", MP " + w[2] + ")");
                if (World.Player != null) Notify.OverPlayer("Your turn", new Color(255, 210, 90), true);     // big, over your character
                return;
            case "DIED": playing = false; state = null; LeftWorld(); Changed(); return;
            case "PARTY": party = w.Skip(1).ToArray(); Changed(false); return;
            case "SAID": Tell(w.Length > 1 ? w[1] : ""); return;
        }
        if (w[0] == "POPUP") { ShowPopup(w); return; }
        if (!TalkMessage(w) && !LootMessage(w) && !TradeMessage(w) && !ModMessage(null, w)) MirrorMessage(w);
    }

    void Tell(string line)
    {
        messages.Add(line);
        if (messages.Count > 60) messages.RemoveAt(0);
        Changed(false);
        Log.Info(line);
        Commands.Print("co-op: " + line);
        Notify.Log("Co-op: " + line, Say);
    }

    // --- Acting for a character (host side; also the local test partner) ----------------------------------------
    string Act(Character c, string[] w)
    {
        switch (w[0])
        {
            case "GO":
            {
                int x, y;
                if (w.Length < 3 || !int.TryParse(w[1], out x) || !int.TryParse(w[2], out y)) return "coop.go x y";
                bool ok = c.WalkTo(x, y);
                Log.Info(c.Name + ": go " + x + "," + y + ": " + (ok ? "walking" : "refused") + Points(c));
                return ok ? "walking to " + x + "," + y : "can't walk there now" + (Combat.InCombat && !c.IsTakingTurn ? " (not your turn)" : "");
            }
            case "ATTACK":
            {
                var name = string.Join(" ", w.Skip(1).ToArray()).Trim();
                // No name: the nearest character hostile to it.
                var target = name.Length == 0
                    ? World.CharactersNear(c.Position, 40).FirstOrDefault(t => t != c && !t.IsDead && t.AttitudeTo(c) == Attitude.Hostile)
                    : World.CharactersNear(c.Position, 40).FirstOrDefault(t => t != c && !t.IsPlayer && !t.IsDead && t.Name != null && t.Name.IndexOf(name, StringComparison.OrdinalIgnoreCase) >= 0);
                if (target == null) return name.Length == 0 ? "no enemy near you" : "nobody called " + name + " near you";
                bool ok = c.Attack(target);
                Log.Info(c.Name + ": attack " + target.Name + " (" + target.Health + " HP): " + (ok ? "attacking" : "refused") + Points(c));
                return ok ? "attacking " + target.Name : "can't attack now" + (Combat.InCombat && !c.IsTakingTurn ? " (not your turn)" : "");
            }
            case "FOLLOW":
            {
                var p = World.Player;
                if (p == null) return "no host character";
                var at = p.Position;
                var tile = World.FreeTilesNear((int)at.X, (int)at.Y, 3).OrderBy(t => Math.Abs(t.X - at.X) + Math.Abs(t.Y - at.Y)).FirstOrDefault(t => Math.Abs(t.X - at.X) + Math.Abs(t.Y - at.Y) >= 1);
                if (tile == Point.Zero) return "no room next to " + p.Name;
                return c.WalkTo(tile.X, tile.Y) ? "walking to " + p.Name : "can't walk there now" + (Combat.InCombat && !c.IsTakingTurn ? " (not your turn)" : "");
            }
            case "END":
            {
                bool ok = c.EndTurn();
                Log.Info(c.Name + ": end turn: " + (ok ? "ended" : "not its turn"));
                return ok ? "turn ended" : "not your turn";
            }
            case "ATTACKID":
            {
                var target = w.Length > 1 ? Resolve(w[1]) : null;
                if (target == null || target == c || target.IsDead) return "no such target";
                bool ok = c.Attack(target);
                Log.Info(c.Name + ": attack " + target.Name + ": " + (ok ? "attacking" : "refused") + Points(c));
                return ok ? "attacking " + target.Name : "can't attack now" + (Combat.InCombat && !c.IsTakingTurn ? " (not your turn)" : "");
            }
            case "USE":
            {
                var door = w.Length > 1 ? World.Doors.FirstOrDefault(d => d.EntityId == w[1]) : null;
                if (door != null && door.IsLocked && !door.IsOpen) return "that door is locked";
                bool ok = w.Length > 1 && c.UseObject(w[1]);
                // Not an object the host knows by that id (a corpse: each game makes its own when someone dies): the
                // container on that tile.
                int ux, uy;
                if (!ok && w.Length > 3 && int.TryParse(w[2], out ux) && int.TryParse(w[3], out uy))
                {
                    var box = World.Containers.Where(b => Math.Abs(b.Position.X - ux) <= 1.5f && Math.Abs(b.Position.Y - uy) <= 1.5f)
                        .OrderBy(b => Math.Abs(b.Position.X - ux) + Math.Abs(b.Position.Y - uy)).FirstOrDefault();
                    if (box != null) ok = c.UseObject(box.EntityId);
                }
                Log.Info(c.Name + ": use " + (w.Length > 1 ? w[1] : "?") + ": " + (ok ? "going" : "refused"));
                return ok ? "using it" : "can't use that now" + (Combat.InCombat && !c.IsTakingTurn ? " (not your turn)" : "");
            }
            case "EXIT":
            {
                int x, y;
                var away = travelTogether ? Party.Missing(c, TogetherTiles) : new List<Character>();
                if (away.Count > 0) return "everyone needs to be near you to go (" + string.Join(", ", away.Select(a => a.Name).ToArray()) + " isn't)";
                bool ok = w.Length > 2 && int.TryParse(w[1], out x) && int.TryParse(w[2], out y) && c.TakeExitAt(x, y);
                Log.Info(c.Name + ": exit " + (w.Length > 2 ? w[1] + "," + w[2] : "?") + ": " + (ok ? "going" : "refused"));
                if (ok) { Notify.Log("Co-op: " + c.Name + " is heading for the exit: everyone goes along", Say); foreach (var s in seats.Values.Concat(orphans)) Remember(s); }
                return ok ? "heading for the exit" : "can't take that exit now";
            }
            case "TAKE": return w.Length > 2 ? Take(c, w[1], w[2]) : "";
            case "TAKEALL": return w.Length > 1 ? Take(c, w[1], null) : "";
            case "DEAL": if (w.Length > 4) { var seat = seats.Values.FirstOrDefault(s => s.Character == c); if (seat != null) Deal(seat, w[1], w[2], w[3], w[4]); } return "";
            case "SHOPCLOSED": { var seat = seats.Values.FirstOrDefault(s => s.Character == c); if (seat != null) { shopSeen.Remove(seat); Log.Info(seat.Peer.Name + " closed the shop"); } return ""; }
            case "MYSHEET":
            {
                var seat = seats.Values.FirstOrDefault(s => s.Character == c);
                if (seat == null || w.Length < 2) return "";
                // Made from an older sheet than the host's (a change on the host crossed it): the host's stands.
                if (Stale(seat, w.Length > 2 ? w[2] : null)) { PushSheet(seat); return "your change crossed one made on the host: your character is as the host has it now"; }
                TakeSheet(seat, w[1]);
                return "";
            }
            case "TRADEWITH":
            {
                // A trade without a conversation (testing; a mod's own "trade" button): as the conversation's would.
                var seat = seats.Values.FirstOrDefault(s => s.Character == c); var m = w.Length > 1 ? Resolve(w[1]) : null;
                if (seat == null || m == null || !m.IsMerchant) return "not a merchant";
                shopKey = KeyOf(m); shopSign = StockSign(m.Stock);
                OpenShopFor(seat, m);
                return "";
            }
            case "ABILITY":
            {
                if (w.Length < 4) return "";
                object target = null;
                if (w[3].StartsWith("tile:")) { var xy = w[3].Substring(5).Split(','); int tx, ty; if (xy.Length == 2 && int.TryParse(xy[0], out tx) && int.TryParse(xy[1], out ty)) target = new Point(tx, ty); }
                else if (w[3] != "self") { target = Resolve(w[3]); if (target == null) return "no such target"; }
                bool ok = c.UseAbility(w[1], w[2].Length == 0 ? null : w[2], target);
                Log.Info(c.Name + ": ability " + w[1] + (w[2].Length > 0 ? " (item " + w[2] + ")" : "") + " at " + w[3] + ": " + (ok ? "used" : "refused") + Points(c));
                return ok ? "" : "can't do that now" + (Combat.InCombat && !c.IsTakingTurn ? " (not your turn)" : "");
            }
            case "SWAP": return c.SwapWeapons() ? "" : "can't swap weapons now";
            case "RELOAD": return c.Reload() ? "" : "can't reload now";
            case "REVIVE": return ReviveNear(c);
            case "TALK": return w.Length > 1 ? Talk(c, w[1]) : "talk to whom?";
            case "DLGSUGGEST": return w.Length > 1 ? Suggest(c, w[1]) : "";
            case "GIVE": case "GIVEMONEY": return GiveFrom(c, w);
            case "STATUS":
            {
                var cur = Combat.Current;
                var s = c.Name + " at " + (int)Math.Round(c.Position.X) + "," + (int)Math.Round(c.Position.Y) + ", " + c.Health + " HP"
                    + (Combat.InCombat ? ", combat: " + (cur == null ? "?" : cur.Name + "'s turn") + Points(c) : ", no combat");
                Log.Info(s);
                return s;
            }
        }
        return "unknown command " + w[0];
    }

    static string Points(Character c) { return Combat.InCombat ? " (AP " + Combat.ActionPoints(c) + ", MP " + Combat.MovementPoints(c) + ")" : ""; }

    // --- Local test partner (step 1) ----------------------------------------------------------------------------
    Character partner;
    bool Missing() { if (partner != null && (partner.IsDead || partner.Area == null)) partner = null; return partner == null; }

    // A command for "my" character: sent to the host when joined, else the local partner.
    string Mine(params string[] w)
    {
        if (host != null && !host.Closed)
        {
            if (!playing) return "pick a character first (coop.pick / coop.new)";
            host.Send(w);
            return null;
        }
        if (Missing()) return "no partner (coop.spawn) and not joined (coop.join)";
        return Act(partner, w);
    }

    public override void Load()
    {
        Commands.Add(this, "coop.host", "coop.host: (hosting starts on the main menu: coop.hostnew / coop.hostsave)", a => "players join before the game starts: on the main menu, Host New Game or Host Saved Game (coop.hostnew <players> / coop.hostsave <save>)");
        LoadLobby();
        LoadMatch();
        LoadExpansions();
        LoadGive();
        LoadXp();
        LoadHost();
        LoadListening();
        LoadScreens();
        LoadDeath();
        LoadUi();
        LoadMirror();
        LoadLoot();
        LoadTrade();
        Commands.Add(this, "coop.stop", "coop.stop: stop hosting (joined characters stay, back to their AI)", a => { if (listener == null) return "not hosting"; StopHosting(); return "stopped"; });
        Commands.Add(this, "coop.who", "coop.who: who is connected and what they play", a =>
        {
            if (listener == null) return "not hosting";
            var s = "hosting on " + listener.Port + "; " + (seats.Count == 0 ? "nobody connected" : string.Join(", ", seats.Values.Select(x => x.Peer.Name + (x.Character != null ? " as " + x.Character.Name : " (choosing)")).ToArray()))
                + (orphans.Count > 0 ? "; left behind: " + string.Join(", ", orphans.Select(o => o.Record.Name).ToArray()) : "");
            Log.Info(s); return s;
        });
        Commands.Add(this, "coop.join", "coop.join <address[:port]> [your name] | coop.join online:<lobby> [your name] | coop.join friend [friend's name] [your name]: join a co-op game (by address, or a friend's Steam lobby)", Join);
        Commands.Add(this, "coop.friends", "coop.friends: Steam friends hosting a co-op game (join with coop.join online:<lobby>)", a =>
        {
            if (!Online.Available) return "not signed in to Steam";
            var f = Online.FriendsHosting();
            var r = f.Count == 0 ? "no friends hosting on " + Online.Service : string.Join("; ", f.Select(x => x.Name + ": online:" + x.Lobby).ToArray());
            Log.Info("friends: " + r); return r;
        });
        Commands.Add(this, "coop.online", "coop.online: whether Steam play-together works here, and the lobby", a =>
        {
            var r = !Online.Available ? "not signed in to Steam" : Online.Service + " as " + Online.MyName + (Online.Lobby == null ? ", no lobby" : (Online.IsHosting ? ", hosting lobby " : ", in lobby ") + Online.Lobby);
            Log.Info("online: " + r); return r;
        });
        LoadOnline();
        Commands.Add(this, "coop.leave", "coop.leave: leave the co-op game", a => { if (host == null) return "not joined"; Leave(); return "left"; });
        Commands.Add(this, "coop.follow", "coop.follow: your character walks next to the host's", a => Mine("FOLLOW"));
        Commands.Add(this, "coop.pick", "coop.pick <number>: play that character from the host's list", a => { if (host == null) return "not joined"; host.Send("PICK", a.Length > 0 ? a[0] : ""); return null; });
        Commands.Add(this, "coop.new", "coop.new <name>: a new character in the host's game", a => { if (host == null) return "not joined"; host.Send("NEW", string.Join(" ", a)); return null; });
        Commands.Add(this, "coop.go", "coop.go x y: your character walks to tile x, y", a => Mine(new[] { "GO" }.Concat(a).ToArray()));
        Commands.Add(this, "coop.attack", "coop.attack <name>: your character attacks the nearest one of that name", a => Mine(new[] { "ATTACK" }.Concat(a).ToArray()));
        Commands.Add(this, "coop.end", "coop.end: ends your character's combat turn", a => Mine("END"));
        Commands.Add(this, "coop.door", "coop.door <id start>: your character uses that door (wt.doors lists them)", a => { var d = a.Length == 0 ? null : World.Doors.FirstOrDefault(x => x.EntityId.StartsWith(a[0])); return d == null ? "no such door" : Mine("USE", d.EntityId); });
        // No importing in co-op (host or joiner): characters come from creation only, never from another game or another
        // mod setup (its items and stats might not exist here).
        GameEvents.Update.Add(this, dt => World.CreationImport = listener == null && host == null);
        // Tests: open a window by name (layouts are checked by Works: "ui: window ... fits" in the log).
        Commands.Add(this, "coop.ui", "coop.ui <coop|join|hostsave|page|create|hostcreate|hostnew|editrules|give|loot|talk|close>: open a Co-op window (tests)", a2 =>
        {
            switch (a2.Length == 0 ? "" : a2[0])
            {
                case "coop": Build(); return "opened";
                case "join": AskJoin(); return "opened";
                case "hostsave": PickSave(); return "opened";
                case "page": if (coopPage != null) coopPage.Open(); return "opened";
                case "create": CreateCharacter(); return "creating";
                case "hostcreate": CreateHostCharacter(); return "creating";
                case "hostnew": HostFromMenu("new", null); return "hosting";
                case "editrules": EditRules(); return "rules";
                case "give": OpenGive(); return "opened";
                case "loot": if (lootName == null) lootName = "Test crate"; ShowLoot(); return "opened";
                case "talk": ShowTalk(); return "opened";
                case "close":
                    foreach (var w2 in new[] { window, joinWindow, saveWindow, giveWindow, lootWindow, talkWindow }) if (w2 != null && w2.IsOpen) w2.Close();
                    return "closed";
            }
            return "coop.ui <coop|join|hostsave|page|create|hostcreate|hostnew|editrules|give|loot|talk|close>";
        });
        Commands.Add(this, "coop.suggest", "coop.suggest <answer number>: in the host's conversation, suggest that answer (again: take it back)", a =>
        {
            int n; if (a.Length < 1 || !int.TryParse(a[0], out n) || n < 1 || n > talkChoices.Count) return "coop.suggest <answer number, 1-" + talkChoices.Count + ">";
            SuggestAnswer(talkChoices[n - 1].Key);
            return "suggested " + n;
        });
        Commands.Add(this, "coop.talk", "coop.talk <name>: your character talks to that character (nearest of that name)", a => { var t = a.Length == 0 || World.Player == null ? null : World.CharactersNear(World.Player.Position, 80).FirstOrDefault(x => x.Name != null && x.Name.IndexOf(a[0], StringComparison.OrdinalIgnoreCase) >= 0); return t == null ? "nobody called that" : Mine("TALK", KeyHere(t)); });
        Commands.Add(this, "coop.lootnear", "coop.lootnear: open the nearest container here (as a click on it)", a => { var me = World.Player; var b = me == null ? null : World.Containers.OrderBy(x => Math.Abs(x.Position.X - me.Position.X) + Math.Abs(x.Position.Y - me.Position.Y)).FirstOrDefault(); if (b == null) return "none"; host.Send("USE", b.EntityId, (int)Math.Round(b.Position.X), (int)Math.Round(b.Position.Y)); return "opening " + b.Name; });
        Commands.Add(this, "coop.box", "coop.box <id start>: your character opens that container", a => { var b = a.Length == 0 ? null : World.Containers.FirstOrDefault(x => x.EntityId.StartsWith(a[0])); return b == null ? "no such container" : Mine("USE", b.EntityId); });
        Commands.Add(this, "coop.threats", "coop.threats: for each joined character, the enemies in the fight and who has noticed whom", a =>
        {
            var lines = seats.Values.Concat(orphans).Where(x => x.Character != null).Select(x => x.Character.Name + " at " + (int)x.Character.Position.X + "," + (int)x.Character.Position.Y + (x.Character.IsInCombat ? " (in the fight)" : "") + ": " + string.Join("; ", World.CharactersNear(x.Character.Position, 60).Where(e => e != x.Character && !e.IsDead && !e.Passive && e.AttitudeTo(x.Character) == Attitude.Hostile).Select(e => e.Name + " " + (int)Vector2.Distance(new Vector2(e.Position.X, e.Position.Y), new Vector2(x.Character.Position.X, x.Character.Position.Y)) + " tiles" + ", its sight " + e.SightRange + (Sees(e, x.Character) ? ", sees it" : "") + (Sees(x.Character, e) ? ", seen by it" : "") + (e.HasNoticed(x.Character) ? " (AI knows of it)" : "")).ToArray())).ToArray();
            var t = string.Join(" | ", lines); Log.Info("threats: " + t); return t;
        });
        Commands.Add(this, "coop.trade", "coop.trade <name>: trade with that merchant (testing)", a => { var t = a.Length == 0 || World.Player == null ? null : World.CharactersNear(World.Player.Position, 200).FirstOrDefault(x => x.Name != null && x.Name.IndexOf(a[0], StringComparison.OrdinalIgnoreCase) >= 0 && x.IsMerchant); return t == null ? "no merchant called that" : Mine("TRADEWITH", KeyHere(t)); });
        Commands.Add(this, "coop.exit", "coop.exit x y: your character takes the exit on that tile", a => a.Length < 2 ? "coop.exit x y" : Mine("EXIT", a[0], a[1]));
        Commands.Add(this, "coop.status", "coop.status: your character's state", a => Mine("STATUS"));
        Commands.Add(this, "coop.spawn", "coop.spawn [blueprint]: a local test partner next to you (no network)", a =>
        {
            var c = SpawnFor(new Record { Name = "Partner", Blueprint = a.Length > 0 ? a[0] : DefaultBlueprint }, false);
            if (c == null) return "couldn't spawn";
            partner = c; CharacterControl.Take(this, c);
            return "partner at " + (int)c.Position.X + "," + (int)c.Position.Y;
        });
        Commands.Add(this, "coop.attitudes", "coop.attitudes: how the characters around stand towards your character", a =>
        {
            var c = partner ?? seats.Values.Select(s => s.Character).FirstOrDefault(x => x != null);
            if (c == null) return "no character";
            var s2 = string.Join("; ", World.CharactersNear(c.Position, 30).Where(x => x != c).Select(x => x.Name + " -> " + c.Name + " " + x.AttitudeTo(c) + ", back " + c.AttitudeTo(x)).ToArray());
            Log.Info("attitudes: " + s2); return s2;
        });

        GameEvents.Update.Add(this, dt =>
        {
            foreach (var m in inbox.Take())
            {
                try { if (m.Key == host) Simulation.Apply(() => JoinerMessage(m.Value)); else if (listener != null) HostMessage(m.Key, m.Value); }
                catch (Exception ex) { Log.Error("message " + m.Value[0] + ": " + ex); }
            }
        });
        // Everyone's health (the host's character and each joined one), for the players' Co-op windows.
        double partyTimer = 0; string lastParty = null;
        GameEvents.Update.Add(this, dt =>
        {
            if (listener == null || (partyTimer -= dt) > 0) return;
            partyTimer = 1;
            var list = new List<object> { "PARTY" };
            foreach (var c in Party.Members.Concat(seats.Values.Concat(orphans).Where(s => s.Character != null && s.Character.IsDead).Select(s => s.Character)).Distinct())
            { list.Add(c.Name); list.Add((int)c.Health); list.Add((int)c.MaxHealth); list.Add(c.IsDead ? 1 : 0); }
            var key = string.Join("|", list.Select(o => o.ToString()).ToArray());
            if (key == lastParty) return;
            lastParty = key;
            foreach (var s in seats.Values.Where(x => x.Peer != null)) s.Peer.Send(list.ToArray());
        });
        // Each joined player's state, sent when it changes (checked twice a second).
        double stateTimer = 0;
        GameEvents.Update.Add(this, dt =>
        {
            if (listener == null || (stateTimer -= dt) > 0) return;
            stateTimer = 0.5;
            var cur = Combat.Current;
            foreach (var seat in seats.Values.Where(x => x.Character != null))
            {
                var c = seat.Character;
                var state = new object[] { "STATE", c.Name, (int)c.Health, (int)c.MaxHealth, Combat.InCombat ? "1" : "0", cur == null ? "" : cur.Name, Combat.ActionPoints(c), Combat.MovementPoints(c), (int)Math.Round(c.Position.X), (int)Math.Round(c.Position.Y), c.IsDead ? "1" : "0" };
                var key = string.Join("|", state.Select(o => Convert.ToString(o)).ToArray());
                if (key == seat.LastState) continue;
                seat.LastState = key;
                seat.Peer.Send(state);
            }
        });
        // A joined player's turn: told to them. A character whose player left: its turn passes at once.
        Combat.TurnStarted.Add(this, c =>
        {
            if (c == null) return;
            if (partner != null && c == partner) Log.Info("partner's turn" + Points(c));
            var seat = seats.Values.FirstOrDefault(s => s.Character == c);
            if (seat != null && downed.Contains(c)) { }     // down: its turn passes (CoopDeath), no "your turn"
            else if (seat != null) { seat.Peer.Send("TURN", Combat.ActionPoints(c), Combat.MovementPoints(c)); Log.Info(c.Name + "'s turn (" + seat.Peer.Name + ")" + Points(c)); }
            else if (orphans.Any(o => o.Character == c)) { c.EndTurn(); Log.Info(c.Name + "'s turn passed (no player)"); }
        });
        // In a fight the whole zone is turn-based and every character is in the turn order (the game's rule): joined
        // characters take their turns like the host's, wherever they are in the zone.
        Combat.AttackResolved.Add(this, o =>
        {
            foreach (var s in seats.Values.Where(s => s.Character != null))
            {
                if (o.Target == s.Character) s.Peer.Send("SAID", o.AttackerName + (o.Hit ? " hit you for " + o.HealthLost : " missed you") + (downed.Contains(s.Character) ? " - you're down" : o.Killed ? " - you're dead" : ""));
                else if (o.AttackerName == s.Character.Name && o.Target != null) s.Peer.Send("SAID", "you " + (o.Hit ? "hit " + o.Target.Name + " for " + o.HealthLost : "missed " + o.Target.Name) + (o.Killed ? ", killed" : ""));
            }
        });
        GameEvents.Saving.Add(this, name => { if (listener != null || records.Count > 0) WriteRecords(); });
        // Leaving the zone: each joined character's sheet as it is now (health, items...), as it's built again from it there.
        GameEvents.ZoneLeaving.Add(this, e => { if (listener != null && !e.Cancel) foreach (var s in seats.Values.Concat(orphans)) Remember(s); });
        // Joined characters follow the host's character into other zones (spawned characters stay behind).
        GameEvents.ZoneLoaded.Add(this, (zone, first) =>
        {
            if (listener == null) return;
            bool fromSave = reloaded; reloaded = false;
            foreach (var s in seats.Values.Where(s => s.Record != null).ToList())
            {
                if (s.Character != null && s.Character.Area != null) continue;
                // After a load: the character as that save has it (its place, its sheet), if the save knew it.
                bool saved = false;
                if (fromSave)
                {
                    var r = records.FirstOrDefault(x => x.Name.Equals(s.Record.Name, StringComparison.OrdinalIgnoreCase));
                    if (r != null) { s.Record = r; saved = true; } else records.Add(s.Record);
                }
                var c = SpawnFor(s.Record, saved);
                if (c == null) { Log.Warn("couldn't place " + s.Record.Name + " for " + s.Peer.Name); continue; }
                s.Character = c; CharacterControl.Take(this, c); Party.Add(this, c);
                s.LastSign = SignOf(c);
                Log.Info(s.Peer.Name + " plays " + s.Record.Name + " at " + (int)c.Position.X + "," + (int)c.Position.Y);
                s.Peer.Send("PLAYING", s.Record.Name, (int)c.Position.X, (int)c.Position.Y);
                if (!s.Announced) { s.Announced = true; Notify.Log("Co-op: " + s.Peer.Name + " plays " + s.Record.Name, Say); if (Api != null) Api.RaiseJoined(s.Peer.Name, c); }
            }
            // The joiners follow into the new zone once it has settled (Works' ZoneSettled: its first game update after
            // the arrival, when what the zone starts on entry has started): a save before that stopped a new game's
            // intro from ever opening (measured: a save at any step of the arrival, or 1 frame after, no intro; after
            // the first game update, the intro every time). If the zone never says so: after 10 s.
            worldSendAt = DateTime.MaxValue; settleBy = DateTime.UtcNow.AddSeconds(10);
            // Meanwhile only the host's character sets off the map's triggers: the zone's own start (a new game's intro)
            // is the player's, not a joined character's placed beside it (seen: Bobby taking the intro's trigger).
            Party.TriggersCountMembers = false; triggersBack = true;
            foreach (var s in seats.Values.Where(s => s.Character != null)) { s.WorldPending = true; s.WorldWaiting = true; }
            orphans.RemoveAll(o => o.Character == null || o.Character.Area == null);
        });
        // The host loaded a save (or started a new game) while hosting: everyone stays connected; each one's character
        // from that save is placed where it was saved (in this zone), else next to the host, when the zone is there.
        GameEvents.GameStarted.Add(this, () =>
        {
            ReadRecords();
            if (listener == null) return;
            if (!SameGame())
            {
                Log.Info("another game was loaded while hosting (not these players' characters): the session ends");
                foreach (var s in seats.Values) s.Peer.Send("BYE", "the host loaded another game");
                Notify.Log("Co-op: you loaded a game without these players' characters: the co-op session ended", Say);
                StopHosting();
                return;
            }
            // A loaded game plays with the settings it was saved with (a new game: the lobby's).
            var savedOptions = SaveData.Of(this).Get("options");
            if (savedOptions != null) RestoreOptions(savedOptions);
            reloaded = true; downed.Clear(); ending = false;
            if (talking != null) talking = null;
            foreach (var s in seats.Values) { s.Character = null; s.InWorld = false; s.Sent.Clear(); s.Walks.Clear(); s.Doors.Clear(); s.LastSign = null; s.Peer.Send("SAID", (World.Player == null ? "the host" : World.Player.Name) + " loaded a game: you'll be back in a moment"); }
            orphans.Clear();
            Log.Info("a game was loaded while hosting: players placed when its zone is there");
        });
    }

    public override void Unload()
    {
        if (PlayerScreens.Provider == this) PlayerScreens.Provider = null;
        StopHosting();
        if (host != null) { host.Close(); host = null; }
    }
}
