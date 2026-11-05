// UnderRailWorks: map markers from mods (quest targets, notes, caches): a place in a zone with a label and a colour,
// kept per save in the mod's save data (never in the game's files). Map mods (the Minimap) draw Markers.Here.
//   Markers.Add(this, "cache", "LU-SouthGateStation-N", 12, 30, "Hidden cache", Color.Gold);
//   Markers.Remove(this, "cache");
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class MapMarker
    {
        public string Mod { get; internal set; }
        public string Id { get; internal set; }
        public string Zone { get; internal set; }
        public string Area { get; internal set; }      // null: the zone's area the player is in
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public string Label { get; internal set; }
        public Color Colour { get; internal set; }
    }

    public static class Markers
    {
        const string Key = "works_markers";
        // Adds (or moves) the mod's marker id. colour: null = a light blue.
        public static void Add(WorksMod mod, string id, string zone, int x, int y, string label, Color? colour = null, string area = null)
        {
            var list = Raw(mod);
            list.RemoveAll(o => Field(o, "id") == id);
            var c = colour ?? new Color(110, 190, 255);
            list.Add(new Dictionary<string, object> { { "id", id }, { "zone", zone }, { "area", area }, { "x", x }, { "y", y }, { "label", label }, { "colour", c.R + "," + c.G + "," + c.B } });
            SaveData.Of(mod).Set(Key, list.Cast<object>().ToList());
            Raise();
        }
        public static void Remove(WorksMod mod, string id)
        {
            var list = Raw(mod);
            if (list.RemoveAll(o => Field(o, "id") == id) == 0) return;
            SaveData.Of(mod).Set(Key, list.Cast<object>().ToList());
            Raise();
        }
        public static IList<MapMarker> Of(WorksMod mod) { return Raw(mod).Select(o => Read(mod.Id, o)).Where(m => m != null).ToList(); }
        // Every loaded mod's markers in this game.
        public static IList<MapMarker> All
        {
            get { return ModLoader.Mods.Where(m => m.Loaded && m.Instance != null).SelectMany(m => Of(m.Instance)).ToList(); }
        }
        // The markers in the current zone (and the player's area, for markers that name one).
        public static IList<MapMarker> Here
        {
            get
            {
                var zone = World.ZoneId; var p = World.Player; var area = p == null || p.Area == null ? null : p.Area.Name;
                return All.Where(m => string.Equals(m.Zone, zone, StringComparison.OrdinalIgnoreCase) && (m.Area == null || string.Equals(m.Area, area, StringComparison.OrdinalIgnoreCase))).ToList();
            }
        }
        // After a mod added or removed one (and after a load, when the game's markers are new).
        public static readonly Hook<Action> Changed = new Hook<Action>("MarkersChanged");
        internal static void Raise() { if (Changed.Any) Changed.Run(h => h()); }

        static List<Dictionary<string, object>> Raw(WorksMod mod)
        {
            // As stored this session (a list of dictionaries) or as read back from the save's JSON (List<object>).
            var v = SaveData.Of(mod).Get<object>(Key, null) as System.Collections.IEnumerable;
            return v == null ? new List<Dictionary<string, object>>() : v.OfType<Dictionary<string, object>>().ToList();
        }
        static string Field(Dictionary<string, object> o, string k) { object v; return o.TryGetValue(k, out v) && v != null ? Convert.ToString(v, System.Globalization.CultureInfo.InvariantCulture) : null; }
        static MapMarker Read(string mod, Dictionary<string, object> o)
        {
            try
            {
                var rgb = (Field(o, "colour") ?? "110,190,255").Split(',').Select(int.Parse).ToArray();
                return new MapMarker { Mod = mod, Id = Field(o, "id"), Zone = Field(o, "zone"), Area = Field(o, "area"), X = Convert.ToInt32(o["x"]), Y = Convert.ToInt32(o["y"]),
                    Label = Field(o, "label"), Colour = new Color(rgb[0], rgb[1], rgb[2]) };
            }
            catch { return null; }
        }
    }
}
