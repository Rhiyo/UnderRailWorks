// The joiner's own changes to their character, and merchants' stock on the joiner's side (the shared shop itself:
// CoopShop.cs). MYSHEET goes whenever the joiner changes their character themselves (equipping, using, dropping: its
// level, XP, bag or gear changed since the host's last sheet), checked every 3 s; the host takes it.
// Messages, host to joiner: STOCK <key> <items> (packed with the game's own serializer: every item detail kept).
// Joiner to host: MYSHEET <sheet>.
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
        LoadShop();
        GameEvents.Update.Add(this, dt => { if (host != null && inWorld) Simulation.Apply(() => JoinerTick(dt)); });
    }

    // The joiner's own sheet: their character is built again from it where it stands (its health kept: the host's).
    void TakeSheet(Seat seat, string sheet)
    {
        var old = seat.Character;
        if (old == null || old.Area == null || seat.Record == null) return;
        // In place (Works' ReplaceSheet: the same body), else a new body from it.
        int before = old.Level;
        bool swapped = false;
        try { swapped = old.ReplaceSheet(WriteSheet(seat.Record.Name, sheet)); } catch (Exception ex) { Log.Warn("sheet in place: " + ex.Message); }
        if (swapped)
        {
            seat.LastSign = SignOf(old);
            Log.Info(seat.Record.Name + ": sheet from " + seat.Peer.Name + " (in place)");
            if (old.Level > before) Party.LeveledUp(old, old.Level);
            SendSheet(seat, sheet, seat);
            return;
        }
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
        seat.LastSign = SignOf(c);
        World.Despawn(old);
        Log.Info(seat.Record.Name + ": sheet from " + seat.Peer.Name);
        if (c.Level > levelBefore) Party.LeveledUp(c, c.Level);     // levelled up on their machine: mods here hear it
        SendSheet(seat, sheet, seat);
    }

    // --- Joiner -------------------------------------------------------------------------------------------------
    string lastSign;     // my character as the host last sent it (or as I last sent it)
    int mySheetVersion;  // the version of my character's sheet my changes start from (the host's numbering)

    // --- Sheet versions (both sides) --------------------------------------------------------------------------------
    // Both machines change a joined character: the host (items given, quest items moved, deals, its game's own effects)
    // and its player (equipping, using). Each host-side copy has a version, raised with every sheet the host sends or
    // takes; a joiner's sheet (MYSHEET, DEALT) says the version it was made from. One made from an older version than
    // the host's has crossed a change on the host: it isn't taken (the host's copy is sent back), so a change can be
    // undone on the joiner's side but nothing is lost or doubled. Host to joiner: SHEET <key> <sheet> <version>,
    // SHEETVER <version> (a sheet of theirs taken).
    // Also stale: the host's copy changed since it was last sent or taken (a change on the host not sent yet: the host
    // notices its own changes every few seconds, a joiner's sheet can arrive first).
    bool Stale(Seat seat, string version)
    {
        int v;
        if (!int.TryParse(version, out v) || v != seat.SheetVersion) return true;
        return seat.Character != null && seat.LastSign != null && SignOf(seat.Character) != seat.LastSign;
    }
    // A new version of seat's character to everyone; except: the one whose sheet it is (it has it: only the version).
    void SendSheet(Seat seat, string sheet, Seat except)
    {
        seat.SheetVersion++;
        seat.Record.Sheet = sheet;
        foreach (var s in seats.Values.Where(x => x.InWorld && x.Peer != null))
            if (s == except) s.Peer.Send("SHEETVER", seat.SheetVersion); else s.Peer.Send("SHEET", "c:" + seat.Record.Name, sheet, seat.SheetVersion);
    }
    double signTimer;
    readonly Dictionary<string, string> stocks = new Dictionary<string, string>();

    bool TradeMessage(string[] w)
    {
        switch (w[0])
        {
            case "STOCK":
                if (w.Length > 2)
                {
                    // The open shop: closed first (the offers go back where they came from, as the game's Close does),
                    // so nothing offered from the old stock outlives it; reopened by the SHOP that follows.
                    // The open shop's new stock: the merchant's side of the offer goes (it was picked from the old stock;
                    // left there, it could be bought twice). This player's own offer stays.
                    if (w[1] == shopOpen && World.IsTrading) Trade.ClearMerchantOffer();
                    stocks[w[1]] = w[2]; ApplyStock(w[1]);
                }
                return true;
            case "SHOP": case "TRADE": if (w.Length > 1) ShopArrived(w[1]); return true;
            case "SHEETVER": { int v; if (w.Length > 1 && int.TryParse(w[1], out v)) mySheetVersion = v; return true; }
            case "SHOPEND": if (shopOpen != null && World.IsTrading) Trade.Close(); shopOpen = null; return true;
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
        if (shopOpen != null) { JoinerShopTick(); return; }
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
        host.Send("MYSHEET", sheet, mySheetVersion);
    }
}
