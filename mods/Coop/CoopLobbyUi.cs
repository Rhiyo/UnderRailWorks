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
        int rowH = line + Ui.S(6), rows = slots.Count + 1;
        list = Ui.List(inner, rows * rowH + Ui.S(6), rowH);
        at(list.View, pad, y); y += rows * rowH + Ui.S(6) + section;

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
                var label = Ui.Label(Expansions.Name(id), controlW);
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
        textRow("When a player's health runs out", DeathRules[Math.Max(0, Math.Min(DeathRules.Length - 1, deathRule))]);
        y += section - gap;

        // How friends join
        at(heading("How friends join"), pad, y); y += line + gap;
        if (Online.Available)
            note(Online.Service + " friends: Co-op, then Join a Game on the main menu, or Join Game in " + Online.Service + ".", Normal);
        var lan = Addresses().Where(a => !a.StartsWith("127.")).ToList();
        if (listener != null)
        {
            note("By address: " + (lan.Count == 0 ? "127.0.0.1" : WithPort(lan[0], listener.Port)) + (lan.Count > 1 ? "  (or " + string.Join(", ", lan.Skip(1).Select(a => WithPort(a, listener.Port)).ToArray()) + ")" : ""), Online.Available ? Grey : Normal);
            note((Online.Available ? "Over the internet without " + Online.Service : "Over the internet") + ": forward port " + listener.Port + " or use a VPN.", Grey);
        }
        y += section;

        // Buttons
        var buttons = new List<UiElement>();
        var start = Ui.Button("Start game", () => { var r = StartGame(); if (r != "starting") Notify.Log("Co-op: " + r, Say); });
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
        Ui.Tooltip(start, editable ? "Start with everyone who has joined (open places are dropped)" : "Load the save (every place must be taken)");
        Ui.Tooltip(cancel, "Close the lobby: everyone who joined is sent back");
    }

    void RememberOption(HostOption o) { if (!o.NotRemembered) Settings.Set("option." + o.Mod + "." + o.Key, o.Value.ToString()); }

    void RefreshLobby(List<string> items, List<Color> colours)
    {
        int free = slots.Count(sl => sl.Seat == null), taken = slots.Count - free;
        bool isNew = lobbyKindShown != "saved";
        RefreshExpansionRow();
        string status; Color colour = Normal;
        if (starting) { status = "Starting the game..."; colour = Mine_; }
        else if (free == 0) { status = "Everyone is here: Start game."; colour = Mine_; }
        else if (isNew) status = taken == 0 ? "Waiting for friends to join." : taken + (taken == 1 ? " player has" : " players have") + " joined. Start when everyone is here.";
        else status = "Waiting for " + free + " more player" + (free == 1 ? "" : "s") + ": every saved character needs its player.";
        Ui.SetText(statusLabel, status); Ui.SetColor(statusLabel, colour);
        if (playersHeading != null) Ui.SetText(playersHeading, "Players  (" + (taken + 1) + " of " + (slots.Count + 1) + ")");
        items.Add((World.Player != null ? World.Player.Name : "You") + "  -  host"); colours.Add(Mine_);
        foreach (var sl in slots)
        {
            if (sl.Seat != null)
            {
                var who = sl.Seat.Peer.Name;
                items.Add(sl.New ? who + "  -  " + sl.Seat.Record.Name + " (new character)" : sl.Saved.Name + "  -  " + who);
                colours.Add(Normal);
            }
            else { items.Add(sl.New ? "Open" : sl.Saved.Name + "  -  waiting for its player"); colours.Add(Grey); }
        }
        foreach (var s in seats.Values.Where(x => !slots.Any(sl => sl.Seat == x))) { items.Add(s.Peer.Name + "  -  choosing a character..."); colours.Add(Grey); }
    }
}
