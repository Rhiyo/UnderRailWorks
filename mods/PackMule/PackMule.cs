// Pack Mule: an UnderRailWorks mod. Mark kinds of items as junk (hover one in any window and press the key; the mark
// is per item kind and per save), see "Junk" in their tooltips, and offer all of it at a merchant with one button on
// the barter window. The game's own Trade button still makes the deal (its prices, what the merchant takes).
// Built from Works' API only (Items.Hovered, Items.ModifyTooltip, Trade.Window/Offer, Keybinds, SaveData).
using System;
using System.Linq;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class PackMule : WorksMod
{
    Keybind markKey;
    UiElement button;

    SaveData Saved { get { return SaveData.Of(this); } }
    bool IsJunk(string code) { return !string.IsNullOrEmpty(code) && Saved.Get("junk:" + code, false); }

    public override void Load()
    {
        markKey = Keybinds.Add(this, "mark", T("key"), Keys.Delete);
        Items.ModifyTooltip(this, (item, tip) => { if (IsJunk(item.Code)) tip.AddLine(T("tooltip"), new Microsoft.Xna.Framework.Color(200, 160, 90)); });
        GameEvents.Update.Add(this, seconds => Tick());
    }

    void Tick()
    {
        if (markKey.Pressed)
        {
            var item = Items.Hovered;
            if (item != null && !string.IsNullOrEmpty(item.Code))
            {
                bool junk = !IsJunk(item.Code);
                if (junk) Saved.Set("junk:" + item.Code, true); else Saved.Remove("junk:" + item.Code);
                Notify.OverPlayer(T(junk ? "marked" : "unmarked", item.Name));
            }
        }
        // The barter window is new each time it opens: put the button on it.
        var window = Trade.Window;
        if (window == null) { button = null; return; }
        if (button != null && Ui.IsShown(button)) return;
        button = Ui.Button(T("button"), OfferJunk);
        Ui.Add(window, button, window.Width - button.Width - 15, 50);     // top right, beside the merchant's message
    }

    void OfferJunk()
    {
        var player = World.Player;
        if (player == null) return;
        var junk = player.Inventory.Items.Where(i => IsJunk(i.Code)).ToList();
        if (junk.Count == 0) { Notify.OverPlayer(T("none")); return; }
        var wanted = junk.Where(Trade.WillBuy).ToList();
        int n = 0;
        foreach (var item in wanted) if (Trade.Offer(item)) n++;
        Log.Info("offered " + n + " stack(s) of junk, " + (junk.Count - wanted.Count) + " the merchant doesn't buy");
        Notify.OverPlayer(wanted.Count == 0 ? T("notWanted") : T("offered", Text.Count(n, "item")) + (junk.Count > wanted.Count ? T("skipped", junk.Count - wanted.Count) : ""));
    }
}
