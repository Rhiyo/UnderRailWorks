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
    readonly List<object[]> lastLine = new List<object[]>();     // its current line, for a joiner arriving meanwhile
    // Line breaks through Co-op's one-line messages (a conversation's paragraphs): \u001e there, back here.
    // (\r kept as it was: the game's dialogs have \r\n, and its narration layout counts on them.)
    static string Lines(string text) { return (text ?? "").Replace('\r', '\u001d').Replace('\n', '\u001e'); }
    static string Unlines(string text) { return (text ?? "").Replace('\u001d', '\r').Replace('\u001e', '\n'); }
    Character convoFocus;     // who started the next conversation (the view goes to them)
    bool wasTalking;
    Vector3? lastCam;     // the host's camera as last sent in a conversation

    // In a conversation: the host's camera to the joiners when it moves (everyone sees the same spot).
    void SendCamera()
    {
        if (!hostTalking) return;
        var p = Camera.Position;
        if (p == null || (lastCam != null && Vector3.Distance(lastCam.Value, p.Value) < 0.05f)) return;
        lastCam = p;
        var ci = System.Globalization.CultureInfo.InvariantCulture;
        foreach (var s in seats.Values.Where(x => x.InWorld && x.Peer != null)) s.Peer.Send("CAM", p.Value.X.ToString("0.###", ci), p.Value.Y.ToString("0.###", ci), p.Value.Z.ToString("0.###", ci));
    }
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
        convoFocus = c;
        if (!Dialogs.StartOwn(npc)) { convoFocus = null; return "can't talk to " + npc.Name + " now"; }
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
            // The conversation's first line: the view on whoever started it (a joiner who clicked, or a member who set
            // off its trigger), for everyone (the host's camera goes to the joiners while it's open).
            if (!wasTalking && convoFocus != null && convoFocus.Area != null) { Camera.CenterOn(convoFocus); Log.Info("conversation: the view on " + convoFocus.Name + ", who started it"); }
            convoFocus = null; wasTalking = true;
            lastLine.Clear();
            lastLine.Add(new object[] { "DLGW", talker, line.Speaker, Lines(line.RawText ?? line.Text), line.SpeakerCharacter == null ? "" : KeyOf(line.SpeakerCharacter) });
            foreach (var o in line.Choices) lastLine.Add(new object[] { "DLGOPT", o.Index, o.Enabled ? 1 : 0, Lines(o.Text) });
            lastLine.Add(new object[] { "DLGSHOW" });
            foreach (var s in seats.Values.Where(x => x.InWorld && x.Peer != null)) foreach (var l in lastLine) s.Peer.Send(l);
        });
        // A conversation a member set off (a map trigger run for them): the view goes to them.
        GameEvents.DialogStarted.Add(this, (name, npc) => { if (listener != null && Party.TriggeringCharacter != null) convoFocus = Party.TriggeringCharacter; });
        GameEvents.DialogEnded.Add(this, name =>
        {
            if (listener == null) return;
            hostTalking = false; suggested.Clear(); hostChoices.Clear(); hostChoiceTexts.Clear(); lastLine.Clear(); wasTalking = false; lastCam = null;
            foreach (var s in seats.Values.Where(x => x.InWorld && x.Peer != null)) s.Peer.Send("DLGEND");
        });
        // The host's window: who suggested each answer, after it.
        TalkAs.AnswerText.Add(this, (index, text) =>
        {
            if (listener == null && talkActive)
            {
                // The joiner's window: the votes after each answer, as on the host's.
                if (index < 0 || index >= talkChoices.Count) return text;
                string voters; talkSuggested.TryGetValue(talkChoices[index].Key, out voters);
                return string.IsNullOrEmpty(voters) ? text : text + "   [" + voters.Replace(",", ", ") + "]";     // "Alice, Bob" as on the host's
            }
            if (listener == null || !hostTalking || index < 0 || index >= hostChoices.Count || hostChoiceTexts[index] != text) return text;
            var names = suggested.Where(kv => kv.Value == hostChoices[index]).Select(kv => kv.Key).ToList();
            return names.Count == 0 ? text : text + "   [" + string.Join(", ", names.ToArray()) + "]";
        });
    }

    // --- Joiner -------------------------------------------------------------------------------------------------
    UiWindow talkWindow;
    bool talkActive;     // the host's conversation is going (its window may have gone with a HUD rebuild: shown again)
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
                talkWatching = w.Length > 1 ? w[1] : "?"; talkSpeaker = w.Length > 2 ? w[2] : ""; talkText = w.Length > 3 ? Unlines(w[3]) : ""; talkSpeakerKey = w.Length > 4 ? w[4] : "";
                talkChoices.Clear(); talkEnabled.Clear(); talkSuggested.Clear(); mySuggestion = -1;
                return true;
            case "DLGOPT":
            {
                int i; if (w.Length < 4 || !int.TryParse(w[1], out i)) return true;
                talkChoices.Add(new KeyValuePair<int, string>(i, Unlines(w[3]))); talkEnabled.Add(w[2] == "1");
                return true;
            }
            case "DLGSUGG":
                talkSuggested.Clear();
                foreach (var pair in w.Skip(1))
                {
                    int eq = pair.IndexOf('='); int i;
                    if (eq > 0 && int.TryParse(pair.Substring(0, eq), out i)) talkSuggested[i] = pair.Substring(eq + 1);
                }
                if (TalkAs.IsShown) TalkAs.RefreshAnswers();
                else if (talkWindow != null && talkWindow.IsOpen) ShowTalk();
                return true;
            case "DLGSHOW": talkActive = true; Log.Info("conversation: " + talkSpeaker + ": " + talkText + " (" + talkChoices.Count + " answers)"); ShowConversation(); return true;
            case "DLGEND":
                talkActive = false; talkEnding = true; if (talkWindow != null && talkWindow.IsOpen) talkWindow.Close(); talkWindow = null; talkEnding = false; mySuggestion = -1;
                openPending = false;
                if (TalkAs.IsShown) TalkAs.CloseShown();
                hostCamera = null;
                if (World.Player != null) { Camera.Follow(World.Player); Camera.CenterOn(World.Player); }     // back to your own character
                return true;
            case "CAM":
            {
                // The host's camera in a conversation: the same spot here.
                if (!talkActive || w.Length < 4) return true;
                var ci = System.Globalization.CultureInfo.InvariantCulture; float x, y, z;
                if (float.TryParse(w[1], System.Globalization.NumberStyles.Float, ci, out x) && float.TryParse(w[2], System.Globalization.NumberStyles.Float, ci, out y) && float.TryParse(w[3], System.Globalization.NumberStyles.Float, ci, out z))
                    { hostCamera = new Vector3(x, y, z); Camera.Position = hostCamera; }
                return true;
            }
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
        foreach (var l in Ui.Wrap((string.IsNullOrEmpty(talkSpeaker) ? "" : talkSpeaker + ": ") + Narration(talkText), width)) { items.Add(l); colours.Add(Normal); rowChoice.Add(-2); }
        items.Add(""); colours.Add(Normal); rowChoice.Add(-2);
        for (int i = 0; i < talkChoices.Count; i++)
        {
            int index = talkChoices[i].Key;
            string who; talkSuggested.TryGetValue(index, out who);
            var text = (i + 1) + ". " + Plain(talkChoices[i].Value) + (string.IsNullOrEmpty(who) ? "" : "   [" + who + "]");
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

    // A line as the game shows it: narration between "::" marks (the game shows it in another colour) as plain text.
    static string Narration(string text) { return System.Text.RegularExpressions.Regex.Replace((text ?? "").Replace("::", " "), @"\s{2,}", " ").Trim(); }

    // An answer as the game shows it: its markup off (meta answers like ::Continue: show as Continue).
    static string Plain(string text)
    {
        var t = (text ?? "").Trim();
        if (t.StartsWith("::")) t = t.Substring(2);
        if (t.EndsWith(":") && t.Length > 1 && !t.EndsWith("::")) t = t.Substring(0, t.Length - 1);
        return t.Trim();
    }

    void SuggestAnswer(int index)
    {
        if (host == null || host.Closed) return;
        mySuggestion = mySuggestion == index ? -1 : index;
        host.Send("DLGSUGGEST", index);
        if (TalkAs.IsShown && talkActive) TalkAs.RefreshAnswers(); else ShowTalk();
    }

    // --- The joiner's view of the host's conversation: the game's own window ------------------------------------
    // Each line of the host's conversation is shown here as the game shows it there (portrait, name, text, answers in
    // the game's colours): a one-line conversation with the same speaker, built with Works' Dialogs. An answer clicked
    // here is a vote: it goes to the host (DLGSUGGEST) and the line stays; every voter's name is shown after the answer,
    // on every player's window (TalkAs.AnswerText). The host's answer moves everyone on. The camera is the host's.
    string talkSpeakerKey;
    Vector3? hostCamera;     // the host's camera in the conversation: kept here every frame (this window's own opening moves it)
    bool dialogShown, openPending, waitLogged;
    Character lastSpeaker;

    Character SpeakerHere()
    {
        Character c = null;
        if (!string.IsNullOrEmpty(talkSpeakerKey)) { if (!spawned.TryGetValue(talkSpeakerKey, out c) && !talkSpeakerKey.StartsWith("c:")) c = World.ByEntityId(talkSpeakerKey); }
        if (c != null && c.Area != null) lastSpeaker = c;
        return lastSpeaker != null && lastSpeaker.Area != null ? lastSpeaker : null;
    }

    void ShowConversation()
    {
        var npc = SpeakerHere();
        if (npc == null || World.Player == null) { ShowTalk(); return; }     // nobody to show it with: the plain window
        if (talkWindow != null && talkWindow.IsOpen) talkWindow.Close();
        var text = talkText; var choices = talkChoices.ToList(); var enabled = talkEnabled.ToList();
        Dialogs.Add(this, "coop/listen", d =>
        {
            d.Cancellable(false);     // as the host's: no close button (it ends when the host's does)
            var l = d.Line("l", text);
            if (choices.Count == 0) l.Next("l");     // the host goes on (Continue): here it stays until the next line
            for (int i = 0; i < choices.Count; i++)
            {
                int index = choices[i].Key;
                if (enabled[i]) l.Option(choices[i].Value, "l", Outcome.Code(() => SuggestAnswer(index)));
                else l.OptionGreyed(choices[i].Value, "l", Condition.Code(() => false));
            }
        });
        // The previous line's window closes; the new one opens on a later frame, once no window is up (asked of the
        // game, TalkAs.IsShown: a flag of our own went wrong when a close's event came after the next opening).
        openPending = true;
        if (TalkAs.IsShown) TalkAs.CloseShown();
    }

    void OpenConversation()
    {
        if (!openPending || !talkActive) return;
        // Not before this machine's own character is in place: making it the player builds the HUD again, and the
        // conversation's window would go with the old one. (That rebuild is done once it's followed; the view's
        // centring after it waits for the HUD, which a conversation hides: not a reason to wait.)
        if (followed == null) { if (!waitLogged) { waitLogged = true; Log.Info("conversation: waiting for this machine's character before showing it"); } return; }
        waitLogged = false;
        var npc = SpeakerHere();
        if (npc == null || World.Player == null) { openPending = false; ShowTalk(); return; }
        if (TalkAs.IsShown) return;     // still closing the previous one
        openPending = false;
        bool ok = false;
        Simulation.Apply(() => ok = Dialogs.Start(npc, "coop/listen"));     // this game is a copy: Works lets Co-op's own conversation through
        dialogShown = ok;
        if (!ok) { Log.Warn("couldn't show the conversation in the game's window: the plain one"); ShowTalk(); }
    }

    void LoadConversationView()
    {
        GameEvents.DialogEnded.Add(this, name => { if (listener == null) dialogShown = TalkAs.IsShown; });
        GameEvents.Update.Add(this, dt =>
        {
            if (listener != null) return;
            if (openPending) OpenConversation();     // the next line, once the window is free
            if (talkActive && hostCamera != null) { var at = Camera.Position; if (at == null || Vector3.Distance(at.Value, hostCamera.Value) > 0.01f) Camera.Position = hostCamera; }
        });
    }
}
