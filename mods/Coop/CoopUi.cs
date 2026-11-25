// Co-op's interface: a "Co-op" button on the HUD (top right, with the other mod buttons) opens the Co-op window, the
// game's titled window. What it shows follows the state:
//   not connected   Join a game... (the address asked with the game's text popup); hosting starts on the main menu.
//                   After a refusal for different mods: what differs, Match host (CoopMatch.cs); Restore my mods.
//   lobby (host)    CoopLobbyUi.cs: players, game settings, how friends join; Start game / Invite friends... / Cancel
//   hosting         who is playing what, Stop hosting
//   joining         CoopJoinUi.cs: connecting, choosing a character, waiting for the host, playing
// Built from Works' UI (Ui.Window, Ui.List, Ui.Button, Ui.Ask, Hud.AddPart), sized with Ui.S and Ui.LineHeight.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Sockets;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    static readonly Color Grey = new Color(140, 135, 125), Normal = new Color(225, 215, 195), Mine_ = new Color(120, 200, 255), Bad = new Color(225, 100, 80);
    UiWindow window;
    string builtFor;
    UiElement statusLabel;
    UiList list;
    int selected = -1;
    // The address last joined (remembered between sessions); none the first time.
    string lastAddress { get { return Settings.Get("lastAddress") ?? ""; } set { Settings.Set("lastAddress", value); } }
    Keybind windowKey;

    void LoadUi()
    {
        windowKey = Keybinds.Add(this, "window", "Co-op window");
        Hud.AddButton(this, "Co-op", Toggle, "Players, Give, Leave");     // in Works' strip of mod buttons
        GameEvents.Update.Add(this, dt => { if (windowKey.Pressed) Toggle(); });
    }

    void Toggle()
    {
        if (window != null && window.IsOpen) { window.Close(); return; }
        Build();
    }

    string Mode()
    {
        if (listener != null) return lobby != null || starting ? "lobby" : "host";
        if (onlineJoining != null) return "connecting";
        if (host != null && !host.Closed) return playing ? "play" : readyAs != null ? "ready" : choices.Count > 0 || hostName != null ? "choose" : "connecting";
        return "idle";
    }

    // Something changed: the window rebuilt if what it shows changed kind, else its texts and list updated.
    void Changed(bool structural = true)
    {
        if (window == null || !window.IsOpen) return;
        // Into the game (the joiner's world arrived, or the host's game started): the lobby window closes; the HUD's Co-op
        // button opens the in-game one.
        if (builtFor != Mode() && (Mode() == "play" || Mode() == "host")) { window.Close(); return; }
        if (builtFor != Mode()) { window.Close(); Build(); return; }
        Refresh();
    }

    // The window built again for what it shows now, if it's open (never opened by this).
    void RebuildIfOpen() { if (window == null || !window.IsOpen) return; window.Close(); Build(); }

    void Build()
    {
        if (Mode() == "lobby") { builtFor = "lobby"; selected = -1; BuildLobby(); if (window != null) Refresh(); else builtFor = null; return; }
        if (Mode() == "host") { builtFor = "host"; selected = -1; BuildHosting(); if (window != null) Refresh(); else builtFor = null; return; }
        if (JoinTitles.ContainsKey(Mode())) { builtFor = Mode(); selected = -1; BuildJoiner(); if (window != null) Refresh(); else builtFor = null; return; }
        int w = Ui.S(600), h = Ui.S(Mode() == "lobby" ? 520 : 380), line = Ui.LineHeight, pad = Ui.S(10);
        window = Ui.Window(this, "coop", "Co-op", w, h);
        if (window == null) return;
        window.Closed += () => { window = null; builtFor = null; };
        w = window.Width; h = window.Height;
        builtFor = Mode();
        selected = -1;

        statusLabel = Ui.Label("", w - 2 * pad);
        Ui.Add(window, statusLabel, pad, window.Top + Ui.S(2));

        // Buttons along the bottom, from the left, wrapping to a second row when they don't fit.
        var buttons = new List<UiElement>();
        Action<string, Action> button = (text, click) => buttons.Add(Ui.Button(text, click));
        switch (builtFor)
        {
            case "idle":
                if (hostMods != null) button("Match host", () => MatchHost());
                button("Join a game...", AskJoin);
                if (Mods.HasProfile(BackupProfile)) button("Restore my mods", () => RestoreMine());
                break;
            case "host":
                if (World.Player != null) button("Give...", OpenGive);
                button("Stop hosting", () => StopHosting());
                break;
        }
        int gap = Ui.S(6), bh = buttons.Count == 0 ? 0 : buttons.Max(b => b.Height);
        var rowsOf = new List<List<UiElement>> { new List<UiElement>() };
        int used = 0;
        foreach (var b in buttons)
        {
            if (used > 0 && used + b.Width > w - 2 * pad) { rowsOf.Add(new List<UiElement>()); used = 0; }
            rowsOf[rowsOf.Count - 1].Add(b); used += b.Width + gap;
        }
        int y = h - pad - rowsOf.Count * (bh + gap) + gap;
        foreach (var row in rowsOf)
        {
            int x = pad;
            foreach (var b in row) { Ui.Add(window, b, x, y); x += b.Width + gap; }
            y += bh + gap;
        }

        int listTop = window.Top + line + Ui.S(8);
        int listBottom = h - pad - rowsOf.Count * (bh + gap);
        list = Ui.List(w - 2 * pad, Math.Max(line * 2, listBottom - listTop));
        list.Selected += i => { selected = i; Refresh(); };
        Ui.Add(window, list.View, pad, listTop);
        Refresh();
    }

    void Refresh()
    {
        if (window == null || !window.IsOpen || statusLabel == null) return;
        var items = new List<string>(); var colours = new List<Color>();
        switch (builtFor)
        {
            case "idle":
                if (hostMods != null)
                {
                    Ui.SetText(statusLabel, "Your mods differ from the host's. Match host switches yours to the host's set.");
                    foreach (var l in MismatchLines()) { items.Add(l); colours.Add(Bad); }
                    items.Add(""); colours.Add(Grey);
                    break;
                }
                Ui.SetText(statusLabel, World.Player == null ? "Play together: Co-op on the main menu (host a game, or join one)." : "Players join before a game starts: host from the main menu's Co-op page.");
                items.AddRange(messages.Skip(Math.Max(0, messages.Count - 30))); colours.AddRange(items.Select(_ => Normal));
                break;
            case "host":
                RefreshHosting(items, colours);
                break;
            case "lobby":
                RefreshLobby(items, colours);
                break;
            case "connecting":
            case "choose":
            case "ready":
            case "play":
                RefreshJoiner(items, colours);
                break;
        }
        if (list == null) return;
        list.Items = items;
        list.ItemColors = colours;
        if (builtFor == "choose") list.Selection = selected;
        else if (builtFor == "play" || builtFor == "ready" || builtFor == "idle")
        {
            int rows = Math.Max(1, list.View.Height / Math.Max(1, Ui.LineHeight + 2));
            list.First = Math.Max(0, items.Count - rows);     // the newest messages in view
        }
    }

    // STATE name hp max combat current ap mp x y dead
    string StateText()
    {
        var s = state;
        if (s == null || s.Length < 11) return "Waiting for the host...";
        if (s[10] == "1") return s[1] + " is dead.";
        var text = s[1] + ": " + s[2] + "/" + s[3] + " HP";
        if (s[4] == "1") text += s[5] == s[1] ? "  -  your turn: AP " + s[6] + ", MP " + s[7] : "  -  " + s[5] + "'s turn";
        else text += "  -  at " + s[8] + "," + s[9];
        // The others: name and health.
        var p = party;
        if (p != null)
            for (int i = 0; i + 3 < p.Length; i += 4)
                if (p[i] != s[1]) text += "   |   " + p[i] + (p[i + 3] == "1" ? " (down)" : " " + p[i + 1] + "/" + p[i + 2]);
        return text;
    }

    void Run(params string[] w)
    {
        if (host == null || host.Closed || !playing) return;
        host.Send(w);
    }

    // Join a Game: friends hosting on Steam (if signed in), or an address.
    UiWindow joinWindow;
    void AskJoin()
    {
        if (!Online.CheckNow()) { AskAddress(); return; }     // asked now: the first Join right after launch found no store yet
        if (joinWindow != null && joinWindow.IsOpen) joinWindow.Close();
        var friendsList = new List<OnlinePlayer>();
        int chosen = -1;
        UiList rows = null;
        Action fill = () =>
        {
            friendsList.Clear(); friendsList.AddRange(Online.FriendsHosting());
            rows.Items = friendsList.Count == 0 ? new List<string> { "Nobody is hosting right now: Refresh in a moment." } : friendsList.Select(f => f.Name).ToList();
            chosen = -1;
        };
        var join = Ui.Button("Join", () =>
        {
            if (chosen < 0 || chosen >= friendsList.Count) { Tell("choose a friend's game"); return; }
            var r = Join(new[] { "online:" + friendsList[chosen].Lobby });
            Tell(r);
            if (joinWindow != null) joinWindow.Close();
            if (window == null || !window.IsOpen) Build(); else Changed();
        });
        var refresh = Ui.Button("Refresh", () => fill());
        var address = Ui.Button("By address...", () => { if (joinWindow != null) joinWindow.Close(); AskAddress(); });
        var close = Ui.Button("Close", () => { if (joinWindow != null) joinWindow.Close(); });
        var buttons = new List<UiElement> { join, refresh, address, close };
        int pad = Ui.S(10);
        // Its width stays; the buttons wrap onto a second row when they don't fit (no wider window for them).
        int jw = Ui.S(620);
        joinWindow = Ui.Window(this, "join", "Join a Game", jw, Ui.S(300) + Ui.ButtonsHeight(buttons, jw));
        if (joinWindow == null) return;
        joinWindow.Closed += () => joinWindow = null;
        int w = joinWindow.Width, y = joinWindow.Top + Ui.S(4);
        Ui.Add(joinWindow, Ui.Label(Online.Service + " friends hosting a game:", w - 2 * pad), pad, y);
        y += Ui.LineHeight + Ui.S(6);
        int by = Ui.AddButtons(joinWindow, buttons);
        rows = Ui.List(w - 2 * pad, by - Ui.S(8) - y);
        rows.Selected += i => chosen = i;
        Ui.Add(joinWindow, rows.View, pad, y);
        fill();
    }

    void AskAddress()
    {
        Ui.Ask(this, "Host's address", lastAddress, address =>
        {
            if (string.IsNullOrEmpty(address)) return;
            lastAddress = address.Trim();
            var r = Join(new[] { lastAddress });
            Tell(r);
            if (window == null || !window.IsOpen) Build(); else Changed();
        }, "Join", ".:-");
    }

    void Leave()
    {
        if (onlineJoining != null) { onlineJoining = null; Online.Leave(); Changed(); return; }
        if (host == null) return;
        if (host is OnlinePeer) Online.Leave();
        host.Send("BYE"); host.Close(); host = null; playing = false; readyAs = null; state = null; choices.Clear(); hostName = null;
        LeftWorld();
        Tell("you left");
        Changed();
    }

    // "1.2.3.4" or "127.0.0.1 (on this computer)" with the port after the address, unless it's the default.
    static string WithPort(string a, int port)
    {
        if (port == DefaultPort) return a;
        int note = a.IndexOf(" (");
        return note < 0 ? a + ":" + port : a.Substring(0, note) + ":" + port + a.Substring(note);
    }

    // This computer's IPv4 addresses for others to join with (the LAN ones; over the internet, the router's).
    static List<string> addresses;
    static IEnumerable<string> Addresses()
    {
        if (addresses != null) return addresses;
        var list = new List<string>();
        // The one used to reach the internet (a UDP "connect" sends nothing), then every network card's.
        try { using (var u = new Socket(AddressFamily.InterNetwork, SocketType.Dgram, ProtocolType.Udp)) { u.Connect("8.8.8.8", 53); list.Add(((IPEndPoint)u.LocalEndPoint).Address.ToString()); } } catch { }
        try
        {
            foreach (var n in System.Net.NetworkInformation.NetworkInterface.GetAllNetworkInterfaces())
                foreach (var a in n.GetIPProperties().UnicastAddresses)
                    if (a.Address.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a.Address)) list.Add(a.Address.ToString());
        }
        catch { }
        try { list.AddRange(Dns.GetHostAddresses(Dns.GetHostName()).Where(a => a.AddressFamily == AddressFamily.InterNetwork && !IPAddress.IsLoopback(a)).Select(a => a.ToString())); } catch { }
        list = list.Where(a => !a.StartsWith("0.")).Distinct().ToList();
        list.Add("127.0.0.1 (on this computer)");
        return addresses = list;
    }
}
