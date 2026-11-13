// UnderRailWorks: a live view of the world in a mod's window or panel: the area the player is in, drawn by the game's
// own world view (mv), centred on a map position, as the game's own remote view popup (aqu: security cameras) makes
// one: its own camera bounded to the area, no zoom wheel, the player's sight and the shroud as in the main view.
// Moving its window keeps it in place inside (Ui.Move re-lays out world views, UI.md).
//   var w = Ui.Window(this, "cam", "Front door", 420, 300);
//   var view = Ui.WorldView(400, 260, new Vector2(30, 12));
//   Ui.Add(w, view, 10, w.Top + 4);
//   view.Center = World.Player.Position2D;     // look somewhere else
// Each view draws the area again every frame: keep them small and few.
using System;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks.Internal
{
    // Clips its children to its own rectangle. (euz.UseScissorRect can't be overridden from here: the game calls its
    // getter by an obfuscated name.) A world view reads the device's clip rectangle when its pass starts (a7m.vo) and
    // keeps it, so setting it around the draw is enough; it goes through Works' mapping (interface units -> pixels).
    sealed class UiClipControl : euz
    {
        bool failed;
        public override void iv(e6d time, cp9 context)
        {
            if (failed) return;
            var g = context.c;
            var saved = UiScaleFix.GetScissor(g);
            try
            {
                context.a();     // end the open batch: the clip applies from the next one
                UiScaleFix.SetScissor(g, Rectangle.Intersect(saved, Ui.Bounds(this)));
                context.a(Clipping);     // a batch open again (the world view's pass starts by ending it)
                base.iv(time, context);
            }
            catch (Exception ex) { failed = true; Log.Write("ui: a world view failed to draw (switched off): " + ex); }
            finally
            {
                // As a7m.vo ends: the plain batch open again, clipping only when the saved clip was a real one (X or Y
                // not 0: the game's own test for "clipped").
                context.a(); UiScaleFix.SetScissor(g, saved);
                context.a(saved.X != 0 || saved.Y != 0 ? Clipping : null);
            }
        }
        static readonly Microsoft.Xna.Framework.Graphics.RasterizerState Clipping = new Microsoft.Xna.Framework.Graphics.RasterizerState { ScissorTestEnable = true, CullMode = Microsoft.Xna.Framework.Graphics.CullMode.None };
    }
}

namespace UnderRailWorks
{
    public sealed class UiWorldView : UiElement
    {
        readonly mv view;
        // The view sits in a holder that clips its children (the game's controls clip only when UseScissorRect says so; a
        // world view draws as far as the screen otherwise).
        internal UiWorldView(mv view) : base(new UiClipControl { Size = view.Size }) { this.view = view; Control.aox(view); }
        mv View { get { return view; } }

        // The map position it is centred on (tiles). Setting it looks there.
        public Vector2 Center
        {
            get { var p = View.an9().t1(); return new Vector2(p.X, p.Y); }
            set { View.an9().t2(new Vector3(value.X, value.Y, 0f)); }
        }
    }

    public static partial class Ui
    {
        // A view of the player's area, width x height, centred on centre (null: the player). Null without a game.
        public static UiWorldView WorldView(int width, int height, Vector2? centre = null)
        {
            var game = bpo.GetService<cnk>(); var scene = bpo.GetService<li>();
            var player = World.Player == null ? null : World.Player.Entity as c5x;
            if (game == null || scene == null || game.aqu() == null || player == null || player.Area == null) return null;
            var area = player.Area;
            var v = new mv(scene.ka());
            v.Size = new esj(width, height);
            v.an0(area);
            v.aoa(new bw7());
            v.an9().t9(true);
            v.an9().t5(new Vector2(0f, 0f));
            v.an9().t7(new Vector2(UnderrailDropdownScroll.W(area.Size), UnderrailDropdownScroll.H(area.Size)));
            var at = centre ?? new Vector2(player.Location.X, player.Location.Y);
            v.an9().t2(new Vector3(at.X, at.Y, 0f));
            // As aqu sets them (these dnn fields share their names with methods: by reflection): input options off, the
            // player's sight, the cursor mode, no zoom wheel.
            var input = Field(typeof(dnn), "d").GetValue(v);
            Call(input, "a", false); Call(input, "c", false);
            Field(typeof(dnn), "c").SetValue(v, game.aqu().v().a());
            var cursor = Field(typeof(dnn), "h").GetValue(v); Field(cursor.GetType(), "a").SetValue(cursor, edg.b);
            var zoom = Field(typeof(dnn), "e").GetValue(v); Field(zoom.GetType(), "a").SetValue(zoom, false);
            return new UiWorldView(v);
        }

        static System.Reflection.FieldInfo Field(Type t, string name)
        {
            return t.GetField(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly);
        }
        static void Call(object o, string name, bool arg)
        {
            var m = o.GetType().GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, new[] { typeof(bool) }, null);
            if (m != null) m.Invoke(o, new object[] { arg });
        }
    }
}
