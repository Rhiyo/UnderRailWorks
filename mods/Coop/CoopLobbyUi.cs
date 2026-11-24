// The host's lobby window (Host New Game / Host Saved Game): three sections, then the buttons.
//   Players          you, then each place: open, taken (who, as which character), or someone still choosing
//   Game settings    a new game's: expansions (ticks) and every host option, changed in place (remembered for next
//                    time); a saved game's: shown as the save has them
//   How friends join Steam (Join a Game / Join Game in Steam / Invite friends...), or this computer's address
// Buttons: Start game, Invite friends... (Steam), Cancel (closes the lobby).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    UiElement playersHeading;
    string lobbyKindShown;     // the lobby's kind as the window was built ("new" / "saved"; lobby is null once starting)

    static string OptionText(HostOption o, int value)
    {
        if (o.Names != null) return o.Names[Math.Max(0, Math.Min(o.Names.Length - 1, value))];
        if (value == 0 && o.ZeroText != null) return o.ZeroText;
        return value + (o.Unit ?? "");
    }

    void BuildLobby()
    {
        bool editable = lobby == "new";
        lobbyKindShown = lobby ?? lobbyKindShown ?? "new";
        bool saved = lobbyKindShown == "saved";
        int pad = Ui.S(12), w = Ui.S(600), line = Ui.LineHeight, gap = Ui.S(6), section = Ui.S(14);
        int inner = w - 2 * pad, labelW = inner * 11 / 20, controlW = inner - labelW - Ui.S(10);
        var place = new List<KeyValuePair<UiElement, Point>>();
        int y = 0;
        Action<UiElement, int, int> at = (e, x, yy) => place.Add(new KeyValuePair<UiElement, Point>(e, new Point(x, yy)));
        Func<string, UiElement> heading = text => { var h = Ui.Label(text, inner); Ui.SetColor(h, Mine_); return h; };
        Action<string, Color> note = (text, colour) =>
        {
            foreach (var l in Ui.Wrap(text, inner - Ui.S(10))) { var e = Ui.Label(l, inner - Ui.S(10)); Ui.SetColor(e, colour); at(e, pad + Ui.S(10), y); y += line + Ui.S(2); }
        };

        statusLabel = Ui.Label("", inner);
        at(statusLabel, pad, y); y += line + section;

        // Players
        playersHeading = heading("Players");
        at(playersHeading, pad, y); y += line + gap;
        int rows = slots.Count + 1;
        list = null; playerRows = new List<PlayerRow>();
        playersTable = PlayersTable(inner, rows, null);
        at(playersTable, pad, y); y += TableHeight(rows) + section;

        // Game settings
        at(heading(saved ? "Game settings (kept from the save)" : "Game settings"), pad, y); y += line + gap;
        Action<string, UiElement, int> row = (label, control, h) =>
        {
            int rh = Math.Max(line, h);
            at(Ui.Label(label, labelW), pad + Ui.S(10), y + (rh - line) / 2);
            if (control != null) at(control, pad + Ui.S(10) + labelW, y + (rh - h) / 2);
            y += rh + gap;
        };
        Action<string, string> textRow = (label, value) => { var v = Ui.Label(value, controlW); Ui.SetColor(v, Normal); row(label, v, line); };
        expansionTicks.Clear(); expansionLabels.Clear();
        if (editable)
        {
            // Expansions: a tick each, side by side.
            int x = pad + Ui.S(10) + labelW, rh = line;
            at(Ui.Label("Expansions", labelW), pad + Ui.S(10), y);
            foreach (var id in Expansions.Known)
            {
                var tick = Ui.TickBox(Unavailable(id) == null && !unticked.Contains(id));
                var label = Ui.Label(Expansions.Name(id), controlW - tick.Width - Ui.S(6));
                at(tick, x, y); at(label, x + tick.Width + Ui.S(6), y + Math.Max(0, (tick.Height - line) / 2));
                expansionTicks[id] = tick; expansionLabels[id] = label;
                Ui.Tooltip(tick, Expansions.Name(id) + ": everyone needs it to play this game");
                rh = Math.Max(tick.Height, line);
                y += rh + Ui.S(2);
            }
            y += gap;
        }
        else textRow("Expansions", Names(LobbyExpansions()));
        foreach (var o in options)
        {
            var opt = o;
            if (!editable) { textRow(o.Label, OptionText(o, o.Value)); continue; }
            // A choice for named options and for numbers with few steps (each shown as its value), else a slider.
            var values = o.Names != null ? Enumerable.Range(0, o.Names.Length).ToList()
                : (o.Max - o.Min) / Math.Max(1, o.Step) <= 24 ? Enumerable.Range(0, (o.Max - o.Min) / Math.Max(1, o.Step) + 1).Select(i => o.Min + i * o.Step).ToList() : null;
            if (values != null)
            {
                int index = Math.Max(0, values.IndexOf(o.Value));
                var c = Ui.Choice(controlW, values.Select(v => OptionText(opt, v)).ToList(), index);
                c.Changed += i => { opt.Value = values[Math.Max(0, Math.Min(values.Count - 1, i))]; RememberOption(opt); };
                Ui.Tooltip(c.View, "Click: next, right-click: back");
                row(o.Label, c.View, c.View.Height);
            }
            else
            {
                var sl = Ui.Slider(controlW, o.Min, o.Max, o.Value);
                sl.Step = o.Step;
                sl.Changed += v => { opt.Value = (int)Math.Round(v); RememberOption(opt); };
                row(o.Label, sl.View, sl.View.Height);
            }
        }
        y += section - gap;

        // How friends join: one line (the details in its tooltip).
        var lan = Addresses().Where(a => !a.StartsWith("127.")).ToList();
        if (listener != null)
        {
            var address = lan.Count == 0 ? "127.0.0.1" : WithPort(lan[0], listener.Port);
            var how = Ui.Label(Cut("Friends join with Co-op, Join a Game" + (Online.Available ? "" : ": " + address) + (Online.Available ? " (or the address " + address + ")" : ""), inner), inner);
            Ui.SetColor(how, Grey);
            Ui.Tooltip(how, (Online.Available ? Online.Service + " friends see your game in Join a Game, or use Join Game in " + Online.Service + ". " : "")
                + "By address: " + string.Join(", ", lan.Select(a => WithPort(a, listener.Port)).DefaultIfEmpty("127.0.0.1").ToArray())
                + ". Over the internet" + (Online.Available ? " without " + Online.Service : "") + ": forward port " + listener.Port + " on your router, or use a VPN.");
            at(how, pad, y); y += line + section;
        }

        // Buttons
        var buttons = new List<UiElement>();
        if (editable)
        {
            // The host makes their character here too (before the start), as everyone else does.
            hostCreateButton = Ui.Button(hostCharacter == null ? "Create character" : "Recreate character", CreateHostCharacter);
            Ui.Tooltip(hostCreateButton, "Your character, made in the game's character creation; you come back here when it's done");
            buttons.Add(hostCreateButton);
        }
        else hostCreateButton = null;
        var start = startButton = Ui.Button("Start game", () => { var r = StartGame(); if (r != "starting") Notify.Log("Co-op: " + r, Say); });
        buttons.Add(start);
        if (Online.Available) buttons.Add(Ui.Button("Invite friends...", () =>
        {
            if (!Online.IsHosting) { Tell("the " + Online.Service + " lobby isn't ready yet"); return; }
            if (!Online.OpenInviteDialog()) Tell("the " + Online.Service + " overlay isn't available here: friends join from Co-op, Join a Game, or Join Game in their friends list");
        }));
        var cancel = Ui.Button("Cancel", () => { StopHosting(); if (window != null) window.Close(); });     // back to the main menu
        int bh = Math.Max(start.Height, cancel.Height);

        int height = y + bh + pad;
        window = Ui.Window(this, "coop", saved ? "Host saved game: " + (lobbySave ?? "") : "Host a new game", w, 22 + Ui.S(4) + height);
        if (window == null) return;
        window.Closed += () => { window = null; builtFor = null; };
        int top = window.Top + Ui.S(4);
        foreach (var p in place) Ui.Add(window, p.Key, p.Value.X, top + p.Value.Y);
        int bx = pad, by = window.Height - pad - bh;
        foreach (var b in buttons) { Ui.Add(window, b, bx, by); bx += b.Width + gap; }
        Ui.Add(window, cancel, window.Width - pad - cancel.Width, by);
        Ui.Tooltip(start, editable ? "When everyone (you too) has a character: the game's Rules, then the game starts with everyone who joined (open places are dropped)" : "Load the save (every place must be taken)");
        Ui.Tooltip(cancel, "Close the lobby: everyone who joined is sent back");
    }

    UiElement hostCreateButton, startButton;
    string hostCharacter, hostSheet;     // a new game's lobby: the host's character, made here (its .char)
    bool creatingHost;

    void CreateHostCharacter()
    {
        if (lobby != "new") return;
        creatingHost = true; inCreation = true;
        if (!World.OpenCharacterCreation()) { creatingHost = false; Notify.Log("Co-op: couldn't open character creation", Say); }
    }

    // Start game: a new game when the host and everyone who joined have characters; a save when every place is taken.
    bool CanStart()
    {
        if (starting || lobby == null) return false;
        if (lobby == "saved") return slots.All(s => s.Seat != null);
        return hostCharacter != null && slots.Any(s => s.Seat != null) && slots.All(s => s.Seat == null || s.Seat.Record != null);
    }

    void RememberOption(HostOption o) { if (!o.NotRemembered) Settings.Set("option." + o.Mod + "." + o.Key, o.Value.ToString()); }

    void RefreshLobby(List<string> items, List<Color> colours)
    {
        int free = slots.Count(sl => sl.Seat == null), taken = slots.Count - free;
        var makingNames = slots.Where(sl => sl.Seat != null && sl.Seat.Record == null).Select(sl => sl.Seat.Peer.Name).ToList();
        bool isNew = lobbyKindShown != "saved";
        RefreshExpansionRow();
        string status; Color colour = Normal;
        if (starting) { status = "Starting the game..."; colour = Mine_; }
        else if (isNew && hostCharacter == null) status = "Create your character" + (taken == 0 ? "; friends can join meanwhile." : ".");
        else if (makingNames.Count > 0) status = "Waiting for " + string.Join(", ", makingNames.ToArray()) + " to create " + (makingNames.Count == 1 ? "a character." : "characters.");
        else if (isNew && taken == 0) status = "Waiting for friends to join.";
        else if (isNew) { status = "Everyone has a character: Start game when everyone is here."; colour = Mine_; }
        else if (free == 0) { status = "Everyone is here: Start game."; colour = Mine_; }
        else status = "Waiting for " + free + " more player" + (free == 1 ? "" : "s") + ": every saved character needs its player.";
        Ui.SetText(statusLabel, status); Ui.SetColor(statusLabel, colour);
        if (playersHeading != null) Ui.SetText(playersHeading, "Players  (" + (taken + 1) + " of " + (slots.Count + 1) + ")");
        var rowsNow = new List<PlayerRow> { new PlayerRow { Player = Online.Available && !string.IsNullOrEmpty(Online.MyName) ? Online.MyName : "You", Character = World.Player != null ? World.Player.Name : hostCharacter, Tag = "Host (you)", Me = true, Host = true } };
        foreach (var sl in slots)
        {
            if (sl.Seat != null) rowsNow.Add(new PlayerRow { Player = sl.Seat.Peer.Name, Character = sl.New ? (sl.Seat.Record == null ? null : sl.Seat.Record.Name) : sl.Saved.Name });
            else if (sl.New) rowsNow.Add(new PlayerRow { Open = true });
            else rowsNow.Add(new PlayerRow { Open = true, Character = sl.Saved.Name, Tag = "saved character" });
        }
        foreach (var s in seats.Values.Where(x => !slots.Any(sl => sl.Seat == x))) rowsNow.Add(new PlayerRow { Player = s.Peer.Name, Tag = "choosing" });
        playerRows = rowsNow;
        if (startButton != null) startButton.Enabled = CanStart();
        if (hostCreateButton != null) Ui.SetText(hostCreateButton, hostCharacter == null ? "Create character" : "Recreate character");
    }

    // Hosting, in game: who plays what (health), who left; Give... / Stop hosting. Nobody new joins a game under way;
    // a player who left comes back to their own character with Co-op, Join a Game.
    void BuildHosting()
    {
        int pad = Ui.S(12), w = Ui.S(600), line = Ui.LineHeight, gap = Ui.S(6), section = Ui.S(14), inner = w - 2 * pad;
        var place = new List<KeyValuePair<UiElement, Point>>();
        int y = 0;
        Action<UiElement, int> at = (e, yy) => place.Add(new KeyValuePair<UiElement, Point>(e, new Point(pad, yy)));
        statusLabel = Ui.Label("", inner);
        at(statusLabel, y); y += line + section;
        playersHeading = Ui.Label("Players", inner); Ui.SetColor(playersHeading, Mine_);
        at(playersHeading, y); y += line + gap;
        int rows = Math.Max(2, Math.Min(6, seats.Count + orphans.Count + 1));
        list = null; playerRows = new List<PlayerRow>();
        playersTable = PlayersTable(inner, rows, null);
        at(playersTable, y); y += TableHeight(rows) + gap;
        foreach (var l in Ui.Wrap("A player who leaves can come back to their character: Co-op, Join a Game, with the same name.", inner))
        { var e = Ui.Label(l, inner); Ui.SetColor(e, Grey); at(e, y); y += line + Ui.S(2); }
        y += section;
        var buttons = new List<UiElement>();
        if (World.Player != null) { var g = Ui.Button("Give...", OpenGive); Ui.Tooltip(g, "Give an item or charons to another player"); buttons.Add(g); }
        var stop = Ui.Button("Stop hosting", () => StopHosting()); Ui.Tooltip(stop, "End the co-op game: everyone else is sent back to their main menu"); buttons.Add(stop);
        int bh = buttons.Max(b => b.Height);
        window = Ui.Window(this, "coop", "Co-op", Math.Max(w, Ui.ButtonsWidth(buttons, pad)), 22 + Ui.S(4) + y + bh + pad);
        if (window == null) return;
        window.Closed += () => { window = null; builtFor = null; };
        int top = window.Top + Ui.S(4);
        foreach (var p in place) Ui.Add(window, p.Key, p.Value.X, top + p.Value.Y);
        Ui.AddButtons(window, buttons, pad);
    }

    void RefreshHosting(List<string> items, List<Color> colours)
    {
        int n = seats.Values.Count(s => s.Character != null);
        Ui.SetText(statusLabel, "Hosting a co-op game: " + (n == 0 ? "nobody else is playing right now." : n + (n == 1 ? " other player." : " other players.")));
        if (playersHeading != null) Ui.SetText(playersHeading, "Players  (" + (n + 1) + ")");
        Func<Character, string> hp = c => c == null ? null : c.IsDead ? "dead" : (int)c.Health + "/" + (int)c.MaxHealth + " HP";
        var me = World.Player;
        var rowsNow = new List<PlayerRow> { new PlayerRow { Player = Online.Available && !string.IsNullOrEmpty(Online.MyName) ? Online.MyName : "You", Character = me == null ? null : me.Name, Tag = (hp(me) ?? "") + "  Host", Me = true, Host = true } };
        foreach (var s in seats.Values) rowsNow.Add(new PlayerRow { Player = s.Peer.Name, Character = s.Character == null ? null : s.Character.Name, Tag = s.Character == null ? "joining" : hp(s.Character) });
        foreach (var o in orphans) rowsNow.Add(new PlayerRow { Player = (o.PlayerName ?? "?") + " (left)", Character = o.Record.Name, Tag = "passes its turns" });
        playerRows = rowsNow;
    }
}
