// UnderRailWorks: events that can stop what the game is about to do (tModLoader's CanX hooks, sfall's hook return
// values). Each runs before the game's step; a handler sets Cancel (in hook order, see HookOrder) and the game's
// step doesn't happen, quietly, as if it had been refused.
using System;
using System.Collections.Generic;
using System.Linq;
using TimelapseVertigo.Rules.Items;
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
    public sealed class ZoneLeave : Cancellable { public string From { get; internal set; } public string To { get; internal set; } public string Entry { get; internal set; } }
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
        public static readonly Hook<Action<CraftAttempt>> Crafting = new Hook<Action<CraftAttempt>>("Crafting");
    }
}

namespace UnderRailWorks.Internal
{
    static class CancelHooks
    {
        // a0l.a(character, destination, ...): queues the game's ChangeZone workflow (exits and scripts alike).
        static bool Leave(c5x __0, c2w __1)
        {
            if (!GameEvents.ZoneLeaving.Any || __0 == null || !__0.IsPlayer || __1 == null) return true;
            try
            {
                var e = new ZoneLeave { From = World.ZoneId, To = __1.b(), Entry = __1.c() };
                GameEvents.ZoneLeaving.Run(h => h(e));
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
            GameEvents.ContainerOpening.Run(h => h(e));
            return !e.Cancel;
        }

        static bool Take(c7j __instance, coh __1, object __2, ref bool __result)
        {
            if (!GameEvents.ItemTaking.Any || __1 == null) return true;
            var e = new ItemTake { Container = new Container(__instance), Item = new Item(__1), Taker = __2 is amc ? Character.Of(((amc)__2).a8()) : World.Player };
            GameEvents.ItemTaking.Run(h => h(e));
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
            __result = null;
            return false;
        }

        // The inventory screen asks the target control ye(source, item) before it moves anything (c69.a: take from the
        // source, put into the target, the old item back): refusing there leaves both where they were. (Refusing in the
        // data slot's own check lost the item: the screen had already taken it out of the bag.)
        static void CanDrop(object __instance, coh __1, ref bool __result)
        {
            if (!__result || !GameEvents.Equipping.Any || __1 == null || World.Player == null) return;
            var t = __instance.GetType();
            if (!t.IsGenericType || t.GetGenericTypeDefinition() != typeof(TimelapseVertigo.UI.Controls.PlayerGearItemSlotControl<>)) return;
            try
            {
                var e = new EquipAttempt { Character = World.Player, Item = new Item(__1) };
                GameEvents.Equipping.Run(h => h(e));
                if (e.Cancel) __result = false;
            }
            catch (Exception ex) { Log.Write("equip: " + ex.Message); }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(CancelHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(dka).GetMethods(any).First(x => x.Name == "amw" && x.GetParameters().Length == 2), prefix: m("Trade"));
            harmony.Patch(typeof(c7j).GetMethod("mz", any, null, new[] { typeof(c5x), typeof(string) }, null), prefix: m("Open"));
            harmony.Patch(typeof(c7j).GetMethod("kj", any, null, new[] { typeof(dbi), typeof(coh), typeof(object) }, null), prefix: m("Take"));
            harmony.Patch(typeof(TimelapseVertigo.UI.Controls.FreeItemSlotControl<coh>).GetMethod("ye", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(ebk), typeof(coh) }, null), postfix: m("CanDrop"));
            harmony.Patch(typeof(a0l).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(c5x), typeof(c2w), typeof(int), typeof(Microsoft.Xna.Framework.Point?) }, null), prefix: m("Leave"));
            harmony.Patch(typeof(ahu).GetMethod("i", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null), prefix: m("Craft"));
        }
    }
}
