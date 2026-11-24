// The players table in Co-op's lobby windows (host and joiner) and the host's in-game window: one framed row per
// place, the player's name on the left, their character in the middle, a tag on the right (Host, You, health).
// Open places say "Open"; a player without a character yet: "No character". A save's lobby lets the joiner click
// the row of the character that's theirs.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    internal sealed class PlayerRow { public string Player, Character, Tag; public bool Open, Me, Host; }
    List<PlayerRow> playerRows = new List<PlayerRow>();
    UiElement playersTable;
    int tableSelected = -1;
    static readonly Color RowFill = new Color(30, 26, 20, 235), OpenFill = new Color(14, 13, 11, 225), Frame = new Color(120, 70, 20), MeFrame = new Color(230, 160, 60), Hover = new Color(70, 40, 10, 235), Picked = new Color(110, 60, 10, 240);

    static int TableRowHeight { get { return Ui.LineHeight + Ui.S(12); } }
    static int TableHeight(int rows) { return rows * (TableRowHeight + Ui.S(4)) - Ui.S(4); }

    // A table for rows places. clicked: a row was clicked (null: rows can't be picked).
    UiElement PlayersTable(int width, int rows, Action<int> clicked)
    {
        tableSelected = -1;
        UiElement e = null;
        bool wasDown = false;
        e = Ui.Custom(width, TableHeight(rows), c =>
        {
            var b = c.Bounds;
            int rh = TableRowHeight, gap = Ui.S(4), pad = Ui.S(10), line = Ui.LineHeight;
            var m = Input.Mouse;
            bool down = Input.MouseDown(MouseButton.Left), press = down && !wasDown;
            wasDown = down;
            var list = playerRows;
            for (int i = 0; i < list.Count; i++)
            {
                var r = list[i];
                var row = new Rectangle(b.X, b.Y + i * (rh + gap), b.Width, rh);
                if (row.Bottom > b.Bottom + 1) break;
                bool hot = clicked != null && !r.Host && !(r.Open && string.IsNullOrEmpty(r.Character)) && Ui.IsHot(e) && row.Contains(m);
                c.Fill(row, i == tableSelected ? Picked : hot ? Hover : r.Open ? OpenFill : RowFill);
                c.Frame(row, r.Me ? MeFrame : Frame);
                int ty = row.Y + (rh - line) / 2;
                int nameW = b.Width * 2 / 5, charX = row.X + pad + nameW;
                int tagW = string.IsNullOrEmpty(r.Tag) ? 0 : Ui.TextWidth(r.Tag);
                c.Text(Cut(r.Open ? "Open" : r.Player, nameW - pad), row.X + pad, ty, r.Open ? Grey : r.Me ? Mine_ : Normal);
                if (!r.Open || !string.IsNullOrEmpty(r.Character))     // an open place of a save: its character
                {
                    bool none = string.IsNullOrEmpty(r.Character);
                    c.Text(Cut(none ? "No character" : r.Character, row.Right - pad - tagW - Ui.S(10) - charX), charX, ty, none ? Grey : Normal);
                }
                if (tagW > 0) c.Text(r.Tag, row.Right - pad - tagW, ty, r.Me ? Mine_ : Grey);
                if (hot && press) { tableSelected = i; clicked(i); }
            }
        });
        return e;
    }

    static string Cut(string text, int width)
    {
        if (string.IsNullOrEmpty(text) || width <= 0 || Ui.TextWidth(text) <= width) return text ?? "";
        int n = text.Length;
        while (n > 0 && Ui.TextWidth(text.Substring(0, n) + "...") > width) n--;
        return text.Substring(0, n).TrimEnd() + "...";
    }
}
