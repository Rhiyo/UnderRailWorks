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
        if (WorldDraw.LayerStarting.Any) { var v = new WorldView(view); var c = new DrawContext(context); var l = new Layer(layer); WorldDraw.LayerStarting.Run(h => h(v, c, l, shaderPass)); }
    }

    public static void DrawLayer(a2f layer, cp9 context, bkx view)
    {
        if (WorldDraw.DrawLayer.Any) { var l = new Layer(layer); var c = new DrawContext(context); var v = new LayerView(view); if (WorldDraw.DrawLayer.Run(h => h(l, c, v))) return; }
        layer.f3(context, view);
    }

    public static void TileExtras(a7m view, cp9 context, ak1 tile)
    {
        if (WorldDraw.DrawTileExtras.Any) { var v = new WorldView(view); var c = new DrawContext(context); var t = new MapTile(tile); if (WorldDraw.DrawTileExtras.Run(h => h(v, c, t))) return; }
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
