// UnderRailWorks: world-drawing events and outlines (the drawing area of the API).
// The patched exe calls the methods at the bottom at fixed points of the game's world pass (a7m) and creature outline
// builder (eg0). Mods add handlers to the events (Hooks.cs: Hook = every handler runs, ReplaceHook = the
// handler that takes the step replaces the game's; asked from the lowest mod in the load order up). With no handlers each point does exactly what the stock game does.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public static class WorldDraw
{
    public delegate bool OffsetHandler(out Vector2 from);
    // A layer's colour this frame: change colour to tint or fade it (it starts as the game's tint, usually white). Colours
    // are premultiplied: colour * 0.5f fades to half, colour.R/G/B scaled alone tints. Every handler runs, in load order,
    // each on what the one before left, so tints from several mods combine.
    public delegate void ColourHandler(Layer layer, ref Color colour);

    // Two levels (docs/ModdingGuide.md, "Drawing"):
    // - Composing (stable): LayerColour, TileMarks, World.ToScreen/TileBounds, Character.ScreenBounds, Ui.Overlay. Every
    //   mod's changes show together, and none of them can break another mod's drawing.
    // - The world pass itself ([Preview]): layer by layer, render targets and batches. Built for Wall Vision; a mod that
    //   uses it shares the frame with Wall Vision's keyhole, so keep to the rules on each member.
    //
    // A drawing handler that throws may have left a batch open or another render target set: that mod's drawing
    // handlers (all of them, its outline requests and tile marks) are removed for the rest of the session at its first
    // error, so the game's own drawing goes on as stock. The game itself keeps running; the Mods tab shows the error.
    static WorldDraw()
    {
        foreach (var h in All) h.SetOnFailure(StopDrawing);
    }
    static HookOwner[] All { get { return new HookOwner[] { LayerColour, LayerStarting, RouteLayer, DrawLayer, DrawTileExtras, WorldDone, ShaderPassStarting, ShaderPassDone, InterfaceOffset }; } }
    static readonly HashSet<string> stopped = new HashSet<string>();
    internal static void StopDrawing(string mod)
    {
        if (mod == null) return;
        lock (stopped) if (!stopped.Add(mod)) return;
        foreach (var h in All) ((IModOwned)h).RemoveOwner(mod);
        Outlines.Forget(mod);
        TileMarks.Forget(mod);
        Log.Always("mods: " + mod + ": an error while drawing; its drawing is off until the game restarts");
        lock (stopped) stopped.Remove(mod);     // a reloaded copy may add handlers again
    }

    // Each world layer's colour (not the shader pass's outlines): see ColourHandler. Composes across mods.
    public static readonly Hook<ColourHandler> LayerColour = new Hook<ColourHandler>("LayerColour");

    // Start of a7m.a(cp9, a2f, bool shaderPass, bkx): each layer the world pass draws, in drawing order (back to front).
    // Watch only: don't draw or change render targets here.
    [Preview("the world pass, built for Wall Vision")]
    public static readonly Hook<Action<WorldView, DrawContext, Layer, bool>> LayerStarting = new Hook<Action<WorldView, DrawContext, Layer, bool>>("LayerStarting");
    // Where a layer goes: return true to take it and draw it somewhere else (another render target, several, or not at
    // all), drawing it with LayerStep wherever it should appear, so other mods' DrawLayer looks still show. One mod
    // takes a layer (asked from the lowest mod in the load order up): Wall Vision's keyhole takes every layer after its
    // split, so two mods that both route layers don't combine; `works.drawstats` names the mods that took layers.
    // Leave the screen and the world batch as they were when you return.
    [Preview("the world pass, built for Wall Vision")]
    public static readonly ReplaceHook<Func<Layer, DrawContext, LayerView, bool>> RouteLayer = new ReplaceHook<Func<Layer, DrawContext, LayerView, bool>>("RouteLayer");
    // How a layer looks: return true to draw it yourself instead of the game. Draw with context.Batch (the world batch,
    // begun) into whatever is set: Wall Vision may be drawing this layer into its own picture. Don't end or begin batches
    // or change render targets here; a handler that does is stopped like one that throws. One mod draws a layer (asked
    // from the lowest mod in the load order up); for tints and fades use LayerColour, which composes.
    [Preview("the world pass, built for Wall Vision")]
    public static readonly ReplaceHook<Func<Layer, DrawContext, LayerView, bool>> DrawLayer = new ReplaceHook<Func<Layer, DrawContext, LayerView, bool>>("DrawLayer");
    // Where a tile's extras go (a7m.b(cp9, ak1): the tile grid, transition tiles, Works' tile marks): return true to
    // take them, drawing them with TileExtrasStep wherever they should appear. The same rules as RouteLayer.
    [Preview("the world pass, built for Wall Vision")]
    public static readonly ReplaceHook<Func<WorldView, DrawContext, MapTile, bool>> DrawTileExtras = new ReplaceHook<Func<WorldView, DrawContext, MapTile, bool>>("DrawTileExtras");
    // The world pass is done, right before the screen is set as the target again.
    [Preview("the world pass, built for Wall Vision")]
    public static readonly Hook<Action<WorldView, DrawContext>> WorldDone = new Hook<Action<WorldView, DrawContext>>("WorldDone");
    // Start / end of the pass for layers with their own shader (a7m.b(cp9): outlines). Outline layers are in
    // view.ShaderLayers["Outline"]; outlines built only for mods are already taken out (Outlines.IsHidden).
    [Preview("the world pass, built for Wall Vision")]
    public static readonly Hook<Action<WorldView>> ShaderPassStarting = new Hook<Action<WorldView>>("ShaderPassStarting");
    [Preview("the world pass, built for Wall Vision")]
    public static readonly Hook<Action<WorldView, DrawContext>> ShaderPassDone = new Hook<Action<WorldView, DrawContext>>("ShaderPassDone");
    // The interface is being drawn into an off-screen picture placed at from (screen pixels): the handler that
    // returns true sets it (asked from the lowest mod in the load order up), and UiScaleFix maps drawing by it.
    [Preview("the world pass, built for Wall Vision")]
    public static readonly ReplaceHook<OffsetHandler> InterfaceOffset = new ReplaceHook<OffsetHandler>("InterfaceOffset");

    // The game's own tile-extras step.
    internal static readonly Action<a7m, cp9, ak1> GameTileExtras = (Action<a7m, cp9, ak1>)Delegate.CreateDelegate(typeof(Action<a7m, cp9, ak1>),
        typeof(a7m).GetMethod("b", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(cp9), typeof(ak1) }, null));
    // A tile's extras as they'd be drawn with no RouteLayer-style taker: the game's, then mods' tile marks.
    [Preview("the world pass, built for Wall Vision")]
    public static void TileExtrasStep(WorldView view, DrawContext context, MapTile tile)
    {
        GameTileExtras(view.Game, context.Game, tile.Game);
        TileMarks.DrawOn(view.Game, context.Game, tile.Game);
    }
    // A layer drawn as it would be with no router: by the mod whose DrawLayer takes it, else the game's way.
    [Preview("the world pass, built for Wall Vision")]
    public static void LayerStep(Layer layer, DrawContext context, LayerView view) { WorldDrawHooks.Look(layer.Game, context.Game, view.Game); }

    internal static bool Offset(out Vector2 from)
    {
        Vector2 found = Vector2.Zero;
        bool set = InterfaceOffset.Any && InterfaceOffset.Run(h => h(out found));
        from = found;
        return set;
    }

    // works.drawstats: what the composing parts and the routers did since the last call.
    internal static long Coloured, Routed, Looked, LookedRouted, MarksDrawn, MarksRouted;
    internal static readonly Dictionary<string, long> RoutedBy = new Dictionary<string, long>(), LookedBy = new Dictionary<string, long>();
    internal static string Stats()
    {
        Func<Dictionary<string, long>, string> who = d => d.Count == 0 ? "" : " (" + string.Join(", ", System.Linq.Enumerable.Select(d, kv => kv.Key + " " + kv.Value)) + ")";
        var s = "drawstats: layers coloured " + Coloured + ", routed " + Routed + who(RoutedBy) + ", drawn by a mod's look " + Looked + who(LookedBy)
            + " (inside a route " + LookedRouted + "), tile marks drawn " + MarksDrawn + " (inside a route " + MarksRouted + ")";
        Coloured = Routed = Looked = LookedRouted = MarksDrawn = MarksRouted = 0; RoutedBy.Clear(); LookedBy.Clear();
        return s;
    }
}

