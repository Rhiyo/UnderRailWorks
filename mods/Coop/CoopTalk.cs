// Co-op conversations. The joiner clicks an NPC: the host's game runs the conversation for the joiner's character
// (Works' TalkAs: its own sheet answers the checks, the host's window stays hidden and the host keeps playing) and sends
// each line; the joiner's Conversation window shows it and sends the answer back.
// Messages, host to joiner: DLG <speaker> <text>, DLGOPT <index> <1|0 enabled> <text>..., DLGSHOW, DLGEND.
// Joiner to host: TALK <key>, DLGPICK <index> (-1: go on / end).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    // --- Host ---------------------------------------------------------------------------------------------------
    Seat talking;     // whose conversation is running (one at a time, as in the game)

    string Talk(Character c, string key)
    {
        var seat = seats.Values.FirstOrDefault(s => s.Character == c);
        var npc = Resolve(key);
        if (seat == null || npc == null || npc.IsDead) return "nobody to talk to there";
        if (!c.HasPlayerSheet) return "only characters made in the game's creation can talk";
        if (TalkAs.IsOpen) return "someone is already in a conversation";
        if (Combat.InCombat) return "not in combat";
        var peer = seat.Peer;
        bool ok = TalkAs.Talk(c, npc, line =>
        {
            peer.Send("DLG", line.Speaker, line.Text);
            foreach (var o in line.Choices) peer.Send("DLGOPT", o.Index, o.Enabled ? 1 : 0, o.Text);
            peer.Send("DLGSHOW");
        }, () => { peer.Send("DLGEND"); if (talking == seat) talking = null; });
        if (!ok) return "can't talk to " + npc.Name + " now";
        talking = seat;
        Log.Info(c.Name + " talks to " + npc.Name);
        return "going to talk to " + npc.Name;
    }

    string Pick(Character c, string index)
    {
        int i;
        if (talking == null || talking.Character != c) return "you're not in a conversation";
        if (!int.TryParse(index, out i)) return "";
        return TalkAs.Choose(i) ? "" : "you can't say that";
    }

    // --- Joiner -------------------------------------------------------------------------------------------------
    UiWindow talkWindow;
    bool talkEnding;
    UiList talkList;
    string talkSpeaker, talkText;
    readonly List<KeyValuePair<int, string>> talkChoices = new List<KeyValuePair<int, string>>();
    readonly List<bool> talkEnabled = new List<bool>();
    readonly List<int> rowChoice = new List<int>();     // list row -> choice index (-2: text)

    bool TalkMessage(string[] w)
    {
        switch (w[0])
        {
            case "DLG": talkSpeaker = w.Length > 1 ? w[1] : ""; talkText = w.Length > 2 ? w[2] : ""; talkChoices.Clear(); talkEnabled.Clear(); return true;
            case "DLGOPT":
            {
                int i; if (w.Length < 4 || !int.TryParse(w[1], out i)) return true;
                talkChoices.Add(new KeyValuePair<int, string>(i, w[3])); talkEnabled.Add(w[2] == "1");
                return true;
            }
            case "DLGSHOW": ShowTalk(); return true;
            case "DLGEND": talkEnding = true; if (talkWindow != null && talkWindow.IsOpen) talkWindow.Close(); talkWindow = null; talkEnding = false; return true;
        }
        return false;
    }

    void ShowTalk()
    {
        if (talkWindow == null || !talkWindow.IsOpen)
        {
            int w = Ui.S(640), h = Ui.S(420), pad = Ui.S(10);
            talkWindow = Ui.Window(this, "talk", "Conversation", w, h);
            if (talkWindow == null) return;
            // Closed by the player (not by the conversation ending): it ends on the host too.
            talkWindow.Closed += () => { talkWindow = null; if (!talkEnding && host != null && !host.Closed) host.Send("DLGCLOSE"); };
            w = talkWindow.Width; h = talkWindow.Height;
            var go = Ui.Button("Continue", () => Answer(-1));
            int listTop = talkWindow.Top + Ui.S(4);
            talkList = Ui.List(w - 2 * pad, h - listTop - pad - go.Height - Ui.S(8));
            talkList.Selected += row => { if (row >= 0 && row < rowChoice.Count && rowChoice[row] >= 0) Answer(rowChoice[row]); };
            Ui.Add(talkWindow, talkList.View, pad, listTop);
            Ui.Add(talkWindow, go, pad, h - pad - go.Height);
        }
        var items = new List<string>(); var colours = new List<Color>();
        rowChoice.Clear();
        int width = talkList.View.Width - Ui.S(12);
        foreach (var l in Ui.Wrap((string.IsNullOrEmpty(talkSpeaker) ? "" : talkSpeaker + ": ") + talkText, width)) { items.Add(l); colours.Add(Normal); rowChoice.Add(-2); }
        items.Add(""); colours.Add(Normal); rowChoice.Add(-2);
        for (int i = 0; i < talkChoices.Count; i++)
            foreach (var l in Ui.Wrap((i + 1) + ". " + talkChoices[i].Value, width).Select((t, n) => n == 0 ? t : "    " + t))
            { items.Add(l); colours.Add(talkEnabled[i] ? Mine_ : Grey); rowChoice.Add(talkEnabled[i] ? talkChoices[i].Key : -2); }
        talkList.Items = items;
        talkList.ItemColors = colours;
        talkList.Selection = -1;
        talkList.First = 0;
    }

    void Answer(int index)
    {
        if (host == null || host.Closed) return;
        if (index == -1 && talkChoices.Count > 0) { Tell("pick an answer"); return; }
        host.Send("DLGPICK", index);
    }
}
