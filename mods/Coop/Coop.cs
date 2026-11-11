// Co-op: an UnderRailWorks mod (in development; design and open questions: docs/Multiplayer.md in the Works repo).
// The host's game is the only real one. A joiner connects, gets the host's character list (the host's own character
// first, not choosable, then the co-op characters saved with this game), picks one or makes a new one, and that
// character appears in the host's game (where it left off in this zone, else next to the host's character),
// controlled by the joiner's commands instead of an AI (Works' CharacterControl). The joiner's own screen doesn't show
// the host's world yet (step 3).
// The Co-op button on the HUD opens the Co-op window (CoopUi.cs) for all of this; the console commands do the same.
// Host (console, ` key):   coop.host [port]   coop.who   coop.stop
// Joiner:                  coop.join <address[:port]> [your name]   then coop.pick <number> or coop.new <name>
// Playing (joiner):        coop.go x y   coop.attack <name>   coop.end   coop.status   coop.leave
// Without a connection the same commands drive a local test partner (coop.spawn), as in step 1.
// Co-op characters are kept in the mod's save data next to the save (never in the game's save): name, blueprint,
// zone and tile. They follow the host's character to other zones (spawned again next to it).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("0.6.0")]

public sealed partial class Coop : WorksMod
{
    const int DefaultPort = 27650;
    const float SightRange = 14;     // tiles: an enemy in a fight this close brings a joined character into it
    const string DefaultBlueprint = "creatures\\generichumans\\jkkgunslinger";
    static readonly Color Say = new Color(120, 200, 255);

    // --- Saved co-op characters ---------------------------------------------------------------------------------
    sealed class Record { public string Name, Blueprint, Zone, Sheet; public int X, Y; }     // Sheet: the character's .char (base64), made in the game's creation
    List<Record> records = new List<Record>();

