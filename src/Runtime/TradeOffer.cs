// UnderRailWorks: the item under the mouse in the game's windows, and offering items in the barter window.
//   var item = Items.Hovered;                       // inventory, barter grids, containers... (null: none)
//   if (Trade.IsOpen) Trade.Offer(item);            // into the player's offer, as dragging it there would
// The barter window (b6o) has three grids: the player's goods (m_e), the merchant's (m_f) and the player's offer (g);
// its own Auto button moves goods the same way (takes them out of the player's bag, adds them to the offer's container,
// then g() recounts the totals). The game's Trade button still decides the deal: prices, what the merchant takes, money.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static partial class Items
    {
        // The item under the mouse in any of the game's windows (an item cell: the game's `ta` controls), or null.
        public static Item Hovered
        {
            get
            {
                try
                {
                    var ui = bpo.GetService<bcm>();
                    var cell = Find(ui == null ? null : ui.aau());
                    var item = cell == null ? null : ((ta)cell).x6();
                    return item == null ? null : new Item(item);
                }
                catch (Exception ex) { Log.Write("items: hovered: " + ex.Message); return null; }
            }
        }

        // The deepest visible control under the mouse that holds an item.
        static euz Find(euz c)
        {
            if (c == null || !c.Visible) return null;
            foreach (euz child in c.Controls.Cast<euz>().Reverse())     // the last drawn is on top
            {
                var hit = Find(child);
                if (hit != null) return hit;
            }
            return c is ta && c.IsMousedOver && ((ta)c).x6() != null ? c : null;
        }
    }

    [Preview("multiplayer: built for Co-op so far")]
    public sealed class TradeDeal
    {
        internal TradeDeal() { }
        public Character Merchant { get; internal set; }
        public IList<Item> Buying { get; internal set; }     // the merchant's offer
        public IList<Item> Selling { get; internal set; }    // the player's offer
        public bool Cancel { get; set; }
    }

    public static partial class Trade
    {
        static readonly FieldInfo OfferGrid = typeof(b6o).GetField("g", BindingFlags.Instance | BindingFlags.Public);     // b6o.g: also a method name
        static readonly MethodInfo Recount = typeof(b6o).GetMethod("g", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        static b6o OpenWindow { get { var w = Windows.OpenWindows.FirstOrDefault(o => o.Kind == UnderRailWorks.Window.Barter); return w == null ? null : w.Element.Control as b6o; } }

        // The barter window is open.
        public static bool IsOpen { get { return OpenWindow != null; } }

        // The barter window's Trade button, before the deal: what the merchant offers (Buying) and what the player offers
        // (Selling); Cancel = the deal doesn't happen here (the offers stay as they are).
        [Preview("multiplayer: built for Co-op so far")] public static readonly Hook<Action<TradeDeal>> Dealing = new Hook<Action<TradeDeal>>("Trade.Dealing");
        internal static bool RaiseDealing(b6o w)
        {
            if (!Dealing.Any || w == null) return true;
            var offer = MerchantOffer == null ? null : MerchantOffer.GetValue(w) as b3w;
            var mine = OfferGrid == null ? null : OfferGrid.GetValue(w) as b3w;
            var deal = new TradeDeal { Merchant = Merchant,
                Buying = offer == null ? new List<Item>() : offer.x().z2().Select(i => new Item(i)).ToList(),
                Selling = mine == null ? new List<Item>() : mine.x().z2().Select(i => new Item(i)).ToList() };
            Dealing.Run(h => h(deal));
            return !deal.Cancel;
        }

        // A deal was made in the barter window (its Trade button: both offers moved). The window stays open. The
        // merchant: Trade.Merchant.
        [Preview("multiplayer: built for Co-op so far")] public static readonly Hook<Action> Dealt = new Hook<Action>("Trade.Dealt");
        internal static void RaiseDealt() { if (Dealt.Any) Dealt.Run(h => h()); }

        // The merchant in the open barter window, or null.
        static readonly FieldInfo MerchantField = typeof(b6o).GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).FirstOrDefault(f => f.FieldType != typeof(object) && f.FieldType.IsAssignableFrom(typeof(dvp)));     // declared as the merchant interface (eg)
        [Preview("multiplayer: built for Co-op so far")] public static Character Merchant
        {
            get { var w = OpenWindow; var m = w == null || MerchantField == null ? null : MerchantField.GetValue(w) as dvp; return m == null ? null : Character.Of(m.a8()); }
        }

        // Empties the merchant's side of the offer (its items back to nowhere: for a stock replaced from elsewhere, where
        // they came from no longer exists). False: no window.
        [Preview("multiplayer: built for Co-op so far")] public static bool ClearMerchantOffer()
        {
            var w = OpenWindow;
            var offer = w == null || MerchantOffer == null ? null : MerchantOffer.GetValue(w) as b3w;
            if (offer == null) return false;
            offer.x().z7();
            if (Recount != null) Recount.Invoke(w, null);
            return true;
        }

        // Closes the barter window as its Close button does (offers go back where they came from). False: none open.
        [Preview("multiplayer: built for Co-op so far")] public static bool Close()
        {
            var screen = bpo.GetService<ps>() as dka;
            if (screen == null || OpenWindow == null) return false;
            screen.ad();
            return true;
        }

        // For tests (works.tradebuy / works.tradeaccept): the merchant's list, the merchant's offer, the Trade button.
        static FieldInfo ListField(string n)
        {
            const BindingFlags f = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            var x = typeof(b6o).GetField(n, f);
            return x != null && x.FieldType == typeof(b3w) ? x : typeof(b6o).GetField("m_" + n, f);
        }
        static readonly FieldInfo StockList = ListField("e"), MerchantOffer = ListField("f");
        static readonly MethodInfo TradeButton = typeof(b6o).GetMethod("h", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(object), typeof(b25) }, null);
        internal static string TestBuy(string code, int stacks)
        {
            try { return TestBuyIn(code, stacks); } catch (Exception ex) { Log.Write("tradebuy: " + ex); return "failed: " + ex.Message; }
        }
        static string TestBuyIn(string code, int stacks)
        {
            var w = OpenWindow;
            Log.Write("tradebuy: window " + (w != null) + ", stock list " + (StockList != null && w != null && StockList.GetValue(w) != null) + ", merchant offer " + (MerchantOffer != null && w != null && MerchantOffer.GetValue(w) != null)
                + ", stock bag " + (w != null && StockList != null && StockList.GetValue(w) is b3w b && b.x() != null) + ", windows open " + Windows.OpenWindows.Count(o => o.Kind == UnderRailWorks.Window.Barter));
            var list = w == null || StockList == null ? null : StockList.GetValue(w) as b3w;
            var offer = w == null || MerchantOffer == null ? null : MerchantOffer.GetValue(w) as b3w;
            if (list == null || offer == null) return "no barter window";
            var game = list.x().z2().FirstOrDefault(i => i.Definition != null && (code == "*" ? !(i.Definition.a5() ?? "").StartsWith("currency", StringComparison.OrdinalIgnoreCase) : i.Definition.a5() == code));     // "*": the first that isn't a currency
            if (game == null) return "the merchant has no " + code;
            int n = stacks <= 0 || stacks >= game.Stacks ? game.Stacks : stacks;
            if (n == game.Stacks) { list.x().z5(game); offer.x().z3(game); }
            else { var part = game.fy(); part.Stacks = n; game.Stacks -= n; offer.x().z3(part); }
            if (Recount != null) Recount.Invoke(w, null);
            return "buying " + n + " " + code;
        }
        internal static string TestAccept()
        {
            var w = OpenWindow;
            if (w == null || TradeButton == null) return "no barter window";
            try { TradeButton.Invoke(w, new object[] { null, null }); } catch (Exception ex) { Log.Write("tradeaccept: " + (ex.InnerException ?? ex)); return "failed: " + (ex.InnerException ?? ex).Message; }
            return "traded";
        }
        // The barter window (to put a mod's button on it), or null.
        public static UiElement Window { get { var w = OpenWindow; return w == null ? null : UiElement.Of(w); } }

        // The merchant in the open barter window buys this item (the game's own test: b6o.aeg; it crosses out the rest).
        public static bool WillBuy(Item item) { var w = OpenWindow; return w != null && item != null && w.aeg(item.Game); }

        // Moves stacks of an item from the player's bag into their offer (all of them: stacks <= 0). False if the barter
        // window isn't open or the item isn't in the bag. The game's Trade button then decides as usual.
        public static bool Offer(Item item, int stacks = 0)
        {
            var w = OpenWindow; var p = World.Player;
            if (w == null || p == null || item == null || OfferGrid == null) return false;
            var bag = p.Inventory.Owner.Bag;
            var game = item.Game;
            if (!bag.z2().Contains(game)) return false;
            var offer = (OfferGrid.GetValue(w) as b3w);
            if (offer == null) return false;
            int n = stacks <= 0 || stacks >= game.Stacks ? game.Stacks : stacks;
            if (n == game.Stacks) { bag.z5(game); offer.l().z3(game); }
            else { var part = game.fy(); part.Stacks = n; game.Stacks -= n; offer.l().z3(part); }
            if (Recount != null) Recount.Invoke(w, null);
            return true;
        }
    }
}
