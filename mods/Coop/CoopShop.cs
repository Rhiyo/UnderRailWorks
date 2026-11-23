// Co-op's shared shop. When the host's game opens a shop (the host's conversation's "let's trade", or a click), every
// joiner in the world gets the merchant's stock and their own shop window beside the host's (once the host's window is
// up: opening restocks the merchant); everyone trades from their own bag.
// A joiner's Trade button doesn't make the deal on their machine (Works' Trade.Dealing, cancelled): what both sides
// offer goes to the host (DEAL: each item by code, value and stacks: item ids differ between machines), and the host
// makes the deal for real: the merchant's items into its copy of the joiner's bag, theirs into the merchant's stock,
// after checking every one is there (one another player got first, or one the joiner no longer has: refused, nothing
// moved). Then the joiner gets their character from the host (their window closes, takes it and opens again) and
// every open shop the merchant's new stock and cash; the host's own deals (Trade.Dealt) reach every shop the same way.
// Each player closes their own window; the shop stays open for the others.
// Messages, host to joiner: STOCK <key> <items>, SHOP <key> (open or refresh), SHOPEND. Joiner to host: DEAL <key>
// <buying> <selling> <sheet version> (items "code|value|stacks;..."), SHOPCLOSED <key>.
using System;
using System.Collections.Generic;
using System.Linq;
using UnderRailWorks;

public sealed partial class Coop
{
    // --- Host ---------------------------------------------------------------------------------------------------
    string shopKey;     // the shared shop's merchant
    readonly HashSet<Seat> shopSeen = new HashSet<Seat>();     // joiners with the shop open
    string shopSign;
    double shopTimer;
    Character shopOpening;     // the host's shop just opened: sent to everyone once its window is up

    static string StockSign(IEnumerable<Item> items) { return string.Join(",", items.Select(i => i.Id + "x" + i.Stacks).OrderBy(x => x).ToArray()); }

    void OpenShopFor(Seat seat, Character merchant)
    {
        if (seat.Peer == null || seat.Peer.Closed || merchant == null) return;
        shopSeen.Add(seat);
        var key = KeyOf(merchant);
        seat.Peer.Send("STOCK", key, Convert.ToBase64String(Item.Pack(merchant.Stock)));
        seat.Peer.Send("SHOP", key);
    }

    // Everyone with the shop open: the merchant's stock now (the dealer too).
    void RefreshShops(Character merchant)
    {
        shopSign = StockSign(merchant.Stock);
        foreach (var s in shopSeen.ToList()) OpenShopFor(s, merchant);
    }

    // "code|value|stacks;..." for a deal's offers.
    static string DealItems(IEnumerable<Item> items) { return string.Join(";", items.Select(i => i.Code + "|" + i.Value + "|" + i.Stacks).ToArray()); }
    static List<KeyValuePair<string, int>> ParseDeal(string s)
    {
        var r = new List<KeyValuePair<string, int>>();
        foreach (var part in (s ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries))
        {
            var p = part.Split('|'); int n;
            if (p.Length == 3 && int.TryParse(p[2], out n) && n > 0) r.Add(new KeyValuePair<string, int>(p[0] + "|" + p[1], n));
        }
        return r;
    }
    // Takes n stacks of what (code|value) from pool, a stack at a time; null if there aren't enough.
    static List<KeyValuePair<Item, int>> Pick(IList<Item> pool, Dictionary<Item, int> used, string what, int n)
    {
        var picked = new List<KeyValuePair<Item, int>>();
        foreach (var i in pool.Where(x => x.Code + "|" + x.Value == what))
        {
            int u; used.TryGetValue(i, out u);
            int free = i.Stacks - u;
            if (free <= 0) continue;
            int take = Math.Min(free, n);
            used[i] = u + take; picked.Add(new KeyValuePair<Item, int>(i, take)); n -= take;
            if (n == 0) return picked;
        }
        return null;
    }

    void Deal(Seat seat, string key, string buying, string selling, string version)
    {
        var merchant = Resolve(key);
        var who = seat.Character;
        if (merchant == null || !merchant.IsMerchant || who == null || who.IsDead || !shopSeen.Contains(seat)) return;
        Action<string> refuse = why =>
        {
            Log.Info(seat.Peer.Name + "'s deal refused: " + why);
            seat.Peer.Send("SAID", "your deal didn't happen: " + why);
            PushSheet(seat); OpenShopFor(seat, merchant);
        };
        if (Stale(seat, version)) { refuse("your character changed on the host meanwhile"); return; }
        var stock = merchant.Stock; var bag = who.Inventory.Items;
        var usedStock = new Dictionary<Item, int>(); var usedBag = new Dictionary<Item, int>();
        var bought = new List<KeyValuePair<Item, int>>(); var sold = new List<KeyValuePair<Item, int>>();
        foreach (var b in ParseDeal(buying))
        {
            var p = Pick(stock, usedStock, b.Key, b.Value);
            if (p == null) { refuse("someone else got " + b.Key.Split('|')[0] + " first"); return; }
            bought.AddRange(p);
        }
        foreach (var s in ParseDeal(selling))
        {
            var p = Pick(bag, usedBag, s.Key, s.Value);
            if (p == null) { refuse("you don't have " + s.Key.Split('|')[0] + " any more"); return; }
            sold.AddRange(p);
        }
        // All there: the merchant's items to the joiner, the joiner's to the merchant, item by item as the game's own deal
        // does (an open window on the stock, the host's, follows that).
        foreach (var t in bought)
        {
            if (t.Value >= t.Key.Stacks) { if (merchant.RemoveFromStock(t.Key)) who.Inventory.Put(t.Key); }
            else { var part = t.Key.Split(t.Value); if (part != null) who.Inventory.Put(part); }
        }
        foreach (var t in sold)
        {
            if (t.Value >= t.Key.Stacks) { if (who.Inventory.Remove(t.Key)) merchant.AddToStock(t.Key); }
            else { var part = t.Key.Split(t.Value); if (part != null) merchant.AddToStock(part); }
        }
        Log.Info(seat.Peer.Name + " traded with " + merchant.Name + ": bought " + string.Join(", ", bought.Select(t => t.Key.Name + " x" + t.Value).ToArray()) + "; gave " + string.Join(", ", sold.Select(t => t.Key.Name + " x" + t.Value).ToArray()));
        PushSheet(seat);     // the joiner's character as the host has it now
        RefreshShops(merchant);
    }

