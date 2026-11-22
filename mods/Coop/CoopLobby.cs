// Co-op's lobby: players join only before the game starts, never into a game under way (a player arriving mid-game
// meets a world that already moved on: fights, conversations, zones). The host opens the lobby from the main menu:
//   Host New Game     how many players (you included); each joiner makes a character in the game's own creation. When
//                     every place is taken, Start: the host makes their own character, and everyone starts together.
//   Host Saved Game   a co-op game's save: its co-op characters are the places, each joiner takes one (no places
//                     added or left out; single-player saves aren't offered). The game loads only when every place is
//                     taken: a saved character never goes missing from the game it belongs to.
// After the start nobody else joins (refused with the reason). A player who leaves leaves their character in the game,
// passing its turns. The host loading another game while hosting: only one with the same co-op characters as the
// players now (reloading after a defeat, an earlier save of this game); any other ends the session.
// Console: coop.hostnew <players> [port], coop.hostsave <save> [new players] [port], coop.start, coop.stop.
using System;
using System.Collections.Generic;
using System.Linq;
using UnderRailWorks;

public sealed partial class Coop
{
    sealed class Slot { public Record Saved; public Seat Seat; public bool New { get { return Saved == null; } } }
    const int MaxPlayers = 4;     // the host and three others
    string lobby;                 // null: not gathering players; "new" or "saved" (lobbySave)
    string lobbySave;
    readonly List<Slot> slots = new List<Slot>();
    bool starting;                // Start pressed: the game is being made or loaded

    static List<Record> ParseRecords(object data)
    {
        var result = new List<Record>();
        var list = data as List<object>;
        if (list == null) return result;
        foreach (var o in list.OfType<Dictionary<string, object>>())
        {
            Func<string, string> s = k => { object v; return o.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null; };
            Func<string, int> n = k => { double d; return double.TryParse(s(k), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out d) ? (int)d : 0; };
            if (s("name") == null) continue;
            result.Add(new Record { Name = s("name"), Blueprint = s("blueprint") ?? DefaultBlueprint, Zone = s("zone"), X = n("x"), Y = n("y"), Sheet = s("sheet") });
        }
        return result;
    }

    // A save's co-op characters, read from the save on disk.
    List<Record> SavedCharacters(string save) { return ParseRecords(SaveGames.ModData(this, save, "characters")); }

    string OpenLobby(string kind, string save, int newPlayers, int port)
    {
        if (World.Player != null) return "host from the main menu (Escape -> main menu): Host New Game or Host Saved Game";
        if (host != null && !host.Closed) return "you're joined to someone's game (Leave first)";
        StopHosting();
        slots.Clear(); lobby = null; starting = false;
        if (kind == "saved")
        {
            if (string.IsNullOrEmpty(save) || !SaveGames.Names.Contains(save)) return "no save called " + save;
            records = SavedCharacters(save);
            // Only a co-op game's save, with exactly its players: no places added or left out (a saved character never
            // goes missing from its game). A single-player game becomes co-op only from the start: Host New Game.
            if (records.Count == 0) return save + " isn't a co-op game (no co-op characters): co-op games start with Host New Game";
            foreach (var r in records) slots.Add(new Slot { Saved = r });
            RestoreOptions(SaveGames.ModData(this, save, "options"));     // the game's own settings (the console's way in too)
        }
        else
        {
            records = new List<Record>();
            for (int i = 0; i < newPlayers; i++) slots.Add(new Slot());
        }
        if (slots.Count == 0) return "at least one other player";
        if (slots.Count > MaxPlayers - 1) return "at most " + MaxPlayers + " players (you and " + (MaxPlayers - 1) + " others); this would be " + (slots.Count + 1);
        var failed = Listen(port);
        if (failed != null) return failed;
        lobby = kind; lobbySave = save;
        unticked.Clear();
        Log.Info("lobby: " + (kind == "saved" ? "saved game " + save + " (expansions: " + Names(SaveGames.ExpansionsOf(save) ?? new List<string>()) + "), " : "new game, ") + slots.Count + " places (" + string.Join(", ", slots.Select(s => s.New ? "new" : s.Saved.Name).ToArray()) + ") on port " + port);
        Changed();
        if (window == null || !window.IsOpen) Build();
        return "waiting for " + slots.Count + " player" + (slots.Count == 1 ? "" : "s") + " on port " + port;
    }

    int FreePlaces() { return slots.Count(s => s.Seat == null); }

