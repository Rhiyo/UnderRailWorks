// UI scale and insets. The interface (and the world view, which is one of its controls) is laid out for a virtual
// screen: the interface area (the screen minus UiInsetX/UiInsetY) divided by the scale. Drawing maps UI
// coordinates to pixels as pixel = ui * scale + area offset, so a 7680x4320 screen at 200% looks like 3840x2160,
// and insets centre the interface. The world keeps its size on screen because its zoom is divided by the scale
// (UnderrailDisplayModes), and with insets it is stretched back out to the whole screen (FullScreen).
//
// Hooks (Installer.cs): bef.k0/k4 screen size -> Virtual; SpriteBatch.Begin in cp9/bc2 -> Begin5/Begin6;
// the world batch's MatrixTransform -> Transform; GraphicsDevice.ScissorRectangle get/set -> GetScissor/SetScissor;
// Mouse.GetState -> GetMouse; bso.abb (cursor warp) -> ToScreen; tb.Draw's loading-screen size reads ->
// VirtualWidth/VirtualHeight; ci3 sizing/moving the world view -> FullScreen/WorldLocation; daj.iz -> InScreen; a7m.b's at() -> FloorOrigin; d0p's fill -> DimRect; czo.a (menu backgrounds) -> BackgroundSize/BackgroundPlace; bg1.iv's draw rectangle -> BackgroundDraw.
// Drawing is only mapped while the viewport covers the whole back buffer: off-screen render targets (light
// effects and the like) keep the game's own pixel sizes, while screenshots, which use a screen-sized viewport,
// are mapped like the screen. Save screenshots of screens over 2048 pixels are drawn straight into a 1080p
// picture (SaveScreenshot): while that is the target, the mapping also takes the screen's centre into it.
//
// This file uses XNA types. The build compiles it against FNA and then points every XNA type reference at the
// XNA 4.0 assembly the game itself uses for that type, so it runs on FNA (Wine Mono) and on Microsoft XNA alike.
using System;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

public static class UiScaleFix
{
    static GraphicsDevice device;
    static int screenW, screenH;     // the last real screen size seen (back buffer, or what bef.k0/k4 reported)

    public static float For(int width, int height) { return ScaleRules.Ui(width, height); }

    static ScaleRules.Area Area
    {
        get
        {
            if (device != null) { screenW = device.PresentationParameters.BackBufferWidth; screenH = device.PresentationParameters.BackBufferHeight; }
            return screenW > 0 ? ScaleRules.Layout(screenW, screenH) : new ScaleRules.Area { K = 1f };
        }
    }

    static bool Identity(ScaleRules.Area a) { return a.K == 1f && a.Left == 0 && a.Top == 0; }

    // UI coordinates -> pixels of the current target: pixel = ui * K + (X, Y).
    struct Map { public float K, X, Y; }

    // The mapping to draw with right now: only while drawing to the whole screen, or into the save screenshot
    // (the screen's mapping, then the screen's centre into the smaller picture: SaveScreenshot).
    static bool Drawing(GraphicsDevice g, out Map m)
    {
        device = g;
        var a = Area;
        m = new Map { K = a.K, X = a.Left, Y = a.Top };
        Vector2 from; float k;
        if (SaveScreenshot.Capturing(g, out from, out k))
        {
            m = new Map { K = m.K * k, X = (m.X - from.X) * k, Y = (m.Y - from.Y) * k };
            return true;
        }
        if (WorldDraw.Offset(out from))
        {
            m = new Map { K = m.K, X = m.X - from.X, Y = m.Y - from.Y };
            return true;
        }
        if (Identity(a)) return false;
        Viewport v = g.Viewport;
        return v.Width == g.PresentationParameters.BackBufferWidth && v.Height == g.PresentationParameters.BackBufferHeight;
    }

    static Matrix ToPixels(Map m) { return Matrix.CreateScale(m.K, m.K, 1f) * Matrix.CreateTranslation(m.X, m.Y, 0f); }