    // The host's copy of a joiner's character, sent to everyone.
    void PushSheet(Seat seat)
    {
        var c = seat.Character; var sheet = c == null ? null : SheetOf(c);
        if (sheet == null || seat.Record == null) return;
        seat.LastSign = SignOf(c);
        SendSheet(seat, sheet, null);
    }

    void HostShopTick(double dt)
    {
        if (listener == null || shopKey == null) return;
        if (shopOpening != null && Trade.IsOpen)
        {
            var m = shopOpening; shopOpening = null;
            shopSign = StockSign(m.Stock);
            foreach (var s in seats.Values.Where(x => x.InWorld && x.Character != null && !x.Character.IsDead)) OpenShopFor(s, m);
            Log.Info("shop with " + m.Name + " opened for everyone (" + m.Stock.Count + " items)");
            Notify.Log("Co-op: the shop is open for everyone", Say);
        }
        if ((shopTimer -= dt) > 0) return;
        shopTimer = 0.5;
        var merchant = Resolve(shopKey);
        if (merchant == null || merchant.Area == null) { EndShop(); return; }
        if (!Trade.IsOpen && shopSeen.Count == 0) EndShop();
    }

    void EndShop() { shopKey = null; shopSign = null; foreach (var s in shopSeen.ToList()) if (s.Peer != null) s.Peer.Send("SHOPEND"); shopSeen.Clear(); }

    // --- Joiner -------------------------------------------------------------------------------------------------
    string shopOpen;     // the shop's key while this player's window is (or should be) open
    bool shopReopening;  // closed to take our character from the host, reopened by the SHOP that follows

    void ShopArrived(string key)
    {
        var m = Local(key);
        if (m == null) { Tell("can't open the shop here"); return; }
        bool wasOpen = shopOpen == key && World.IsTrading;
        shopOpen = key; shopReopening = false;
        if (!wasOpen && !World.OpenTrade(m)) { Tell("can't open the shop here"); shopOpen = null; return; }
        // Opening a shop restocks the merchant (the game's dvp.aqc): here that made items the host doesn't have. The
        // host's stock again.
        if (!wasOpen) ApplyStock(key);
        Log.Info((wasOpen ? "shop updated: " : "shop open: ") + m.Name + ", " + m.Stock.Count + " items");
    }

    void JoinerShopTick()
    {
        if (shopOpen == null || host == null || host.Closed || shopReopening) return;
        if (World.IsTrading) return;
        // Closed by this player (the others keep theirs).
        host.Send("SHOPCLOSED", shopOpen);
        shopOpen = null;
    }

    // Our own character from the host while our shop is open (after a deal): the window closes (a new body under an open
    // barter window leaves it holding the old one), the sheet is taken, the SHOP that follows opens it again.
    void OwnSheetDuringShop()
    {
        if (shopOpen == null || !World.IsTrading) return;
        shopReopening = true;
        Trade.Close();
    }

    void LoadShop()
    {
        // (Sent once the host's window is open: opening restocks the merchant, after this event.)
        GameEvents.TradeOpening.Add(this, e =>
        {
            if (listener == null || e.Cancel || e.Merchant == null) return;
            shopKey = KeyOf(e.Merchant);
            shopSeen.Clear();
            shopOpening = e.Merchant;
        }, HookOrder.Late);
        GameEvents.Update.Add(this, HostShopTick);
        GameEvents.ZoneLeaving.Add(this, e => { if (listener != null && shopKey != null) EndShop(); });
        // A joiner's Trade button: the deal goes to the host instead.
        Trade.Dealing.Add(this, d =>
        {
            if (host == null || host.Closed || shopOpen == null) return;
            d.Cancel = true;
            host.Send("DEAL", shopOpen, DealItems(d.Buying), DealItems(d.Selling), mySheetVersion);
            Log.Info("deal sent to the host: buying " + DealItems(d.Buying) + "; selling " + DealItems(d.Selling));
        });
        // The host's own deal: every shop gets the new stock.
        Trade.Dealt.Add(this, () =>
        {
            var merchant = Trade.Merchant;
            if (listener != null && shopKey != null && merchant != null && KeyOf(merchant) == shopKey) { Log.Info("the host traded with " + merchant.Name); RefreshShops(merchant); }
        });
    }
}
