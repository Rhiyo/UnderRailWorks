// Every player's colour, used wherever that player shows: their Tab outline and minimap dot (Works' Party.SetColor),
// their name in the lobby, in conversation votes (TalkAs.Colored) and elsewhere. The host keeps the game's own colour
// for the player (gold, as holding Tab shows it); the others get pink, turquoise and violet by their place, kept for
// the session: none clashes with the game's attitude colours (red, orange, grey, green, blue, sandy brown, white for
// the hovered) or its orange interface, nor with each other.
// The host decides and sends them: COLORS <player> <character> <RRGGBB> ... (the host first).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    internal static readonly Color HostColor = Color.Gold;
    internal static readonly Color[] PlayerColors = { new Color(255, 105, 180), new Color(64, 224, 208), new Color(170, 130, 255) };

    // Host: a joined player's colour (by the place they took first, kept).
    Color ColorOf(Seat s) { return PlayerColors[Math.Max(0, s.ColorIndex) % PlayerColors.Length]; }
    void GiveColor(Seat s)
    {
        if (s.ColorIndex >= 0) return;
        var used = seats.Values.Where(x => x != s).Select(x => x.ColorIndex).ToList();
        int i = slots.FindIndex(sl => sl.Seat == s);
        if (i < 0 || used.Contains(i % PlayerColors.Length)) i = Enumerable.Range(0, PlayerColors.Length).FirstOrDefault(k => !used.Contains(k));
        s.ColorIndex = i % PlayerColors.Length;
    }

    // Both sides: player name / character name -> colour (the host's from its seats, a joiner's from COLORS).
    readonly Dictionary<string, Color> colorByPlayer = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
    readonly Dictionary<string, Color> colorByCharacter = new Dictionary<string, Color>(StringComparer.OrdinalIgnoreCase);
    internal Color? PlayerColor(string player) { Color c; return player != null && colorByPlayer.TryGetValue(player, out c) ? c : (Color?)null; }

    // Host: the colours now, kept here, set on the characters, and sent to everyone.
    void ShareColors()
    {
        if (listener == null) return;
        colorByPlayer.Clear(); colorByCharacter.Clear();
        var hostName = Online.Available && !string.IsNullOrEmpty(Online.MyName) ? Online.MyName : "the host";
        var hostChar = World.Player != null ? World.Player.Name : hostCharacter;
        colorByPlayer[hostName] = HostColor;
        if (hostChar != null) colorByCharacter[hostChar] = HostColor;
        var words = new List<object> { "COLORS", hostName, hostChar ?? "", Hex(HostColor) };
        foreach (var s in seats.Values.Concat(orphans))
        {
            GiveColor(s);
            var colour = ColorOf(s);
            var who = s.Peer != null ? s.Peer.Name : s.PlayerName;
            var name = s.Character != null ? s.Character.Name : s.Record != null ? s.Record.Name : "";
            if (who != null) colorByPlayer[who] = colour;
            if (name != "") colorByCharacter[name] = colour;
            words.Add(who ?? ""); words.Add(name); words.Add(Hex(colour));
        }
        ApplyColors();
        foreach (var s in seats.Values) if (s.Peer != null && !s.Peer.Closed) s.Peer.Send(words.ToArray());
    }

    // Joiner: COLORS from the host.
    readonly List<KeyValuePair<string, string>> colorOrder = new List<KeyValuePair<string, string>>();     // joiner: player, character, the host first
    void ColorsArrived(string[] w)
    {
        colorByPlayer.Clear(); colorByCharacter.Clear(); colorOrder.Clear();
        for (int i = 1; i + 2 < w.Length; i += 3)
        {
            colorOrder.Add(new KeyValuePair<string, string>(w[i], w[i + 1]));
            Color c; if (!ParseHex(w[i + 2], out c)) continue;
            if (w[i] != "") colorByPlayer[w[i]] = c;
            if (w[i + 1] != "") colorByCharacter[w[i + 1]] = c;
        }
        ApplyColors();
        Changed(false);
    }

    // The colours on the characters here (by name: the same characters on every machine).
    void ApplyColors()
    {
        if (World.Player == null || colorByCharacter.Count == 0) return;
        foreach (var c in World.Characters) { Color k; if (c != null && c.Name != null && colorByCharacter.TryGetValue(c.Name, out k)) Party.SetColor(this, c, k); }
    }

    // A player's name in their colour, for the game's rich text (conversations).
    string ColoredName(string player) { var c = PlayerColor(player); return c.HasValue ? TalkAs.Colored(player, c.Value) : player; }

    static string Hex(Color c) { return c.R.ToString("X2") + c.G.ToString("X2") + c.B.ToString("X2"); }
    static bool ParseHex(string s, out Color c)
    {
        c = Color.White;
        if (s == null || s.Length != 6) return false;
        try { c = new Color(Convert.ToInt32(s.Substring(0, 2), 16), Convert.ToInt32(s.Substring(2, 2), 16), Convert.ToInt32(s.Substring(4, 2), 16)); return true; }
        catch { return false; }
    }

    void LoadColors()
    {
        // Characters come and go (zones, spawns): the colours on them again now and then; the host shares changes.
        double timer = 0;
        GameEvents.Update.Add(this, dt =>
        {
            if ((timer -= dt) > 0) return;
            timer = 1;
            if (listener != null) { var sign = string.Join(",", seats.Values.Select(s => (s.Peer == null ? "" : s.Peer.Name) + ":" + (s.Character == null ? "" : s.Character.Name) + ":" + s.ColorIndex).ToArray()) + "|" + (World.Player == null ? hostCharacter : World.Player.Name); if (sign != colorSign) { colorSign = sign; ShareColors(); } else ApplyColors(); }
            else if (host != null && !host.Closed) ApplyColors();
        });
        GameEvents.ZoneLoaded.Add(this, (zone, first) => ApplyColors());
        // A joiner's own End Turn key (the game's setting): their turn ends on the host (this game is a copy: its own
        // combat doesn't run).
        GameEvents.Update.Add(this, dt =>
        {
            var s = state;
            if (listener != null || !playing || s == null || s.Length < 6 || s[4] != "1" || s[5] != s[1] || Input.Typing) return;
            var key = Keybinds.GameKey("EndTurn");
            if (key != Microsoft.Xna.Framework.Input.Keys.None && Input.Pressed(key)) Run("END");
        });
    }
    string colorSign;
}
