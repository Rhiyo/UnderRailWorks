// Co-op's interface: a "Co-op" button on the HUD (top right, with the other mod buttons) opens the Co-op window, the
// game's titled window. What it shows follows the state:
//   not connected   Host this game / Join a game... (the address asked with the game's text popup)
//   hosting         the addresses others join with, who is connected and what they play, Stop hosting
//   choosing        the host's character list (the host's own and taken ones greyed), Play selected / New character...
//   playing         your character's health and whose turn it is, the messages, End turn / Attack nearest enemy /
//                   Follow host / Walk to... / Leave ("Attack nearest": the nearest character hostile to yours)
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
    string lastAddress = "127.0.0.1";
    Keybind windowKey;

    void LoadUi()
    {
        windowKey = Keybinds.Register(this, "window", "Co-op window");
        Hud.WhenReady(this, root =>
        {
            var button = Ui.Button("Co-op", Toggle);
            Hud.AddPart(this, "Co-op button", button, HudAnchor.TopRight, 70, 62);

        });
        GameEvents.Update.Add(this, dt => { if (windowKey.Pressed) Toggle(); });
        MainMenu.AddButton(this, "Join a Game", AskJoin);
    }

    void Toggle()
    {
        if (window != null && window.IsOpen) { window.Close(); return; }
        Build();
    }

    string Mode()
    {
        if (listener != null) return "host";
        if (host != null && !host.Closed) return playing ? "play" : choices.Count > 0 || hostName != null ? "choose" : "connecting";
        return "idle";
    }

    // Something changed: the window rebuilt if what it shows changed kind, else its texts and list updated.
    void Changed(bool structural = true)
    {
        if (window == null || !window.IsOpen) return;
        if (builtFor != Mode()) { window.Close(); Build(); return; }
        Refresh();
    }

    void Build()
    {
        int w = Ui.S(600), h = Ui.S(380), line = Ui.LineHeight, pad = Ui.S(10);
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
                if (World.Player != null) button("Host this game...", AskAndHost);
                button("Join a game...", AskJoin);
                break;
            case "host":
                button("Revive", () => { var p = World.Player; var r = p == null ? "" : ReviveNear(p); if (!string.IsNullOrEmpty(r)) Notify.Log("Co-op: " + r, Grey); });
                button("Stop hosting", () => StopHosting());
                break;
            case "connecting":
                button("Leave", Leave);
                break;
            case "choose":
                button("Play selected", () =>
                {
                    if (selected < 1) { Tell(selected == 0 ? "that's the host's character" : "select a character first"); return; }
                    host.Send("PICK", selected);
                });
                button("New character...", () =>
                {
                    // Made in the game's own creation (a new game on this machine, left as soon as it starts).
                    if (World.Player != null) { Tell("new characters are made from the main menu: go there (Escape -> main menu) and use Join a Game"); return; }
                    creating = true;
                    Tell("make your character; you'll join " + (hostName ?? "the host") + " when it's done");
                    if (!World.StartNewGame()) { creating = false; Tell("couldn't open character creation"); }
                });
                button("Leave", Leave);
                break;
            case "play":
                button("End turn", () => Run("END"));
                button("Attack nearest", () => Run("ATTACK"));
                button("Follow host", () => Run("FOLLOW"));
                button("Revive", () => Run("REVIVE"));
                button("Walk to...", () => Ui.Ask(this, "Walk to tile (x y)", "", text =>
                {
                    var p = (text ?? "").Split(new[] { ' ', ',' }, StringSplitOptions.RemoveEmptyEntries);
                    int tx, ty;
                    if (p.Length == 2 && int.TryParse(p[0], out tx) && int.TryParse(p[1], out ty)) Run("GO", tx.ToString(), ty.ToString()); else Tell("two numbers, e.g. 12 30");
                }, "Walk"));
                button("Leave", Leave);
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
                Ui.SetText(statusLabel, "Play together: host this game, or join someone's.");
                items.AddRange(messages.Skip(Math.Max(0, messages.Count - 30))); colours.AddRange(items.Select(_ => Normal));
                break;
            case "host":
                Ui.SetText(statusLabel, "Hosting on port " + listener.Port + ". Who's playing:");
                foreach (var s in seats.Values)
                {
                    items.Add(s.Peer.Name + (s.Character == null ? "  (choosing a character)" : "  plays " + s.Character.Name + (s.Character.IsDead ? " (dead)" : ", " + (int)s.Character.Health + "/" + (int)s.Character.MaxHealth + " HP")));
                    colours.Add(Normal);
                }
                foreach (var o in orphans) { items.Add(o.Record.Name + "  (player left; passes its turns)"); colours.Add(Grey); }
                if (items.Count == 0) { items.Add("Nobody has joined yet."); colours.Add(Grey); }
                items.Add(""); colours.Add(Grey);
                items.Add("Others join with one of these addresses:"); colours.Add(Grey);
                foreach (var a in Addresses()) { items.Add("   " + a + (listener.Port == DefaultPort ? "" : ":" + listener.Port)); colours.Add(Mine_); }
                items.Add("(over the internet: your router's address, with port " + listener.Port + " forwarded)"); colours.Add(Grey);
                {
                    // What joiners need: the same mods, same versions, except those that only change one player's screen.
                    var need = Mods.Running(true); var all = Mods.Running();
                    items.Add(""); colours.Add(Grey);
                    items.Add("Others need these " + need.Length + " mods, same versions (" + (all.Length - need.Length) + " others only change your screen):"); colours.Add(Grey);
                    var names = need.Select(x => { int at = x.LastIndexOf('@'); var m = ModLoader.Find(x.Substring(0, at)); return (m == null ? x.Substring(0, at) : m.Name) + " " + x.Substring(at + 1); }).ToList();
                    for (int i = 0; i < names.Count; i += 3) { items.Add("   " + string.Join(", ", names.Skip(i).Take(3).ToArray())); colours.Add(Normal); }
                }
                break;
            case "connecting":
                Ui.SetText(statusLabel, "Connecting...");
                break;
            case "choose":
                Ui.SetText(statusLabel, "Choose your character in " + (hostName ?? "the host") + "'s game" + (selected > 0 && selected <= choices.Count ? ": " + choices[selected - 1].Key : ""));
                items.Add((hostName ?? "?") + "  (the host)"); colours.Add(Grey);
                foreach (var c in choices) { items.Add(c.Key + (c.Value ? "  (taken)" : "")); colours.Add(c.Value ? Grey : Normal); }
                if (choices.Count == 0) { items.Add("No co-op characters yet: make a new one."); colours.Add(Grey); }
                break;
            case "play":
                Ui.SetText(statusLabel, StateText());
                items.AddRange(messages.Skip(Math.Max(0, messages.Count - 40))); colours.AddRange(items.Select(_ => Normal));
                break;
        }
        list.Items = items;
        list.ItemColors = colours;
        if (builtFor == "choose") list.Selection = selected;
        else if (builtFor == "play" || builtFor == "idle")
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

    void AskJoin()
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
        if (host == null) return;
        host.Send("BYE"); host.Close(); host = null; playing = false; state = null; choices.Clear(); hostName = null;
        LeftWorld();
        Tell("you left");
        Changed();
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
