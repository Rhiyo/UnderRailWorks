// Co-op conversations (0.0.1): every conversation is the host's. A joiner clicking an NPC starts that NPC's own
// conversation with the host's character, from where everyone stands (Works' Dialogs.StartOwn); the host answers in the
// game's own window, so the game's checks, quests and story see one protagonist, as it was written. Every joiner follows
// the conversation in their Conversation window and can suggest an answer (click it; click it again to take it back):
// the host's window shows "[Mira]" after that answer (Works' TalkAs.AnswerText), and so does every player's window.
// Messages, host to joiner: DLGW <talker> <speaker> <text>, DLGOPT <index> <1|0 enabled> <text>..., DLGSHOW, DLGEND,
// DLGSUGG <index>=<name,name>... (the suggestions now). Joiner to host: TALK <key>, DLGSUGGEST <index>.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    // --- Host ---------------------------------------------------------------------------------------------------
    Seat talking;     // kept for older paths: nobody but the host holds a conversation now
    bool hostTalking;     // the host's conversation is open
    readonly Dictionary<string, int> suggested = new Dictionary<string, int>();     // player name -> answer index
    List<int> hostChoices = new List<int>();     // the line's answers' indexes, in the window's order
    List<string> hostChoiceTexts = new List<string>();

    // A joiner clicked an NPC: its conversation with the host's character.
    string Talk(Character c, string key)
    {
        var seat = seats.Values.FirstOrDefault(s => s.Character == c);
        var npc = Resolve(key);
        if (seat == null || npc == null || npc.IsDead) return "nobody to talk to there";
        if (hostTalking || TalkAs.IsOpen) return "a conversation is already going";
        if (Combat.InCombat) return "not in combat";
        var me = World.Player;
        if (me == null || me.IsDead || downed.Contains(me)) return "the host can't talk now";
        if (!Dialogs.StartOwn(npc)) return "can't talk to " + npc.Name + " now";
        Log.Info(seat.Peer.Name + " started a conversation with " + npc.Name + " (the host answers)");
        Notify.Log("Co-op: " + seat.Peer.Name + " started a conversation with " + npc.Name + ": you choose the answers; the others can suggest one", Say);
        return npc.Name + " talks with " + me.Name + ": the host answers. Click an answer to suggest it.";
    }

    // DLGSUGGEST <index>: a joiner's suggestion (the same one again takes it back).
    string Suggest(Character c, string index)
    {
        var seat = seats.Values.FirstOrDefault(s => s.Character == c);
        int i;
        if (seat == null || !hostTalking || !int.TryParse(index, out i) || !hostChoices.Contains(i)) return "";
        var name = seat.Peer.Name;
        int was;
        if (suggested.TryGetValue(name, out was) && was == i) suggested.Remove(name); else suggested[name] = i;
        Log.Info(name + (suggested.ContainsKey(name) ? " suggests answer " + (hostChoices.IndexOf(i) + 1) : " took back their suggestion"));
        TalkAs.RefreshAnswers();
        SendSuggestions();
        return "";
    }

    void SendSuggestions()
    {
        var words = new List<object> { "DLGSUGG" };
        foreach (var g in suggested.GroupBy(kv => kv.Value)) words.Add(g.Key + "=" + string.Join(",", g.Select(kv => kv.Key).ToArray()));
        foreach (var s in seats.Values.Where(x => x.InWorld && x.Peer != null)) s.Peer.Send(words.ToArray());
    }

    // Every line of the host's conversation goes to the joiners, to follow and suggest.
    void LoadListening()
    {
        GameEvents.ConversationLine.Add(this, line =>
        {
            if (listener == null) return;
            // Every conversation shown in the host's game window is the host's to answer: one the host or a joiner's click
            // started, or one a trigger started with whoever stepped in (Party.TriggersCountMembers).
            hostTalking = true;
            suggested.Clear();
            hostChoices = line.Choices.Select(o => o.Index).ToList();
            hostChoiceTexts = line.Choices.Select(o => o.Text).ToList();
            var talker = line.Talker == null ? "?" : line.Talker.Name;
            foreach (var s in seats.Values.Where(x => x.InWorld && x.Peer != null))
            {
                s.Peer.Send("DLGW", talker, line.Speaker, line.Text);
                foreach (var o in line.Choices) s.Peer.Send("DLGOPT", o.Index, o.Enabled ? 1 : 0, o.Text);
                s.Peer.Send("DLGSHOW");
            }
        });
        GameEvents.DialogEnded.Add(this, name =>
        {
            if (listener == null) return;
            hostTalking = false; suggested.Clear(); hostChoices.Clear(); hostChoiceTexts.Clear();
            foreach (var s in seats.Values.Where(x => x.InWorld && x.Peer != null)) s.Peer.Send("DLGEND");
        });
        // The host's window: who suggested each answer, after it.
        TalkAs.AnswerText.Add(this, (index, text) =>
        {
            if (listener == null || !hostTalking || index < 0 || index >= hostChoices.Count || hostChoiceTexts[index] != text) return text;
            var names = suggested.Where(kv => kv.Value == hostChoices[index]).Select(kv => kv.Key).ToList();
            return names.Count == 0 ? text : text + "   [" + string.Join(", ", names.ToArray()) + "]";
        });
    }

    // --- Joiner -------------------------------------------------------------------------------------------------
    UiWindow talkWindow;
    bool talkEnding;
    string talkWatching;     // whose conversation it is (the host's)
    UiList talkList;
    string talkSpeaker, talkText;
    readonly List<KeyValuePair<int, string>> talkChoices = new List<KeyValuePair<int, string>>();
    readonly List<bool> talkEnabled = new List<bool>();
    readonly List<int> rowChoice = new List<int>();     // list row -> choice index (-2: text)
    readonly Dictionary<int, string> talkSuggested = new Dictionary<int, string>();     // choice index -> names
    int mySuggestion = -1;

    bool TalkMessage(string[] w)
    {
        switch (w[0])
        {
            case "DLGW":
                talkWatching = w.Length > 1 ? w[1] : "?"; talkSpeaker = w.Length > 2 ? w[2] : ""; talkText = w.Length > 3 ? w[3] : "";
                talkChoices.Clear(); talkEnabled.Clear(); talkSuggested.Clear(); mySuggestion = -1;
                return true;
            case "DLGOPT":
            {
                int i; if (w.Length < 4 || !int.TryParse(w[1], out i)) return true;
                talkChoices.Add(new KeyValuePair<int, string>(i, w[3])); talkEnabled.Add(w[2] == "1");
                return true;
            }
            case "DLGSUGG":
                talkSuggested.Clear();
                foreach (var pair in w.Skip(1))
                {
                    int eq = pair.IndexOf('='); int i;
                    if (eq > 0 && int.TryParse(pair.Substring(0, eq), out i)) talkSuggested[i] = pair.Substring(eq + 1);
                }
                if (talkWindow != null && talkWindow.IsOpen) ShowTalk();
                return true;
            case "DLGSHOW": Log.Info("conversation: " + talkSpeaker + ": " + talkText + " (" + talkChoices.Count + " answers)"); ShowTalk(); return true;
            case "DLGEND": talkEnding = true; if (talkWindow != null && talkWindow.IsOpen) talkWindow.Close(); talkWindow = null; talkEnding = false; mySuggestion = -1; return true;
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
            talkWindow.Closed += () => { talkWindow = null; };
            w = talkWindow.Width; h = talkWindow.Height;
            int listTop = talkWindow.Top + Ui.S(4);
            talkList = Ui.List(w - 2 * pad, h - listTop - pad);
            talkList.Selected += row => { if (row >= 0 && row < rowChoice.Count && rowChoice[row] >= 0) SuggestAnswer(rowChoice[row]); };
            Ui.Add(talkWindow, talkList.View, pad, listTop);
        }
        var items = new List<string>(); var colours = new List<Color>();
        rowChoice.Clear();
        int width = talkList.View.Width - Ui.S(12);
        items.Add((talkWatching ?? "The host") + " is talking. Click an answer to suggest it (again to take it back):"); colours.Add(Grey); rowChoice.Add(-2);
        foreach (var l in Ui.Wrap((string.IsNullOrEmpty(talkSpeaker) ? "" : talkSpeaker + ": ") + talkText, width)) { items.Add(l); colours.Add(Normal); rowChoice.Add(-2); }
        items.Add(""); colours.Add(Normal); rowChoice.Add(-2);
        for (int i = 0; i < talkChoices.Count; i++)
        {
            int index = talkChoices[i].Key;
            string who; talkSuggested.TryGetValue(index, out who);
            var text = (i + 1) + ". " + talkChoices[i].Value + (string.IsNullOrEmpty(who) ? "" : "   [" + who + "]");
            var colour = !talkEnabled[i] ? Grey : index == mySuggestion ? Mine_ : Normal;
            foreach (var l in Ui.Wrap(text, width).Select((t, n) => n == 0 ? t : "    " + t))
            { items.Add(l); colours.Add(colour); rowChoice.Add(talkEnabled[i] ? index : -2); }
        }
        if (talkChoices.Count == 0) { items.Add("(the host goes on)"); colours.Add(Grey); rowChoice.Add(-2); }
        talkList.Items = items;
        talkList.ItemColors = colours;
        talkList.Selection = -1;
        talkList.First = 0;
    }

    void SuggestAnswer(int index)
    {
        if (host == null || host.Closed) return;
        mySuggestion = mySuggestion == index ? -1 : index;
        host.Send("DLGSUGGEST", index);
        ShowTalk();
    }
}
