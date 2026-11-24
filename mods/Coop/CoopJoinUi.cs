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

    int builtPlaces, builtSettings;     // choose / ready: what the window was built for (more arriving: built again)

    void BuildJoiner()
    {
        if (builtFor == "choose" || builtFor == "ready") { BuildJoinLobby(); return; }
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
        creating = true;
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
                var s = state;
                if (s == null || s.Length < 11) { Ui.SetText(statusLabel, "Waiting for the host..."); Ui.SetText(joinDetail, ""); }
                else
                {
                    Ui.SetText(statusLabel, s[10] == "1" ? s[1] + " is dead." : s[1] + ":  " + s[2] + "/" + s[3] + " HP" + (s[4] == "1" ? (s[5] == s[1] ? "   -   your turn: AP " + s[6] + ", MP " + s[7] : "   -   " + s[5] + "'s turn") : ""));
                    var others = new List<string>();
                    var p = party;
                    if (p != null) for (int k = 0; k + 3 < p.Length; k += 4) if (p[k] != s[1]) others.Add(p[k] + (p[k + 3] == "1" ? " (down)" : " " + p[k + 1] + "/" + p[k + 2]));
                    Ui.SetText(joinDetail, others.Count == 0 ? "" : "With " + string.Join(",  ", others.ToArray()));
                }
                Ui.SetColor(statusLabel, s != null && s.Length > 5 && s[4] == "1" && s[5] == s[1] ? Mine_ : Normal);
                items.AddRange(messages.Skip(Math.Max(0, messages.Count - 40))); colours.AddRange(items.Select(_ => Normal));
                break;
            }
        }
    }

    // Choose / ready: the host's lobby, read-only, in the host lobby's layout (CoopLobbyUi.BuildLobby).
    void BuildJoinLobby()
    {
        bool choosing = builtFor == "choose";
        int pad = Ui.S(12), w = Ui.S(600), line = Ui.LineHeight, gap = Ui.S(6), section = Ui.S(14);
        int inner = w - 2 * pad, labelW = inner * 11 / 20, valueW = inner - labelW - Ui.S(10);
        var place = new List<KeyValuePair<UiElement, Point>>();
        int y = 0;
        Action<UiElement, int, int> at = (e, x, yy) => place.Add(new KeyValuePair<UiElement, Point>(e, new Point(x, yy)));
        Func<string, UiElement> heading = text => { var h = Ui.Label(text, inner); Ui.SetColor(h, Mine_); return h; };
        builtPlaces = choices.Count; builtSettings = lobbySettings.Count;

        statusLabel = Ui.Label("", inner);
        at(statusLabel, pad, y); y += line + Ui.S(4);
        joinDetail = Ui.Label("", inner);
        Ui.SetColor(joinDetail, Grey);
        at(joinDetail, pad, y); y += line + section;

        playersHeading = heading("Players");
        at(playersHeading, pad, y); y += line + gap;
        int rows = choices.Count + 1;
        bool picking = choosing && lobbyKindSeen == "saved";     // a save: pick which of its characters is yours; a new game: your place is given
        list = null; playerRows = new List<PlayerRow>();
        playersTable = PlayersTable(inner, rows, picking ? (Action<int>)(i => { selected = i; Refresh(); }) : null);
        at(playersTable, pad, y); y += TableHeight(rows) + section;

        at(heading(lobbyKindSeen == "saved" ? "Game settings (kept from the save)" : "Game settings (the host's)"), pad, y); y += line + gap;
        foreach (var kv in lobbySettings)
        {
            var wrapped = Ui.Wrap(kv.Value, valueW);
            at(Ui.Label(kv.Key, labelW), pad + Ui.S(10), y);
            for (int i = 0; i < wrapped.Count; i++) { var v = Ui.Label(wrapped[i], valueW); Ui.SetColor(v, Normal); at(v, pad + Ui.S(10) + labelW, y); y += line + (i == wrapped.Count - 1 ? gap : Ui.S(2)); }
            if (wrapped.Count == 0) y += line + gap;
        }
        if (lobbySettings.Count == 0) { var e = Ui.Label("(not sent by the host)", inner); Ui.SetColor(e, Grey); at(e, pad + Ui.S(10), y); y += line + gap; }
        y += section - gap;

        var buttons = new List<UiElement>();
        if (choosing && picking) { var c = Ui.Button("Play this character", ChooseContinue); Ui.Tooltip(c, "Play the selected saved character"); buttons.Add(c); }
        else if (lobbyKindSeen != "saved") { var c = Ui.Button(readyAs == null ? "Create character" : "Change character", CreateCharacter); Ui.Tooltip(c, "Your character, made in the game's character creation; you come back here when it's done"); buttons.Add(c); }
        var leave = Ui.Button("Leave", Leave); Ui.Tooltip(leave, choosing ? "Leave the lobby" : "Give up your place and leave the lobby"); buttons.Add(leave);
        int bh = buttons.Max(b => b.Height);
        string who = hostName == null || hostName == "the host" ? "the host" : hostName;
        window = Ui.Window(this, "coop", who == "the host" ? "Join a game" : "Join " + who + "'s game", Math.Max(w, Ui.ButtonsWidth(buttons, pad)), 22 + Ui.S(4) + y + bh + pad);
        if (window == null) return;
        window.Closed += () => { window = null; builtFor = null; };
        int top = window.Top + Ui.S(4);
        foreach (var p in place) Ui.Add(window, p.Key, p.Value.X, top + p.Value.Y);
        Ui.AddButtons(window, buttons, pad);
    }

    void RefreshJoinLobby(List<string> items, List<Color> colours)
    {
        // More places or settings than the window was built for (the list arrived after it opened): built again.
        if (choices.Count != builtPlaces || lobbySettings.Count != builtSettings) { var w = window; builtFor = null; window = null; if (w != null) w.Close(); Build(); return; }
        string who = hostName == null || hostName == "the host" ? "the host" : hostName;
        int taken = choices.Count(c => c.Value), open = choices.Count - taken;
        if (playersHeading != null) Ui.SetText(playersHeading, "Players  (" + (taken + 1) + " of " + (choices.Count + 1) + ")");
        var rowsNow = new List<PlayerRow> { new PlayerRow { Player = who == "the host" ? "Host" : who, Character = hostCharacterSeen, Tag = "Host", Host = true } };
        for (int i = 0; i < choices.Count; i++)
        {
            bool isNew = choices[i].Key == "(new character)";
            var taker = i < choiceTakers.Count ? choiceTakers[i] : new KeyValuePair<string, string>("", "");
            bool mine = (myLobbyName != null && taker.Key == myLobbyName) || (readyAs != null && (taker.Value == readyAs || (!isNew && choices[i].Key == readyAs)));
            if (!choices[i].Value) rowsNow.Add(isNew ? new PlayerRow { Open = true } : new PlayerRow { Open = true, Character = choices[i].Key, Tag = "saved character" });
            else rowsNow.Add(new PlayerRow { Player = taker.Key == "" ? "?" : taker.Key, Character = isNew ? (taker.Value == "" ? null : taker.Value) : choices[i].Key, Tag = mine ? "You" : null, Me = mine });
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