// Creature outlines (eg0 builds them while Tab or the reverse-highlight key is held, or a creature is hovered, or in
// combat with Always). A mod can ask for outlines for every creature Tab would outline, all the time and during zone
// transitions too (Request); those extra outlines are hidden: Works takes them out of the game's shader pass, and
// mods draw them as they like (e.g. only where the creature is covered). The game's own outlines stay as they are.
[Preview("built for Wall Vision")]
public static class Outlines
{
    // Each mod's "I want outlines" / "pick through" questions; a mod's are forgotten when it is reloaded. One that throws
    // is reported to its mod and counts as no.
    sealed class Asks : IModOwned
    {
        readonly List<KeyValuePair<string, Func<bool>>> list = new List<KeyValuePair<string, Func<bool>>>();
        KeyValuePair<string, Func<bool>>[] snapshot = new KeyValuePair<string, Func<bool>>[0];
        readonly string what;
        internal Asks(string what) { this.what = what; lock (ModScope.Hooks) ModScope.Hooks.Add(this); }
        internal void Add(WorksMod mod, Func<bool> wanted)
        {
            if (mod == null || wanted == null) throw new ArgumentNullException(mod == null ? "mod" : "wanted");
            lock (list) { list.Add(new KeyValuePair<string, Func<bool>>(mod.Id, wanted)); snapshot = list.ToArray(); }
        }
        public void RemoveOwner(string mod) { lock (list) { list.RemoveAll(kv => kv.Key == mod); snapshot = list.ToArray(); } }
        internal bool Any()
        {
            var s = snapshot;
            for (int i = 0; i < s.Length; i++)
            {
                try { if (s[i].Value()) return true; }
                catch (Exception ex) { ModErrors.Report(s[i].Key, what, ex); RemoveOwner(s[i].Key); }
            }
            return false;
        }
    }
    static readonly Asks Requests = new Asks("Outlines.Request"), PickRequests = new Asks("Outlines.PickThrough");
    static bool Requested() { return Requests.Any(); }
    internal static void Forget(string mod) { Requests.RemoveOwner(mod); PickRequests.RemoveOwner(mod); }