    void ReadRecords()
    {
        records = new List<Record>();
        var list = SaveData.Of(this).Get("characters") as List<object>;
        if (list == null) return;
        foreach (var o in list.OfType<Dictionary<string, object>>())
        {
            Func<string, string> s = k => { object v; return o.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null; };
            Func<string, int> n = k => { double d; return double.TryParse(s(k), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? (int)d : 0; };
            records.Add(new Record { Name = s("name"), Blueprint = s("blueprint") ?? DefaultBlueprint, Zone = s("zone"), X = n("x"), Y = n("y"), Sheet = s("sheet") });
        }
    }

    void WriteRecords()
    {
        foreach (var seat in seats.Values) Remember(seat);
        foreach (var o in orphans) Remember(o);
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
        catch (Exception ex) { Log.Warn("can't export " + c.Name + ": " + ex.Message); return null; }
    }
    string SheetPath(string name) { return System.IO.Path.Combine(Info.Folder, "sheets", new string(name.Where(char.IsLetterOrDigit).ToArray()) + ".char"); }
    string WriteSheet(string name, string base64) { var path = SheetPath(name); System.IO.Directory.CreateDirectory(System.IO.Path.GetDirectoryName(path)); System.IO.File.WriteAllBytes(path, Convert.FromBase64String(base64)); return path; }

    // --- Host ---------------------------------------------------------------------------------------------------
    sealed class Seat { public Peer Peer; public Record Record; public Character Character; public string LastState; public string LastSign; public bool WorldPending, InWorld; public readonly Dictionary<string, string> Sent = new Dictionary<string, string>(), Walks = new Dictionary<string, string>(), Doors = new Dictionary<string, string>(); }
    Listener listener;
    readonly HashSet<Character> engaged = new HashSet<Character>();     // attacked or attacking in this fight: stays in it
    readonly Inbox inbox = new Inbox();
    readonly Dictionary<Peer, Seat> seats = new Dictionary<Peer, Seat>();
    readonly List<Seat> orphans = new List<Seat>();     // joiners who left: their character stays, passing its turns

    string Host(string[] args)
    {
        if (World.Player == null) return "load a game first";
        if (listener != null) return "already hosting on port " + listener.Port;
        int port = DefaultPort;
        if (args.Length > 0 && !int.TryParse(args[0], out port)) return "coop.host [port]";
        try { listener = new Listener(port, tcp => new Peer(tcp, inbox.Add, p => inbox.Add(p, new[] { "_closed" }))); }
        catch (Exception ex) { listener = null; return "can't host on port " + port + ": " + ex.Message; }
        ReadRecords();
        Log.Info("hosting on port " + port);
        Changed();
        Notify.Log("Co-op: hosting on port " + port + ". Others join with coop.join <your address>" + (port == DefaultPort ? "" : ":" + port), Say);
        return "hosting on port " + port;
    }

    void StopHosting()
    {
        if (listener == null) return;
        WriteRecords();
        foreach (var p in seats.Keys.ToList()) { p.Send("BYE", "the host stopped"); p.Close(); }
        foreach (var s in seats.Values.Concat(orphans)) if (s.Character != null) CharacterControl.Release(s.Character);
        seats.Clear(); orphans.Clear();
        listener.Stop(); listener = null;
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
                seats.Remove(p);
                if (seat.Character != null) { Remember(seat); seat.Peer = null; orphans.Add(seat); }
                Log.Info(p.Name + " left" + (seat.Character != null ? " (" + seat.Character.Name + " stays, passing its turns)" : ""));
                Notify.Log("Co-op: " + p.Name + " left", Say);
                Changed(false);
                return;
            case "HELLO":
                p.Name = w.Length > 2 && w[2].Trim().Length > 0 ? w[2].Trim() : "Player " + p.Id;
                if (w.Length < 2 || w[1] != Info.VersionText) { p.Send("BYE", "the host has Co-op " + Info.VersionText + ", you have " + (w.Length > 1 ? w[1] : "?")); p.Close(); return; }
                seats[p] = new Seat { Peer = p };
                Log.Info(p.Name + " connected from " + p.Address);
                Changed(false);
                Notify.Log("Co-op: " + p.Name + " is choosing a character", Say);
                SendList(p);
                return;
            case "BYE": p.Close(); return;
        }
        if (seat == null) { p.Send("BYE", "say HELLO first"); p.Close(); return; }
        if (w[0] == "LOADED") { HostMirrorMessage(seat, w); return; }
        switch (w[0])
        {
            case "PICK":
            {
                int i;
                if (seat.Character != null) { p.Send("SAID", "you're already playing " + seat.Character.Name); return; }
                if (w.Length < 2 || !int.TryParse(w[1], out i)) { p.Send("SAID", "pick a number from the list"); return; }
                if (i == 0) { p.Send("SAID", "that's the host's character"); SendList(p); return; }
                if (i < 1 || i > records.Count) { p.Send("SAID", "no character " + i); SendList(p); return; }
                var r = records[i - 1];
                if (Taken(r)) { p.Send("SAID", r.Name + " is taken"); SendList(p); return; }
                Enter(seat, r, true);
                return;
            }
            case "NEW":
            {
                var name = w.Length > 1 ? w[1].Trim() : "";
                if (seat.Character != null) { p.Send("SAID", "you're already playing " + seat.Character.Name); return; }
                if (name.Length == 0) { p.Send("SAID", "coop.new <name>"); return; }
                Func<string, bool> used = n => records.Any(r => r.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) || (World.Player != null && n.Equals(World.Player.Name, StringComparison.OrdinalIgnoreCase));
                if (used(name))
                {
                    // A character made elsewhere (an import keeps its name): numbered rather than refused.
                    if (w.Length < 3) { p.Send("SAID", "there is already a " + name); return; }
                    int n = 2; while (used(name + " " + n)) n++;
                    p.Send("SAID", "there's already a " + name + " here: yours is " + name + " " + n);
                    name = name + " " + n;
                }
                var rec = new Record { Name = name, Blueprint = DefaultBlueprint, Sheet = w.Length > 2 && w[2].Length > 0 ? w[2] : null };
                records.Add(rec);
                Enter(seat, rec, false);
                return;
            }
        }
        if (seat.Character == null || seat.Character.IsDead || seat.Character.Area == null) { p.Send("SAID", seat.Character != null && seat.Character.IsDead ? "your character is dead" : "pick a character first"); return; }
        var said = Act(seat.Character, w);
        if (!string.IsNullOrEmpty(said)) p.Send("SAID", said);
    }

    bool Taken(Record r) { return seats.Values.Any(s => s.Record == r); }

    void SendList(Peer p)
    {
        p.Send("LIST", World.Player == null ? "?" : World.Player.Name);
        for (int i = 0; i < records.Count; i++) p.Send("CHAR", i + 1, records[i].Name, Taken(records[i]) ? "taken" : "free");
        p.Send("LISTEND");
    }

