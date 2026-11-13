// WorksTest: the drawing API under test (tools/smoketest/drawing.sh).
//   wt.drawprobe [frames]: for that many frames (default 120) every drawing event is handled and every member of every
//     handle it gets is read; every 7th plain floor layer and every 5th tile's extras are drawn by the probe the game's
//     way (DrawLayer / DrawTileExtras taken over). Then one line: "drawprobe: ..." with counts and errors.
//   wt.drawthrow: the next DrawLayer handler call throws: Works must stop WorksTest's drawing and the game go on.
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

    void DrawProbe()
    {
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
        WorldDraw.DrawTileExtras.Add(this, (view, context, tile) =>
        {
            if (!probeOn) return false;
            probeTiles++;
            if ((++tileSeen % 5) != 0) return false;
            WorldDraw.TileExtrasStep(view, context, tile);
            probeTilesOwn++;
            return true;
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
