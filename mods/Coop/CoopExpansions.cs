// Co-op and the game's expansions (Expedition, Heavy Duty): a hosted game uses only expansions every player has.
//   Host New Game   each expansion the host has is ticked in the lobby; the host can untick it (off for this game). One
//                   the host doesn't have, or a player in the lobby doesn't have, is greyed out and off.
//   Host Saved Game the save's expansions (the game loads a save only with them): a player without one can't join.
// Players say which expansions they have when connecting (HELLO). The lobby's set goes to every player (EXPANSIONS)
// and both sides switch the others off with Works' Expansions before the game is made or loaded (the joiner before
// making a character, so its creation offers only what the game will have); the game's saves then record only those.
// Every installed expansion is on again once the player is back on the main menu with no co-op session.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    readonly HashSet<string> unticked = new HashSet<string>(StringComparer.OrdinalIgnoreCase);     // the host's choice
    readonly Dictionary<string, UiTickBox> expansionTicks = new Dictionary<string, UiTickBox>();
    readonly Dictionary<string, UiElement> expansionLabels = new Dictionary<string, UiElement>();
    List<string> sessionExpansions;     // joiner: the lobby's set, as the host last sent it
    bool expansionsSwitched;

    static string[] ParseExpansions(string s) { return (s ?? "").Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries).Select(x => x.Trim().ToLowerInvariant()).ToArray(); }
    static string Names(IEnumerable<string> ids) { var l = ids.Select(Expansions.Name).ToList(); return l.Count == 0 ? "none" : string.Join(", ", l.ToArray()); }

    // Why an expansion is greyed out in a new game's lobby (null: available).
    string Unavailable(string id)
    {
        if (!Expansions.Installed.Contains(id)) return "you don't have it";
        var lacking = seats.Values.Where(s => s.Owns != null && !s.Owns.Contains(id)).Select(s => s.Peer.Name).ToList();
        return lacking.Count == 0 ? null : string.Join(", ", lacking.ToArray()) + (lacking.Count == 1 ? " doesn't" : " don't") + " have it";
    }

    // The expansions this hosted game uses.
    List<string> LobbyExpansions()
    {
        if (lobby == "saved") return (SaveGames.ExpansionsOf(lobbySave) ?? new List<string>()).ToList();
        return Expansions.Known.Where(id => Unavailable(id) == null && !unticked.Contains(id)).ToList();
    }

    // HELLO into a saved game's lobby: the joiner must have every expansion the save uses.
    string RefuseForExpansions(string[] owns)
    {
        if (lobby != "saved") return null;
        var missing = LobbyExpansions().Where(id => !owns.Contains(id)).ToList();
        return missing.Count == 0 ? null : "this game uses " + Names(missing) + ", which you don't have";
    }

    void SendExpansions(Peer p) { p.Send(new object[] { "EXPANSIONS" }.Concat(LobbyExpansions().Cast<object>()).ToArray()); }

    // Both sides, before the game is made or loaded: everything but the game's set off.
    void UseOnly(IEnumerable<string> ids)
    {
        var keep = new HashSet<string>(ids, StringComparer.OrdinalIgnoreCase);
        var offNow = Expansions.Installed.Where(x => !keep.Contains(x)).ToList();
        if (offNow.Count == 0 && !expansionsSwitched) return;
        if (Expansions.SwitchOff(offNow)) { expansionsSwitched = true; Log.Info("expansions for this game: " + Names(Expansions.Active) + (offNow.Count > 0 ? " (off: " + Names(offNow) + ")" : "")); }
    }

    void ExpansionsArrived(string[] w)
    {
        sessionExpansions = w.Skip(1).Select(x => x.ToLowerInvariant()).ToList();
        if (World.Player == null) UseOnly(sessionExpansions);
        Changed(false);
    }

    void LoadExpansions()
    {
        // Back on the main menu with no session: every installed expansion on again. The host's ticks are read twice a
        // second while the lobby is open (a change goes to the players).
        double timer = 0;
        GameEvents.Update.Add(this, dt =>
        {
            if ((timer -= dt) > 0) return;
            timer = 0.5;
            if (expansionsSwitched && listener == null && (host == null || host.Closed) && World.Player == null)
            {
                expansionsSwitched = false; sessionExpansions = null;
                if (Expansions.AllOn()) Log.Info("expansions: all installed ones on again (" + Names(Expansions.Installed) + ")");
            }
            if (lobby == "new" && expansionTicks.Count > 0)
            {
                bool changed = false;
                foreach (var kv in expansionTicks)
                {
                    bool available = Unavailable(kv.Key) == null;
                    if (!available) { if (kv.Value.Checked) kv.Value.Checked = false; continue; }
                    bool wasOn = !unticked.Contains(kv.Key);
                    if (kv.Value.Checked != wasOn) { if (kv.Value.Checked) unticked.Remove(kv.Key); else unticked.Add(kv.Key); changed = true; }
                }
                if (changed) LobbyChanged();
            }
        });
        Commands.Add(this, "coop.expansion", "coop.expansion <xpbl|xphw> <on|off>: in a new game's lobby, use that expansion or not", a =>
        {
            if (lobby != "new" || a.Length < 2) return "coop.expansion <xpbl|xphw> <on|off> (in a new game's lobby)";
            if (a[1] == "off") unticked.Add(a[0].ToLowerInvariant()); else unticked.Remove(a[0].ToLowerInvariant());
            foreach (var kv in expansionTicks) kv.Value.Checked = Unavailable(kv.Key) == null && !unticked.Contains(kv.Key);
            LobbyChanged();
            return "expansions for this game: " + Names(LobbyExpansions());
        });
    }

    // The lobby window's expansion row (a new game): a tick per expansion, greyed with the reason when unavailable.
    int AddExpansionRow(UiWindow w, int x, int y, int width)
    {
        expansionTicks.Clear(); expansionLabels.Clear();
        if (lobby != "new") return 0;
        Ui.Add(w, Ui.Label("Expansions:", Ui.S(110)), x, y + Ui.S(4));
        int cx = x + Ui.S(116), h = 0;
        foreach (var id in Expansions.Known)
        {
            var tick = Ui.TickBox(Unavailable(id) == null && !unticked.Contains(id));
            var label = Ui.Label(Expansions.Name(id), Ui.S(220));
            Ui.Add(w, tick, cx, y); Ui.Add(w, label, cx + tick.Width + Ui.S(4), y + Ui.S(4));
            expansionTicks[id] = tick; expansionLabels[id] = label;
            cx += tick.Width + Ui.S(130); h = Math.Max(h, tick.Height);
        }
        return h + Ui.S(6);
    }

    void RefreshExpansionRow()
    {
        foreach (var kv in expansionLabels)
        {
            var why = Unavailable(kv.Key);
            Ui.SetText(kv.Value, Expansions.Name(kv.Key) + (why == null ? "" : " (" + why + ")"));
            Ui.SetColor(kv.Value, why == null ? Normal : Grey);
            if (why != null && expansionTicks[kv.Key].Checked) expansionTicks[kv.Key].Checked = false;
        }
    }
}