    // Start: every place taken, then the game is made (Host New Game: the host's own creation) or loaded.
    string StartGame()
    {
        if (lobby == null) return starting ? "starting..." : "no lobby (Host New Game / Host Saved Game on the main menu)";
        int missing = FreePlaces();
        if (missing > 0) return "waiting for " + missing + " more player" + (missing == 1 ? "" : "s") + ": every place must be taken first";
        foreach (var s in seats.Values.Where(x => !slots.Any(sl => sl.Seat == x)).ToList()) { s.Peer.Send("BYE", "the game is full"); s.Peer.Close(); }
        var needed = LobbyExpansions();
        var notHere = needed.Where(id => !Expansions.Installed.Contains(id)).ToList();
        if (notHere.Count > 0) return "this save uses " + Names(notHere) + ", which you don't have";
        records = slots.Select(s => s.Seat.Record).ToList();
        var kind = lobby;
        UseOnly(needed);     // the others off here; the players did the same when the set arrived
        foreach (var s in seats.Values) SendExpansions(s.Peer);
        lobby = null; starting = true;     // from here nobody joins
        foreach (var s in seats.Values) s.Peer.Send("SAID", kind == "saved" ? "the host is loading the game" : "the host is making their character; the game starts after that");
        bool ok = kind == "saved" ? SaveGames.Load(lobbySave) : World.StartNewGame();
        if (!ok) { lobby = kind; starting = false; return kind == "saved" ? "couldn't load " + lobbySave : "couldn't open the new game"; }
        Log.Info("starting the " + (kind == "saved" ? "saved game " + lobbySave : "new game") + " with " + string.Join(", ", slots.Select(s => s.Seat.Peer.Name + " as " + s.Seat.Record.Name).ToArray()));
        Changed();
        return "starting";
    }

    // HELLO from someone new: only into an open lobby with a free place.
    string RefuseJoin()
    {
        if (lobby == null) return "the game has already started: players join in the host's lobby, before the game starts";
        if (FreePlaces() == 0) return "the game is full";
        return null;
    }

    // The lobby's places, for a joiner choosing: saved characters by name, new places as "(new character)".
    void SendLobbyList(Peer p)
    {
        p.Send("LIST", "the host", lobby == "saved" ? "saved" : "new");
        for (int i = 0; i < slots.Count; i++)
            p.Send("CHAR", i + 1, slots[i].New ? "(new character)" : slots[i].Saved.Name, slots[i].Seat != null ? "taken" : "free");
        p.Send("LISTEND");
        SendExpansions(p);
    }
    void LobbyChanged() { foreach (var s in seats.Values) if (s.Peer != null && !s.Peer.Closed) SendLobbyList(s.Peer); Changed(false); }

    void TakeSlot(Seat seat, Slot slot, Record r)
    {
        slot.Seat = seat; seat.Record = r;
        seat.Peer.Send("READY", r.Name);
        Log.Info(seat.Peer.Name + " will play " + r.Name);
        Notify.Log("Co-op: " + seat.Peer.Name + " will play " + r.Name + (FreePlaces() == 0 ? ". Everyone is here: Start." : ""), Say);
        LobbyChanged();
    }

    // PICK <n> / NEW <name> [sheet] in the lobby.
    void LobbyChoice(Seat seat, string[] w)
    {
        var p = seat.Peer;
        if (slots.Any(s => s.Seat == seat)) { p.Send("SAID", "you already have a place"); return; }
        if (w[0] == "PICK")
        {
            int i;
            if (w.Length < 2 || !int.TryParse(w[1], out i) || i < 1 || i > slots.Count) { p.Send("SAID", "pick a number from the list"); SendLobbyList(p); return; }
            var slot = slots[i - 1];
            if (slot.Seat != null) { p.Send("SAID", "that place is taken"); SendLobbyList(p); return; }
            if (slot.New) { p.Send("SAID", "that place is for a new character: make one (New character...)"); return; }
            TakeSlot(seat, slot, slot.Saved);
            return;
        }
        var free = slots.FirstOrDefault(s => s.New && s.Seat == null);
        if (free == null) { p.Send("SAID", "this game has no place for a new character: pick one of its characters"); return; }
        var name = w.Length > 1 ? w[1].Trim() : "";
        if (name.Length == 0) { p.Send("SAID", "coop.new <name>"); return; }
        Func<string, bool> used = n => slots.Any(s => (s.Saved != null && s.Saved.Name.Equals(n, StringComparison.OrdinalIgnoreCase)) || (s.Seat != null && s.Seat.Record != null && s.Seat.Record.Name.Equals(n, StringComparison.OrdinalIgnoreCase)));
        if (used(name)) { int n2 = 2; while (used(name + " " + n2)) n2++; p.Send("SAID", "there's already a " + name + " in this game: yours is " + name + " " + n2); name = name + " " + n2; }
        TakeSlot(seat, free, new Record { Name = name, Blueprint = DefaultBlueprint, Sheet = w.Length > 2 && w[2].Length > 0 ? w[2] : null });
    }

