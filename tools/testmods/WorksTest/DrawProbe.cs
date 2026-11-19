// WorksTest: the drawing API under test (tools/smoketest/drawing.sh).
//   wt.drawprobe [frames]: for that many frames (default 120) every drawing event is handled and every member of every
//     handle it gets is read; every 7th plain floor layer is drawn by the probe the game's way (DrawLayer taken over)
//     and every 5th tile's extras also copied into the probe's own picture (RouteTileExtras). Then one line:
//     "drawprobe: ..." with counts and errors.
//   wt.drawthrow: the next DrawLayer handler call throws: Works must stop WorksTest's drawing and the game go on.
//   wt.drawcompose [frames]: the composing drawing parts alongside Wall Vision's keyhole: characters tinted
//     (LayerColour), tiles around the player marked (TileMarks, both styles), every 3rd standing plain sprite drawn by
//     WorksTest's look (DrawLayer, into whatever is set), World.ToScreen/TileBounds checked against the player's
//     ScreenBounds; characters' bodies copied into WorksTest's own picture and their shadows hidden (RouteLayer), also
//     while Wall Vision's keyhole has split the world. Then "drawcompose: ..." with the copies and the lit pixels of
//     that picture. works.drawstats shows whether the looks and marks reached Wall Vision's split picture.
//   wt.drawbadlook: a DrawLayer look that switches the render target: Works must stop it (and WorksTest's drawing).
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UnderRailWorks;

public sealed partial class WorksTest
{
    int probeFrames, probeLeft, probeErrors, probeLayers, probeFlat, probeCharacters, probeOutlined, probeReplaced, probeTiles, probeTilesOwn,
        probeDone, probePasses, probeOutlines, probeHidden, probeShaderLayers, probeDrawable;
    float probeZoom; string probeFirstError; bool throwNext, probeOn;
    readonly HashSet<string> probeKinds = new HashSet<string>(), probeIds = new HashSet<string>();
    readonly HashSet<object> probeEntities = new HashSet<object>();

    void ProbeError(string where, Exception ex) { probeErrors++; if (probeFirstError == null) probeFirstError = where + ": " + ex.GetType().Name + ": " + ex.Message; }

    int composeLeft, composeTinted, composeLooks, composeErrors, composeCopies, composeCopiesInSplit, composeHidden, composeLit; string composeError; bool badLook;
    TileMarks composeFill, composeEdge; RenderTarget2D badTarget;

