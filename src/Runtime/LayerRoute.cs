// UnderRailWorks: where world layers go, as requests from every mod that combine (WorldDraw.RouteLayer /
// RouteTileExtras). A mod never takes a layer over: it asks for it to be hidden, copied into a picture, or for the
// world from here on to go into a picture of its own (a split). Works does the switching of render targets and
// batches, so every mod's requests apply to every layer, and other mods' looks (DrawLayer) and tints (LayerColour)
// show in every copy. docs/Drawing.md, "Where layers go".
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

// A picture the world pass draws into: the world's own (the screen, or Works' picture while a mod post-processes), a
// split's (LayerRoute.SplitHere), or a mod's own (WorldDraw.Picture). Screen-sized.
[Preview("the world pass, built for Wall Vision")]
public sealed class PassPicture
{
    internal PassPicture() { }
    // Its render target; null for the world's own picture (which may be the back buffer).
    public RenderTarget2D Target { get; internal set; }
    // A split's: the picture the world went into before it (Works puts the split's picture onto it after WorldDone).
    // Null for the world's own picture and for mods' pictures.
    public PassPicture Below { get; internal set; }
    // The mod that split the world into it, or that owns it; null for the world's own picture.
    public string Owner { get; internal set; }
    public bool IsSplit { get { return Below != null; } }
    // Something was drawn into it this frame (a mod's picture is cleared at its first drawing in a frame).
    public bool DrawnThisFrame { get { return frame == Passes.Frame; } }
    internal int frame = -1;
    internal string name;
}

// One layer's (or one tile's extras') way through the world pass, for the RouteLayer / RouteTileExtras handlers. Every
// handler gets the same route and adds its requests; after the last one Works carries them out: a split first, then
// the copies in the order asked, then the layer into the world's current picture unless hidden. Valid only during the
// handler. During the save screenshot's pass only Hide applies.
[Preview("the world pass, built for Wall Vision")]
public sealed class LayerRoute
{
    internal LayerRoute() { }
    internal string mod;
    internal bool hidden;
    internal readonly List<PassPicture> splits = new List<PassPicture>();
    internal readonly List<KeyValuePair<PassPicture, BlendState>> copies = new List<KeyValuePair<PassPicture, BlendState>>();
    internal readonly List<string> hiddenBy = new List<string>(), copiedBy = new List<string>();
    internal void Reset() { hidden = false; splits.Clear(); copies.Clear(); hiddenBy.Clear(); copiedBy.Clear(); }

    // The layer (IsNull for tile extras).
    public Layer Layer { get; internal set; }
    // The tile whose extras these are (IsNull for a layer).
    public MapTile Tile { get; internal set; }
    // The picture the world is being drawn into now (a split's after one).
    public PassPicture Current { get { return Passes.Current; } }
    // Some mod asked for it not to go into the current picture.
    public bool Hidden { get { return hidden; } }

    // Not drawn into the current picture (copies still are). Any mod's Hide hides it.
    public void Hide() { hidden = true; hiddenBy.Add(mod); }

    // Drawn into picture as well (alpha-blended), with every mod's look and tint: picture can be a mod's own
    // (WorldDraw.Picture), the current one, or one below a split.
    public void AlsoInto(PassPicture picture) { AlsoInto(picture, null); }
    // ... with blend (e.g. one that only erases what's under the layer).
    public void AlsoInto(PassPicture picture, BlendState blend)
    {
        if (picture == null) throw new ArgumentNullException("picture");
        copies.Add(new KeyValuePair<PassPicture, BlendState>(picture, blend));
        copiedBy.Add(mod);
    }

    // From this layer on, the world goes into a new picture of your mod's (cleared, screen-sized); the picture before it
    // is its Below. After WorldDone's handlers (change your picture there, e.g. cut holes into it) Works draws it onto
    // Below. Once per mod per frame: a second call returns the same picture. Null in the save screenshot's pass.
    // Splits of several mods nest (each goes onto the one before), so they all show.
    public PassPicture SplitHere() { return Passes.SplitFor(this, mod); }
}

}

