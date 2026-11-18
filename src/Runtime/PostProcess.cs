// UnderRailWorks: the post-processing stage. While any mod has an effect, the world pass draws into Works' own picture
// instead of the screen (bound at the pass's start, WorldStarting); at its end, after every WorldDone handler (Wall
// Vision's keyhole has put its picture back by then), each effect gets the finished world and draws the next picture,
// in load order, so effects stack (lighting, then a colour grade, then fog...). The last picture goes onto the screen,
// and the game goes on with the outline pass and the interface, which effects don't touch. With no effects nothing is
// redirected and the world pass is the game's.
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

// What an effect gets: the world so far (Input) and the picture to draw (Output, already set as the render target).
// Draw the whole picture: Input changed as you like (CopyInput draws it unchanged first). Your own render targets are
// fine (Scratch keeps them per size); set Output again before drawing the result. Coordinates are pixels of the picture
// (the screen's, or the save screenshot's); ToPixels turns map positions and interface rectangles into them.
public sealed class WorldPicture
{
    internal WorldPicture() { }
    public Texture2D Input { get; internal set; }
    public RenderTarget2D Output { get; internal set; }
    public GraphicsDevice Device { get; internal set; }
    public int Width { get { return Output.Width; } }
    public int Height { get { return Output.Height; } }
    // A sprite batch for effects (not begun; End it before returning).
    public SpriteBatch Batch { get; internal set; }
    // The world's zoom (1080p-equivalent: 1.5 at the default view).
    public float Zoom { get; internal set; }
    // This picture is the save screenshot's, not the screen's.
    public bool ForSaveScreenshot { get; internal set; }
    internal Vector2 Offset;     // the picture's top-left in screen pixels (the save screenshot's: its own)

    // A map position (as World.ToScreen) in this picture's pixels; null without a world.
    public Vector2? ToPixels(Vector3 mapPosition)
    {
        var at = World.ToScreen(mapPosition);
        if (at == null) return null;
        var p = Interface.ToPixels(new Rectangle((int)Math.Round(at.Value.X), (int)Math.Round(at.Value.Y), 0, 0));
        return new Vector2(p.X - Offset.X, p.Y - Offset.Y);
    }
    // An interface rectangle (Character.ScreenBounds, World.TileBounds) in this picture's pixels.
    public Rectangle ToPixels(Rectangle interfaceUnits)
    {
        var p = Interface.ToPixels(interfaceUnits);
        p.Offset(-(int)Offset.X, -(int)Offset.Y);
        return p;
    }

    // Input drawn unchanged into Output.
    public void CopyInput()
    {
        Device.SetRenderTarget(Output);
        Batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        Batch.Draw(Input, Vector2.Zero, Color.White);
        Batch.End();
    }

    // A render target of the effect's own, kept between frames (name: yours per use; made again at another size).
    public RenderTarget2D Scratch(WorksMod mod, string name, int width, int height)
    {
        return PostProcess.ScratchFor(mod.Id + "/" + name, width, height);
    }
}

static class PostProcess
{
    // a7m's world pass between WorldStarting and WorldDone: drawing into our picture.
    static bool active;
    static RenderTargetBinding[] saved = new RenderTargetBinding[0];
    static Viewport savedViewport;
    static RenderTarget2D world, second;
    static SpriteBatch batch;
    static readonly WorldPicture picture = new WorldPicture();
    static readonly System.Collections.Generic.Dictionary<string, RenderTarget2D> scratch = new System.Collections.Generic.Dictionary<string, RenderTarget2D>();
    static bool broken;
    internal static long Frames, Effects;

    internal static RenderTarget2D ScratchFor(string key, int w, int h)
    {
        RenderTarget2D t;
        if (scratch.TryGetValue(key, out t) && !t.IsDisposed && t.Width == w && t.Height == h) return t;
        if (t != null) t.Dispose();
        t = new RenderTarget2D(picture.Device, Math.Max(1, w), Math.Max(1, h), false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
        scratch[key] = t;
        return t;
    }

    static RenderTarget2D Target(GraphicsDevice g, RenderTarget2D t, int w, int h, DepthFormat depth)
    {
        if (t != null && !t.IsDisposed && t.Width == w && t.Height == h) return t;
        if (t != null) t.Dispose();
        // PreserveContents: FNA clears a DiscardContents target whenever it's set again (Wall Vision switches away and back).
        return new RenderTarget2D(g, w, h, false, SurfaceFormat.Color, depth, 0, RenderTargetUsage.PreserveContents);
    }

    // After the world pass's first cp9.a() (no batch begun), before it sets its scissor and begins the world batch.
    internal static void Starting(a7m view, cp9 context)
    {
        active = false;
        if (broken || !WorldDraw.PostProcess.Any) return;
        try
        {
            var g = DrawContexts.Device(context);
            saved = g.GetRenderTargets();
            savedViewport = g.Viewport;
            int w, h;
            var into = saved.Length > 0 ? saved[0].RenderTarget as RenderTarget2D : null;
            if (into != null) { w = into.Width; h = into.Height; } else { w = g.PresentationParameters.BackBufferWidth; h = g.PresentationParameters.BackBufferHeight; }
            world = Target(g, world, w, h, DepthFormat.Depth24Stencil8);
            g.SetRenderTarget(world);
            g.Viewport = savedViewport;
            g.Clear(ClearOptions.Target | ClearOptions.DepthBuffer | ClearOptions.Stencil, Color.Black, 1f, 0);
            picture.ForSaveScreenshot = into != null;
            active = true;
        }
        catch (Exception ex) { Broke(ex); }
    }

    // After WorldDraw.WorldDone's handlers, before the game sets the screen again.
    internal static void Done(a7m view, cp9 context)
    {
        if (!active) return;
        active = false;
        var g = DrawContexts.Device(context);
        try
        {
            if (batch == null || batch.IsDisposed) batch = new SpriteBatch(g);
            second = Target(g, second, world.Width, world.Height, DepthFormat.None);
            picture.Device = g; picture.Batch = batch; picture.Zoom = WorldViews.Zoom(view);
            picture.Offset = Vector2.Zero;
            RenderTarget2D input = world, output = second;
            var hook = WorldDraw.PostProcess;
            var list = hook.Snapshot;
            for (int i = 0; i < list.Length; i++)
            {
                picture.Input = input; picture.Output = output;
                g.SetRenderTarget(output);
                g.Viewport = new Viewport(0, 0, output.Width, output.Height);
                long t = hook.Starting();
                bool drew = false;
                try { drew = list[i](picture); }
                catch (Exception ex) { drew = false; try { batch.End(); } catch { } hook.Failed(list, i, ex); }
                finally { hook.Done(i, t); }
                if (!drew) continue;
                Effects++;
                var swap = input; input = output; output = swap;
            }
            // The last picture onto what was bound before the world pass.
            if (saved.Length == 0) g.SetRenderTarget(null); else g.SetRenderTargets(saved);
            g.Viewport = savedViewport;
            batch.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            batch.Draw(input, new Rectangle(0, 0, savedViewport.Width, savedViewport.Height), new Rectangle(savedViewport.X, savedViewport.Y, savedViewport.Width, savedViewport.Height), Color.White);
            batch.End();
            Frames++;
        }
        catch (Exception ex)
        {
            Broke(ex);
            try { if (saved.Length == 0) g.SetRenderTarget(null); else g.SetRenderTargets(saved); g.Viewport = savedViewport; } catch { }
        }
    }

    static void Broke(Exception ex)
    {
        broken = true; active = false;
        Log.Always("post-processing: " + ex.GetType().Name + ": " + ex.Message + " (off until restart)");
    }
}
}
