// UnderRailWorks: area triggers and usable objects, never saved.
// - Triggers: rectangles of tiles in an area, checked by Works a few times a second against where characters stand
//   (the game's own triggers are saved objects of the area; Works keeps its own instead): entered / left.
// - Usable objects: a Placement whose Blueprint is an object with a use (a lever, a terminal: anything the player can
//   click to use) and an OnUse handler. Placed like characters (taken out around saves, re-created on zone load);
//   using it runs the handler instead of the object's own use (a prefix on bim.a(user, variant, skip, skip)).
//   Triggers.Add(this, "LU-SouthGateStation-N", "LU-SouthGateStation-N", new Rectangle(10, 20, 3, 3), who => Notify.OverPlayer("A draft..."), null);
//   World.Place(new Placement { Id = "lever", Zone = ..., X = 12, Y = 22, Blueprint = "objects\\devices\\...", OnUse = who => ... });
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class AreaTrigger
    {
        public string Mod { get; internal set; }
        public string Zone { get; internal set; }
        public string Area { get; internal set; }
        public Rectangle Tiles { get; internal set; }
        // Only the player (default), or every character.
        public bool PlayerOnly { get; set; } = true;
        internal Action<Character> Enter, Leave;
        internal readonly HashSet<object> Inside = new HashSet<object>();
        public void Remove() { lock (Triggers.All) Triggers.All.Remove(this); }
    }

    public static class Triggers
    {
        internal static readonly List<AreaTrigger> All = new List<AreaTrigger>();
        // zone/area: ids as World.ZoneId and Area.Name show them (area null: any area of the zone).
        public static AreaTrigger Add(WorksMod mod, string zone, string area, Rectangle tiles, Action<Character> entered, Action<Character> left)
        {
            var t = new AreaTrigger { Mod = mod.Id, Zone = zone, Area = area, Tiles = tiles, Enter = entered, Leave = left };
            lock (All) All.Add(t);
            return t;
        }

        static double since;
        internal static void Tick(double seconds)
        {
            since += seconds;
            if (since < 0.2 || All.Count == 0) return;
            since = 0;
            var zone = World.ZoneId;
            if (zone == null || World.Player == null) return;
            List<AreaTrigger> here;
            lock (All) here = All.Where(t => string.Equals(t.Zone, zone, StringComparison.OrdinalIgnoreCase)).ToList();
            if (here.Count == 0) return;
            var people = here.Any(t => !t.PlayerOnly) ? World.Characters : new List<Character> { World.Player };
            foreach (var t in here)
            {
                var now = new HashSet<object>();
                foreach (var c in people)
                {
                    if (t.PlayerOnly && !c.IsPlayer) continue;
                    if (t.Area != null && (c.Area == null || !string.Equals(c.Area.Name, t.Area, StringComparison.OrdinalIgnoreCase))) continue;
                    var p = c.Position;
                    if (!t.Tiles.Contains((int)Math.Floor(p.X), (int)Math.Floor(p.Y))) continue;
                    now.Add(c.Entity);
                    if (!t.Inside.Contains(c.Entity) && t.Enter != null) Run(t, t.Enter, c);
                }
                foreach (var gone in t.Inside.Where(e => !now.Contains(e)).ToList())
                    if (t.Leave != null) { var c = Character.Of((b0b)gone); if (c != null) Run(t, t.Leave, c); }
                t.Inside.Clear(); foreach (var e in now) t.Inside.Add(e);
            }
        }

        static void Run(AreaTrigger t, Action<Character> a, Character c) { try { a(c); } catch (Exception ex) { ModErrors.Report(t.Mod, "trigger", ex); } }
    }

    public static partial class World
    {
        // Exports the player character as the game does at its end (My Games/Underrail/Characters/<name>.char, for
        // the import button of a new game), with the mods' CharacterData beside it. False outside a game.
        public static bool ExportPlayer()
        {
            var game = bpo.GetService<cnk>();
            if (game == null || Player == null) return false;
            game.arv();
            return true;
        }

        // Sends the player to another zone as an exit does (zone id and entry point, as the game's maps name them,
        // e.g. "lu-c1b", "entry"; the ids are in UnderRailWorks-content.txt). False if there's no player.
        public static bool Travel(string zone, string entry)
        {
            var p = Player == null ? null : Player.Entity as c5x;
            if (p == null || string.IsNullOrEmpty(zone) || string.IsNullOrEmpty(entry)) return false;
            a0l.a(p, new c2w(zone, entry), 0, null);
            return true;
        }

        // A character uses a placed object (as clicking it does, without walking there or the game's checks).
        public static bool Use(Character user, string placementId)
        {
            var e = Placed(placementId) == null ? World.PlacedEntity(placementId) : Placed(placementId).Entity;
            var b = e is c5x ? ((c5x)e).GetAspect<bim>() : null;
            return b != null && user != null && b.a((c5x)user.Entity, null, true, true);
        }
    }

    public sealed partial class Placement
    {
        // For objects: using it (the player clicking it) runs this instead of the object's own use.
        public Action<Character> OnUse { get; set; }
    }
}

namespace UnderRailWorks.Internal
{
    static class UseHooks
    {
        // bim.a(user, variant, skipCheck, skipDelay): an object is used.
        static bool Using(bim __instance, c5x __0, ref bool __result)
        {
            try
            {
                var p = World.PlacementOf(__instance.a8());
                if (p == null || p.OnUse == null) return true;
                var who = __0 == null ? null : Character.Of(__0);
                p.OnUse(who);
                __result = true;
                return false;
            }
            catch (Exception ex) { Log.Write("objects: use failed: " + ex.Message); return true; }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var use = typeof(bim).GetMethods(BindingFlags.Instance | BindingFlags.Public).First(m => m.Name == "a" && m.GetParameters().Length == 4 && m.GetParameters()[0].ParameterType == typeof(c5x) && m.ReturnType == typeof(bool));
            harmony.Patch(use, prefix: new HarmonyLib.HarmonyMethod(typeof(UseHooks).GetMethod("Using", BindingFlags.Static | BindingFlags.NonPublic)));
        }
    }
}
