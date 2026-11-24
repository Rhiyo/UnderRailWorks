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

        // The view on a character: centred on it, at its height (a floor above or below draws elsewhere on screen).
        public static void CenterOn(Character c)
        {
            var v = View; var cam = v == null ? null : v.an9();
            if (cam == null || c == null) return;
            FitToArea();
            var e = c.Entity as b0b;
            cam.t2(e != null ? e.aj() : new Vector3(c.Position.X, c.Position.Y, c.Position.Z));     // the entity's own place (its height included)
        }
        // A world view made after its zone loaded (a HUD built again: World.MakePlayer) starts with the camera kept
        // inside (0,0)-(1,1) (ci3 builds it so; the zone's load widens it): widened to its area here.
        internal static string BoxText { get { var v = View; var c = v == null ? null : v.an9(); var a = v == null ? null : v.anz(); return c == null ? "?" : c.t4().X + "," + c.t4().Y + "-" + c.t6().X + "," + c.t6().Y + " area " + (a == null ? "none" : UnderrailDropdownScroll.W(a.Size) + "x" + UnderrailDropdownScroll.H(a.Size)); } }
        static int fitFrames;
        internal static void FitSoon() { fitFrames = 30; }     // the next frames, once the new view has its area
        internal static void Tick() { if (fitFrames > 0) { fitFrames--; FitToArea(); } }
        internal static void FitToArea()
        {
            var v = View; var cam = v == null ? null : v.an9();
            var area = v == null ? null : v.anz();
            if (cam == null || area == null) return;
            var max = cam.t6();
            int w = UnderrailDropdownScroll.W(area.Size), h = UnderrailDropdownScroll.H(area.Size);
            if (max.X >= w && max.Y >= h) return;
            cam.t5(new Vector2(0f, 0f));
            cam.t7(new Vector2(w, h));
            Log.Write("camera: kept inside the area again (" + w + "x" + h + ")");
        }
        // The camera's height (the floor level it looks at).
        public static float Height { get { var v = View; var c = v == null ? null : v.an9(); return c == null ? 0 : c.t1().Z; } }

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