    void DrawProbe()
    {
        composeFill = new TileMarks(this); composeEdge = new TileMarks(this) { Style = TileMarkStyle.Outline };
        Commands.Add(this, "wt.drawcompose", "wt.drawcompose [frames]: tints, tile marks and a layer look alongside Wall Vision, then log a summary", a =>
        {
            composeLeft = a.Length > 0 ? int.Parse(a[0]) : 120;
            composeTinted = composeLooks = composeErrors = composeCopies = composeCopiesInSplit = composeHidden = composeLit = 0; composeError = null;
            var at = World.Player.Position;
            int px = (int)Math.Floor(at.X), py = (int)Math.Floor(at.Y);
            var ring = new List<Point>(); var edge = new List<Point>();
            for (int y = py - 4; y <= py + 4; y++) for (int x = px - 4; x <= px + 4; x++) { int d = Math.Abs(x - px) + Math.Abs(y - py); if (d >= 2 && d <= 4) ring.Add(new Point(x, y)); else if (d == 5) edge.Add(new Point(x, y)); }
            for (int x = px - 5; x <= px + 5; x++) edge.Add(new Point(x, py + 5));
            composeFill.Replace(ring, new Color(40, 220, 255) * 0.7f);
            composeEdge.Replace(edge, Color.Yellow);
            if (composeFill.Count != ring.Count || !composeFill.Has(ring[0])) composeErrors++;
            Commands.CloseConsole();
            return "composing " + composeLeft + " frames";
        });
        // Added when run (after wt.drawthrow has stopped WorksTest's other drawing handlers).
        Commands.Add(this, "wt.drawbadlook", "wt.drawbadlook: a DrawLayer look that switches the render target (Works must stop it)", a =>
        {
            badLook = true;
            WorldDraw.DrawLayer.Add(this, (layer, context, view) =>
            {
                if (!badLook || !layer.Drawable) return false;
                badLook = false;
                Log.Info("drawbadlook: switching the render target now");
                if (badTarget == null) badTarget = new RenderTarget2D(context.Device, 16, 16);
                context.Device.SetRenderTarget(badTarget);
                layer.Draw(context, Color.White);
                return true;
            });
            Commands.CloseConsole();
            return "bad look on";
        });
        WorldDraw.LayerColour.Add(this, (Layer layer, ref Color colour) =>
        {
            if (composeLeft <= 0) return;
            var c = layer.Entity.Character;
            if (c == null || layer.Id == "SHD" || layer.Id == "marker") return;
            colour = new Color(colour.R, (byte)(colour.G * 0.55f), (byte)(colour.B * 0.55f), colour.A);
            composeTinted++;
        });
        WorldDraw.RouteLayer.Add(this, (layer, route) =>
        {
            if (composeLeft <= 0 || !layer.Drawable || layer.Entity.Character == null) return;
            if (layer.Id == "SHD") { route.Hide(); composeHidden++; return; }
            if (layer.Id == "marker" || layer.ShaderName != null) return;
            route.AlsoInto(WorldDraw.Picture(this, "glow"));
            composeCopies++;
            if (route.Current.IsSplit) composeCopiesInSplit++;
        });
        int standingSeen = 0;
        WorldDraw.DrawLayer.Add(this, (layer, context, view) =>
        {
            if (composeLeft <= 0 || layer.IsFlat || !layer.IsPlainSprite || layer.ShaderName != null || (++standingSeen % 3) != 0) return false;
            layer.Draw(context, layer.Tint ?? Color.White);     // the sprite as the game would, by the mod: into whatever is set
            composeLooks++;
            return true;
        });
        WorldDraw.WorldDone.Add(this, (view, context) =>
        {
            if (composeLeft <= 0) return;
            try
            {
                var p = World.Player; var b = p == null ? null : p.ScreenBounds; var feet = p == null ? null : World.ToScreen(p.Position);
                var at = p.Position; var tile = World.TileBounds(new Point((int)Math.Floor(at.X), (int)Math.Floor(at.Y)));
                if (b != null && feet != null && tile != null)
                {
                    var r = b.Value; r.Inflate(r.Width / 2, r.Height / 2);
                    if (!r.Contains((int)feet.Value.X, (int)feet.Value.Y)) throw new InvalidOperationException("ToScreen " + feet.Value + " far from the player's bounds " + b.Value);
                    if (!tile.Value.Contains((int)feet.Value.X, (int)feet.Value.Y)) throw new InvalidOperationException("ToScreen " + feet.Value + " outside its tile " + tile.Value);
                }
            }
            catch (Exception ex) { composeErrors++; if (composeError == null) composeError = ex.Message; }
            if (--composeLeft > 0) return;
            composeFill.Clear(); composeEdge.Clear();
            try
            {
                var glow = WorldDraw.Picture(this, "glow");
                if (glow.DrawnThisFrame && glow.Target != null)
                {
                    var px = new Color[glow.Target.Width * glow.Target.Height];
                    glow.Target.GetData(px);
                    for (int i = 0; i < px.Length; i += 7) if (px[i].A > 0) composeLit++;
                }
            }
            catch (Exception ex) { composeErrors++; if (composeError == null) composeError = "glow: " + ex.Message; }
            Log.Info("drawcompose: tinted " + composeTinted + ", looks " + composeLooks + ", copies " + composeCopies + " (during a split " + composeCopiesInSplit + "), shadows hidden " + composeHidden
                + ", glow pixels " + composeLit + ", errors " + composeErrors + (composeError == null ? "" : " (first: " + composeError + ")"));
        });
        Commands.Add(this, "wt.drawprobe", "wt.drawprobe [frames]: exercise the drawing API for that many frames, then log a summary", a =>
        {
            probeFrames = probeLeft = a.Length > 0 ? int.Parse(a[0]) : 120;
            probeErrors = probeLayers = probeFlat = probeCharacters = probeOutlined = probeReplaced = probeTiles = probeTilesOwn = probeDone = probePasses = probeOutlines = probeHidden = probeShaderLayers = probeDrawable = 0;
            probeFirstError = null; probeKinds.Clear(); probeIds.Clear(); probeEntities.Clear();
            probeOn = true;
            Commands.CloseConsole();
            return "probing " + probeFrames + " frames";
        });
        Commands.Add(this, "wt.crowd", "wt.crowd <n> <blueprint> [radius]: n neutral characters on free tiles around the player (some behind scenery)", a =>
        {
            int n = int.Parse(a[0]), r = a.Length > 2 ? int.Parse(a[2]) : 8, made = 0;
            var at = World.Player.Position;
            var tiles = World.FreeTilesNear((int)at.X, (int)at.Y, r).Where(t => Math.Abs(t.X - (int)at.X) + Math.Abs(t.Y - (int)at.Y) > 2).ToList();
            var rnd = new Random(7);
            foreach (var t in tiles.OrderBy(x => rnd.Next()).Take(n)) { var c = World.Spawn(a[1], t.X, t.Y, SpawnSide.Neutral); if (c != null) made++; }
            Log.Info("crowd: " + made + " of " + n + " from " + tiles.Count + " free tiles");
            return "crowd " + made;
        });
        Commands.Add(this, "wt.drawthrow", "wt.drawthrow: WorksTest's next DrawLayer handler throws", a => { throwNext = true; Commands.CloseConsole(); return "throwing next"; });

        WorldDraw.LayerStarting.Add(this, (view, context, layer, shaderPass) =>
        {
            if (!probeOn) return;
            try
            {
                probeLayers++;
                if (view.IsNull || context.IsNull || layer.IsNull) throw new InvalidOperationException("a null handle");
                var b = layer.Bounds; var s = layer.Source; var t = layer.Tint; var r = layer.Rotation; var f = layer.Flip;
                var px = Interface.ToPixels(b);
                if (b.Width < 0 || b.Height < 0 || px.Width < 0 || float.IsNaN(r)) throw new InvalidOperationException("odd bounds " + b + " / " + px);
                if (layer.Drawable) probeDrawable++;
                if (layer.IsFlat) probeFlat++;
                var id = layer.Id; if (id != null) probeIds.Add(id);
                probeKinds.Add(layer.SpriteKind);
                var plain = layer.IsPlainSprite; var shader = layer.ShaderName; var param = layer.ShaderParameter("OutlineColor");
                var owner = layer.Owner;
                if (!owner.IsNull)
                {
                    if (owner.HasOutline) probeOutlined++;
                    var e = owner.Entity;
                    if (!e.IsNull && probeEntities.Add(e.Key))
                    {
                        var at = e.MapPosition; var flat = e.IsFlat;
                        var c = e.Character;
                        if (c != null) { probeCharacters++; var n = c.Name; }
                    }
                }
                var le = layer.Entity; if (!le.IsNull) { var p2 = le.MapPosition; }
                if (layer.Equals(layer) == false || layer != layer || layer.GetHashCode() != layer.GetHashCode()) throw new InvalidOperationException("equality");
            }
            catch (Exception ex) { ProbeError("layer", ex); }
        });
        int floorSeen = 0, tileSeen = 0;
        WorldDraw.DrawLayer.Add(this, (layer, context, view) =>
        {
            if (throwNext) { throwNext = false; Log.Info("drawthrow: throwing now"); throw new InvalidOperationException("WorksTest: a drawing handler failing on purpose"); }
            if (!probeOn || !layer.IsFlat || !layer.IsPlainSprite || (++floorSeen % 7) != 0) return false;
            layer.DrawAsGame(context, view);     // the game's own drawing, from a handler: the picture must stay the same
            probeReplaced++;
            return true;
        });
        WorldDraw.RouteTileExtras.Add(this, (tile, route) =>
        {
            if (!probeOn) return;
            probeTiles++;
            if (tile.IsNull || !route.Layer.IsNull) throw new InvalidOperationException("tile route: tile " + tile.IsNull + ", layer " + route.Layer.IsNull);
            if ((++tileSeen % 5) != 0) return;
            route.AlsoInto(WorldDraw.Picture(this, "tiles"));
            probeTilesOwn++;
        });
        WorldDraw.ShaderPassStarting.Add(this, view =>
        {
            if (!probeOn) return;
            try
            {
                probePasses++;
                foreach (var kv in Outlines.All(view)) { probeOutlines++; if (kv.Value) probeHidden++; var o = kv.Key; if (!o.IsNull) { var ob = o.Bounds; if (!Outlines.IsHidden(o) != !kv.Value) throw new InvalidOperationException("IsHidden disagrees with All"); } }
                foreach (var kv in view.ShaderLayers) probeShaderLayers += kv.Value.Count;
            }
            catch (Exception ex) { ProbeError("shader pass", ex); }
        });
        WorldDraw.ShaderPassDone.Add(this, (view, context) =>
        {
            if (!probeOn) return;
            try { var d = context.Device; if (d == null) throw new InvalidOperationException("no device"); var batch = context.Batch; }
            catch (Exception ex) { ProbeError("shader pass done", ex); }
        });
        WorldDraw.WorldDone.Add(this, (view, context) =>
        {
            if (!probeOn) return;
            try
            {
                probeDone++;
                probeZoom = view.Zoom;
                var clip = view.Clip;
                var shader = LayerShader.Find("Outline");
                if (shader.IsNull) throw new InvalidOperationException("no Outline shader");
                if (!LayerShader.Find("no such shader").IsNull) throw new InvalidOperationException("a shader that doesn't exist was found");
                if (Interface.PixelsPerUnit <= 0) throw new InvalidOperationException("scale " + Interface.PixelsPerUnit);
                if (Interface.CapturingSaveScreenshot(context.Device)) Log.Info("drawprobe: capturing a save screenshot this frame");
            }
            catch (Exception ex) { ProbeError("world done", ex); }
            if (--probeLeft > 0) return;
            probeOn = false;
            Log.Info("drawprobe: frames " + probeDone + "/" + probeFrames + ", layers " + probeLayers + " (drawable " + probeDrawable + ", flat " + probeFlat + "), entities " + probeEntities.Count
                + ", characters " + probeCharacters + ", outlined sets " + probeOutlined + ", replaced " + probeReplaced + ", tiles " + probeTiles + " (own " + probeTilesOwn + ")"
                + ", shader passes " + probePasses + ", outlines " + probeOutlines + " (hidden " + probeHidden + "), shader layers " + probeShaderLayers + ", zoom " + probeZoom
                + ", scale " + Interface.PixelsPerUnit + ", kinds " + string.Join("/", probeKinds.OrderBy(k => k).ToArray()) + ", ids " + string.Join("/", probeIds.OrderBy(k => k).ToArray())
                + ", errors " + probeErrors + (probeFirstError == null ? "" : " (first: " + probeFirstError + ")"));
        });
    }
}