namespace UnderRailWorks.Internal
{

// The world pass's pictures for one frame: the world's own, splits, mods' pictures; and drawing layers into them.
static class Passes
{
    internal static int Frame;
    static a7m view; static cp9 context;
    static GraphicsDevice G { get { return DrawContexts.Device(context); } }
    static bool capture, broken, haveBase;
    static readonly PassPicture world = new PassPicture();
    static RenderTargetBinding[] worldTargets; static Viewport worldViewport; static Rectangle worldScissor;
    internal static PassPicture Current = world;
    static readonly List<PassPicture> splits = new List<PassPicture>();
    // Kept between frames: split pictures by mod (and depth), mods' pictures by mod/name.
    static readonly Dictionary<string, PassPicture> kept = new Dictionary<string, PassPicture>();
    static SpriteBatch batch;
    static readonly RasterizerState Scissored = new RasterizerState { ScissorTestEnable = true };     // as a7m begins its world batch
    // While a copy is drawn or the world goes into a split: a look or tile mark lands in a mod's picture (drawstats).
    internal static bool InModPicture { get { return copying > 0 || Current != world; } }
    static int copying;
    internal static long Hidden, Copies, Splits;
    internal static readonly Dictionary<string, long> HiddenBy = new Dictionary<string, long>(), CopiedBy = new Dictionary<string, long>(), SplitBy = new Dictionary<string, long>();

    static Passes() { lock (ModScope.Hooks) ModScope.Hooks.Add(new Owner()); }
    sealed class Owner : IModOwned { public void RemoveOwner(string mod) { Forget(mod); } }
    internal static void Forget(string mod)
    {
        foreach (var k in new List<string>(kept.Keys))
            if (k.StartsWith(mod + "/", StringComparison.OrdinalIgnoreCase)) { try { kept[k].Target?.Dispose(); } catch { } kept.Remove(k); }
    }

    // WorldStarting (no batch begun; the target is the world's, its scissor not set yet).
    internal static void Starting(a7m v, cp9 c)
    {
        Frame++;
        view = v; context = c;
        splits.Clear(); Current = world; haveBase = false; copying = 0;
        capture = Interface.CapturingSaveScreenshot(G);
    }

