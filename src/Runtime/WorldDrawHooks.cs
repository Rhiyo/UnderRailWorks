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
        string colouredBy = null;
        if (!shaderPass) { World.LayerDrawn(layer); if (WorldDraw.LayerColour.Any && layer.d != null) colouredBy = Colour(layer); }
        if (DrawTrace.On) DrawTrace.Layer(layer, shaderPass, colouredBy);
        var hook = WorldDraw.LayerStarting;
        if (!hook.Any) return;
        var v = new WorldView(view); var c = new DrawContext(context); var l = new Layer(layer);
        var list = hook.Snapshot;     // every world layer: no closure per call
        for (int i = 0; i < list.Length; i++) { long t = hook.Starting(); try { list[i](v, c, l, shaderPass); } catch (Exception ex) { hook.Failed(list, i, ex); } finally { hook.Done(i, t); } }
    }

    // Who changed the colour (for works.drawtrace), or null.
    static string Colour(a2f layer)
    {
        var hook = WorldDraw.LayerColour;
        var was = Layers.Tint(layer);
        var start = was ?? Color.White; var colour = start;
        var l = new Layer(layer);
        var list = hook.Snapshot;
        string by = null;
        for (int i = 0; i < list.Length; i++)
        {
            long t = hook.Starting();
            var before = colour;
            try { list[i](l, ref colour); } catch (Exception ex) { colour = before; hook.Failed(list, i, ex); } finally { hook.Done(i, t); }
            if (DrawTrace.On && colour != before) by = (by == null ? "" : by + ", ") + hook.OwnerAt(list, i);
        }
        if (colour == start) return null;
        tinted = layer; tintWas = was;
        Layers.SetTint(layer, colour);
        WorldDraw.Coloured++;
        return by ?? "?";
    }

    // a7m.a's a2f.f3 call: every mod's route requests (RouteLayer), then the layer into each picture they ask for.
    static readonly LayerRoute route = new LayerRoute();
    static a2f lookLayer; static cp9 lookContext; static bkx lookView; static a7m extrasView; static ak1 extrasTile;
    static readonly Action LookNow = () => Look(lookLayer, lookContext, lookView);
    static readonly Action ExtrasNow = () => { WorldDraw.GameTileExtras(extrasView, lookContext, extrasTile); TileMarks.DrawOn(extrasView, lookContext, extrasTile); };
    static readonly Action<string> TraceLine = DrawTrace.Line;
    public static void DrawLayer(a2f layer, cp9 context, bkx view)
    {
        try
        {
            var hook = WorldDraw.RouteLayer;
            if (!hook.Any) { Look(layer, context, view); return; }
            route.Reset(); route.Layer = new Layer(layer); route.Tile = default(MapTile);
            var l = route.Layer;
            var list = hook.Snapshot;
            for (int i = 0; i < list.Length; i++)
            {
                long t = hook.Starting();
                route.mod = hook.OwnerAt(list, i);
                try { list[i](l, route); }
                catch (Exception ex) { hook.Failed(list, i, ex); }
                finally { hook.Done(i, t); }
            }
            lookLayer = layer; lookContext = context; lookView = view;
            Passes.Carry(route, LookNow, DrawTrace.On ? TraceLine : null);
        }
        finally { lookLayer = null; lookView = null; Untint(); }
    }
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
                    WorldDraw.Looked++; if (Passes.InModPicture) WorldDraw.LookedInPicture++;
                    Passes.Count(WorldDraw.LookedBy, hook.OwnerAt(list, i));
                    if (DrawTrace.On) DrawTrace.Line("      drawn by " + hook.OwnerAt(list, i) + "'s look" + (Passes.InModPicture ? " (into a mod's picture)" : ""));
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

    public static void TileExtras(a7m view, cp9 context, ak1 tile)
    {
        if (DrawTrace.On) DrawTrace.Line("    tile " + tile.Location.X + "," + tile.Location.Y + " extras");
        var hook = WorldDraw.RouteTileExtras;
        if (!hook.Any) { WorldDraw.GameTileExtras(view, context, tile); TileMarks.DrawOn(view, context, tile); return; }
        try
        {
            route.Reset(); route.Layer = default(Layer); route.Tile = new MapTile(tile);
            var mt = route.Tile;
            var list = hook.Snapshot;
            for (int i = 0; i < list.Length; i++)
            {
                long t = hook.Starting();
                route.mod = hook.OwnerAt(list, i);
                try { list[i](mt, route); }
                catch (Exception ex) { hook.Failed(list, i, ex); }
                finally { hook.Done(i, t); }
            }
            extrasView = view; extrasTile = tile; lookContext = context;
            Passes.Carry(route, ExtrasNow, DrawTrace.On ? TraceLine : null);
        }
        finally { extrasView = null; extrasTile = null; lookContext = null; }
    }

    // The world pass's start (after its first cp9.a(), no batch begun).
    public static void WorldStarting(a7m view, cp9 context)
    {
        Untint();
        Lights.Frame();
        DrawTrace.FrameStarting();
        PostProcess.Starting(view, context);
        Passes.Starting(view, context);
        if (DrawTrace.On) DrawTrace.Line("world pass starts" + (WorldDraw.PostProcess.Any ? " (into Works' picture for post-processing)" : ""));
    }

    public static void WorldDone(a7m view, cp9 context)
    {
        Untint();
        World.WorldDone();
        if (DrawTrace.On) DrawTrace.Line("world done: " + Owners(WorldDraw.WorldDone) + "; game lighting at " + Lights.GameStrength.ToString("0.##"));
        if (WorldDraw.WorldDone.Any) { var v = new WorldView(view); var c = new DrawContext(context); WorldDraw.WorldDone.Run(h => h(v, c)); }
        Passes.Done();
        if (DrawTrace.On && WorldDraw.PostProcess.Any) DrawTrace.Line("post-processing: " + Owners(WorldDraw.PostProcess) + ", in this order");
        PostProcess.Done(view, context);
    }

    public static void BeforePass(a7m view)
    {
        Outlines.BeforePass(view);
        if (DrawTrace.On) DrawTrace.Line("outline pass starts (ShaderPassStarting: " + Owners(WorldDraw.ShaderPassStarting) + ")");
        if (WorldDraw.ShaderPassStarting.Any) { var v = new WorldView(view); WorldDraw.ShaderPassStarting.Run(h => h(v)); }
    }

    public static void AfterPass(a7m view, cp9 context)
    {
        if (WorldDraw.ShaderPassDone.Any) { var v = new WorldView(view); var c = new DrawContext(context); WorldDraw.ShaderPassDone.Run(h => h(v, c)); }
        Outlines.AfterPass(view);
        DrawTrace.FrameDone();
    }
    static string Owners<T>(HookBase<T> hook) where T : class
    {
        var list = hook.Snapshot;
        if (list.Length == 0) return "no handlers";
        var names = new List<string>();
        for (int i = 0; i < list.Length; i++) names.Add(hook.OwnerAt(list, i) ?? "?");
        return string.Join(", ", names.ToArray());
    }
}
}
