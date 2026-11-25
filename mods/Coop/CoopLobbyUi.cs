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
        int line = Ui.LineHeight, valueW = LobbyValueWidth;
        var v = new LobbyView { Title = saved ? "Host saved game: " + (lobbySave ?? "") : "Host a new game", Rows = slots.Count + 1 };

        // Your character, under the players (a new game: the host makes one here too, as everyone does).
        hostCreateButton = null;
        if (editable)
        {
            hostCreateButton = Ui.Button(hostCharacter == null ? "Create character" : "Recreate character", CreateHostCharacter);
            Ui.Tooltip(hostCreateButton, "Your character, made in the game's character creation; you come back here when it's done");
            v.PlayerAction = hostCreateButton;
        }

        // Game settings (a new game's rules: Edit settings opens the game's Rules screen again).
        v.SettingsHeading = saved ? "Game settings (kept from the save)" : "Game settings";
        if (editable && hostRules != null) { v.SettingsAction = Ui.Button("Edit settings", EditRules); Ui.Tooltip(v.SettingsAction, "The game's rules again (difficulty, experience system)"); }
        if (editable && hostRules != null) { v.Settings.Add(Pair("Difficulty", LobbyValue(hostRules.Difficulty))); v.Settings.Add(Pair("Experience", LobbyValue(hostRules.Experience))); }
        expansionTicks.Clear(); expansionLabels.Clear();
        if (editable)
        {
            // Expansions: a tick each, one under the other.
            var ticks = new List<KeyValuePair<UiElement, UiElement>>();
            foreach (var id in Expansions.Known)
            {
                var tick = Ui.TickBox(Unavailable(id) == null && !unticked.Contains(id));
                var label = Ui.Label(Expansions.Name(id), valueW - tick.Width - Ui.S(6));
                Ui.Tooltip(tick, Expansions.Name(id) + ": everyone needs it to play this game");
                expansionTicks[id] = tick; expansionLabels[id] = label;
                ticks.Add(new KeyValuePair<UiElement, UiElement>(tick, label));
            }
            v.Settings.Add(new LobbySetting { Label = "Expansions", Ticks = ticks });
        }
        else
        {
            var difficulty = lobbySave == null ? null : SaveGames.DifficultyOf(lobbySave);
            if (difficulty != null) v.Settings.Add(Pair("Difficulty", LobbyValue(difficulty)));
            v.Settings.Add(Pair("Expansions", LobbyValue(Names(LobbyExpansions()))));
        }
        foreach (var o in options)
        {
            var opt = o;
            if (!editable) { v.Settings.Add(Pair(o.Label, LobbyValue(OptionText(o, o.Value)))); continue; }
            // A choice for named options and for numbers with few steps (each shown as its value), else a slider.
            var values = o.Names != null ? Enumerable.Range(0, o.Names.Length).ToList()
                : (o.Max - o.Min) / Math.Max(1, o.Step) <= 24 ? Enumerable.Range(0, (o.Max - o.Min) / Math.Max(1, o.Step) + 1).Select(i => o.Min + i * o.Step).ToList() : null;
            if (values != null)
            {
                int index = Math.Max(0, values.IndexOf(o.Value));
                var c = Ui.Choice(valueW, values.Select(x => OptionText(opt, x)).ToList(), index);
                c.Changed += i => { opt.Value = values[Math.Max(0, Math.Min(values.Count - 1, i))]; RememberOption(opt); };
                Ui.Tooltip(c.View, "Click: next, right-click: back");
                v.Settings.Add(Pair(o.Label, c.View));
            }
            else
            {
                var sl = Ui.Slider(valueW, o.Min, o.Max, o.Value);
                sl.Step = o.Step;
                sl.Changed += x => { opt.Value = (int)Math.Round(x); RememberOption(opt); };
                v.Settings.Add(Pair(o.Label, sl.View));
            }
        }

        // How friends join: one line (the details in its tooltip).
        var lan = Addresses().Where(a => !a.StartsWith("127.")).ToList();
        if (listener != null)
        {
            var address = lan.Count == 0 ? "127.0.0.1" : WithPort(lan[0], listener.Port);
            v.Note = "Friends join with Co-op, Join a Game" + (Online.Available ? " (or the address " + address + ")" : ": " + address);
            v.NoteTip = (Online.Available ? Online.Service + " friends see your game in Join a Game, or use Join Game in " + Online.Service + ". " : "")
                + "By address: " + string.Join(", ", lan.Select(a => WithPort(a, listener.Port)).DefaultIfEmpty("127.0.0.1").ToArray())
                + ". Over the internet" + (Online.Available ? " without " + Online.Service : "") + ": forward port " + listener.Port + " on your router, or use a VPN.";
        }

        // Start game (and Invite friends...) on the left, Cancel on the right.
        startButton = Ui.Button("Start game", () => { var r = StartGame(); if (r != "starting") Notify.Log("Co-op: " + r, Say); });
        Ui.Tooltip(startButton, editable ? "When everyone (you too) has a character: the game starts with these settings and everyone who joined (open places are dropped)" : "Load the save (every place must be taken)");
        v.Left.Add(startButton);
        if (Online.Available) v.Left.Add(Ui.Button("Invite friends...", () =>
        {
            if (!Online.IsHosting) { Tell("the " + Online.Service + " lobby isn't ready yet"); return; }
            if (!Online.OpenInviteDialog()) Tell("the " + Online.Service + " overlay isn't available here: friends join from Co-op, Join a Game, or Join Game in their friends list");
        }));
        v.Right = Ui.Button("Cancel", () => { StopHosting(); if (window != null) window.Close(); if (coopPage != null && MainMenu.IsShowing) coopPage.Open(); });     // back to the Co-op page
        Ui.Tooltip(v.Right, "Close the lobby: everyone who joined is sent back");
        BuildLobbyWindow(v);
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
        var rowsNow = new List<PlayerRow> { new PlayerRow { Player = Online.Available && !string.IsNullOrEmpty(Online.MyName) ? Online.MyName : "You", Character = World.Player != null ? World.Player.Name : hostCharacter, Tag = "Host (you)", Me = true, Host = true, Color = HostColor } };
        foreach (var sl in slots)
        {
            if (sl.Seat != null) { GiveColor(sl.Seat); rowsNow.Add(new PlayerRow { Player = sl.Seat.Peer.Name, Character = sl.New ? (sl.Seat.Record == null ? null : sl.Seat.Record.Name) : sl.Saved.Name, Color = ColorOf(sl.Seat) }); }
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
        var left = new List<UiElement>();
        if (World.Player != null) { var g = Ui.Button("Give...", OpenGive); Ui.Tooltip(g, "Give an item or charons to another player"); left.Add(g); }
        var stop = Ui.Button("Stop hosting", () => StopHosting()); Ui.Tooltip(stop, "End the co-op game: everyone else is sent back to their main menu");
        BuildInGameWindow(Math.Max(2, Math.Min(6, seats.Count + orphans.Count + 1)), "A player who leaves can come back to their character: Co-op, Join a Game, with the same name.", left, stop);
    }

    void RefreshHosting(List<string> items, List<Color> colours)
    {
        int n = seats.Values.Count(s => s.Character != null);
        Ui.SetText(statusLabel, "Hosting a co-op game: " + (n == 0 ? "nobody else is playing right now." : n + (n == 1 ? " other player." : " other players.")));
        if (playersHeading != null) Ui.SetText(playersHeading, "Players  (" + (n + 1) + ")");
        Func<Character, string> hp = c => c == null ? null : c.IsDead ? "dead" : (int)c.Health + "/" + (int)c.MaxHealth + " HP";
        var me = World.Player;
        var rowsNow = new List<PlayerRow> { new PlayerRow { Player = Online.Available && !string.IsNullOrEmpty(Online.MyName) ? Online.MyName : "You", Character = me == null ? null : me.Name, Tag = (hp(me) ?? "") + "  Host", Me = true, Host = true, Color = HostColor } };
        foreach (var s in seats.Values) { GiveColor(s); rowsNow.Add(new PlayerRow { Player = s.Peer.Name, Character = s.Character == null ? null : s.Character.Name, Tag = s.Character == null ? "joining" : hp(s.Character), Color = ColorOf(s) }); }
        foreach (var o in orphans) rowsNow.Add(new PlayerRow { Player = (o.PlayerName ?? "?") + " (left)", Character = o.Record.Name, Tag = "passes its turns" });
        playerRows = rowsNow;
    }
}
