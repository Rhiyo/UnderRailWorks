// Merchant Ledger: an UnderRailWorks mod. Every time you trade, the merchant's stock and prices are written down. The
// ledger (key L, changeable in Options -> Controls) searches everything you've seen: type part of an item's name and it
// lists who had it, where, and what they asked. Kept in plain game variables, so it belongs to the save and the stock
// game ignores it. Built from Works' API only (TradeOpened, Character.Stock/Markup, Ui.TextBox, Keybinds, Variables).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class MerchantLedger : WorksMod
{
    const string Index = "ml_index";     // merchant ids, comma-separated
    const int Shown = 14;
    Keybind openKey;
    UiElement hud;
    UiWindow panel;
    UiTextBox search;
    readonly List<UiElement> rows = new List<UiElement>();

    sealed class Entry { public string Merchant, Zone, Item; public int Stacks, Price; }

    public override void Load()
    {
        openKey = Keybinds.Register(this, "open", "Ledger", Keys.L);
        UiEvents.HudCreated.Add(this, root => { hud = root; panel = null; });
        UiEvents.HudDestroyed.Add(this, () => hud = null);     // back to the main menu
        GameEvents.TradeOpened.Add(this, merchant =>
        {
            if (merchant == null || !Variables.Global.Available) return;
            float markup = merchant.Markup;
            var items = merchant.Stock.Where(i => i.Code != ItemCodes.Charons)
                .Select(i => Clean(i.Name) + "~" + i.Stacks + "~" + (int)Math.Round(i.Value * markup));
            Variables.Global.Set("ml_" + merchant.Key, Clean(merchant.Name) + "|" + Clean(World.ZoneId ?? "") + "|" + string.Join(";", items));
            var ids = Ids();
            if (!ids.Contains(merchant.Key)) { ids.Add(merchant.Key); Variables.Global.Set(Index, string.Join(",", ids)); }
            Log.Info("recorded " + merchant.Name + ": " + merchant.Stock.Count + " items");
        });
        GameEvents.Update.Add(this, seconds =>
        {
            if (hud == null || !openKey.Pressed) return;
            if (panel == null) Open(); else Close();
        });
    }

    static string Clean(string s) { return (s ?? "").Replace("|", "/").Replace(";", ",").Replace("~", "-"); }
    static List<string> Ids() { return (Variables.Global.Get<string>(Index, null) ?? "").Split(',').Where(x => x.Length > 0).ToList(); }

    static List<Entry> All()
    {
        var all = new List<Entry>();
        foreach (var id in Ids())
        {
            var parts = (Variables.Global.Get<string>("ml_" + id, null) ?? "").Split('|');
            if (parts.Length < 3) continue;
            foreach (var item in parts[2].Split(';'))
            {
                var f = item.Split('~');
                int stacks, price;
                if (f.Length == 3 && int.TryParse(f[1], out stacks) && int.TryParse(f[2], out price))
                    all.Add(new Entry { Merchant = parts[0], Zone = parts[1], Item = f[0], Stacks = stacks, Price = price });
            }
        }
        return all;
    }

    void Open()
    {
        int w = 560, h = 92 + Shown * 20;
        int seen = Ids().Count;
        panel = Ui.Window(this, "ledger", "Merchant ledger: " + seen + (seen == 1 ? " merchant" : " merchants") + " seen", w, h);
        if (panel == null) return;
        panel.Closed += () => { panel = null; search = null; rows.Clear(); };
        var title = Ui.Label("Type to search.", w - 20);
        Ui.SetColor(title, new Color(230, 160, 60));
        Ui.Add(panel, title, 10, panel.Top + 4);
        search = Ui.TextBox(w - 20, 40);
        search.Changed += text => Show(text);
        Ui.Add(panel, search, 10, panel.Top + 26);
        Show("");
    }

    void Close() { if (panel != null) panel.Close(); }

    void Show(string text)
    {
        foreach (var r in rows) Ui.Remove(r);
        rows.Clear();
        var found = All().Where(e => text.Length == 0 || e.Item.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(e => e.Item).ThenBy(e => e.Price).ToList();
        int y = panel.Top + 62;
        if (found.Count == 0)
        {
            var none = Ui.Label(Ids().Count == 0 ? "Nothing yet: trade with a merchant and their stock is written down." : "No item matches.", 520);
            rows.Add(none); Ui.Add(panel, none, 10, y);
            return;
        }
        foreach (var e in found.Take(Shown))
        {
            var line = Ui.Label(e.Item + (e.Stacks > 1 ? " x" + e.Stacks : "") + "   " + e.Price + " charons   " + e.Merchant + (e.Zone.Length > 0 ? " (" + e.Zone + ")" : ""), 540);
            Ui.SetColor(line, new Color(215, 215, 205));
            rows.Add(line); Ui.Add(panel, line, 10, y);
            y += 20;
        }
        if (found.Count > Shown) { var more = Ui.Label("... " + (found.Count - Shown) + " more: type more of the name.", 520); rows.Add(more); Ui.Add(panel, more, 10, y); }
    }
}
