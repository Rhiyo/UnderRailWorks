// UnderRailWorks: reloading, by the player and by NPCs' AI alike (the game's reload action nh, its step nh.fr): the
// weapon is the one in hand (or the item the action names), the ammo the item it was given (none: unloading).
//   GameEvents.Reloading.Add(this, r => { if (!r.Character.IsPlayer && Combat.Round < 2) r.Cancel = true; });
//   GameEvents.Reloaded.Add(this, r => Log.Info(r.Character.Name + " reloaded " + (r.Weapon == null ? "" : r.Weapon.Name)));
using System;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class Reload : Cancellable
    { internal Reload() { }
        public Character Character { get; internal set; }
        public Item Weapon { get; internal set; }     // null if it couldn't be told
        public Item Ammo { get; internal set; }       // null: unloading, or not known
    }

    public static partial class GameEvents
    {
        // A character about to reload (Cancel: it doesn't; the action ends without AP spent on loading).
        public static readonly Hook<Action<Reload>> Reloading = new Hook<Action<Reload>>("Reloading");
        // A reload went through (ammo loaded).
        public static readonly Hook<Action<Reload>> Reloaded = new Hook<Action<Reload>>("Reloaded");
    }
}

namespace UnderRailWorks.Internal
{
    static class ReloadHooks
    {
        // nh: m_a which weapon (a: the one in hand, c: the target item, b: the action's item), the target item is the
        // ammo unless unloading. (The decompile's m_a is the field "a".)
        static readonly FieldInfo Mode = typeof(nh).GetField("m_a", BindingFlags.Instance | BindingFlags.NonPublic) ?? typeof(nh).GetField("a", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);

        static bool Reloading(nh __instance, ref dc4 __result, ref Reload __state)
        {
            __state = null;
            if (!GameEvents.Reloading.Any && !GameEvents.Reloaded.Any) return true;
            try
            {
                var ctx = __instance.vg();
                var actor = ctx == null ? null : ctx.q();
                var data = actor == null ? null : actor.GetAspect<amc>();
                if (data == null) return true;
                int mode = Mode == null ? 0 : Convert.ToInt32(Mode.GetValue(__instance));
                var target = ctx.e() == null ? null : ctx.e().b9();
                coh weapon = mode == 0 ? data.aa().Offense.u() as coh : mode == 2 ? target as coh : __instance.fm() as coh;
                coh ammo = mode == 2 ? null : target as coh;
                var e = new Reload { Character = Character.Of(actor), Weapon = weapon == null ? null : new Item(weapon), Ammo = ammo == null ? null : new Item(ammo) };
                if (GameEvents.Reloading.Any) GameEvents.Reloading.Run(h => h(e));
                if (e.Cancel) { __result = dc4.e; return false; }
                __state = e;
            }
            catch (Exception ex) { Log.Write("reload: " + ex.Message); }
            return true;
        }

        static void Reloaded(dc4 __result, Reload __state)
        {
            if (__state == null || __result != dc4.c || !GameEvents.Reloaded.Any) return;
            try { GameEvents.Reloaded.Run(h => h(__state)); } catch (Exception ex) { Log.Write("reload: " + ex.Message); }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(ReloadHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(nh).GetMethod("fr", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(TimeSpan), typeof(TimeSpan) }, null), prefix: m("Reloading"), postfix: m("Reloaded"));
        }
    }
}
