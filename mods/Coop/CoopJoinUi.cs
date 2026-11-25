// The joiner's Co-op window, in the host lobby's style (CoopLobbyUi.cs): a status line, sections, buttons.
//   connecting  "Joining..." and Cancel
//   choose      the host's lobby as the host sees it: Players (the host, each place: open, saved character, who took
//   ready       it), Game settings (as text: only the host changes them). Choosing: Continue (play the selected
//               character, or make a new one for an open place) / Leave; ready: waiting for the host to start, Leave
//   play        your character (health, whose turn), the others, the messages; End turn / Follow host / Give... / Leave
// (Attack nearest and Walk to... are console commands only now: coop.attack, coop.go.)
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    UiElement joinDetail;     // choose: what Continue will do; ready / play: the second line

    static readonly Dictionary<string, string> JoinTitles = new Dictionary<string, string> { { "connecting", "Joining a game" }, { "choose", "Join a game" }, { "ready", "Join a game" }, { "play", "Co-op" } };

    int builtPlaces;
    string builtSettingsSign;     // the settings as built (the host edited one: built again)
    string SettingsSign() { return string.Join("|", lobbySettings.Select(kv => kv.Key + "=" + kv.Value).ToArray()); }     // choose / ready: what the window was built for (more arriving: built again)

    void BuildJoiner()
    {
        if (builtFor == "choose" || builtFor == "ready") { BuildJoinLobby(); return; }
        if (builtFor == "play")
        {
            // In game: the host's window, with Leave where the host has Stop hosting.
            var give = Ui.Button("Give...", OpenGive); Ui.Tooltip(give, "Give an item or charons to another player");
            var leave = Ui.Button("Leave", Leave); Ui.Tooltip(leave, "Leave the game (your character stays, passing its turns, until you come back)");
            BuildInGameWindow(Math.Max(2, Math.Min(6, colorOrder.Count)), null, new List<UiElement> { give }, leave);
            return;
        }
        int pad = Ui.S(12), w = Ui.S(600), line = Ui.LineHeight, gap = Ui.S(6), section = Ui.S(14), inner = w - 2 * pad;
        var place = new List<KeyValuePair<UiElement, Point>>();
        int y = 0;
        Action<UiElement, int> at = (e, yy) => place.Add(new KeyValuePair<UiElement, Point>(e, new Point(pad, yy)));
        Func<string, UiElement> heading = text => { var h = Ui.Label(text, inner); Ui.SetColor(h, Mine_); return h; };

        statusLabel = Ui.Label("", inner);
        at(statusLabel, y); y += line + Ui.S(4);
        joinDetail = Ui.Label("", inner);
        Ui.SetColor(joinDetail, Grey);
        at(joinDetail, y); y += line + section;

        int rowH = line + Ui.S(6), rows = 0;
        switch (builtFor)
        {
            case "play":
                at(heading("Messages"), y); y += line + gap;
                rows = 8; break;
        }
        list = null;
        if (rows > 0)
        {
            list = Ui.List(inner, rows * rowH + Ui.S(6), rowH);
            list.Selected += i => { selected = i; Refresh(); };
            at(list.View, y); y += rows * rowH + Ui.S(6) + section;
        }

        var buttons = new List<UiElement>();
        Action<string, Action, string> button = (text, click, tip) => { var b = Ui.Button(text, click); if (tip != null) Ui.Tooltip(b, tip); buttons.Add(b); };
        switch (builtFor)
        {
            case "connecting":
                button("Cancel", Leave, null); break;
            case "play":
                button("End turn", () => Run("END"), "In a fight: end your character's turn");
                button("Follow host", () => Run("FOLLOW"), "Walk to the host's character");
                button("Give...", OpenGive, "Give an item or charons to another player");
                button("Leave", Leave, "Leave the game (your character stays, passing its turns, until you come back)"); break;
        }
        int bh = buttons.Max(b => b.Height);
        int width = Math.Max(w, Ui.ButtonsWidth(buttons, pad));
        window = Ui.Window(this, "coop", JoinTitles[builtFor], width, 22 + Ui.S(4) + y + bh + pad);
        if (window == null) return;
        window.Closed += () => { window = null; builtFor = null; };
        int top = window.Top + Ui.S(4);
        foreach (var p in place) Ui.Add(window, p.Key, p.Value.X, top + p.Value.Y);
        Ui.AddButtons(window, buttons, pad);
    }

    // A new game's lobby: make the character for our place (the game's creation, no game started: back here after).
    void CreateCharacter()
    {
        if (World.Player != null) { Tell("characters are made from the main menu: go there (Escape -> main menu) and use Co-op, Join a Game"); return; }
        if (host == null || host.Closed) return;
        creating = true; inCreation = true;
        Tell("make your character; you'll come back to " + (hostName == null || hostName == "the host" ? "the host" : hostName) + "'s lobby when it's done");
        if (!World.OpenCharacterCreation()) { creating = false; Tell("couldn't open character creation"); }
    }

    // A save's lobby: the selected character (row 0 is the host).
    void ChooseContinue()
    {
        int i = selected - 1;
        if (i < 0 || i >= choices.Count) { Tell(selected == 0 ? "that's the host's character" : "select a character first"); return; }
        if (choices[i].Value) { Tell("that character is taken"); return; }
        if (choices[i].Key != "(new character)") { host.Send("PICK", selected); return; }
        // A new character, made in the game's own creation (a new game on this machine, left as soon as it starts).
        if (World.Player != null) { Tell("new characters are made from the main menu: go there (Escape -> main menu) and use Co-op, Join a Game"); return; }
        creating = true;
        Tell("make your character; you'll join " + (hostName ?? "the host") + " when it's done");
        if (!World.OpenCharacterCreation()) { creating = false; Tell("couldn't open character creation"); }
    }

    void RefreshJoiner(List<string> items, List<Color> colours)
    {
        string who = hostName == null || hostName == "the host" ? "the host" : hostName;
        switch (builtFor)
        {
            case "connecting":
                Ui.SetText(statusLabel, onlineJoining != null ? "Joining through " + Online.Service + "..." : "Connecting...");
                Ui.SetText(joinDetail, "");
                break;
            case "choose":
            case "ready":
                RefreshJoinLobby(items, colours);
                break;
            case "play":
            {
                // Your character's health and whose turn, then everyone as the host's window lists them.
                var s = state;
                if (s == null || s.Length < 11) Ui.SetText(statusLabel, "Waiting for the host...");
                else Ui.SetText(statusLabel, s[10] == "1" ? s[1] + " is dead." : s[4] == "1" ? (s[5] == s[1] ? "Your turn: AP " + s[6] + ", MP " + s[7] + "   (" + Keybinds.Name(Keybinds.GameKey("EndTurn")) + ": end turn)" : s[5] + "'s turn") : "Playing " + s[1] + ".");
                Ui.SetColor(statusLabel, s != null && s.Length > 5 && s[4] == "1" && s[5] == s[1] ? Mine_ : Normal);
                var health = new Dictionary<string, string>();
                var p = party;
                if (p != null) for (int k = 0; k + 3 < p.Length; k += 4) health[p[k]] = p[k + 3] == "1" ? "down" : p[k + 1] + "/" + p[k + 2] + " HP";
                var rowsNow = new List<PlayerRow>();
                foreach (var e in colorOrder)
                {
                    string hp; health.TryGetValue(e.Value, out hp);
                    bool me = s != null && s.Length > 1 && e.Value == s[1];
                    rowsNow.Add(new PlayerRow { Player = e.Key, Character = e.Value, Tag = (hp ?? "") + (rowsNow.Count == 0 ? "  Host" : me ? "  You" : ""), Me = me, Host = rowsNow.Count == 0, Color = PlayerColor(e.Key) });
                }
                playerRows = rowsNow;
                break;
            }
        }
    }

    // Choose / ready: the host's lobby, read-only, in the host lobby's layout (CoopLobbyUi.BuildLobby).
    void BuildJoinLobby()
    {
        bool choosing = builtFor == "choose";
        builtPlaces = choices.Count; builtSettingsSign = SettingsSign();
        bool picking = choosing && lobbyKindSeen == "saved";     // a save: pick which of its characters is yours; a new game: your place is given
        string who = hostName == null || hostName == "the host" ? "the host" : hostName;
        var v = new LobbyView
        {
            Title = who == "the host" ? "Join a game" : "Join " + who + "'s game", Detail = true, Rows = choices.Count + 1,
            Pick = picking ? (Action<int>)(i => { selected = i; Refresh(); }) : null,
            SettingsHeading = lobbyKindSeen == "saved" ? "Game settings (kept from the save)" : "Game settings (the host's)",
        };
        // Your character, under the players, where the host has theirs.
        if (picking) { v.PlayerAction = Ui.Button("Play this character", ChooseContinue); Ui.Tooltip(v.PlayerAction, "Play the selected saved character"); }
        else if (lobbyKindSeen != "saved") { v.PlayerAction = Ui.Button(readyAs == null ? "Create character" : "Recreate character", CreateCharacter); Ui.Tooltip(v.PlayerAction, "Your character, made in the game's character creation; you come back here when it's done"); }
        foreach (var kv in lobbySettings) v.Settings.Add(Pair(kv.Key, LobbyValue(kv.Value)));
        // Leave where the host has Cancel.
        v.Right = Ui.Button("Leave", Leave);
        Ui.Tooltip(v.Right, choosing ? "Leave the lobby" : "Give up your place and leave the lobby");
        BuildLobbyWindow(v);
    }

    void RefreshJoinLobby(List<string> items, List<Color> colours)
    {
        // More places or settings than the window was built for (the list arrived after it opened): built again.
        if (choices.Count != builtPlaces || SettingsSign() != builtSettingsSign) { var w = window; builtFor = null; window = null; if (w != null) w.Close(); Build(); return; }
        string who = hostName == null || hostName == "the host" ? "the host" : hostName;
        int taken = choices.Count(c => c.Value), open = choices.Count - taken;
        if (playersHeading != null) Ui.SetText(playersHeading, "Players  (" + (taken + 1) + " of " + (choices.Count + 1) + ")");
        var rowsNow = new List<PlayerRow> { new PlayerRow { Player = who == "the host" ? "Host" : who, Character = hostCharacterSeen, Tag = "Host", Host = true, Color = PlayerColor(hostName) ?? HostColor } };
        for (int i = 0; i < choices.Count; i++)
        {
            bool isNew = choices[i].Key == "(new character)";
            var taker = i < choiceTakers.Count ? choiceTakers[i] : new KeyValuePair<string, string>("", "");
            bool mine = (myLobbyName != null && taker.Key == myLobbyName) || (readyAs != null && (taker.Value == readyAs || (!isNew && choices[i].Key == readyAs)));
            if (!choices[i].Value) rowsNow.Add(isNew ? new PlayerRow { Open = true } : new PlayerRow { Open = true, Character = choices[i].Key, Tag = "saved character" });
            else rowsNow.Add(new PlayerRow { Player = taker.Key == "" ? "?" : taker.Key, Character = isNew ? (taker.Value == "" ? null : taker.Value) : choices[i].Key, Tag = mine ? "You" : null, Me = mine, Color = PlayerColor(taker.Key) });
        }
        playerRows = rowsNow;
        if (builtFor == "ready")
        {
            Ui.SetText(statusLabel, "You'll play " + readyAs + ".");
            Ui.SetColor(statusLabel, Mine_);
            Ui.SetText(joinDetail, "Waiting for " + who + " to start the game" + (open > 0 && lobbyKindSeen == "saved" ? " (every saved character needs its player)." : "..."));
            return;
        }
        if (lobbyKindSeen != "saved")
        {
            Ui.SetText(statusLabel, "You're in " + (who == "the host" ? "the host's" : who + "'s") + " game" + (myLobbyName != null ? " as " + myLobbyName : "") + ".");
            Ui.SetColor(statusLabel, Normal);
            Ui.SetText(joinDetail, "Create your character; you'll come back here when it's done.");
            return;
        }
        Ui.SetText(statusLabel, "Pick the character you'll play in " + (who == "the host" ? "the host's" : who + "'s") + " game.");
        int s = selected - 1;
        Ui.SetText(joinDetail, s < 0 || s >= choices.Count ? "Select your saved character."
            : choices[s].Value ? "That character is taken."
            : "Play this character: you'll play " + choices[s].Key + ".");
    }
}
