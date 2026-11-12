// UnderRailWorks: events that can stop what the game is about to do (tModLoader's CanX hooks, sfall's hook return
// values). Each runs before the game's step; a handler sets Cancel (in hook order, see HookOrder) and the game's
// step doesn't happen, quietly, as if it had been refused.
using System;
using System.Collections.Generic;
using System.Linq;
using TimelapseVertigo.Rules.Items;
using TimelapseVertigo.Rules.Characters;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public class Cancellable { public bool Cancel { get; set; } }
    public sealed class TradeStart : Cancellable { public Character Merchant { get; internal set; } }
    public sealed class ContainerOpen : Cancellable { public Container Container { get; internal set; } public Character Opener { get; internal set; } }
    public sealed class ItemTake : Cancellable { public Container Container { get; internal set; } public Item Item { get; internal set; } public Character Taker { get; internal set; } }
    public sealed class DialogStart : Cancellable { public string Name { get; set; } public Character With { get; internal set; } }
    // The player putting an item into a gear or quick slot (Cancel: it stays where it was). The game's own scripted
    // equips (NPC loadouts, Items.ToQuickSlot) aren't asked.
    public sealed class EquipAttempt : Cancellable { public Character Character { get; internal set; } public Item Item { get; internal set; } }
    // The player about to leave the zone (an exit, a door between maps, a script, World.Travel). To: the destination
    // zone's id, Entry: the entry point there.
    public sealed class ZoneLeave : Cancellable { public string From { get; internal set; } public string To { get; internal set; } public string Entry { get; internal set; } public Character Who { get; internal set; } }
    // A character (the player or an NPC's AI) about to switch between its primary and secondary weapon.
    public sealed class WeaponSwitch : Cancellable { public Character Character { get; internal set; } public bool ToSecondary { get; internal set; } }
    public sealed class CraftAttempt : Cancellable { public string Blueprint { get; internal set; } }

    public static partial class GameEvents
    {
        public static readonly Hook<Action<TradeStart>> TradeOpening = new Hook<Action<TradeStart>>("TradeOpening");
        public static readonly Hook<Action<ContainerOpen>> ContainerOpening = new Hook<Action<ContainerOpen>>("ContainerOpening");
        // An item about to be taken from a container by the player (Cancel: it stays; in "Take all" the rest stay too).
        public static readonly Hook<Action<ItemTake>> ItemTaking = new Hook<Action<ItemTake>>("ItemTaking");
        // A conversation about to open (Cancel, or change Name to open another). Not combat barks.
        public static readonly Hook<Action<DialogStart>> DialogStarting = new Hook<Action<DialogStart>>("DialogStarting");
        public static readonly Hook<Action<EquipAttempt>> Equipping = new Hook<Action<EquipAttempt>>("Equipping");
        public static readonly Hook<Action<ZoneLeave>> ZoneLeaving = new Hook<Action<ZoneLeave>>("ZoneLeaving");
        // A character other than the player is about to take a zone exit (which changes the zone for the whole game, as
        // the player's would): Who, From, To, Entry. Cancel: nothing happens.
        public static readonly Hook<Action<ZoneLeave>> CharacterLeavingZone = new Hook<Action<ZoneLeave>>("CharacterLeavingZone");
        public static readonly Hook<Action<WeaponSwitch>> WeaponSwitching = new Hook<Action<WeaponSwitch>>("WeaponSwitching");
        public static readonly Hook<Action<CraftAttempt>> Crafting = new Hook<Action<CraftAttempt>>("Crafting");
    }
}