    // The game's Character Outlines option is "Never" (no outlines at all, as with Tab).
    public static bool GameOff
    {
        get { var game = bpo.GetService<cnk>(); return game != null && game.aq1().b().k() == TimelapseVertigo.Common.Settings.OutlinesOption.Never; }
    }

    // wanted: checked each frame (e.g. "my effect is on").
    public static void Request(WorksMod mod, Func<bool> wanted) { Requests.Add(mod, wanted); }

    // Hidden outlines still pick their creature, as Tab's outlines do: the game checks outline layers before the world's
    // and keeps their creature even when a wall comes next (a7m's hit test), so a character behind scenery can be
    // hovered and clicked without Tab. Ask with Request too (no outline, nothing to pick). wanted: checked each frame.
    public static void PickThrough(WorksMod mod, Func<bool> wanted) { PickRequests.Add(mod, wanted); }
    static bool PickWanted() { return PickRequests.Any(); }
    // a7m.y: the layers the hit test checks, in drawing order (List<cy5>; cy5(layer, from the shader pass)).
    static readonly System.Reflection.FieldInfo HitList = typeof(a7m).GetField("y", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.DeclaredOnly);

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
        if (HitList != null && PickWanted())
        {
            var hits = HitList.GetValue(view) as List<cy5>;
            if (hits != null) foreach (var kv in list) if (kv.Value) hits.Add(new cy5(kv.Key, true));
        }
    }

    internal static void AfterPass(a7m view)
    {
        List<KeyValuePair<a2f, bool>> list;
        if (Frames.TryGetValue(view, out list)) list.Clear();
    }
}
}
