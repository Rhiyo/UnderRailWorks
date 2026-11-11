// UnderRailWorks: marks on the world map (the game's global map window): a zone outlined in a colour, as the game
// outlines the player's own (where other co-op players are, a quest's destination...).
//   WorldMap.Mark(this, "buddy", "cc_ph", Color.CornflowerBlue);   WorldMap.Unmark(this, "buddy");
// The game's side: eg0.a(e5q, d69) draws each map node; a node whose locale ids (dy9.b() / f()) contain the current
// zone (d69.e) gets a copy of its layer with the "MapOutline" shader. Works adds one per mark, with the shader's
// OutlineColor parameter (a2f.v).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static class WorldMap
    {
        internal sealed class MarkInfo { public string Mod, Key, Zone; public Color Colour; }
        internal static readonly List<MarkInfo> Marks = new List<MarkInfo>();
        public static void Mark(WorksMod mod, string key, string zoneId, Color colour)
        {
            lock (Marks) { Marks.RemoveAll(m => m.Mod == mod.Id && m.Key == key); if (!string.IsNullOrEmpty(zoneId)) Marks.Add(new MarkInfo { Mod = mod.Id, Key = key, Zone = zoneId, Colour = colour }); }
        }
        public static void Unmark(WorksMod mod, string key) { lock (Marks) Marks.RemoveAll(m => m.Mod == mod.Id && m.Key == key); }
        internal static void Forget(string mod) { lock (Marks) Marks.RemoveAll(m => m.Mod == mod); }
    }
}

namespace UnderRailWorks.Internal
{
    static class WorldMapHooks
    {
        static readonly FieldInfo Layers = typeof(bym).GetField("a", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);     // bym.a: also a method name
        static readonly HashSet<string> logged = new HashSet<string>();
        static void Node(e5q __0, d69 __1)
        {
            List<WorldMap.MarkInfo> marks;
            lock (WorldMap.Marks) { if (WorldMap.Marks.Count == 0) return; marks = WorldMap.Marks.ToList(); }
            try
            {
                var node = (bym)__0;
                var layers = Layers == null ? null : Layers.GetValue(node) as System.Collections.IEnumerable;
                var layer = layers == null ? null : layers.OfType<a2f>().FirstOrDefault();
                var places = node.b == null ? null : node.b.GetAspect<dy9>();
                if (layer == null || places == null) return;
                foreach (var m in marks)
                {
                    if (!places.b().Concat(places.f()).Any(z => string.Equals(z, m.Zone, StringComparison.OrdinalIgnoreCase))) continue;     // the map's ids are in their own case ("CC-RES")
                    var outline = layer.f5();
                    outline.u = "Outline";     // the creature outline shader: it takes OutlineColor (MapOutline is always the game's orange)
                    outline.v = new Dictionary<string, object> { { "OutlineColor", m.Colour } };
                    __0.a(outline);
                    if (logged.Add(m.Zone)) Log.Write("world map: marked " + m.Zone + " (" + m.Key + ")");
                }
            }
            catch (Exception ex) { Log.Write("world map: " + ex.Message); }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var draw = typeof(eg0).GetMethod("a", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(e5q), typeof(d69) }, null);
            if (draw == null) { Log.Write("world map: node drawing not found"); return; }
            harmony.Patch(draw, postfix: new HarmonyLib.HarmonyMethod(typeof(WorldMapHooks).GetMethod("Node", BindingFlags.Static | BindingFlags.NonPublic)));
        }
    }
}
