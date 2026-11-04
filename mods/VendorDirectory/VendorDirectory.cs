// Vendor Directory: an UnderRailWorks mod. Press the key (default F6) for a list of every merchant in the current zone,
// nearest first, with how many items they had when you last traded and a Trade button: within reach by default, or
// from anywhere in the zone if "Trade from anywhere" is on. Built from Works' API only (World.Characters,
// Character.IsMerchant/Stock, Trade.Open, GameEvents.TradeOpened, Ui, Variables).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class VendorDirectoryConfig : ModConfig
{
    [Header("Vendor Directory")]
    [Label("Trade from anywhere in the zone"), Tooltip("Off: the Trade button only works within reach of the merchant.")] public bool Remote = false;
    [Label("Reach (tiles)"), Range(2, 20)] public int Reach = 6;
}


public sealed class VendorDirectory : WorksMod
{
    VendorDirectoryConfig config;
    Keybind listKey;
    UiElement hud;
    UiWindow panel;
    readonly List<UiElement> rows = new List<UiElement>();
    int shownCount = -1;
    double sinceRefresh;

    public override void Load()
    {
        config = GetConfig<VendorDirectoryConfig>();
        listKey = Keybinds.Register(this, "list", "Vendor list", Keys.F6);
        UiEvents.HudCreated.Add(this, root => { hud = root; panel = null; });
        UiEvents.HudDestroyed.Add(this, () => hud = null);     // back to the main menu
        GameEvents.TradeOpened.Add(this, merchant =>
        {
            if (merchant != null && Variables.Global.Available) Variables.Global.Set("vd_" + merchant.Key, merchant.Stock.Count);
        });
        GameEvents.ZoneLoaded.Add(this, (zone, first) => Close());
        GameEvents.Update.Add(this, seconds =>
        {
            if (hud == null) return;
            if (listKey.Pressed) { if (panel == null) Show(); else Close(); }
            sinceRefresh += seconds;
            if (panel != null && sinceRefresh > 1) Show();     // distances change as the player walks
        });
    }

    void Close() { if (panel != null) panel.Close(); }

    void Show()
    {
        sinceRefresh = 0;
        var player = World.Player;
        if (player == null) return;
        // Hostile merchants don't trade; neither does anyone during a fight.
        var merchants = World.Characters.Where(c => c.IsMerchant && !c.IsPlayer && !c.IsHostile && !c.IsDead)
            .OrderBy(c => (c.Position - player.Position).Length()).ToList();
        const int w = 460;
        // The window stays; its rows are rebuilt (a rebuilt window would swallow a click on Trade).
        if (panel == null || shownCount != merchants.Count)
        {
            Close();
            panel = Ui.Window(this, "vendors", "Vendors in this zone" + (config.Remote ? " (trading from anywhere)" : ""), w, 40 + Math.Max(1, merchants.Count) * 28);
            if (panel == null) return;
            panel.Closed += () => { panel = null; rows.Clear(); shownCount = -1; };
            shownCount = merchants.Count;
        }
        foreach (var r in rows) Ui.Remove(r);
        rows.Clear();
        if (merchants.Count == 0) { var none = Ui.Label("No merchants here.", w - 20); rows.Add(none); Ui.Add(panel, none, 10, panel.Top + 6); }
        int y = panel.Top + 4;
        foreach (var m in merchants)
        {
            var merchant = m;
            int tiles = (int)Math.Round((m.Position - player.Position).Length());
            int seen = Variables.Global.Get("vd_" + m.Key, -1);
            var text = m.Name + "  -  " + tiles + " tiles" + (seen >= 0 ? ", " + seen + " items last time" : ", not visited");
            var label = Ui.Label(text, w - 110);
            Ui.SetColor(label, new Color(215, 215, 205));
            rows.Add(label); Ui.Add(panel, label, 10, y + 4);
            bool canTrade = (config.Remote || tiles <= config.Reach) && !Combat.InCombat;
            if (canTrade) { var b = Ui.Button("Trade", () => { Close(); Trade.Open(merchant); }); rows.Add(b); Ui.Add(panel, b, w - 95, y); }
            y += 28;
        }
    }

}