    // The joiner's character enters the host's world: where it left (this zone, a free floor tile), else beside the host.
    void Enter(Seat seat, Record r, bool saved)
    {
        // A character left behind by its last player: taken over as it stands.
        var old = orphans.FirstOrDefault(o => o.Record == r);
        if (old != null) orphans.Remove(old);
        if (old != null && old.Character != null && !old.Character.IsDead && old.Character.Area != null) { seat.Record = r; seat.Character = old.Character; }
        else
        {
            var c = SpawnFor(r, saved);
            if (c == null) { seat.Peer.Send("SAID", "couldn't place " + r.Name + " here (no room?)"); return; }
            seat.Record = r; seat.Character = c;
        }
        CharacterControl.Take(this, seat.Character);
        Party.Add(this, seat.Character);     // other mods' "everyone" (Works' Party) includes it
        var at = seat.Character.Position;
        Log.Info(seat.Peer.Name + " plays " + r.Name + " at " + (int)at.X + "," + (int)at.Y);
        Notify.Log("Co-op: " + seat.Peer.Name + " plays " + r.Name, Say);
        Changed(false);
        seat.Peer.Send("PLAYING", r.Name, (int)at.X, (int)at.Y);
        SendWorld(seat);
    }

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
    readonly List<KeyValuePair<string, bool>> choices = new List<KeyValuePair<string, bool>>();     // name, taken
    readonly List<string> messages = new List<string>();
    string[] state;     // the host's last STATE for our character

    string Join(string[] args)
    {
        if (listener != null) return "you're hosting";
        if (host != null && !host.Closed) return "already connected (coop.leave first)";
        if (args.Length < 1) return "coop.join <address[:port]> [your name]";
        var parts = args[0].Split(':');
        int port = DefaultPort;
        if (parts.Length > 1 && !int.TryParse(parts[1], out port)) return "coop.join <address[:port]> [your name]";
        string name = args.Length > 1 ? string.Join(" ", args.Skip(1).ToArray()) : (World.Player != null ? World.Player.Name : Environment.UserName);
        try
        {
            var tcp = new System.Net.Sockets.TcpClient();
            tcp.Connect(parts[0], port);
            host = new Peer(tcp, inbox.Add, p => inbox.Add(p, new[] { "_closed" }));
        }
        catch (Exception ex) { host = null; return "can't reach " + args[0] + ": " + ex.Message; }
        playing = false; state = null; choices.Clear();
        host.Send("HELLO", Info.VersionText, name);
        Log.Info("connected to " + args[0] + " as " + name);
        return "connected to " + args[0] + ", waiting for the character list";
    }

