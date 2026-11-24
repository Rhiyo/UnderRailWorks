// The lobby window, one layout for the host and the joiners (CoopLobbyUi.BuildLobby, CoopJoinUi.BuildJoinLobby): the
// same sections in the same places, so the two always line up (NOTES.md: "host and joiner UI line up"):
//   status (and, for a joiner, a grey second line)
//   Players  (n of m)          the players table (CoopPlayersUi), under it the action for your own character
//                              (Create / Recreate character, Play this character)
//   Game settings  [Edit]      label: value rows (a control on the host's side where it can change something)
//   a grey note line           (how friends join: the host's)
//   [left buttons ...]   [right button]      Start game / Invite friends... | Cancel; (nothing) | Leave
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    // A settings row: a label and its value (text, or a control), or a label and tick rows under each other (each a
    // tick box and its label), laid straight into the window as the lobby always had them.
    internal sealed class LobbySetting { public string Label; public UiElement Control; public List<KeyValuePair<UiElement, UiElement>> Ticks; }
    static LobbySetting Pair(string label, UiElement control) { return new LobbySetting { Label = label, Control = control }; }

    internal sealed class LobbyView
    {
        public string Title;
        public bool Detail;                    // a second status line (grey)
        public int Rows;                       // players table rows
        public Action<int> Pick;               // a row clicked (null: not pickable)
        public UiElement PlayerAction;         // under the players table
        public string SettingsHeading = "Game settings";
        public UiElement SettingsAction;       // beside the settings heading
        public readonly List<LobbySetting> Settings = new List<LobbySetting>();
        public string Note, NoteTip;
        public readonly List<UiElement> Left = new List<UiElement>();
        public UiElement Right;
    }

    // The lobby's sizes: the window's width and its columns.
    static int LobbyWidth { get { return Ui.S(600); } }
    static int LobbyPad { get { return Ui.S(12); } }
    static int LobbyInner { get { return LobbyWidth - 2 * LobbyPad; } }
    static int LobbyLabelWidth { get { return LobbyInner * 11 / 20; } }
    static int LobbyValueWidth { get { return LobbyInner - LobbyLabelWidth - Ui.S(10); } }

    // A setting's value as text (cut to its column).
    UiElement LobbyValue(string text) { var v = Ui.Label(Cut(text ?? "", LobbyValueWidth), LobbyValueWidth); Ui.SetColor(v, Normal); return v; }

    void BuildLobbyWindow(LobbyView v)
    {
        int pad = LobbyPad, w = LobbyWidth, inner = LobbyInner, labelW = LobbyLabelWidth, line = Ui.LineHeight, gap = Ui.S(6), section = Ui.S(14);
        var place = new List<KeyValuePair<UiElement, Point>>();
        int y = 0;
        Action<UiElement, int, int> at = (e, x, yy) => place.Add(new KeyValuePair<UiElement, Point>(e, new Point(x, yy)));
        Func<string, UiElement> heading = text => { var h = Ui.Label(text, inner); Ui.SetColor(h, Mine_); return h; };

        statusLabel = Ui.Label("", inner);
        at(statusLabel, pad, y); y += line + (v.Detail ? Ui.S(4) : section);
        joinDetail = null;
        if (v.Detail) { joinDetail = Ui.Label("", inner); Ui.SetColor(joinDetail, Grey); at(joinDetail, pad, y); y += line + section; }

        playersHeading = heading("Players");
        at(playersHeading, pad, y); y += line + gap;
        list = null; playerRows = new List<PlayerRow>();
        playersTable = PlayersTable(inner, v.Rows, v.Pick);
        at(playersTable, pad, y); y += TableHeight(v.Rows) + gap;
        if (v.PlayerAction != null) { at(v.PlayerAction, pad, y); y += v.PlayerAction.Height; }
        y += section;

        int headH = v.SettingsAction == null ? line : Math.Max(line, v.SettingsAction.Height);
        at(heading(v.SettingsHeading), pad, y + (headH - line) / 2);
        if (v.SettingsAction != null) at(v.SettingsAction, pad + inner - v.SettingsAction.Width, y);
        y += headH + gap;
        foreach (var s in v.Settings)
        {
            int x = pad + Ui.S(10) + labelW;
            if (s.Ticks != null)
            {
                at(Ui.Label(s.Label, labelW), pad + Ui.S(10), y);
                foreach (var t in s.Ticks)
                {
                    at(t.Key, x, y); at(t.Value, x + t.Key.Width + Ui.S(6), y + Math.Max(0, (t.Key.Height - line) / 2));
                    y += Math.Max(t.Key.Height, line) + Ui.S(2);
                }
                y += gap;
                continue;
            }
            int h = s.Control == null ? line : Math.Max(line, s.Control.Height);
            at(Ui.Label(s.Label, labelW), pad + Ui.S(10), y + (Math.Min(h, line + Ui.S(8)) - line) / 2);
            if (s.Control != null) at(s.Control, x, y + (s.Control.Height < line ? (line - s.Control.Height) / 2 : 0));
            y += h + gap;
        }
        if (v.Settings.Count == 0) { var e = Ui.Label("(not sent by the host)", inner); Ui.SetColor(e, Grey); at(e, pad + Ui.S(10), y); y += line + gap; }
        y += section - gap;

        if (!string.IsNullOrEmpty(v.Note))
        {
            var note = Ui.Label(Cut(v.Note, inner), inner);
            Ui.SetColor(note, Grey);
            if (!string.IsNullOrEmpty(v.NoteTip)) Ui.Tooltip(note, v.NoteTip);
            at(note, pad, y); y += line + section;
        }

        // The bottom row: left buttons from the left, the right one at the right edge (wrapping on a narrow screen).
        var all = v.Left.Concat(v.Right == null ? new UiElement[0] : new[] { v.Right }).ToList();
        bool oneRow = Ui.ButtonsWidth(all, pad) + Ui.S(20) <= w;
        int bh = all.Count == 0 ? 0 : all.Max(b => b.Height);
        int height = y + (oneRow ? bh : Ui.ButtonsHeight(all, w, pad)) + pad;
        window = Ui.Window(this, "coop", v.Title, w, 22 + Ui.S(4) + height);
        if (window == null) return;
        window.Closed += () => { window = null; builtFor = null; };
        int top = window.Top + Ui.S(4);
        foreach (var p in place) Ui.Add(window, p.Key, p.Value.X, top + p.Value.Y);
        if (all.Count == 0) return;
        if (oneRow)
        {
            int by = window.Height - pad - bh, bx = pad;
            foreach (var b in v.Left) { Ui.Add(window, b, bx, by); bx += b.Width + gap; }
            if (v.Right != null) Ui.Add(window, v.Right, window.Width - pad - v.Right.Width, by);
        }
        else Ui.AddButtons(window, all, pad);
    }
}
