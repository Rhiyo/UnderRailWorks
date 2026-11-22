// Co-op: quest items always go to the host (MoveQuestItems), and players give each other items and charons (the Co-op window's Give..., host and joiners alike). Conversations
// and payments use only the host's bag (as in single player), so this is how a group pools what a deal needs: by hand,
// nothing taken from anyone without them choosing it. An item goes as its whole stack (its condition, ammo and stacks
// kept: Works' Inventory.Remove / Put); charons by any amount. Only bag items, not what's worn. It all happens on the
// host; both characters' changed sheets reach their players as any change does.
// Messages, joiner to host: GIVE <to> <item code>, GIVEMONEY <to> <amount>. Console: coop.give <to> <item code>,
// coop.give <to> charons <amount> ("to" is a character's name).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    // --- Host ---------------------------------------------------------------------------------------------------
    // A player's character by its name (the host's own, or a joined one).
    Character PlayerCharacter(string name)
    {
        if (string.IsNullOrEmpty(name)) return null;
        var me = World.Player;
        if (me != null && me.Name.Equals(name, StringComparison.OrdinalIgnoreCase)) return me;
        return seats.Values.Select(s => s.Character).FirstOrDefault(c => c != null && c.Name.Equals(name, StringComparison.OrdinalIgnoreCase));
    }

    // from gives to: an item (whole stack, by code) or money (> 0).
    string Give(Character from, string to, string code, int money)
    {
        var target = PlayerCharacter(to);
        if (from == null || target == null) return "no player called " + to;
        if (target == from) return "that's you";
        if (target.IsDead || target.Area == null) return to + " can't take anything now";
        string what;
        if (money > 0)
        {
            if (from.Inventory.Money < money) return "you have " + from.Inventory.Money + " charons";
            if (!from.Inventory.TakeMoney(money, false)) return "couldn't take the charons";
            target.Inventory.AddMoney(money, false);
            what = money + " charons";
        }
        else
        {
            var item = from.Inventory.Items.FirstOrDefault(i => i.Code == code);
            if (item == null) return "you don't have that in your bag (worn items can't be given)";
            what = item.Name + (item.Stacks > 1 ? " x" + item.Stacks : "");
            if (!from.Inventory.Remove(item)) return "couldn't take " + item.Name + " out of your bag";
            target.Inventory.Put(item);
        }
        Log.Info(from.Name + " gave " + what + " to " + target.Name);
        var line = from.Name + " gave " + what + " to " + target.Name;
        foreach (var s in seats.Values.Where(x => x.Peer != null && (x.Character == from || x.Character == target))) s.Peer.Send("SAID", line);
        if (from.IsPlayer || target.IsPlayer) Notify.Log("Co-op: " + line, Say);
        return "";
    }

    string GiveFrom(Character c, string[] w)
    {
        if (w.Length < 3) return "give what to whom?";
        if (w[0] == "GIVEMONEY") { int n; return int.TryParse(w[2], out n) && n > 0 ? Give(c, w[1], null, n) : "how many charons?"; }
        return Give(c, w[1], w[2], 0);
    }

    // --- Both: the Give window ----------------------------------------------------------------------------------
    UiWindow giveWindow;
    void OpenGive()
    {
        if (giveWindow != null && giveWindow.IsOpen) { giveWindow.Close(); return; }
        var me = World.Player;
        if (me == null) return;
        // Who can be given to: the other players' characters (the host's list, or the party a joiner was sent).
        var names = new List<string>();
        if (listener != null) names.AddRange(seats.Values.Where(s => s.Character != null && !s.Character.IsDead).Select(s => s.Character.Name));
        else if (party != null) for (int i = 0; i + 3 < party.Length; i += 4) if (party[i] != me.Name && party[i + 3] != "1") names.Add(party[i]);
        if (names.Count == 0) { Tell("nobody to give to"); return; }
        var items = me.Inventory.Items.OrderBy(i => i.Name).ToList();
        int pad = Ui.S(10), w = Ui.S(520), h = Ui.S(420);
        giveWindow = Ui.Window(this, "give", "Give", w, h);
        if (giveWindow == null) return;
        giveWindow.Closed += () => giveWindow = null;
        w = giveWindow.Width; h = giveWindow.Height;
        int y = giveWindow.Top + Ui.S(4);
        Ui.Add(giveWindow, Ui.Label("To:", Ui.S(40)), pad, y + Ui.S(4));
        var to = Ui.Choice(w - 2 * pad - Ui.S(46), names, 0);
        Ui.Add(giveWindow, to.View, pad + Ui.S(46), y);
        y += Math.Max(to.View.Height, Ui.LineHeight) + Ui.S(8);
        var rows = new List<string> { "Charons (" + me.Inventory.Money + ")..." };
        rows.AddRange(items.Select(i => i.Name + (i.Stacks > 1 ? "  x" + i.Stacks : "")));
        int chosen = -1;
        var go = Ui.Button("Give", () =>
        {
            var who = names[Math.Max(0, Math.Min(names.Count - 1, to.Index))];
            if (chosen == 0)
            {
                Ui.AskNumber(this, "Charons to give " + who, 1, Math.Max(1, me.Inventory.Money), Math.Min(10, Math.Max(1, me.Inventory.Money)), n => SendGive(who, null, n), "Give");
                return;
            }
            if (chosen < 1 || chosen > items.Count) { Tell("choose what to give"); return; }
            SendGive(who, items[chosen - 1].Code, 0);
            if (giveWindow != null) giveWindow.Close();
        });
        var cancel = Ui.Button("Close", () => { if (giveWindow != null) giveWindow.Close(); });
        int by = h - pad - go.Height;
        var list = Ui.List(w - 2 * pad, by - Ui.S(8) - y);
        list.Items = rows;
        list.Selected += i => chosen = i;
        Ui.Add(giveWindow, list.View, pad, y);
        Ui.Add(giveWindow, go, pad, by);
        Ui.Add(giveWindow, cancel, pad + go.Width + Ui.S(6), by);
    }

    void SendGive(string to, string code, int money)
    {
        if (listener != null)
        {
            var r = Give(World.Player, to, code, money);
            if (!string.IsNullOrEmpty(r)) Notify.Log("Co-op: " + r, Grey);
            return;
        }
        if (host == null || host.Closed || !playing) return;
        if (money > 0) host.Send("GIVEMONEY", to, money); else host.Send("GIVE", to, code);
    }

    // Quest items always go to the host: the game's quests, conversations and scripts look in the host's bag (as in single
    // player), so one a joiner picks up (looting, a reward, a trade) moves there at once, and a story moment can't fail
    // because it sat with another player. Checked every second on the host.
    double questTimer;
    void MoveQuestItems(double dt)
    {
        if (listener == null || (questTimer -= dt) > 0) return;
        questTimer = 1;
        var me = World.Player;
        if (me == null || me.IsDead || Simulation.IsMirror) return;
        foreach (var s in seats.Values.Concat(orphans).Where(x => x.Character != null && !x.Character.IsDead && x.Character.Area != null).ToList())
            foreach (var item in s.Character.Inventory.Items.Where(i => i.IsQuestItem).ToList())
            {
                var what = item.Name + (item.Stacks > 1 ? " x" + item.Stacks : "");
                if (!s.Character.Inventory.Remove(item)) continue;
                me.Inventory.Put(item);
                Log.Info("quest item " + item.Code + " moved from " + s.Character.Name + " to " + me.Name);
                var line = s.Character.Name + "'s " + what + " went to " + me.Name + " (quest items stay with the host)";
                Notify.Log("Co-op: " + line, Say);
                if (s.Peer != null) s.Peer.Send("SAID", line);
            }
    }

    void LoadGive()
    {
        GameEvents.Update.Add(this, MoveQuestItems);
        Commands.Add(this, "coop.give", "coop.give <to> <item code> | coop.give <to> charons <amount>: give a player an item from your bag, or charons", a =>
        {
            if (a.Length < 2) return "coop.give <to> <item code> | coop.give <to> charons <amount>";
            int n = 0;
            if (a[1].Equals("charons", StringComparison.OrdinalIgnoreCase) && (a.Length < 3 || !int.TryParse(a[2], out n) || n <= 0)) return "coop.give <to> charons <amount>";
            SendGive(a[0], n > 0 ? null : a[1], n);
            return "giving";
        });
    }
}