    void JoinerMessage(string[] w)
    {
        switch (w[0])
        {
            case "_closed": Tell("disconnected from the host"); host = null; playing = false; state = null; LeftWorld(); Changed(); return;
            case "BYE": Tell("the host closed the connection: " + (w.Length > 1 ? w[1] : "")); return;
            case "LIST": hostName = w.Length > 1 ? w[1] : "?"; choices.Clear(); return;
            case "CHAR": choices.Add(new KeyValuePair<string, bool>(w[2], w.Length > 3 && w[3] == "taken")); return;
            case "LISTEND":
                Log.Info("characters: " + hostName + " (host), " + string.Join(", ", choices.Select((c, i) => (i + 1) + ". " + c.Key + (c.Value ? " (taken)" : "")).ToArray()));
                Commands.Print("co-op: characters: " + hostName + " (host), " + string.Join(", ", choices.Select((c, i) => (i + 1) + ". " + c.Key + (c.Value ? " (taken)" : "")).ToArray()) + ". coop.pick <number> or coop.new <name>");
                Changed(); return;
            case "PLAYING": playing = true; Tell("You play " + w[1] + " (at " + w[2] + "," + w[3] + ")"); Changed(); return;
            case "STATE": state = w; Changed(false); return;
            case "TURN": Tell("Your turn (AP " + w[1] + ", MP " + w[2] + ")"); return;
            case "SAID": Tell(w.Length > 1 ? w[1] : ""); return;
        }
        if (!TalkMessage(w) && !LootMessage(w)) MirrorMessage(w);
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
                Log.Info(c.Name + ": use " + (w.Length > 1 ? w[1] : "?") + ": " + (ok ? "going" : "refused"));
                return ok ? "using it" : "can't use that now" + (Combat.InCombat && !c.IsTakingTurn ? " (not your turn)" : "");
            }
            case "EXIT":
            {
                int x, y;
                bool ok = w.Length > 2 && int.TryParse(w[1], out x) && int.TryParse(w[2], out y) && c.TakeExitAt(x, y);
                Log.Info(c.Name + ": exit " + (w.Length > 2 ? w[1] + "," + w[2] : "?") + ": " + (ok ? "going" : "refused"));
                if (ok) Notify.Log("Co-op: " + c.Name + " is heading for the exit: everyone goes along", Say);
                return ok ? "heading for the exit (everyone goes along)" : "can't take that exit now";
            }
            case "TAKE": return w.Length > 2 ? Take(c, w[1], w[2]) : "";
            case "TAKEALL": return w.Length > 1 ? Take(c, w[1], null) : "";
            case "TALK": return w.Length > 1 ? Talk(c, w[1]) : "talk to whom?";
            case "DLGPICK": return w.Length > 1 ? Pick(c, w[1]) : "";
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
        Commands.Add(this, "coop.host", "coop.host [port]: let others join this game", Host);
        LoadUi();
        LoadMirror();
        LoadLoot();
        Commands.Add(this, "coop.stop", "coop.stop: stop hosting (joined characters stay, back to their AI)", a => { if (listener == null) return "not hosting"; StopHosting(); return "stopped"; });
        Commands.Add(this, "coop.who", "coop.who: who is connected and what they play", a =>
        {
            if (listener == null) return "not hosting";
            var s = "hosting on " + listener.Port + "; " + (seats.Count == 0 ? "nobody connected" : string.Join(", ", seats.Values.Select(x => x.Peer.Name + (x.Character != null ? " as " + x.Character.Name : " (choosing)")).ToArray()))
                + (orphans.Count > 0 ? "; left behind: " + string.Join(", ", orphans.Select(o => o.Record.Name).ToArray()) : "");
            Log.Info(s); return s;
        });
        Commands.Add(this, "coop.join", "coop.join <address[:port]> [your name]: join a co-op game", Join);
        Commands.Add(this, "coop.leave", "coop.leave: leave the co-op game", a => { if (host == null) return "not joined"; Leave(); return "left"; });
        Commands.Add(this, "coop.follow", "coop.follow: your character walks next to the host's", a => Mine("FOLLOW"));
        Commands.Add(this, "coop.pick", "coop.pick <number>: play that character from the host's list", a => { if (host == null) return "not joined"; host.Send("PICK", a.Length > 0 ? a[0] : ""); return null; });
        Commands.Add(this, "coop.new", "coop.new <name>: a new character in the host's game", a => { if (host == null) return "not joined"; host.Send("NEW", string.Join(" ", a)); return null; });
        Commands.Add(this, "coop.go", "coop.go x y: your character walks to tile x, y", a => Mine(new[] { "GO" }.Concat(a).ToArray()));
        Commands.Add(this, "coop.attack", "coop.attack <name>: your character attacks the nearest one of that name", a => Mine(new[] { "ATTACK" }.Concat(a).ToArray()));
        Commands.Add(this, "coop.end", "coop.end: ends your character's combat turn", a => Mine("END"));
        Commands.Add(this, "coop.door", "coop.door <id start>: your character uses that door (wt.doors lists them)", a => { var d = a.Length == 0 ? null : World.Doors.FirstOrDefault(x => x.EntityId.StartsWith(a[0])); return d == null ? "no such door" : Mine("USE", d.EntityId); });
        Commands.Add(this, "coop.newfrom", "coop.newfrom <file.char> <name>: join with a character exported earlier (as finishing creation does)", a => { if (host == null || a.Length < 2) return "coop.newfrom <file.char> <name>"; host.Send("NEW", a[1], Convert.ToBase64String(System.IO.File.ReadAllBytes(a[0]))); return "sent"; });
        Commands.Add(this, "coop.talk", "coop.talk <name>: your character talks to that character (nearest of that name)", a => { var t = a.Length == 0 || World.Player == null ? null : World.CharactersNear(World.Player.Position, 80).FirstOrDefault(x => x.Name != null && x.Name.IndexOf(a[0], StringComparison.OrdinalIgnoreCase) >= 0); return t == null ? "nobody called that" : Mine("TALK", t.EntityId); });
        Commands.Add(this, "coop.box", "coop.box <id start>: your character opens that container", a => { var b = a.Length == 0 ? null : World.Containers.FirstOrDefault(x => x.EntityId.StartsWith(a[0])); return b == null ? "no such container" : Mine("USE", b.EntityId); });
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
                try { if (m.Key == host) JoinerMessage(m.Value); else if (listener != null) HostMessage(m.Key, m.Value); }
                catch (Exception ex) { Log.Error("message " + m.Value[0] + ": " + ex); }
            }
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
            if (seat != null) { seat.Peer.Send("TURN", Combat.ActionPoints(c), Combat.MovementPoints(c)); Log.Info(c.Name + "'s turn (" + seat.Peer.Name + ")" + Points(c)); }
            else if (orphans.Any(o => o.Character == c)) { c.EndTurn(); Log.Info(c.Name + "'s turn passed (no player)"); }
        });
        // Joined characters are only in a fight as anyone would be: when they attack or are attacked, or an enemy in the
        // fight is within sight of them (Works' Combat.Join; their AI, which would notice, is held). Otherwise they walk
        // and act in real time while the others fight.
        double fightTimer = 0;
        GameEvents.CombatEnded.Add(this, () => engaged.Clear());
        GameEvents.Update.Add(this, dt =>
        {
            if (listener == null || !Combat.InCombat || (fightTimer -= dt) > 0) return;
            fightTimer = 0.5;
            foreach (var s in seats.Values.Concat(orphans).Where(x => x.Character != null && !x.Character.IsDead && x.Character.Area != null).ToList())
            {
                var c = s.Character;
                var foe = World.CharactersNear(c.Position, SightRange).FirstOrDefault(e => e != c && e.IsInCombat && e.AttitudeTo(c) == Attitude.Hostile);
                if (!c.IsInCombat)
                {
                    if (foe != null && Combat.Join(c)) { Log.Info(c.Name + " joins the fight (" + foe.Name + " in sight)"); if (s.Peer != null) s.Peer.Send("SAID", foe.Name + " is in sight: you're in the fight"); }
                }
                // The game brings the player's whole side into a fight; one with no enemy in sight that hasn't fought goes back out.
                else if (foe == null && !engaged.Contains(c) && Combat.Leave(c)) { Log.Info(c.Name + " stays out of the fight (no enemy in sight)"); if (s.Peer != null) s.Peer.Send("SAID", "there's a fight nearby, but no enemy in sight: you're not in it"); }
            }
        });
        Combat.AttackResolved.Add(this, o =>
        {
            foreach (var s in seats.Values.Concat(orphans).Where(x => x.Character != null && !x.Character.IsDead))
                if (o.Target == s.Character || o.AttackerName == s.Character.Name) { engaged.Add(s.Character); if (!s.Character.IsInCombat) Combat.Join(s.Character); }
        });
        Combat.AttackResolved.Add(this, o =>
        {
            foreach (var s in seats.Values.Where(s => s.Character != null))
            {
                if (o.Target == s.Character) s.Peer.Send("SAID", o.AttackerName + (o.Hit ? " hit you for " + o.HealthLost : " missed you") + (o.Killed ? " - you're dead" : ""));
                else if (o.AttackerName == s.Character.Name && o.Target != null) s.Peer.Send("SAID", "you " + (o.Hit ? "hit " + o.Target.Name + " for " + o.HealthLost : "missed " + o.Target.Name) + (o.Killed ? ", killed" : ""));
            }
        });
        GameEvents.Saving.Add(this, name => { if (listener != null || records.Count > 0) WriteRecords(); });
        // Joined characters follow the host's character into other zones (spawned characters stay behind).
        GameEvents.ZoneLoaded.Add(this, (zone, first) =>
        {
            if (listener == null) return;
            foreach (var s in seats.Values.Where(s => s.Record != null).ToList())
            {
                if (s.Character != null && s.Character.Area != null) continue;
                var c = SpawnFor(s.Record, false);
                if (c == null) continue;
                s.Character = c; CharacterControl.Take(this, c); Party.Add(this, c);
                s.Peer.Send("PLAYING", s.Record.Name, (int)c.Position.X, (int)c.Position.Y);
            }
            foreach (var s in seats.Values.Where(s => s.Character != null)) SendWorld(s);     // the joiners follow into the new zone
            orphans.RemoveAll(o => o.Character == null || o.Character.Area == null);
        });
        GameEvents.GameStarted.Add(this, () => { if (listener != null) StopHosting(); ReadRecords(); });
    }

    public override void Unload()
    {
        StopHosting();
        if (host != null) { host.Close(); host = null; }
    }
}
