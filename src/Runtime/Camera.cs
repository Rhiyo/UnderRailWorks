// UnderRailWorks: the world view's camera. Its centre is a map position (tiles: the game points it at an entity's map
// location); the world is drawn isometrically around it: a map step in X moves (TileWidth, TileHeight) interface units
// on screen, a step in Y (-TileWidth, TileHeight) (a7m: screen x = (X - Y) * tile half-width * zoom, y = (X + Y) * tile
// half-height * zoom). Moving the centre moves the view, as the game's right-drag scrolling does.
//   var c = Camera.Center;                      // what the screen is looking at (map tiles)
//   Camera.Center = new Vector2(30, 12);        // look there
using System;
using System.Reflection;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static partial class Camera
    {
        static readonly FieldInfo ViewField = typeof(ci3).GetField("m_t", BindingFlags.Instance | BindingFlags.NonPublic) ?? typeof(ci3).GetField("t", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo ZoomField = typeof(dnn).GetField("y", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic);       // dnn.y: also a method name
        static readonly FieldInfo TileField = typeof(a7m).GetField("as", BindingFlags.Instance | BindingFlags.NonPublic) ?? typeof(a7m).GetField("@as", BindingFlags.Instance | BindingFlags.NonPublic);

        static dnn View { get { return Hud.Current == null || ViewField == null ? null : ViewField.GetValue(Hud.Current) as dnn; } }

        // The map position the view is centred on (tiles), or null (no world view). Setting it moves the view there.
        public static Vector2? Center
        {
            get { var v = View; var c = v == null ? null : v.an9(); if (c == null) return null; var p = c.t1(); return new Vector2(p.X, p.Y); }
            set { var v = View; var c = v == null ? null : v.an9(); if (c == null || value == null) return; var p = c.t1(); c.t2(new Vector3(value.Value.X, value.Value.Y, p.Z)); }
        }

        // The zoom (1 = 100%).
        public static float Zoom { get { var v = View; return v == null || ZoomField == null ? 1f : Convert.ToSingle(ZoomField.GetValue(v)); } }

        // Interface units on screen for half a tile, across and down, at the current zoom (0 without a world view).
        public static float TileWidth { get { var t = Tile(); return t.X * Zoom; } }
        public static float TileHeight { get { var t = Tile(); return t.Y * Zoom; } }

        // The world view's size (interface units: the screen's world area).
        public static Point ViewSize { get { var v = View; return v == null ? Point.Zero : new Point(UnderrailDropdownScroll.W(v.Size), UnderrailDropdownScroll.H(v.Size)); } }

        static Vector2 Tile()
        {
            var v = View as a7m;
            if (v == null || TileField == null) return Vector2.Zero;
            var t = TileField.GetValue(v);
            return t is esj ? new Vector2(UnderrailDropdownScroll.W((esj)t), UnderrailDropdownScroll.H((esj)t)) : Vector2.Zero;
        }
    }
}