    // A joiner left the lobby: their place is free again.
    void LeftLobby(Seat seat) { foreach (var s in slots) if (s.Seat == seat) s.Seat = null; if (lobby != null) LobbyChanged(); }

    // The host's game started while hosting: true to go on with these players. The lobby's own start always; any
    // other load only with exactly the players' co-op characters (all of them, no others).
    bool SameGame()
    {
        if (starting) { starting = false; return true; }
        var playing = seats.Values.Where(s => s.Record != null).Select(s => s.Record.Name.ToLowerInvariant()).OrderBy(n => n).ToList();
        var saved = records.Select(r => r.Name.ToLowerInvariant()).OrderBy(n => n).ToList();
        return playing.Count > 0 && playing.SequenceEqual(saved);
    }

    void LoadLobby()
    {
        Commands.Add(this, "coop.hostnew", "coop.hostnew <players, you included> [port]: on the main menu, a lobby for a new co-op game", a =>
        {
            int n, port = DefaultPort;
            if (a.Length < 1 || !int.TryParse(a[0], out n) || (a.Length > 1 && !int.TryParse(a[1], out port))) return "coop.hostnew <players> [port]";
            return OpenLobby("new", null, n - 1, port);
        });
        Commands.Add(this, "coop.hostsave", "coop.hostsave <save> [port]: on the main menu, a lobby for that co-op save's players", a =>
        {
            int port = DefaultPort;
            if (a.Length < 1 || (a.Length > 1 && !int.TryParse(a[1], out port))) return "coop.hostsave <save> [port]";
            return OpenLobby("saved", a[0], 0, port);
        });
        Commands.Add(this, "coop.start", "coop.start: start the lobby's game (every place taken)", a => StartGame());
        MainMenu.AddButton(this, "Host New Game", () => AskHost("new", null));
        MainMenu.AddButton(this, "Host Saved Game", PickSave);
    }

    // Host Saved Game: the saves, with their co-op characters.
    UiWindow saveWindow;
    void PickSave()
    {
        if (saveWindow != null && saveWindow.IsOpen) return;
        // Only co-op games' saves (with co-op characters): single-player games become co-op with Host New Game.
        var names = SaveGames.Names.Where(n => SavedCharacters(n).Count > 0).ToList();
        int pad = Ui.S(10), w = Ui.S(620), h = Ui.S(420);
        saveWindow = Ui.Window(this, "hostsave", "Host a saved game", w, h);
        if (saveWindow == null) return;
        saveWindow.Closed += () => saveWindow = null;
        w = saveWindow.Width; h = saveWindow.Height;
        int chosen = -1;
        var items = names.Select(n => n + "   (" + string.Join(", ", SavedCharacters(n).Select(r => r.Name).ToArray()) + ")").ToList();
        Ui.Add(saveWindow, Ui.Label("Co-op games' saves: their players take their characters again.", w - 2 * pad), pad, saveWindow.Top + Ui.S(2));
        var next = Ui.Button("Next...", () =>
        {
            if (chosen < 0 || chosen >= names.Count) { Notify.Log("Co-op: select a save first", Grey); return; }
            var save = names[chosen];
            saveWindow.Close();
            AskHost("saved", save);
        });
        var cancel = Ui.Button("Cancel", () => saveWindow.Close());
        int by = h - pad - next.Height;
        var list = Ui.List(w - 2 * pad, by - Ui.S(8) - (saveWindow.Top + Ui.LineHeight + Ui.S(8)));
        list.Items = items.Count == 0 ? new List<string> { "No co-op saves yet: start one with Host New Game." } : items;
        list.Selected += i => chosen = i;
        Ui.Add(saveWindow, list.View, pad, saveWindow.Top + Ui.LineHeight + Ui.S(8));
        Ui.Add(saveWindow, next, pad, by);
        Ui.Add(saveWindow, cancel, pad + next.Width + Ui.S(6), by);
    }
}
