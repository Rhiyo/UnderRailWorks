// UnderRailWorks: what a character wears and wields, equipping from code, and the equip/unequip events.
// The game's side (measured from its code): a character's gear is acd (efk.Gear) with one ItemSlot<T> per slot
// (helmet g(), armor l(), belt e(), boots f(), shield emitter h(), weapons j()[Primary/Secondary], quick slots d()).
// Every slot change goes through acd's handler, which calls its private a(item) for the old item and b(item) for
// the new one: the hooks sit there, so equipping by the inventory screen, by a quick slot, by dialog or by code all
// raise the events. Equipping the game's way is bn.a(character, item, AutoEquipMode, ref cursor) (it picks the slot by
// item class and puts the slot's old item into the bag, but does not take the new one out of it).
//   foreach (var kv in World.Player.Equipment.All) Log.Info(kv.Key + ": " + kv.Value.Name);
//   GameEvents.Equipped.Add(this, e => { if (e.Item.Code == "...") ... });
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TimelapseVertigo.Rules.Items;
using TimelapseVertigo.Rules.Characters;
using TimelapseVertigo.Generators.Items;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public enum EquipSlot { Helmet, Armor, Belt, Boots, Shield, PrimaryWeapon, SecondaryWeapon }

public sealed class EquipChange
{ internal EquipChange() { }
    public Character Character { get; internal set; }
    public Item Item { get; internal set; }
}

public static partial class GameEvents
{
    // An item went into one of a character's slots (gear or quick slot), or came out of one.
    public static readonly Hook<Action<EquipChange>> Equipped = new Hook<Action<EquipChange>>("Equipped");
    public static readonly Hook<Action<EquipChange>> Unequipped = new Hook<Action<EquipChange>>("Unequipped");
}

public sealed class Equipment
{
    readonly Func<efk> owner;
    internal Equipment(Func<efk> owner) { this.owner = owner; }
    efk Owner { get { var o = owner(); if (o == null) throw new InvalidOperationException("the character is gone"); return o; } }

    // The item in a slot (null: empty).
    public Item this[EquipSlot slot] { get { var i = Slot(Owner.Gear, slot); return i == null ? null : new Item(i); } }

    // Everything worn or wielded, by slot.
    public IDictionary<EquipSlot, Item> All
    {
        get
        {
            var gear = Owner.Gear;
            var result = new Dictionary<EquipSlot, Item>();
            foreach (EquipSlot s in Enum.GetValues(typeof(EquipSlot))) { var i = Slot(gear, s); if (i != null) result[s] = new Item(i); }
            return result;
        }
    }

    // The quick slots' items (null for an empty slot), as many as the character has.
    public IList<Item> QuickSlots
    {
        get { return Owner.Gear.d().Where(s => s.Enabled).Select(s => s.Item == null ? null : new Item(s.Item)).ToList(); }
    }

    // Equips an item from the bag the game's way (the slot follows from the item's kind; what was there goes into the
    // bag). Free of AP and requirement checks, as dialog rewards are: a mod that wants them checks them itself.
    // A weapon goes into the primary slot, or the secondary one with secondary = true.
    public bool Equip(Item item) { return Equip(item, false); }
    public bool Equip(Item item, bool secondary)
    {
        if (item == null) return false;
        var c = Owner;
        if (!c.Bag.z5(item.Game)) return false;     // take it out of the bag first: bn.a doesn't
        int cursor = 0;
        bool worn;
        try { worn = bn.a(c, item.Game, secondary ? AutoEquipMode.Secondary : AutoEquipMode.Primary, ref cursor); }
        catch { c.Bag.z3(item.Game, false); throw; }     // never lost between bag and slot
        if (worn) { c.bl(); return true; }
        c.Bag.z3(item.Game, false);     // no slot took it: back into the bag
        return false;
    }

    // Puts an item from the bag into a quick slot (index from 0; -1: the first free one), as the game's scripts do
    // (bn.SwapIn: what was there goes back into the bag). bn.a only takes items it knows as quick-slot items, which
    // medicine like bandages isn't (its quick-slot flag is unset in the data, yet the player can slot it).
    public bool ToQuickSlot(Item item, int slot = -1)
    {
        if (item == null) return false;
        var c = Owner;
        var slots = c.Gear.d();
        if (slot < 0) slot = Enumerable.Range(0, Math.Min(c.AvailableQuickSlots, slots.Length)).Where(i => slots[i].Enabled && slots[i].Item == null).DefaultIfEmpty(-1).First();
        if (slot < 0 || slot >= Math.Min(c.AvailableQuickSlots, slots.Length) || !slots[slot].Enabled) return false;
        if (!c.Bag.z5(item.Game)) return false;
        bn.SwapIn(slots[slot], c.Bag, item.Game);
        c.bl();
        return true;
    }

    // Takes the item out of a slot into the bag.
    public Item Unequip(EquipSlot slot)
    {
        var c = Owner;
        var i = Slot(c.Gear, slot);
        if (i == null) return null;
        c.Gear.c(i);
        c.Bag.z3(i, false);
        c.bl();
        return new Item(i);
    }

    static coh Slot(acd gear, EquipSlot slot)
    {
        switch (slot)
        {
            case EquipSlot.Helmet: return gear.g().Item;
            case EquipSlot.Armor: return gear.l().Item;
            case EquipSlot.Belt: return gear.e().Item;
            case EquipSlot.Boots: return gear.f().Item;
            case EquipSlot.Shield: return gear.h().Item;
            case EquipSlot.PrimaryWeapon: return gear.j()[WeaponKey.Primary].Item;
            case EquipSlot.SecondaryWeapon: return gear.j()[WeaponKey.Secondary].Item;
        }
        return null;
    }
}

static class EquipmentHooks
{
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        const BindingFlags priv = BindingFlags.Instance | BindingFlags.NonPublic;
        Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(EquipmentHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
        Func<string, MethodInfo> notifier = n => typeof(acd).GetMethods(priv).Single(x => x.Name == n && x.ReturnType == typeof(void) && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(coh));
        harmony.Patch(notifier("a"), postfix: m("Off"));     // the slot's old item (c1.b())
        harmony.Patch(notifier("b"), postfix: m("On"));      // its new one (c1.a())
    }

    static void On(acd __instance, coh __0) { try { Raise(GameEvents.Equipped, __instance, __0); } catch (Exception ex_) { PatchGuard.Failed("Equipment.On", ex_); }
    }
    static void Off(acd __instance, coh __0) { try { Raise(GameEvents.Unequipped, __instance, __0); } catch (Exception ex_) { PatchGuard.Failed("Equipment.Off", ex_); }
    }

    static void Raise(Hook<Action<EquipChange>> hook, acd gear, coh item)
    {
        if (!hook.Any || item == null) return;
        try
        {
            var who = OwnerOf(gear);
            if (who == null) return;     // a character not on this map (or a merchant's display copy)
            var e = new EquipChange { Character = who, Item = new Item(item) };
            hook.Run(h => h(e));
        }
        catch (Exception ex) { Log.Write("equipment: " + ex.Message); }
    }

    // The gear belongs to the player, or to a character on the map.
    static Character OwnerOf(acd gear)
    {
        var p = World.Player;
        if (p != null && p.IsCharacter && p.HasGear(gear)) return p;
        foreach (var c in World.Characters) if (c.HasGear(gear)) return c;
        return null;
    }
}
}
