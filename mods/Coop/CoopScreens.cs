// Co-op as Works' player screens: a mod's popup or message meant for a joined player (the acting player, or named with
// Party.As) is shown on that player's machine, and their answer comes back to run the mod's callback on the host.
// Messages, host to joiner: POPUP <id> <kind> <title> <text> <initial> <min> <max> <alsoAllow> <buttons...>,
// SAID <text>. Joiner to host: POPUPANS <id> <answer>.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop : IPlayerScreens
{
    readonly Dictionary<int, Action<string>> popupAnswers = new Dictionary<int, Action<string>>();
    int nextPopup;

    // --- Host ---------------------------------------------------------------------------------------------------
    public bool Popup(Character who, RemotePopup p, Action<string> answer)
    {
        var seat = listener == null ? null : seats.Values.FirstOrDefault(s => s.Character == who && s.Peer != null && !s.Peer.Closed);
        if (seat == null) return false;
        int id = ++nextPopup;
        popupAnswers[id] = answer;
        seat.Peer.Send(new object[] { "POPUP", id, p.Kind.ToString(), p.Title ?? "", p.Text ?? "", p.Initial ?? "", p.Min, p.Max, p.AlsoAllow ?? "" }.Concat((p.Buttons ?? new string[0]).Cast<object>()).ToArray());
        return true;
    }

    public bool Message(Character who, string text, Color colour)
    {
        var seat = listener == null ? null : seats.Values.FirstOrDefault(s => s.Character == who && s.Peer != null && !s.Peer.Closed);
        if (seat == null) return false;
        seat.Peer.Send("SAID", text);
        return true;
    }

    public bool IsRemote(Character who) { return listener != null && seats.Values.Any(s => s.Character == who); }

    // A joiner's mod keys: the key works on their machine (a window it opens shows there, from the mirrored world); one
    // whose handler tried to change the world (refused there: a copy) is sent to the host, where the mod's handler runs
    // again as that player and its changes happen in the real game.
    void LoadKeys()
    {
        Keybinds.NeedsTheRealGame += k =>
        {
            if (host == null || host.Closed || !inWorld || k.ModId == Id) return;
            host.Send("KEY", k.ModId, k.KeyId);
        };
    }

    void LoadButtons()
    {
        Ui.ButtonNeedsTheRealGame += (mod, text) => { if (host != null && !host.Closed && inWorld && mod != Id) host.Send("BUTTON", mod, text); };
    }

    void ButtonFrom(Seat seat, string[] w)
    {
        if (w.Length < 3 || seat.Character == null) return;
        if (Ui.PressButton(w[1], w[2], seat.Character)) Log.Info(seat.Peer.Name + " clicked " + w[1] + "/" + w[2]);
        else seat.Peer.Send("SAID", "that button (" + w[2] + ") can't be pressed from your machine");
    }

    void KeyFrom(Seat seat, string[] w)
    {
        if (w.Length < 3 || seat.Character == null) return;
        if (Keybinds.Replay(w[1], w[2], seat.Character)) Log.Info(seat.Peer.Name + " pressed " + w[1] + "/" + w[2]);
    }

    void PopupAnswered(string[] w)
    {
        int id; Action<string> a;
        if (w.Length < 3 || !int.TryParse(w[1], out id) || !popupAnswers.TryGetValue(id, out a)) return;
        popupAnswers.Remove(id);
        try { a(w[2]); } catch (Exception ex) { Log.Error("popup answer: " + ex.Message); }
    }

    // --- Joiner -------------------------------------------------------------------------------------------------
    void ShowPopup(string[] w)
    {
        if (w.Length < 9) return;
        var id = w[1]; var kind = w[2]; var title = w[3]; var text = w[4]; var initial = w[5];
        int min, max; int.TryParse(w[6], out min); int.TryParse(w[7], out max);
        var also = w[8]; var buttons = w.Skip(9).ToArray();
        Action<string> reply = a => { if (host != null && !host.Closed) host.Send("POPUPANS", id, a); };
        switch (kind)
        {
            case "Ask": Ui.Ask(this, text, initial, t => reply(t), buttons.Length > 0 ? buttons[0] : "OK", also.Length > 0 ? also : null); break;
            case "AskNumber": { int n; int.TryParse(initial, out n); Ui.AskNumber(this, text, min, max, n, v => reply(v.ToString()), buttons.Length > 0 ? buttons[0] : "OK"); break; }
            case "Choose": Ui.Choose(this, title, text, i => reply(i.ToString()), buttons); break;
        }
    }

    void LoadScreens() { PlayerScreens.Provider = this; LoadKeys(); LoadButtons(); }
}