    // bef.k0 (back buffer size) and bef.k4 (requested size): what the UI lays itself out for.
    public static esj Virtual(esj screen)
    {
        int w = (int)SizeW.GetValue(screen), h = (int)SizeH.GetValue(screen);
        if (device == null) { screenW = w; screenH = h; }
        var a = ScaleRules.Layout(w, h);
        if (Identity(a) && a.Width == w && a.Height == h) return screen;
        int vw = (int)Math.Round(a.Width / a.K), vh = (int)Math.Round(a.Height / a.K);
        Once(ref loggedLayout, $"interface: {a.K * 100}% scale, {a.Width}x{a.Height} area at {a.Left},{a.Top} on {w}x{h}, laid out for {vw}x{vh}");
        return new esj(vw, vh);
    }

    static readonly System.Reflection.FieldInfo SizeW = typeof(esj).GetField("c"), SizeH = typeof(esj).GetField("d");     // fields share names with methods

    public static int VirtualWidth(int width) { var a = ScaleRules.Layout(width, Math.Max(1, screenH)); return (int)Math.Round(a.Width / a.K); }
    public static int VirtualHeight(int height) { var a = ScaleRules.Layout(Math.Max(1, screenW), height); return (int)Math.Round(a.Height / a.K); }

    // SpriteBatch.Begin(sort, blend, sampler, depth, rasterizer[, effect]) with the UI-to-pixel mapping as its transform.
    public static void Begin5(SpriteBatch batch, SpriteSortMode sort, BlendState blend, SamplerState sampler, DepthStencilState depth, RasterizerState rasterizer)
    {
        Begin6(batch, sort, blend, sampler, depth, rasterizer, null);
    }

    public static void Begin6(SpriteBatch batch, SpriteSortMode sort, BlendState blend, SamplerState sampler, DepthStencilState depth, RasterizerState rasterizer, Effect effect)
    {
        Map a;
        if (!Drawing(batch.GraphicsDevice, out a)) batch.Begin(sort, blend, sampler, depth, rasterizer, effect);
        else
        {
            Once(ref loggedDrawing, "interface: drawing mapped");
            batch.Begin(sort, blend, sampler, depth, rasterizer, effect, ToPixels(a));
        }
    }

    static bool loggedLayout, loggedDrawing, loggedWorld;
    static void Once(ref bool done, string line) { if (done) return; done = true; Log.Write(line); }

    // The world batch's pixel-to-clip projection, applied to UI coordinates.
    public static Matrix Transform(Matrix projection)
    {
        Map a;
        if (device == null || !Drawing(device, out a)) return projection;
        Once(ref loggedWorld, "interface: world drawing mapped");
        return ToPixels(a) * projection;
    }

    // Clip rectangles: the game works in UI coordinates, the device in pixels. One exception: a clip covering the
    // whole current target (the screen, or the screenshot image while one is captured) means "nothing clipped", and
    // the game tests for that as X == 0 && Y == 0 (a7m: it only puts a saved clip back when X or Y isn't 0). Mapped
    // with insets it would start at -offset and look clipped, and a clip saved during the transition screenshot
    // (2048x576) would then be put back on the screen and kept there. So it is passed as (0, 0, screen size in UI
    // units) both ways.
    static Rectangle Unclipped() { var a = Area; return new Rectangle(0, 0, (int)Math.Ceiling(screenW / a.K), (int)Math.Ceiling(screenH / a.K)); }

    static Rectangle FullTarget(GraphicsDevice g)
    {
        var targets = g.GetRenderTargets();
        if (targets.Length > 0 && targets[0].RenderTarget is Texture2D t) return new Rectangle(0, 0, t.Width, t.Height);
        return new Rectangle(0, 0, g.PresentationParameters.BackBufferWidth, g.PresentationParameters.BackBufferHeight);
    }

    public static void SetScissor(GraphicsDevice g, Rectangle r)
    {
        Map m;
        if (Drawing(g, out m))
        {
            var full = FullTarget(g);
            if (r == Unclipped()) r = full;
            else
            {
                int x = (int)Math.Floor(r.X * m.K + m.X), y = (int)Math.Floor(r.Y * m.K + m.Y);
                r = new Rectangle(x, y, (int)Math.Ceiling(r.Right * m.K + m.X) - x, (int)Math.Ceiling(r.Bottom * m.K + m.Y) - y);
                r = Rectangle.Intersect(r, full);     // the screenshot's picture is smaller than the screen
            }
        }
        g.ScissorRectangle = r;
    }

