// UnderRailWorks: the world-drawing points the patched exe calls.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using UnderRailWorks;

namespace UnderRailWorks.Internal
{

// The points the patched exe calls (names fixed by the patcher, src/Installer/Installer.cs): they run WorldDraw's
// events and Outlines, then the game's own step unless a handler did it.
public static class WorldDrawHooks
{
    public static bool Reverse(bool down) { return Outlines.Reverse(down); }
    public static bool Interactive(bool takesInput) { return Outlines.Interactive(takesInput); }
    public static void Add(bym layers, a2f outline) { Outlines.Added(layers, outline); layers.a(outline); }

    // LayerColour's change to the layer drawn now, put back once it's drawn (scenery layers are kept between frames).
    static a2f tinted; static Color? tintWas;
    static void Untint() { if (tinted != null) { var l = tinted; tinted = null; Layers.SetTint(l, tintWas); } }

    public static void Drawn(a7m view, cp9 context, a2f layer, bool shaderPass)
    {
        Untint();
        if (!shaderPass) { World.LayerDrawn(layer); if (WorldDraw.LayerColour.Any && layer.d != null) Colour(layer); }
        var hook = WorldDraw.LayerStarting;
        if (!hook.Any) return;
        var v = new WorldView(view); var c = new DrawContext(context); var l = new Layer(layer);
        var list = hook.Snapshot;     // every world layer: no closure per call
        for (int i = 0; i < list.Length; i++) { long t = hook.Starting(); try { list[i](v, c, l, shaderPass); } catch (Exception ex) { hook.Failed(list, i, ex); } finally { hook.Done(i, t); } }
    }

    static void Colour(a2f layer)
    {
        var hook = WorldDraw.LayerColour;
        var was = Layers.Tint(layer);
        var start = was ?? Color.White; var colour = start;
        var l = new Layer(layer);
        var list = hook.Snapshot;
        for (int i = 0; i < list.Length; i++)
        {
            long t = hook.Starting();
            var before = colour;
            try { list[i](l, ref colour); } catch (Exception ex) { colour = before; hook.Failed(list, i, ex); } finally { hook.Done(i, t); }
        }
        if (colour == start) return;
        tinted = layer; tintWas = was;
        Layers.SetTint(layer, colour);
        WorldDraw.Coloured++;
    }

    // a7m.a's a2f.f3 call: a router (RouteLayer) may take the layer; else its look.
    [ThreadStatic] static int routing;
    public static void DrawLayer(a2f layer, cp9 context, bkx view)
    {
        try
        {
            var hook = WorldDraw.RouteLayer;
            if (hook.Any)
            {
                var l = new Layer(layer); var c = new DrawContext(context); var v = new LayerView(view);
                var list = hook.Snapshot;
                for (int i = list.Length - 1; i >= 0; i--)
                {
                    long t = hook.Starting();
                    routing++;
                    try { if (list[i](l, c, v)) { WorldDraw.Routed++; Count(WorldDraw.RoutedBy, hook.OwnerAt(list, i)); return; } }
                    catch (Exception ex) { hook.Failed(list, i, ex); }
                    finally { routing--; hook.Done(i, t); }
                }
            }
            Look(layer, context, view);
        }
        finally { Untint(); }
    }
    static void Count(Dictionary<string, long> d, string mod) { if (mod == null) return; long n; d.TryGetValue(mod, out n); d[mod] = n + 1; }

    // How a layer looks: a mod's DrawLayer, else the game's a2f.f3. A look that changed the render target is stopped.
    static readonly Microsoft.Xna.Framework.Graphics.RenderTargetBinding[] targetsBefore = new Microsoft.Xna.Framework.Graphics.RenderTargetBinding[4], targetsAfter = new Microsoft.Xna.Framework.Graphics.RenderTargetBinding[4];
    internal static void Look(a2f layer, cp9 context, bkx view)
    {
        var hook = WorldDraw.DrawLayer;
        if (hook.Any)
        {
            var l = new Layer(layer); var c = new DrawContext(context); var v = new LayerView(view);
            var device = DrawContexts.Device(context);
            var list = hook.Snapshot;
            for (int i = list.Length - 1; i >= 0; i--)
            {
                long t = hook.Starting();
                int before = device.GetRenderTargetsNoAllocEXT(targetsBefore);
                try
                {
                    if (!list[i](l, c, v)) continue;
                    int after = device.GetRenderTargetsNoAllocEXT(targetsAfter);
                    bool same = after == before;
                    for (int k = 0; same && k < before && k < targetsBefore.Length; k++) same = targetsBefore[k].RenderTarget == targetsAfter[k].RenderTarget;
                    if (!same)
                    {
                        if (before == 0) device.SetRenderTarget(null); else device.SetRenderTargets(Copy(targetsBefore, before));
                        throw new InvalidOperationException("its DrawLayer handler changed the render target (draw with context.Batch into whatever is set; see WorldDraw.DrawLayer)");
                    }
                    WorldDraw.Looked++; if (routing > 0) WorldDraw.LookedRouted++;
                    Count(WorldDraw.LookedBy, hook.OwnerAt(list, i));
                    return;
                }
                catch (Exception ex) { hook.Failed(list, i, ex); }
                finally { hook.Done(i, t); }
            }
        }
        layer.f3(context, view);
    }
    static Microsoft.Xna.Framework.Graphics.RenderTargetBinding[] Copy(Microsoft.Xna.Framework.Graphics.RenderTargetBinding[] a, int n)
    {
        var r = new Microsoft.Xna.Framework.Graphics.RenderTargetBinding[n]; Array.Copy(a, r, n); return r;
    }

    [ThreadStatic] internal static int tileRouting;
    public static void TileExtras(a7m view, cp9 context, ak1 tile)
    {
        var hook = WorldDraw.DrawTileExtras;
        if (hook.Any)
        {
            var v = new WorldView(view); var c = new DrawContext(context); var mt = new MapTile(tile);
            var list = hook.Snapshot;
            for (int i = list.Length - 1; i >= 0; i--)
            {
                long t = hook.Starting();
                tileRouting++;
                try { if (list[i](v, c, mt)) return; } catch (Exception ex) { hook.Failed(list, i, ex); } finally { tileRouting--; hook.Done(i, t); }
            }
        }
        WorldDraw.GameTileExtras(view, context, tile);
        TileMarks.DrawOn(view, context, tile);
    }

    public static void WorldDone(a7m view, cp9 context)
    {
        Untint();
        World.WorldDone();
        if (WorldDraw.WorldDone.Any) { var v = new WorldView(view); var c = new DrawContext(context); WorldDraw.WorldDone.Run(h => h(v, c)); }
    }

    public static void BeforePass(a7m view)
    {
        Outlines.BeforePass(view);
        if (WorldDraw.ShaderPassStarting.Any) { var v = new WorldView(view); WorldDraw.ShaderPassStarting.Run(h => h(v)); }
    }

    public static void AfterPass(a7m view, cp9 context)
    {
        if (WorldDraw.ShaderPassDone.Any) { var v = new WorldView(view); var c = new DrawContext(context); WorldDraw.ShaderPassDone.Run(h => h(v, c)); }
        Outlines.AfterPass(view);
    }
}
}
