// UnderRailWorks: world-drawing events and outlines (the drawing area of the API).
// The patched exe calls the methods at the bottom at fixed points of the game's world pass (a7m) and creature outline
// builder (eg0). Mods add handlers to the events (Hooks.cs: Hook = every handler runs, ReplaceHook = the first
// handler that takes the step replaces the game's). With no handlers each point does exactly what the stock game does.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

public static class WorldDraw
{
    public delegate bool OffsetHandler(out Vector2 from);

    // Start of a7m.a(cp9, a2f, bool shaderPass, bkx): each layer the world pass draws, in drawing order (back to front).
    public static readonly Hook<Action<WorldView, DrawContext, Layer, bool>> LayerStarting = new Hook<Action<WorldView, DrawContext, Layer, bool>>("LayerStarting");
    // The layer is drawn (a2f.f3): return true to draw it yourself instead.
    public static readonly ReplaceHook<Func<Layer, DrawContext, LayerView, bool>> DrawLayer = new ReplaceHook<Func<Layer, DrawContext, LayerView, bool>>("DrawLayer");
    // A tile's extras after its floor (a7m.b(cp9, ak1): tile grid, transition tiles...): return true to draw them yourself
    // (TileExtrasStep draws them the game's way).
    public static readonly ReplaceHook<Func<WorldView, DrawContext, MapTile, bool>> DrawTileExtras = new ReplaceHook<Func<WorldView, DrawContext, MapTile, bool>>("DrawTileExtras");
    // The world pass is done, right before the screen is set as the target again.
    public static readonly Hook<Action<WorldView, DrawContext>> WorldDone = new Hook<Action<WorldView, DrawContext>>("WorldDone");
    // Start / end of the pass for layers with their own shader (a7m.b(cp9): outlines). Outline layers are in
    // view.ShaderLayers["Outline"]; outlines built only for mods are already taken out (Outlines.IsHidden).
    public static readonly Hook<Action<WorldView>> ShaderPassStarting = new Hook<Action<WorldView>>("ShaderPassStarting");
    public static readonly Hook<Action<WorldView, DrawContext>> ShaderPassDone = new Hook<Action<WorldView, DrawContext>>("ShaderPassDone");
    // The interface is being drawn into an off-screen picture placed at from (screen pixels): the first handler that
    // returns true sets it, and UiScaleFix maps drawing by it.
    public static readonly ReplaceHook<OffsetHandler> InterfaceOffset = new ReplaceHook<OffsetHandler>("InterfaceOffset");

    // The game's own tile-extras step.
    internal static readonly Action<a7m, cp9, ak1> GameTileExtras = (Action<a7m, cp9, ak1>)Delegate.CreateDelegate(typeof(Action<a7m, cp9, ak1>),
        typeof(a7m).GetMethod("b", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(cp9), typeof(ak1) }, null));
    public static void TileExtrasStep(WorldView view, DrawContext context, MapTile tile) { GameTileExtras(view.Game, context.Game, tile.Game); }

    internal static bool Offset(out Vector2 from)
    {
        Vector2 found = Vector2.Zero;
        bool set = InterfaceOffset.Any && InterfaceOffset.Run(h => h(out found));
        from = found;
        return set;
    }
}

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

// Creature outlines (eg0 builds them while Tab or the reverse-highlight key is held, or a creature is hovered, or in
// combat with Always). A mod can ask for outlines for every creature Tab would outline, all the time and during zone
// transitions too (Request); those extra outlines are hidden: Works takes them out of the game's shader pass, and
// mods draw them as they like (e.g. only where the creature is covered). The game's own outlines stay as they are.
public static class Outlines
{
    static readonly List<Func<bool>> Requests = new List<Func<bool>>();
    static bool Requested()
    {
        foreach (var r in Requests) { try { if (r()) return true; } catch { } }
        return false;
    }

    // The game's Character Outlines option is "Never" (no outlines at all, as with Tab).
    public static bool GameOff
    {
        get { var game = bpo.GetService<cnk>(); return game != null && game.aq1().b().k() == TimelapseVertigo.Common.Settings.OutlinesOption.Never; }
    }

    // wanted: checked each frame (e.g. "my effect is on").
    public static void Request(string mod, Func<bool> wanted) { Requests.Add(wanted); }

    static readonly ConditionalWeakTable<a2f, object> HiddenSet = new ConditionalWeakTable<a2f, object>();
    static readonly ConditionalWeakTable<bym, object> HasSet = new ConditionalWeakTable<bym, object>();
    static readonly object Mark = new object();

    // Built only because a mod asked: the game doesn't draw it.
    public static bool IsHidden(Layer outline) { return IsHidden(outline.Game); }
    internal static bool IsHidden(a2f outline) { object m; return outline != null && HiddenSet.TryGetValue(outline, out m); }
    // The layer set (bym, new every frame) got an outline this frame: Tab would outline this creature.
    public static bool Has(LayerSet layers) { return Has(layers.Game); }
    internal static bool Has(bym layers) { object m; return layers != null && HasSet.TryGetValue(layers, out m); }

    // This frame's outlines in the game's pass order, with whether each is hidden (taken out of the pass); valid from
    // ShaderPassStarting to ShaderPassDone.
    public static IList<KeyValuePair<Layer, bool>> All(WorldView view)
    {
        List<KeyValuePair<a2f, bool>> list;
        var result = new List<KeyValuePair<Layer, bool>>();
        if (Frames.TryGetValue(view.Game, out list)) foreach (var kv in list) result.Add(new KeyValuePair<Layer, bool>(new Layer(kv.Key), kv.Value));
        return result;
    }
    static readonly ConditionalWeakTable<a7m, List<KeyValuePair<a2f, bool>>> Frames = new ConditionalWeakTable<a7m, List<KeyValuePair<a2f, bool>>>();

    [ThreadStatic] static bool forced, transition;

    // eg0: the reverse-highlight key check, reached when the outline isn't otherwise shown and Tab isn't held.
    internal static bool Reverse(bool down)
    {
        if (down || !Requested()) return down;
        forced = true;
        return true;
    }

    // eg0 builds outlines only while the world view takes input (mv.w), which is off during zone transitions.
    internal static bool Interactive(bool takesInput)
    {
        bool on = Requested();
        transition = !takesInput && on;
        return takesInput || on;
    }

    // eg0: right before bym.a(outline): the layer is hidden if only a request made it, or during a transition.
    internal static void Added(bym layers, a2f outline)
    {
        if (transition) { transition = false; forced = true; }
        if (forced) { forced = false; HiddenSet.Remove(outline); HiddenSet.Add(outline, Mark); }
        HasSet.Remove(layers); HasSet.Add(layers, Mark);
    }

    internal static void BeforePass(a7m view)
    {
        var list = Frames.GetOrCreateValue(view);
        list.Clear();
        var all = WorldViews.ShaderLayers(view);
        List<a2f> outlines;
        if (all == null || !all.TryGetValue("Outline", out outlines)) return;
        foreach (var o in outlines) list.Add(new KeyValuePair<a2f, bool>(o, IsHidden(o)));
        outlines.RemoveAll(IsHidden);
    }

    internal static void AfterPass(a7m view)
    {
        List<KeyValuePair<a2f, bool>> list;
        if (Frames.TryGetValue(view, out list)) list.Clear();
    }
}