namespace UnderRailWorks.Internal
{
    static class CancelHooks
    {
        internal static readonly FieldInfo CurrentWeapon = typeof(dcu).GetField("m_d", BindingFlags.Instance | BindingFlags.NonPublic) ?? typeof(dcu).GetField("d", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        // dcu.a(WeaponKey): the switch itself (the player's key, the AI's swap nodes, scripts).
        static bool Switch(dcu __instance, WeaponKey __0)
        {
            if (!GameEvents.WeaponSwitching.Any) return true;
            try
            {
                if (CurrentWeapon != null && Equals(CurrentWeapon.GetValue(__instance), __0)) return true;     // no change
                var who = Combat.OfData(__instance.a1());
                if (who == null) return true;
                var e = new WeaponSwitch { Character = who, ToSecondary = __0 == WeaponKey.Secondary };
                GameEvents.WeaponSwitching.Run(h => h(e));
                return !e.Cancel;
            }
            catch (Exception ex) { Log.Write("weapon switch: " + ex.Message); return true; }
        }

        // a0l.a(character, destination, ...): queues the game's ChangeZone workflow (exits and scripts alike).
        static bool Leave(c5x __0, c2w __1)
        {
            if (__0 == null || __1 == null) return true;
            try { if (!Party.Allows(PartyNeed.EnterZone, __1.b(), Character.Of(__0))) return false; } catch (Exception ex) { Log.Write("party: " + ex.Message); }
            if (!__0.IsPlayer)
            {
                if (!GameEvents.CharacterLeavingZone.Any) return true;
                try { var c = new ZoneLeave { From = World.ZoneId, To = __1.b(), Entry = __1.c(), Who = Character.Of(__0) }; GameEvents.CharacterLeavingZone.Run(h => h(c)); return !c.Cancel; }
                catch (Exception ex) { Log.Write("zone leave: " + ex.Message); return true; }
            }
            if (!GameEvents.ZoneLeaving.Any) { World.PackFollowers(); return true; }
            try
            {
                var e = new ZoneLeave { From = World.ZoneId, To = __1.b(), Entry = __1.c(), Who = Character.Of(__0) };
                GameEvents.ZoneLeaving.Run(h => h(e));
                if (!e.Cancel) World.PackFollowers();
                return !e.Cancel;
            }
            catch (Exception ex) { Log.Write("zone leave: " + ex.Message); return true; }
        }

        static bool Trade(object __1)
        {
            if (!GameEvents.TradeOpening.Any || !(__1 is b0b)) return true;
            var e = new TradeStart { Merchant = Character.Of((b0b)__1) };
            GameEvents.TradeOpening.Run(h => h(e));
            return !e.Cancel;
        }

        static bool Open(c7j __instance, c5x __0)
        {
            if (!GameEvents.ContainerOpening.Any) return true;
            var e = new ContainerOpen { Container = new Container(__instance), Opener = __0 == null ? null : Character.Of(__0) };
            Party.As(e.Opener, () => GameEvents.ContainerOpening.Run(h => h(e)));
            return !e.Cancel;
        }

        static bool Take(c7j __instance, coh __1, object __2, ref bool __result)
        {
            if (!GameEvents.ItemTaking.Any || __1 == null) return true;
            var e = new ItemTake { Container = new Container(__instance), Item = new Item(__1), Taker = __2 is amc ? Character.Of(((amc)__2).a8()) : World.Player };
            Party.As(e.Taker, () => GameEvents.ItemTaking.Run(h => h(e)));
            if (!e.Cancel) return true;
            __result = false;
            return false;
        }

        static readonly FieldInfo Blueprint = typeof(ahu).GetField("m_b", BindingFlags.Instance | BindingFlags.NonPublic) ?? typeof(ahu).GetField("b", BindingFlags.Instance | BindingFlags.NonPublic);
        static bool Craft(ahu __instance, ref cfe __result)
        {
            if (!GameEvents.Crafting.Any) return true;
            var bp = Blueprint.GetValue(__instance) as cwp;
            var e = new CraftAttempt { Blueprint = bp == null ? null : bp.LocalizedName };
            GameEvents.Crafting.Run(h => h(e));
            if (!e.Cancel) return true;
            craftRefused = true;
            __result = null;
            return false;
        }

        // "Craft all" (eed.a(object, b25)) loops while its button is enabled, and the window's refresh (eed.b()) enables it
        // again while the components are there: refused, nothing is used up, so it would loop forever. After a refused
        // craft the refresh leaves the button off.
        static bool craftRefused;
        static readonly FieldInfo CraftAllButton = typeof(eed).GetField("m_e", BindingFlags.Instance | BindingFlags.NonPublic) ?? typeof(eed).GetField("e", BindingFlags.Instance | BindingFlags.NonPublic);
        static void Refreshed(eed __instance)
        {
            if (!craftRefused) return;
            craftRefused = false;
            var button = CraftAllButton == null ? null : CraftAllButton.GetValue(__instance) as euz;
            if (button != null) button.Enabled = false;
        }

        // The inventory screen moves an item with c69.a(source, target, item): it takes the item out of the source
        // (A_0.yd(), inside the condition) before it asks the target (A_1.ye), so a refusal anywhere inside the move
        // loses the item (measured: the armour was gone from bag and body). Mods are asked here, before the move starts;
        // refused, nothing moves. Once per move (the item coming back in a swap isn't asked).
        static bool Move(ebk __0, ebk __1, coh __2, ref bool __result)
        {
            if (!GameEvents.Equipping.Any || __1 == null || __2 == null || World.Player == null) return true;
            var t = __1.GetType();
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(TimelapseVertigo.UI.Controls.PlayerGearItemSlotControl<>)) return true;
            try
            {
                var e = new EquipAttempt { Character = World.Player, Item = new Item(__2) };
                GameEvents.Equipping.Run(h => h(e));
                if (!e.Cancel) return true;
                __result = false;
                return false;
            }
            catch (Exception ex) { Log.Write("equip: " + ex.Message); return true; }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(CancelHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(dka).GetMethods(any).First(x => x.Name == "amw" && x.GetParameters().Length == 2), prefix: m("Trade"));
            harmony.Patch(typeof(c7j).GetMethod("mz", any, null, new[] { typeof(c5x), typeof(string) }, null), prefix: m("Open"));
            harmony.Patch(typeof(c7j).GetMethod("kj", any, null, new[] { typeof(dbi), typeof(coh), typeof(object) }, null), prefix: m("Take"));
            harmony.Patch(typeof(c69).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(ebk), typeof(ebk), typeof(coh), typeof(bool) }, null), prefix: m("Move"));
            harmony.Patch(typeof(a0l).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(c5x), typeof(c2w), typeof(int), typeof(Microsoft.Xna.Framework.Point?) }, null), prefix: m("Leave"));
            harmony.Patch(typeof(eed).GetMethod("b", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null), postfix: m("Refreshed"));
            harmony.Patch(typeof(dcu).GetMethod("a", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(WeaponKey) }, null), prefix: m("Switch"));
            harmony.Patch(typeof(ahu).GetMethod("i", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null), prefix: m("Craft"));
        }
    }
}
