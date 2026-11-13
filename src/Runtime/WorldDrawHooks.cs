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

    public static void Drawn(a7m view, cp9 context, a2f layer, bool shaderPass)
    {
        if (!shaderPass) World.LayerDrawn(layer);
        var hook = WorldDraw.LayerStarting;
        if (!hook.Any) return;
        var v = new WorldView(view); var c = new DrawContext(context); var l = new Layer(layer);
        var list = hook.Snapshot;     // every world layer: no closure per call
        for (int i = 0; i < list.Length; i++) { long t = hook.Starting(); try { list[i](v, c, l, shaderPass); } catch (Exception ex) { hook.Failed(list, i, ex); } finally { hook.Done(i, t); } }
    }

    public static void DrawLayer(a2f layer, cp9 context, bkx view)
    {
        var hook = WorldDraw.DrawLayer;
        if (hook.Any)
        {
            var l = new Layer(layer); var c = new DrawContext(context); var v = new LayerView(view);
            var list = hook.Snapshot;
            for (int i = list.Length - 1; i >= 0; i--) { long t = hook.Starting(); try { if (list[i](l, c, v)) return; } catch (Exception ex) { hook.Failed(list, i, ex); } finally { hook.Done(i, t); } }
        }
        layer.f3(context, view);
    }

    public static void TileExtras(a7m view, cp9 context, ak1 tile)
    {
        var hook = WorldDraw.DrawTileExtras;
        if (hook.Any)
        {
            var v = new WorldView(view); var c = new DrawContext(context); var mt = new MapTile(tile);
            var list = hook.Snapshot;
            for (int i = list.Length - 1; i >= 0; i--) { long t = hook.Starting(); try { if (list[i](v, c, mt)) return; } catch (Exception ex) { hook.Failed(list, i, ex); } finally { hook.Done(i, t); } }
        }
        WorldDraw.GameTileExtras(view, context, tile);
    }

    public static void WorldDone(a7m view, cp9 context)
    {
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
