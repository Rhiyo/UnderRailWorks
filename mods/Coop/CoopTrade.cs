// Co-op trading, and the joiner's own changes to their character. A conversation on the host that opens the barter
// screen for a joined character: the host cancels it, sends the merchant's stock (packed with the game's own
// serializer: every item detail kept) and TRADE; the joiner's game opens its barter screen with its own character and
// its copy of the merchant. When it closes, the merchant's new stock (TRADED) and the character's sheet (MYSHEET) go to
// the host, which takes them. MYSHEET also goes whenever the joiner changes their character themselves (equipping,
// using, dropping: its level, XP, bag or gear changed since the host's last sheet), checked every 3 s.
// Messages, host to joiner: STOCK <key> <items>, TRADE <key>. Joiner to host: TRADED <key> <items>, MYSHEET <sheet>.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    // What a change worth a new sheet looks like (the sheet's bytes change all the time: its fog of war, clocks).
    static string SignOf(Character c)
    {
        if (c == null) return "";
        return c.Level + "|" + c.Experience + "|" + string.Join(",", c.Inventory.Items.Select(i => i.Name + "x" + i.Stacks).OrderBy(x => x).ToArray())
            + "|" + string.Join(",", c.Equipment.All.Select(kv => kv.Key + "=" + kv.Value.Name).OrderBy(x => x).ToArray());
    }

    // --- Host ---------------------------------------------------------------------------------------------------
    void LoadTrade()
    {
        GameEvents.TradeOpening.Add(this, e =>
        {
            if (listener == null || talking == null || talking.Peer == null || e.Merchant == null) return;
            e.Cancel = true;
            var key = KeyOf(e.Merchant);
            talking.Peer.Send("STOCK", key, Convert.ToBase64String(Item.Pack(e.Merchant.Stock)));
            talking.Peer.Send("TRADE", key);
            Log.Info(talking.Character.Name + " trades with " + e.Merchant.Name + " (on " + talking.Peer.Name + "'s machine)");
            TalkAs.Close();
        });
        GameEvents.Update.Add(this, dt => { if (host != null && inWorld) Simulation.Apply(() => JoinerTick(dt)); });
    }

    void Traded(Seat seat, string key, string items)
    {
        var merchant = Resolve(key);
        if (merchant == null || !merchant.IsMerchant) return;
        try { merchant.SetStock(Item.Unpack(Convert.FromBase64String(items))); Log.Info(merchant.Name + "'s stock from " + seat.Peer.Name + "'s trade"); }
        catch (Exception ex) { Log.Error("trade: " + ex.Message); }
    }

    // The joiner's own sheet: their character is built again from it where it stands (its health kept: the host's).
    void TakeSheet(Seat seat, string sheet)
    {
        var old = seat.Character;
        if (old == null || old.Area == null || seat.Record == null) return;
        Character c = null;
        try { c = World.SpawnFromCharacterFile(WriteSheet(seat.Record.Name, sheet), (int)Math.Round(old.Position.X), (int)Math.Round(old.Position.Y)); }
        catch (Exception ex) { Log.Error("can't build " + seat.Record.Name + ": " + ex.Message); }
        if (c == null) return;
        float health = old.Health; int levelBefore = old.Level;
        bool fighting = old.IsInCombat;
        c.Name = seat.Record.Name;
        c.PlaceAt(old.Position, old.Facing);
        c.Health = Math.Min(health, c.MaxHealth);
        CharacterControl.Take(this, c);
        Party.Add(this, c);
        if (fighting) Combat.Join(c);
        seat.Character = c;
        seat.Record.Sheet = sheet;
        seat.LastSign = SignOf(c);
        World.Despawn(old);
        Log.Info(seat.Record.Name + ": sheet from " + seat.Peer.Name);
        if (c.Level > levelBefore) Party.LevelledUp(c, c.Level);     // levelled up on their machine: mods here hear it
        foreach (var s in seats.Values.Where(x => x != seat && x.InWorld && x.Peer != null)) s.Peer.Send("SHEET", "c:" + seat.Record.Name, sheet);
    }

    // --- Joiner -------------------------------------------------------------------------------------------------
    string tradingWith; bool tradeSeen;
    string lastSign;     // my character as the host last sent it (or as I last sent it)
    double signTimer;
    readonly Dictionary<string, string> stocks = new Dictionary<string, string>();

    bool TradeMessage(string[] w)
    {
        switch (w[0])
        {
            case "STOCK":
                if (w.Length > 2) { stocks[w[1]] = w[2]; ApplyStock(w[1]); }
                return true;
            case "TRADE":
            {
                var m = w.Length > 1 ? Local(w[1]) : null;
                if (m == null || !World.OpenTrade(m)) { Tell("can't open the trade here"); return true; }
                tradingWith = w[1]; tradeSeen = false;
                return true;
            }
        }
        return false;
    }

    Character Local(string key) { Character c; if (spawned.TryGetValue(key, out c)) return c; return key.StartsWith("c:") ? null : World.ByEntityId(key); }

    void ApplyStock(string key)
    {
        string items; var m = Local(key);
        if (m == null || !stocks.TryGetValue(key, out items)) return;
        try { m.SetStock(Item.Unpack(Convert.FromBase64String(items))); } catch (Exception ex) { Log.Error("stock: " + ex.Message); }
    }

    void JoinerTick(double dt)
    {
        if (tradingWith != null)
        {
            if (World.IsTrading) { tradeSeen = true; return; }
            if (!tradeSeen) return;
            var m = Local(tradingWith);
            if (m != null) host.Send("TRADED", tradingWith, Convert.ToBase64String(Item.Pack(m.Stock)));
            tradingWith = null;
            SendMySheet();
            return;
        }
        if ((signTimer -= dt) > 0) return;
        signTimer = 3;
        var me = World.Player;
        if (me == null || followed == null || me != followed || lastSign == null) return;
        if (SignOf(me) != lastSign) SendMySheet();
    }

    void SendMySheet()
    {
        var me = World.Player;
        var sheet = SheetOf(me);
        if (sheet == null) return;
        lastSign = SignOf(me);
        host.Send("MYSHEET", sheet);
    }
}
