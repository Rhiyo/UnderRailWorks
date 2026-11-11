// Merchant Ledger: an UnderRailWorks mod. Every time you trade, the merchant's stock and prices are written down. The
// ledger (key L, changeable in Options -> Controls) searches everything you've seen: type part of an item's name and it
// lists who had it, where, and what they asked. Kept in the mod's save data (next to the save; one entry per merchant).
// Version 1.0 kept it in game variables ("ml_..."): those are moved over when a game starts. Built from Works' API only
// (TradeOpened, Character.Stock/Markup, Ui.TextBox, Keybinds, SaveData).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.1.0")]

public sealed class MerchantLedger : WorksMod
{
    const string OldIndex = "ml_index";     // 1.0: merchant ids, comma-separated, each "ml_<id>" = "name|zone|item~stacks~price;..."
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
            if (merchant == null) return;
            float markup = merchant.Markup;
            var items = merchant.Stock.Where(i => i.Code != ItemCodes.Charons)
                .Select(i => (object)new Dictionary<string, object> { { "item", i.Name ?? "" }, { "stacks", i.Stacks }, { "price", (int)Math.Round(i.Value * markup) } }).ToList();
            SaveData.Set("m:" + merchant.Key, new Dictionary<string, object> { { "name", merchant.Name ?? "" }, { "zone", World.ZoneId ?? "" }, { "items", items } });
            Log.Info("recorded " + merchant.Name + ": " + items.Count + " items");
        });
        GameEvents.GameStarted.Add(this, Migrate);
        GameEvents.Update.Add(this, seconds =>
        {
            if (hud == null || !openKey.Pressed) return;
            if (panel == null) Open(); else Close();
        });
    }

    // 1.0's ledger in game variables -> save data (then the variables go: the save no longer carries them).
    void Migrate()
    {
        if (!Variables.Global.Available) return;
        var index = Variables.Global.Get<string>(OldIndex, null);
        if (string.IsNullOrEmpty(index)) return;
        int moved = 0;
        foreach (var id in index.Split(',').Where(x => x.Length > 0))
        {
            var parts = (Variables.Global.Get<string>("ml_" + id, null) ?? "").Split('|');
            Variables.Global.Remove("ml_" + id);
            if (parts.Length < 3 || SaveData.Has("m:" + id)) continue;
            var items = new List<object>();
            foreach (var item in parts[2].Split(';'))
            {
                var f = item.Split('~');
                int stacks, price;
                if (f.Length == 3 && int.TryParse(f[1], out stacks) && int.TryParse(f[2], out price))
                    items.Add(new Dictionary<string, object> { { "item", f[0] }, { "stacks", stacks }, { "price", price } });
            }
            SaveData.Set("m:" + id, new Dictionary<string, object> { { "name", parts[0] }, { "zone", parts[1] }, { "items", items } });
            moved++;
        }
        Variables.Global.Remove(OldIndex);
        Log.Info("moved " + moved + " merchants from game variables to save data");
    }

    List<Entry> All()
    {
        var all = new List<Entry>();
        foreach (var key in SaveData.Keys.Where(k => k.StartsWith("m:")))
        {
            var m = SaveData.Get<Dictionary<string, object>>(key, null);
            if (m == null) continue;
            object items;
            if (!m.TryGetValue("items", out items) || !(items is List<object>)) continue;
            foreach (var o in (List<object>)items)
            {
                var d = o as Dictionary<string, object>;
                if (d == null) continue;
                all.Add(new Entry { Merchant = Str(m, "name"), Zone = Str(m, "zone"), Item = Str(d, "item"), Stacks = Number(d, "stacks"), Price = Number(d, "price") });
            }
        }
        return all;
    }
    int Merchants() { return SaveData.Keys.Count(k => k.StartsWith("m:")); }
    static string Str(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) && v != null ? Convert.ToString(v) : ""; }
    static int Number(Dictionary<string, object> d, string k) { object v; try { return d.TryGetValue(k, out v) && v != null ? Convert.ToInt32(v) : 0; } catch { return 0; } }

    void Open()
    {
        int line = Ui.LineHeight + 2, w = Ui.S(560), h = 40 + Ui.LineHeight + Ui.S(32) + (Shown + 1) * line;
        int seen = Merchants();
        panel = Ui.Window(this, "ledger", "Merchant ledger: " + seen + (seen == 1 ? " merchant" : " merchants") + " seen", w, h);
        if (panel == null) return;
        panel.Closed += () => { panel = null; search = null; rows.Clear(); };
        var title = Ui.Label("Type to search.", w - 20);
        Ui.SetColor(title, new Color(230, 160, 60));
        Ui.Add(panel, title, 10, panel.Top + 4);
        search = Ui.TextBox(w - 20, 40);
        search.Changed += text => Show(text);
        Ui.Add(panel, search, 10, panel.Top + Ui.LineHeight + 8);
        listTop = panel.Top + Ui.LineHeight + 8 + search.Height + 8;
        Show("");
    }

    int listTop;
    void Close() { if (panel != null) panel.Close(); }

    void Show(string text)
    {
        foreach (var r in rows) Ui.Remove(r);
        rows.Clear();
        var found = All().Where(e => text.Length == 0 || e.Item.IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0)
            .OrderBy(e => e.Item).ThenBy(e => e.Price).ToList();
        int y = listTop, step = Ui.LineHeight + 2;
        if (found.Count == 0)
        {
            var none = Ui.Label(Merchants() == 0 ? "Nothing yet: trade with a merchant and their stock is written down." : "No item matches.", panel.Width - 20);
            rows.Add(none); Ui.Add(panel, none, 10, y);
            return;
        }
        foreach (var e in found.Take(Shown))
        {
            var line = Ui.Label(e.Item + (e.Stacks > 1 ? " x" + e.Stacks : "") + "   " + e.Price + " charons   " + e.Merchant + (e.Zone.Length > 0 ? " (" + e.Zone + ")" : ""), panel.Width - 20);
            Ui.SetColor(line, new Color(215, 215, 205));
            rows.Add(line); Ui.Add(panel, line, 10, y);
            y += step;
        }
        if (found.Count > Shown) { var more = Ui.Label("... " + (found.Count - Shown) + " more: type more of the name.", panel.Width - 20); rows.Add(more); Ui.Add(panel, more, 10, y); }
    }
}