    public static Rectangle GetScissor(GraphicsDevice g)
    {
        Rectangle r = g.ScissorRectangle;
        Map m;
        if (!Drawing(g, out m)) return r;
        if (r == FullTarget(g)) return Unclipped();
        int x = (int)Math.Floor((r.X - m.X) / m.K), y = (int)Math.Floor((r.Y - m.Y) / m.K);
        return new Rectangle(x, y, (int)Math.Ceiling((r.Right - m.X) / m.K) - x, (int)Math.Ceiling((r.Bottom - m.Y) / m.K) - y);
    }

    public static int ScreenWidth { get { var unused = Area; return screenW; } }
    public static int ScreenHeight { get { var unused = Area; return screenH; } }

    // Screen pixels per UI unit.
    public static float Scale => Area.K;

    // A rectangle in UI coordinates as screen pixels (where the game's mapped drawing puts it).
    public static Rectangle ToPixels(Rectangle r)
    {
        var a = Area;
        if (Identity(a)) return r;
        int x = (int)Math.Floor(r.X * a.K) + a.Left, y = (int)Math.Floor(r.Y * a.K) + a.Top;
        return new Rectangle(x, y, (int)Math.Ceiling(r.Right * a.K) + a.Left - x, (int)Math.Ceiling(r.Bottom * a.K) + a.Top - y);
    }

    // bso.abb moves the cursor to a point in the window (e.g. back to the anchor while right-dragging the camera):
    // the game gives UI coordinates, SetCursorPos needs pixels.
    public static Point ToScreen(Point p)
    {
        var a = Area;
        return Identity(a) ? p : new Point((int)Math.Round(p.X * a.K) + a.Left, (int)Math.Round(p.Y * a.K) + a.Top);
    }

    // The mouse in UI coordinates (negative or past the layout size in the inset margins).
    public static MouseState GetMouse()
    {
        MouseState m = Mouse.GetState();
        var a = Area;
        if (Identity(a)) return m;
        return new MouseState((int)Math.Floor((m.X - a.Left) / a.K), (int)Math.Floor((m.Y - a.Top) / a.K), m.ScrollWheelValue, m.LeftButton, m.MiddleButton, m.RightButton, m.XButton1, m.XButton2);
    }

    // ci3 (the in-game surface) sizes the world view to the layout size; with insets it must still cover the whole
    // screen, so it is placed at the screen's top-left in UI coordinates and given the full virtual screen size.
    static Point WorldOrigin
    {
        get { var a = Area; return new Point(-(int)Math.Round(a.Left / a.K), -(int)Math.Round(a.Top / a.K)); }
    }

    public static void FullScreen(euz world)
    {
        a2x.c(world);     // the game's own sizing first (the layout size)
        var a = Area;
        if (a.Width == screenW && a.Height == screenH) return;
        SetLocation(world, WorldOrigin);
        world.Size = new esj((int)Math.Ceiling(screenW / a.K), (int)Math.Ceiling(screenH / a.K));
    }

    // The whole screen in UI coordinates (the same as 0,0 + layout size without insets).
    public static Rectangle ScreenRect()
    {
        var a = Area;
        Point o = WorldOrigin;
        if (screenW <= 0) return new Rectangle(0, 0, 0, 0);
        return new Rectangle(o.X, o.Y, (int)Math.Ceiling(screenW / a.K), (int)Math.Ceiling(screenH / a.K));
    }

    // d0p.a(cp9, Color, Rectangle, bool, bool) fills a rectangle; popups (the Escape menu and others) dim the screen
    // with it over 0,0 + layout size, which with insets is only the interface area. That rectangle becomes the screen.
    public static Rectangle DimRect(Rectangle r)
    {
        var a = Area;
        if (r.X != 0 || r.Y != 0 || r.Width != (int)Math.Round(a.Width / a.K) || r.Height != (int)Math.Round(a.Height / a.K)) return r;
        return ScreenRect();
    }

    // czo.a(esj) builds a full-screen menu background (main menu, options, character creation...) for a size: it
    // picks the art closest to that size's aspect ratio, crops it from the top-left to fill, and places it at 0,0.
    // With insets the screens pass the layout size, so the art is chosen and cropped for the whole screen instead,
    // and drawn over it (backgrounds ignore insets). BackgroundFit=1 shows the whole image instead of the crop: as
    // large as fits, centred, never stretched.
    [ThreadStatic] static bool backgroundFull;

