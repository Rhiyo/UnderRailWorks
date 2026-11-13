// Co-op looting. A joined character opening a container on the host (its use, sent by the joiner's click): the host's
// game doesn't open its loot window (Works' ContainerOpening, cancelled) and sends the contents instead; the joiner's
// Loot window takes items one by one or all, into the character's own bag on the host (its sheet then goes to the
// joiner again, so their inventory shows them).
// Messages, host to joiner: LOOT <id> <name>, LOOTITEM <index> <text>..., LOOTSHOW. Joiner to host: TAKE <id> <index>, TAKEALL <id>.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks;

public sealed partial class Coop
{
    // --- Host ---------------------------------------------------------------------------------------------------
    void LoadLoot()
    {
        GameEvents.ContainerOpening.Add(this, e =>
        {
            if (listener == null || e.Opener == null || e.Opener.IsPlayer) return;
            var seat = seats.Values.FirstOrDefault(s => s.Character == e.Opener);
            if (seat == null) return;
            e.Cancel = true;
            SendLoot(seat, e.Container);
        }, HookOrder.First);
    }

    void SendLoot(Seat seat, Container box)
    {
        seat.Peer.Send("LOOT", box.EntityId, box.Name ?? "Container");
        var items = box.Items;
        for (int i = 0; i < items.Count; i++) seat.Peer.Send("LOOTITEM", i, items[i].ToString());
        seat.Peer.Send("LOOTSHOW");
    }

    string Take(Character c, string id, string index)
    {
        var seat = seats.Values.FirstOrDefault(s => s.Character == c);
        var box = World.Containers.FirstOrDefault(x => x.EntityId == id);
        if (seat == null || box == null) return "that's gone";
        var at = box.Position;
        if (Math.Abs(at.X - c.Position.X) + Math.Abs(at.Y - c.Position.Y) > 3.5f) return "you're too far from it";
        var items = box.Items;
        var take = index == null ? items : items.Where((x, i) => i.ToString() == index).ToList();
        foreach (var item in take) if (box.Remove(item)) c.Inventory.Put(item);
        Log.Info(c.Name + " took " + string.Join(", ", take.Select(x => x.ToString()).ToArray()) + " from " + box.Name);
        SendLoot(seat, box);
        return take.Count == 0 ? "" : "took " + string.Join(", ", take.Select(x => x.ToString()).ToArray());
    }

    // --- Joiner -------------------------------------------------------------------------------------------------
    UiWindow lootWindow;
    UiList lootList;
    string lootId, lootName;
    readonly List<string> lootItems = new List<string>();

    bool LootMessage(string[] w)
    {
        switch (w[0])
        {
            case "LOOT": lootId = w.Length > 1 ? w[1] : null; lootName = w.Length > 2 ? w[2] : "Container"; lootItems.Clear(); return true;
            case "LOOTITEM": if (w.Length > 2) lootItems.Add(w[2]); return true;
            case "LOOTSHOW": ShowLoot(); return true;
        }
        return false;
    }

    void ShowLoot()
    {
        if (lootWindow == null || !lootWindow.IsOpen)
        {
            int w = Ui.S(420), h = Ui.S(340), pad = Ui.S(10);
            lootWindow = Ui.Window(this, "loot", "Loot", w, h);
            if (lootWindow == null) return;
            lootWindow.Closed += () => { lootWindow = null; };
            w = lootWindow.Width; h = lootWindow.Height;
            var all = Ui.Button("Take all", () => { if (host != null && lootId != null) host.Send("TAKEALL", lootId); });
            var close = Ui.Button("Close", () => { if (lootWindow != null) lootWindow.Close(); });
            int top = lootWindow.Top + Ui.S(4);
            lootList = Ui.List(w - 2 * pad, h - top - pad - all.Height - Ui.S(8));
            lootList.Selected += row => { if (row >= 0 && row < lootItems.Count && host != null && lootId != null) host.Send("TAKE", lootId, row); };
            Ui.Add(lootWindow, lootList.View, pad, top);
            Ui.Add(lootWindow, all, pad, h - pad - all.Height);
            Ui.Add(lootWindow, close, pad + all.Width + Ui.S(6), h - pad - close.Height);
        }
        lootList.Items = lootItems.Count == 0 ? new List<string> { lootName + " is empty." } : lootItems.ToList();
        lootList.ItemColors = lootList.Items.Select(_ => lootItems.Count == 0 ? Grey : Normal).ToList();
        lootList.Selection = -1;
    }
}
