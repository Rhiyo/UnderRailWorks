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
    [Label("Key that shows the list")] public DirectoryKey Key = DirectoryKey.F6;
    [Label("Trade from anywhere in the zone"), Tooltip("Off: the Trade button only works within reach of the merchant.")] public bool Remote = false;
    [Label("Reach (tiles)"), Range(2, 20)] public int Reach = 6;
}

public enum DirectoryKey { F6, F11, F12 }

public sealed class VendorDirectory : WorksMod
{
    VendorDirectoryConfig config;
    UiElement hud;
    UiPanel panel;
    double sinceRefresh;

    public override void Load()
    {
        config = GetConfig<VendorDirectoryConfig>();
        UiEvents.HudCreated.Add(this, root => { hud = root; panel = null; });
        GameEvents.TradeOpened.Add(this, merchant =>
        {
            if (merchant != null && Variables.Global.Available) Variables.Global.Set("vd_" + merchant.Id, merchant.Stock.Count);
        });
        GameEvents.ZoneLoaded.Add(this, (zone, first) => Close());
        GameEvents.Update.Add(this, seconds =>
        {
            if (hud == null) return;
            if (Input.Pressed(KeyOf(config.Key))) { if (panel == null) Show(); else Close(); }
            sinceRefresh += seconds;
            if (panel != null && sinceRefresh > 1) Show();     // distances change as the player walks
        });
    }

    void Close() { if (panel != null) Ui.Remove(panel); panel = null; }

    void Show()
    {
        sinceRefresh = 0;
        var player = World.Player;
        if (player == null) return;
        var merchants = World.Characters.Where(c => c.IsMerchant && !c.IsPlayer)
            .OrderBy(c => (c.Position - player.Position).Length()).ToList();
        Close();
        int w = 460, h = 40 + Math.Max(1, merchants.Count) * 28;
        panel = Ui.Panel(w, h);
        var title = Ui.Label("Vendors in this zone" + (config.Remote ? " (trading from anywhere)" : ""), w - 20);
        Ui.SetColor(title, new Color(230, 160, 60));
        Ui.Add(panel, title, 10, 8);
        if (merchants.Count == 0) { var none = Ui.Label("No merchants here.", w - 20); Ui.Add(panel, none, 10, 34); }
        int y = 34;
        foreach (var m in merchants)
        {
            var merchant = m;
            int tiles = (int)Math.Round((m.Position - player.Position).Length());
            int seen = Variables.Global.Get("vd_" + m.Id, -1);
            var text = m.Name + "  -  " + tiles + " tiles" + (seen >= 0 ? ", " + seen + " items last time" : ", not visited");
            var label = Ui.Label(text, w - 110);
            Ui.SetColor(label, new Color(215, 215, 205));
            Ui.Add(panel, label, 10, y + 4);
            bool canTrade = config.Remote || tiles <= config.Reach;
            if (canTrade) Ui.Add(panel, Ui.Button("Trade", () => { Close(); Trade.Open(merchant); }), w - 95, y);
            y += 28;
        }
        Ui.Add(hud, panel, (hud.Width - w) / 2, 90);
    }

    static Keys KeyOf(DirectoryKey k) { return k == DirectoryKey.F11 ? Keys.F11 : k == DirectoryKey.F12 ? Keys.F12 : Keys.F6; }
}