    public static esj BackgroundSize(esj requested)
    {
        backgroundFull = false;
        var a = Area;
        if (screenW <= 0 || (a.Width == screenW && a.Height == screenH)) return requested;
        int w = (int)SizeW.GetValue(requested), h = (int)SizeH.GetValue(requested);
        if (w != (int)Math.Round(a.Width / a.K) || h != (int)Math.Round(a.Height / a.K)) return requested;
        backgroundFull = true;
        return new esj((int)Math.Ceiling(screenW / a.K), (int)Math.Ceiling(screenH / a.K));
    }

    // The control itself stays where the game puts it: the main menu's button panel and version text are its
    // children, placed in the layout (inset) area. Only where the picture is drawn changes (BackgroundDraw).
    sealed class Background { public bool Full; public int ArtW, ArtH; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<bg1, Background> backgrounds = new System.Runtime.CompilerServices.ConditionalWeakTable<bg1, Background>();

    public static bg1 BackgroundPlace(bg1 background)
    {
        if (background == null) return background;
        var info = new Background { Full = backgroundFull };
        backgroundFull = false;
        if (FixSettings.BackgroundFit)
        {
            cne resources = bpo.GetService<cne>();
            var name = BgResource.GetValue(background) as string;
            Texture2D art = resources == null || name == null ? null : resources.GetResource<Texture2D>(name);
            if (art != null && art.Width > 0 && art.Height > 0) { info.ArtW = art.Width; info.ArtH = art.Height; BgCrop.SetValue(background, null); }
        }
        if (info.Full || info.ArtW > 0) backgrounds.Add(background, info);
        return background;
    }

    // bg1.iv, right after it builds the rectangle it draws the picture into (from its own position and size).
    public static void BackgroundDraw(ref Rectangle target, bg1 background)
    {
        Background info;
        if (!backgrounds.TryGetValue(background, out info)) return;
        if (info.Full) target = ScreenRect();
        if (info.ArtW > 0)
        {
            double k = Math.Min((double)target.Width / info.ArtW, (double)target.Height / info.ArtH);
            int w = (int)Math.Round(info.ArtW * k), h = (int)Math.Round(info.ArtH * k);
            target = new Rectangle(target.X + (target.Width - w) / 2, target.Y + (target.Height - h) / 2, w, h);
        }
    }

    // bg1: field b (resource name) shares its name with a method, and d (the crop, Rectangle?) is typed with the
    // game's XNA reference, so both go through reflection.
    static readonly System.Reflection.FieldInfo BgResource = typeof(bg1).GetField("b"), BgCrop = typeof(bg1).GetField("d");

    // ci3.iv reads the world view's Location to see whether a screen shake left it off 0,0: relative to the screen too.
    public static Point WorldLocationGet(euz world)
    {
        Point p = (Point)LocationProp.GetValue(world, null), o = WorldOrigin;
        return new Point(p.X - o.X, p.Y - o.Y);
    }

    // ci3's screen shake moves the world view around (0,0); keep that relative to the screen's top-left.
    public static void WorldLocation(euz world, Point p)
    {
        Point o = WorldOrigin;
        SetLocation(world, new Point(p.X + o.X, p.Y + o.Y));
    }

    // euz.Location is typed with the game's XNA reference and this file is compiled against FNA; at run time they
    // are the same Point, so set it through reflection.
    static readonly System.Reflection.PropertyInfo LocationProp = typeof(euz).GetProperty("Location");
    static void SetLocation(euz c, Point p) { LocationProp.SetValue(c, p, null); }

    // a7m.b(x, y) (mouse -> floor position) subtracts the world view's at() from the mouse but compares it with
    // tiles projected in screen coordinates, which only agree while the view is at 0,0 (always, without insets).
    // Keeping the mouse in screen coordinates there is the same thing in the stock case and right with insets.
    public static Point FloorOrigin(euz view) { return Point.Zero; }

    // daj.iz: the in-game surface takes the mouse anywhere on the screen, including the inset margins where only
    // the world is (stock: within the layout size from 0,0, which is the whole screen without insets).
    public static bool InScreen(MouseState m)
    {
        var a = Area;
        double x = m.X * a.K + a.Left, y = m.Y * a.K + a.Top;
        return x >= 0 && y >= 0 && x < (screenW > 0 ? screenW : int.MaxValue) && y < (screenH > 0 ? screenH : int.MaxValue);
    }
}
