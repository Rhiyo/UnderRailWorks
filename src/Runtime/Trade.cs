// UnderRailWorks: items as bytes (to send them elsewhere, keep them aside), merchants' stock, opening a trade.
//   var bytes = Item.Pack(merchant.Stock);  ...  merchant.SetStock(Item.Unpack(bytes));
//   World.OpenTrade(merchant);                 // the game's barter screen with the player
// Items are written with the game's own serializer and binder (eoq, dds: as its saves and .char exports), so every
// detail (condition, components, ammo, stacks) survives, and nothing in them is a Works type.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed partial class Item
    {
        [Preview("multiplayer: built for Co-op so far")] public static byte[] Pack(IEnumerable<Item> items)
        {
            var list = (items ?? Enumerable.Empty<Item>()).Select(i => i.Game).ToList();
            var path = Path.GetTempFileName();
            try { eoq.SerializeToBinaryFileCompressed(path, list, dds.a(), eh9.c, embedDataModelVersion: true); return File.ReadAllBytes(path); }
            finally { try { File.Delete(path); } catch { } }
        }
        [Preview("multiplayer: built for Co-op so far")] public static IList<Item> Unpack(byte[] bytes)
        {
            var path = Path.GetTempFileName();
            try { File.WriteAllBytes(path, bytes); var list = eoq.DeserializeFromBinaryFileCompressed<List<coh>>(path, dds.a(), tryToRetrieveDataModelVersion: true); return list == null ? new List<Item>() : list.Select(i => new Item(i)).ToList(); }
            finally { try { File.Delete(path); } catch { } }
        }
    }

    public sealed partial class Character
    {
        // Replaces a merchant's stock with these items.
        [Preview("multiplayer: built for Co-op so far")] public bool SetStock(IEnumerable<Item> items)
        {
            var bag = StockBag;
            if (bag == null) return false;
            foreach (var i in bag.z2().ToList()) bag.z5(i);
            foreach (var i in items) bag.z3(i.Game, true);
            return true;
        }
        dbi StockBag { get { var c = Entity as c5x; var m = c == null ? null : c.GetAspect<dvp>(); return m == null ? null : m.ap7(); } }
    }

    public static partial class World
    {
        // The game's barter screen between the player and merchant (as a conversation's "let's trade"). False: no
        // player, not a merchant.
        [Preview("duplicate of Trade.Open")] public static bool OpenTrade(Character merchant)
        {
            var screen = bpo.GetService<ps>() as dka;
            var p = Player;
            if (screen == null || p == null || merchant == null || !merchant.IsMerchant) return false;
            return screen.amw(p.Entity, merchant.Entity);
        }
        // The barter screen is open.
        [Preview("duplicate of Trade.IsOpen")] public static bool IsTrading { get { return Windows.OpenWindows.Any(w => w.Kind == Window.Barter); } }
    }
}
