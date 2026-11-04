// Save screenshots (tb.Draw, when a save is made): the game renders the screen into a picture of at most 2048 pixels
// a side, shrinking by 2048 / the longer side. It does that by setting the full-screen viewport on the smaller
// target, which doesn't scale anything: only the top-left 2048 pixels of the screen end up in the picture.
// With ResolutionFix and SaveScreenshots the picture is what a 1920x1080 screen would show: the game is made for
// 1080p, the load menu fits the picture in a box (a 32:9 screen shrunk whole would be a thin strip), and the view at
// a given zoom is the same as at 1080p (ZoomFix). The screen is drawn straight into a picture of that size: while it
// is the render target, UiScaleFix maps all drawing from the screen's centre into it. No image is ever larger than
// 1920x1080, so this also works under Microsoft XNA's Reach profile (Windows), which allows nothing over 2048.
// Screens up to 2048 pixels never went through this and still don't.
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

public static class SaveScreenshot
{
    const int Max = 2048;
    static bool On => FixSettings.ResolutionFix && FixSettings.SaveScreenshots;

    static RenderTarget2D shot;     // the picture being drawn, until tb.Draw stores it
    static Vector2 from;            // the top-left of the screen's part in it, in screen pixels
    static float scale;             // picture pixels per screen pixel

    // The two "> 2048" checks in tb.Draw: with the fix the game neither shrinks the size nor sets a viewport, and
    // Target gets the screen's size.
    public static int Limit(int limit) { return On ? int.MaxValue : limit; }

    // tb.Draw's new RenderTarget2D(...) for the screenshot.
    public static RenderTarget2D Target(GraphicsDevice g, int w, int h, bool mip, SurfaceFormat format, DepthFormat depth, int samples, RenderTargetUsage usage)
    {
        shot = null;
        if (!On || (w <= Max && h <= Max)) return new RenderTarget2D(g, w, h, mip, format, depth, samples, usage);
        // The largest 16:9 area at the centre, at 1080p's size (never enlarged).
        int rw = w, rh = h;
        if (rw * 9 >= rh * 16) rw = rh * 16 / 9; else rh = rw * 9 / 16;
        float k = Math.Min(1f, 1080f / rh);
        var target = new RenderTarget2D(g, (int)Math.Round(rw * k), (int)Math.Round(rh * k), mip, format, depth, samples, usage);
        from = new Vector2((w - rw) / 2, (h - rh) / 2);
        scale = k;
        shot = target;
        return target;
    }

    // Whether the picture is the current render target (so a failed capture can't leave the mapping on).
    public static bool Capturing(GraphicsDevice g, out Vector2 at, out float k)
    {
        at = from; k = scale;
        if (shot == null) return false;
        var targets = g.GetRenderTargets();
        return targets.Length > 0 && (object)targets[0].RenderTarget == shot;
    }

    public static bool Capturing(GraphicsDevice g) { Vector2 at; float k; return Capturing(g, out at, out k); }

    // tb.Draw stores the rendered target as the screenshot (tb.x).
    public static Texture2D Taken(Texture2D picture) { shot = null; return picture; }
}
