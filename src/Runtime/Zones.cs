// UnderRailWorks: new zones (maps) from mods. A zone is the game's own files: <id>.uz (the map) and <id>_L1.uzl (its
// first level, the live state), made with a map editor; a mod ships them in its folder under data\Maps\ and adds the
// zone here. Players reach it through World.Travel (an exit object's OnUse, a dialog, a trigger), entering at one
// of the zone's waypoints, as the game's own exits do.
//   Zones.Add(this, "mymod_cave", "Forgotten Cave", likeZone: "lu-c1b");     // Mods\MyMod\data\Maps\mymod_cave.uz
//   World.Travel("mymod_cave", "Entrance");
// How the game loads a zone (d6f.arp): the id is looked up in the world index (SouthUnderrail.world -> dse, a list of
// bfc: id, name, music, level...); on the first visit its _L1.uzl is copied into the save's store (d6f.a(bfc):
// bmg.a(key, path)); then bl1.a(id) reads <id>.uz from the game's data folder (c1a.a(path)) and the level from the
// store. Works adds the zone to the index, seeds the first visit from the mod's file, and serves the .uz from the mod's
// folder when the game's data has none.
// Saves: the zone's level ends up in the save like any visited zone. A save made inside a mod zone needs the mod (the
// stock game can't find the map); one made after leaving it is fine. See UnderRailWorks.md, "New zones".
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static class Zones
    {
        sealed class Entry { public WorksMod Mod; public string Id, Name, Like, Folder; }
        static readonly Dictionary<string, Entry> added = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

        // A zone from the mod's data\Maps folder (<id>.uz and <id>_L1.uzl). likeZone: a game zone whose music, locale
        // type and level it takes (null: none). Prefix the id with the mod's id.
        public static void Add(WorksMod mod, string id, string name, string likeZone = null)
        {
            if (mod == null || string.IsNullOrEmpty(id)) throw new ArgumentException("a zone needs a mod and an id");
            var folder = Path.Combine(mod.Info.Folder, "data", "Maps");
            if (!File.Exists(Path.Combine(folder, id + ".uz"))) Log.Write("zones: " + mod.Id + ": no " + id + ".uz in " + folder);
            added[id] = new Entry { Mod = mod, Id = id, Name = name ?? id, Like = likeZone, Folder = folder };
            ZoneHooks.Indexed = null;     // add it to the index again
        }

        // An exit: a usable object placed in a zone (any zone, the game's too; re-added each time it loads, never saved)
        // that takes the player to a waypoint of another zone (a mod's or the game's) when used. name: its hover label.
        //   Zones.AddExit(this, "cave_in", "cc-res", "cc-res", 30, 12, "mymod_cave", "Entrance", "Cave entrance");
        public static Placement AddExit(WorksMod mod, string id, string zone, string area, int x, int y, string toZone, string toWaypoint,
            string name = null, string blueprint = "objects\\largemachines\\tchortparts\\tchortholes")
        {
            if (mod == null || id == null || zone == null || toZone == null) throw new ArgumentException("an exit needs a mod, an id, its zone and where it goes");
            var p = new Placement { Id = mod.Id + ".exit." + id, Zone = zone, Area = area ?? zone, X = x, Y = y, Blueprint = blueprint, Name = name,
                OnUse = who => { if (who != null && who.IsPlayer) World.Travel(toZone, toWaypoint); } };
            World.Place(p);
            return p;
        }

        // An exit at a map's edge, as the game's own area transitions: walking onto the tiles takes the player to a waypoint
        // of another zone. Shown on map mods (EdgeExits). Not on arrival (or when it's made under the player): someone
        // already standing on its tiles goes only after stepping off and back on.
        //   Zones.AddEdgeExit(this, "cave_mouth", "cc-res", "cc-res", new Rectangle(0, 30, 1, 4), "mymod_cave", "Entrance");
        public static AreaTrigger AddEdgeExit(WorksMod mod, string id, string zone, string area, Rectangle tiles, string toZone, string toWaypoint)
        {
            if (mod == null || id == null || zone == null || toZone == null) throw new ArgumentException("an exit needs a mod, an id, its zone and where it goes");
            lock (edge) { edge.RemoveAll(e => e.Id == mod.Id + "." + id); edge.Add(new EdgeExit { Id = mod.Id + "." + id, Zone = zone, Area = area, Tiles = tiles, ToZone = toZone }); }
            var made = DateTime.UtcNow;     // someone standing on it when it's made doesn't go either
            return Triggers.Add(mod, zone, area, tiles, who =>
            {
                if (who == null || !who.IsPlayer || (DateTime.UtcNow - World.ZoneLoadedAt).TotalSeconds < 1.0 || (DateTime.UtcNow - made).TotalSeconds < 1.0) return;
                World.Travel(toZone, toWaypoint);
            }, null);
        }
        public sealed class EdgeExit { public string Id { get; internal set; } public string Zone { get; internal set; } public string Area { get; internal set; } public Rectangle Tiles { get; internal set; } public string ToZone { get; internal set; } }
        static readonly List<EdgeExit> edge = new List<EdgeExit>();
        // Mods' edge exits in the current zone (a map draws them like the game's exits).
        public static IList<EdgeExit> EdgeExits { get { var z = World.ZoneId; lock (edge) return edge.Where(e => string.Equals(e.Zone, z, StringComparison.OrdinalIgnoreCase)).ToList(); } }

        // A zone a mod added (true), so not one of the game's.
        public static bool IsModZone(string id) { return id != null && added.ContainsKey(id); }
        public static IList<string> Added { get { return added.Keys.ToList(); } }

        internal static bool TryFile(string id, string fileName, out string path)
        {
            path = null; Entry e;
            if (id == null || !added.TryGetValue(id, out e)) return false;
            path = Path.Combine(e.Folder, fileName);
            return File.Exists(path);
        }

        // The index: each added zone as a bfc, its details copied from likeZone's.
        internal static void Index(dse index)
        {
            var list = index.a();
            foreach (var e in added.Values)
            {
                if (list.Any(b => string.Equals(b.Id, e.Id, StringComparison.OrdinalIgnoreCase))) continue;
                var zone = new bfc(e.Id, e.Name);
                var like = e.Like == null ? null : list.FirstOrDefault(b => string.Equals(b.Id, e.Like, StringComparison.OrdinalIgnoreCase));
                if (like != null) { zone.LocaleType = like.LocaleType; zone.Music = like.Music; zone.BaseLevel = like.BaseLevel; }
                list.Add(zone);
                Log.Write("zones: " + e.Mod.Id + " adds zone " + e.Id + (like != null ? " (like " + like.Id + ")" : ""));
            }
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class ZoneHooks
    {
        internal static dse Indexed;     // the index the added zones are in (it's built once per game start)

        // d6f.aq0(): the world index, built on first use.
        static void IndexReady(dse __result)
        {
            if (__result == null || ReferenceEquals(Indexed, __result)) return;
            try { Zones.Index(__result); Indexed = __result; } catch (Exception ex) { Log.Write("zones: " + ex.Message); }
        }

        // c1a.a(path): reads a zone's .uz; for an added zone with no file in the game's data, from the mod's folder.
        static void ZoneFile(ref string __0)
        {
            try
            {
                if (string.IsNullOrEmpty(__0) || File.Exists(__0)) return;
                string modPath;
                if (Zones.TryFile(Path.GetFileNameWithoutExtension(__0), Path.GetFileName(__0), out modPath)) __0 = modPath;
            }
            catch (Exception ex) { Log.Write("zones: " + ex.Message); }
        }

        // d6f.a(bfc): the first visit copies the zone's _L1.uzl into the save's store; an added zone's from the mod.
        static readonly FieldInfo LooseFiles = typeof(bmg).GetField("a", BindingFlags.Static | BindingFlags.Public);     // bmg.a: also a method name
        static bool FirstVisit(bfc __0, ref bool __result)
        {
            try
            {
                string level;
                if (__0 == null || !Zones.TryFile(__0.Id, __0.Id + "_L1.uzl", out level)) return true;
                string name = __0.Id + "_L1.uzl";
                if (LooseFiles != null && (bool)LooseFiles.GetValue(null))
                {
                    string loose = Path.Combine(d5x.m(), name);
                    __result = !File.Exists(loose);
                    if (__result) File.Copy(level, loose);
                }
                else
                {
                    string key = Path.Combine(d5x.x, name).ToLowerInvariant();
                    __result = !bmg.b(key);
                    if (__result) bmg.a(key, level);
                }
                return false;
            }
            catch (Exception ex) { Log.Write("zones: first visit: " + ex.Message); return true; }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(ZoneHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            var game = typeof(cnk).Assembly.GetType("d6f", true);     // the game service (internal)
            harmony.Patch(game.GetMethod("aq0", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null), postfix: m("IndexReady"));
            harmony.Patch(typeof(c1a).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null), prefix: m("ZoneFile"));
            harmony.Patch(game.GetMethod("a", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(bfc) }, null), prefix: m("FirstVisit"));
        }
    }
}
