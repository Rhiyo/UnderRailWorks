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

    public static partial class Trade
    {
        static readonly FieldInfo OfferGrid = typeof(b6o).GetField("g", BindingFlags.Instance | BindingFlags.Public);     // b6o.g: also a method name
        static readonly MethodInfo Recount = typeof(b6o).GetMethod("g", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

        static b6o OpenWindow { get { var w = Windows.OpenWindows.FirstOrDefault(o => o.Kind == UnderRailWorks.Window.Barter); return w == null ? null : w.Element.Control as b6o; } }

        // The barter window is open.
        public static bool IsOpen { get { return OpenWindow != null; } }
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