    // The world's own picture as it is when first needed (inside the pass: target, viewport, scissor set by the game).
    static void Base()
    {
        if (haveBase) return;
        var g = G;
        worldTargets = g.GetRenderTargets(); worldViewport = g.Viewport; worldScissor = g.ScissorRectangle;
        haveBase = true;
    }
    static Point Size()
    {
        var t = worldTargets.Length > 0 ? worldTargets[0].RenderTarget as RenderTarget2D : null;
        return t != null ? new Point(t.Width, t.Height) : new Point(G.PresentationParameters.BackBufferWidth, G.PresentationParameters.BackBufferHeight);
    }
    static RenderTarget2D Sized(RenderTarget2D t, Point size)
    {
        if (t != null && !t.IsDisposed && t.Width == size.X && t.Height == size.Y) return t;
        t?.Dispose();
        // PreserveContents: FNA clears a DiscardContents target whenever it's set again.
        return new RenderTarget2D(G, size.X, size.Y, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    }

    static void Bind(PassPicture p)
    {
        var g = G;
        g.Textures[0] = null;     // a target still bound as a texture can't be set
        if (p == world) { if (worldTargets.Length == 0) g.SetRenderTarget(null); else g.SetRenderTargets(worldTargets); }
        else g.SetRenderTarget(p.Target);
        g.Viewport = worldViewport;
        g.ScissorRectangle = worldScissor;
    }
    static void BeginWorld(BlendState blend, DepthStencilState depth)
    {
        WorldViews.BeginWorldBatch(view, context, SpriteSortMode.Deferred, blend ?? BlendState.AlphaBlend, SamplerState.PointClamp, depth, Scissored);
    }

    // A mod's own picture (WorldDraw.Picture).
    internal static PassPicture Picture(string mod, string name)
    {
        if (mod == null || name == null) throw new ArgumentNullException(mod == null ? "mod" : "name");
        PassPicture p;
        var key = mod + "/picture/" + name;
        if (!kept.TryGetValue(key, out p)) { p = new PassPicture { Owner = mod, name = name }; kept[key] = p; }
        return p;
    }

    internal static PassPicture SplitFor(LayerRoute route, string mod)
    {
        if (capture || broken || view == null) return null;
        foreach (var s in splits) if (s.Owner == mod) return s;
        foreach (var s in route.splits) if (s.Owner == mod) return s;
        PassPicture p;
        var key = mod + "/split";
        if (!kept.TryGetValue(key, out p)) { p = new PassPicture { Owner = mod }; kept[key] = p; }
        // Its Below is known now (a copy into it may be asked for on this very layer); its target is made when the
        // route is carried out, before the layer is drawn.
        p.Below = route.splits.Count > 0 ? route.splits[route.splits.Count - 1] : Current;
        route.splits.Add(p);
        return p;
    }

    // After every handler asked: the split, the copies, then the layer itself. draw: the layer's look (or the tile's extras).
    internal static void Carry(LayerRoute route, Action draw, Action<string> trace)
    {
        if (capture) { if (!route.hidden) draw(); return; }
        try
        {
            foreach (var p in route.splits) if (!broken) Split(p, trace);
            for (int i = 0; i < route.copies.Count; i++)
            {
                var c = route.copies[i];
                if (broken) break;
                Copies++; Count(CopiedBy, route.copiedBy[i]);
                if (trace != null) trace("      copied into " + Describe(c.Key) + " for " + route.copiedBy[i] + (c.Value != null && c.Value != BlendState.AlphaBlend ? " (own blend)" : ""));
                Into(c.Key, c.Value, draw);
            }
        }
        catch (Exception ex) { Broke(ex); }
        if (route.hidden)
        {
            Hidden++; foreach (var m in route.hiddenBy) Count(HiddenBy, m);
            if (trace != null) trace("      hidden by " + string.Join(", ", route.hiddenBy.ToArray()));
            return;
        }
        draw();
    }
    static string Describe(PassPicture p)
    {
        if (p == world) return "the world's picture";
        if (p.IsSplit) return p.Owner + "'s split" + (p == Current ? " (current)" : "");
        return p.Owner + "'s picture \"" + p.name + "\"";
    }

    static void Split(PassPicture p, Action<string> trace)
    {
        if (splits.Contains(p)) return;
        Base();
        var g = G;
        context.EndBatchSafe();
        p.Target = Sized(p.Target, Size());
        p.Below = Current;
        splits.Add(p);
        Current = p;
        Bind(p);
        g.Clear(Color.Transparent);
        p.frame = Frame;
        BeginWorld(BlendState.AlphaBlend, DepthStencilState.Default);
        Splits++; Count(SplitBy, p.Owner);
        if (trace != null) trace("      split by " + p.Owner + ": the world goes into its picture from here");
    }

    // Draws into p (ending and beginning the world batch around it), then the world batch goes on in the current picture.
    static void Into(PassPicture p, BlendState blend, Action draw)
    {
        Base();
        var g = G;
        bool current = p == Current;
        context.EndBatchSafe();
        if (!current)
        {
            if (p != world && !p.IsSplit) p.Target = Sized(p.Target, Size());
            Bind(p);
            if (p.frame != Frame) { if (p != world && !p.IsSplit) g.Clear(Color.Transparent); p.frame = Frame; }
        }
        BeginWorld(blend, current ? DepthStencilState.Default : DepthStencilState.None);
        copying++;
        try { draw(); }
        finally
        {
            copying--;
            context.EndBatchSafe();
            if (!current) Bind(Current);
            BeginWorld(BlendState.AlphaBlend, DepthStencilState.Default);
        }
    }

    // After WorldDone's handlers (the world batch ended): each split onto the picture below it, the last first; the
    // world's own picture bound again as the game expects.
    internal static void Done()
    {
        if (splits.Count == 0 || view == null) { Clear(); return; }
        var g = G;
        try
        {
            if (batch == null || batch.IsDisposed || batch.GraphicsDevice != g) batch = new SpriteBatch(g);
            for (int i = splits.Count - 1; i >= 0; i--)
            {
                var s = splits[i];
                Bind(s.Below);
                g.ScissorRectangle = new Rectangle(0, 0, worldViewport.Width, worldViewport.Height);
                batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
                batch.Draw(s.Target, Vector2.Zero, Color.White);
                batch.End();
            }
        }
        catch (Exception ex) { try { batch.End(); } catch { } Broke(ex); }
        finally { try { Bind(world); } catch { } Clear(); }
    }
    static void Clear() { splits.Clear(); Current = world; view = null; context = null; }

    // Something failed while switching pictures: back to the world's own picture with the world batch begun (the game
    // goes on drawing), and no more routing until the game restarts.
    static void Broke(Exception ex)
    {
        broken = true;
        Log.Always("world pass routing: " + ex.GetType().Name + ": " + ex.Message + " (layers go only into the world's picture until restart)");
        try { context.EndBatchSafe(); } catch { }
        try { if (haveBase) Bind(world); } catch { }
        try { BeginWorld(BlendState.AlphaBlend, DepthStencilState.Default); } catch { }
        Current = world;
    }

    static void EndBatchSafe(this cp9 c) { DrawContexts.EndBatch(c); }
    internal static void Count(Dictionary<string, long> d, string mod) { if (mod == null) return; long n; d.TryGetValue(mod, out n); d[mod] = n + 1; }
}
}
