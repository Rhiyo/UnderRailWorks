// Show Covered / Occluded characters (GameplayFixes + ShowCovered): characters hidden behind scenery are shown where
// they're hidden, with an effect per kind of character (player, hostile, other): Keyhole (class Keyhole, below), Line
// fill, Dithered fill, Fill or Outline. It follows the game's Tab outlines: the characters holding Tab would outline,
// in the same colours, and nothing with Character Outlines "Never".
//
// How the frame is drawn (a7m): world layers are drawn back to front through a7m.a(cp9, a2f, bool, bkx), so whatever
// is drawn after a creature is in front of it; layers with their own shader (the outline, u = "Outline") are kept
// aside and drawn after the world in a7m.b(cp9), which is why outlines show through walls.
// - eg0 creates the outline layer when Tab (or the reverse-highlight key) is held: the reverse-highlight check is
//   patched (Reverse) so it's created anyway, and the layer is marked (Add).
// - Drawn records the world layers in drawing order.
// - BeforePass takes the marked layers out of the game's shader pass; AfterPass draws each covered creature's effect
//   through a mask, all within the creature's own area:
//   1. the scenery drawn after the creature that overlaps it (not floors, not other characters), in white, into the
//      mask target (their real shapes);
//   2. the outline, with the game's shader and colour, into the work target (Outline); or the creature's own sprites
//      there, recoloured to the outline's colour (the fills), times the line or dot pattern;
//   3. the work target multiplied by the mask's coverage, then put on screen with ShowCoveredTransparency.
// The targets are screen-sized, so the game's own batch (bc2, with the interface mapping) draws into them exactly where
// it draws on screen.
// Windows (Microsoft XNA, the game's Reach profile): render targets and textures may be at most 2048 a side, wrap
// addressing needs power-of-two textures, and colour and alpha must blend the same way. So on XNA the fills and
// outlines only run when the screen fits (Fits); the line pattern is drawn as rectangles; every blend state is
// Reach-safe. The keyhole's render targets stay within 2048 (tiles above that, see Keyhole).
// Any error while drawing puts the game's drawing state back (batches, render targets, viewport, scissor) and turns the
// feature off for the session (Broken), rather than risking the game's own drawing.
using System;
using System.Collections.Generic;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

static class ShowCovered
{
    // --- Settings, read once per change (Drawn runs for every layer drawn) -----------------------------------------

    static int settingsSeen = -1;
    static bool on, anyKeyhole;
    static CoveredEffect player, hostile, other;
    static float opacity;
    static void Refresh()
    {
        if (settingsSeen == CoveredSettings.Changes) return;
        settingsSeen = CoveredSettings.Changes;
        on = CoveredSettings.On;
        player = CoveredSettings.Player; hostile = CoveredSettings.Hostile; other = CoveredSettings.Other;
        anyKeyhole = player == CoveredEffect.Keyhole || hostile == CoveredEffect.Keyhole || other == CoveredEffect.Keyhole;
        opacity = 1f - CoveredSettings.FillTransparency / 100f;
    }

    internal static bool Broken;     // an error while drawing: off for the rest of the session
    internal static bool On { get { Refresh(); return on && !Broken; } }

    internal static void Failed(string where, Exception ex)
    {
        if (!Broken) UnderRailFixesMod.Note("occluded characters: " + where + " failed, turned off until the game restarts: " + ex);
        Broken = true;
    }

    // Microsoft XNA (Windows, standard download) enforces the game's Reach profile; FNA (Proton, 64-bit) doesn't.
    static readonly bool Xna = typeof(GraphicsDevice).Assembly.GetName().Name == "Microsoft.Xna.Framework.Graphics";
    internal static bool Fits(GraphicsDevice g)
    {
        if (SaveScreenshot.Capturing(g)) return false;     // its picture isn't screen-sized; the keyhole still shows
        return !Xna || (g.PresentationParameters.BackBufferWidth <= 2048 && g.PresentationParameters.BackBufferHeight <= 2048);
    }

    // Tab would outline this creature (Works' Outlines; the keyhole takes the same characters: stats alone also took
    // explosive barrels and other destructible props).
    internal static bool HasOutline(bym owner) { return Outlines.Has(owner); }

    // --- a7m: the world pass and the shader pass --------------------------------------------------------------------

    sealed class View { public readonly List<a2f> Drawn = new List<a2f>(), Masked = new List<a2f>(); }
    static readonly ConditionalWeakTable<a7m, View> Views = new ConditionalWeakTable<a7m, View>();

    // Start of a7m.a(cp9, a2f, bool shaderPass, bkx): every layer the world pass draws, in order.
    [ThreadStatic] static bool keyholeLayer;     // the layer about to be drawn (DrawLayer) is the world pass's, with keyholes on
    internal static void Drawn(a7m view, cp9 context, a2f layer, bool shaderPass)
    {
        keyholeLayer = false;
        if (shaderPass || !On || layer == null) return;
        var state = Views.GetOrCreateValue(view);
        if (state.Drawn.Count == 0) Keyhole.Start(view, context);
        state.Drawn.Add(layer);
        if (!anyKeyhole || OutlinesOff()) return;
        try { Keyhole.Layer(layer); keyholeLayer = true; }
        catch (Exception ex) { Failed("keyhole", ex); Keyhole.Abort(true); }
    }

    // a7m.a's a2f.f3(cp9, bkx) call: the layer is drawn, by the game, or by the keyhole when it's in front of a keyhole.
    // True: the keyhole drew it (Works skips the game's draw).
    internal static bool DrawLayer(a2f layer, cp9 context, bkx view)
    {
        if (keyholeLayer)
        {
            keyholeLayer = false;
            try { if (Keyhole.Draw(layer, context, view)) return true; }
            catch (Exception ex) { Failed("keyhole", ex); Keyhole.Abort(true); }
        }
        return false;
    }

    // a7m.b(cp9, ak1): what a7m draws over a tile right after its floor, not as layers (the tile grid when that option is
    // on, the cgr tile renderers such as the red of transition tiles). The keyhole sends it where it sent that floor.
    internal static bool TileExtras(a7m view, cp9 context, ak1 tile)
    {
        if (On && anyKeyhole)
        {
            try { if (Keyhole.Tile(view, context, tile)) return true; }
            catch (Exception ex) { Failed("keyhole", ex); Keyhole.Abort(true); }
        }
        return false;
    }

    // a7m's world pass, after its loop, right before it sets the screen as the target again.
    internal static void WorldDone(a7m view, cp9 context)
    {
        try { Keyhole.Done(); }
        catch (Exception ex) { Failed("keyhole", ex); }     // Done puts the screen back itself
    }


    static readonly System.Diagnostics.Stopwatch PassClock = System.Diagnostics.Stopwatch.StartNew();
    static long passLast;

    // Start of a7m.b(cp9): the outlines to draw on the covered part. Works has already taken the outlines built only for
    // us (hidden) out of the game's pass: those are drawn on the covered part with the creature's effect, or not at
    // all. The game's own outlines (Tab, hovering, Always in combat) stay in its pass; with a fill, the covered part is
    // still filled, drawn after that pass so it's above the outline.
    internal static void BeforePass(a7m view)
    {
        var state = Views.GetOrCreateValue(view);
        state.Masked.Clear();
        if (CoveredSettings.KeyholeDebug && PassClock.ElapsedMilliseconds - passLast > 5000)
        {
            passLast = PassClock.ElapsedMilliseconds;
            var all = WorldViews.ShaderLayers(view);
            if (all != null)
                foreach (var kv in all)
                {
                    var sample = kv.Value.Count > 0 ? kv.Value[0] : null;
                    UnderRailFixesMod.Note("shader pass: \"" + kv.Key + "\" x" + kv.Value.Count + (sample == null ? "" : " e.g. entity " + (Layers.Entity(sample) == null ? "-" : Layers.Entity(sample).GetType().Name) + " id " + (Layers.Id(sample) ?? "-") + " at " + Layers.Bounds(sample)));
                }
        }
        if (!On) return;
        foreach (var o in Outlines.All(view))
        {
            var effect = EffectFor(o.Key.n);
            bool drawn = effect != CoveredEffect.Off && effect != CoveredEffect.Keyhole;
            if (drawn && (o.Value || effect != CoveredEffect.Outline)) state.Masked.Add(o.Key);
        }
    }

    // End of a7m.b(cp9).
    internal static void AfterPass(a7m view, cp9 context)
    {
        View state;
        if (!Views.TryGetValue(view, out state)) return;
        try { if (state.Masked.Count > 0 && On) Draw(view, context, state); }
        catch (Exception ex) { Failed("drawing", ex); }
        finally { state.Drawn.Clear(); state.Masked.Clear(); Effects.Clear(); Characters.Clear(); outlinesOff = null; }
    }

    // --- Who is who, once per frame -------------------------------------------------------------------------------

    // Characters: c5x with stats (amc); walls and props are c5x too. They never count as cover.
    static readonly Dictionary<bym, c5x> Characters = new Dictionary<bym, c5x>();
    internal static c5x Character(bym owner)
    {
        if (owner == null) return null;
        c5x c;
        if (Characters.TryGetValue(owner, out c)) return c;
        c = owner.b == null ? null : Entities.Character(owner.b);
        Characters[owner] = c;
        return c;
    }

    // The effect chosen for a creature: the player's; hostile (the game's hostile and unfriendly attitude towards the
    // player, as eg0 colours outlines: red, orange); or other (everyone else, passive ones too).
    static readonly Dictionary<bym, CoveredEffect> Effects = new Dictionary<bym, CoveredEffect>();
    internal static CoveredEffect EffectFor(bym owner)
    {
        if (owner == null) return CoveredEffect.Off;
        CoveredEffect e;
        if (Effects.TryGetValue(owner, out e)) return e;
        Refresh();
        var character = Character(owner);
        e = character == null ? CoveredEffect.Off : character.IsPlayer ? player : Hostile(character) ? hostile : other;
        Effects[owner] = e;
        return e;
    }

    static bool Hostile(c5x character) { return Entities.IsHostile(character); }

    // Character Outlines "Never": no outlines, so nothing (the keyhole included), as with Tab.
    static bool? outlinesOff;
    static bool OutlinesOff()
    {
        if (outlinesOff.HasValue) return outlinesOff.Value;
        try { var game = bpo.GetService<cnk>(); outlinesOff = game != null && game.aq1().b().k() == TimelapseVertigo.Common.Settings.OutlinesOption.Never; }
        catch { outlinesOff = false; }
        return outlinesOff.Value;
    }

    // --- Drawing --------------------------------------------------------------------------------------------------

    // Shared with the keyhole (which is done before this, each frame): the mask and a work target.
    internal static RenderTarget2D Mask, Work;
    static SpriteBatch batch;
    internal static Texture2D White;
    internal static void Targets(GraphicsDevice g)
    {
        Mask = Sized(g, Mask);
        Work = Sized(g, Work);
        if (White == null || White.IsDisposed) { White = new Texture2D(g, 1, 1); White.SetData(new[] { Color.White }); }
    }

    // Screen-sized; PreserveContents, as they are switched away from and back to (FNA clears DiscardContents targets then).
    internal static RenderTarget2D Sized(GraphicsDevice g, RenderTarget2D t)
    {
        int w = g.PresentationParameters.BackBufferWidth, h = g.PresentationParameters.BackBufferHeight;
        if (t != null && !t.IsDisposed && t.Width == w && t.Height == h) return t;
        t?.Dispose();
        return new RenderTarget2D(g, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    }

    // Reach-safe blends (colour and alpha blend the same way).
    // Recolours what is already in the target to one colour, keeping its alpha (premultiplied: colour x alpha).
    static readonly BlendState Recolour = new BlendState
    {
        ColorSourceBlend = Blend.DestinationAlpha, ColorDestinationBlend = Blend.Zero,
        AlphaSourceBlend = Blend.DestinationAlpha, AlphaDestinationBlend = Blend.Zero,
    };
    internal static readonly BlendState Multiply = new BlendState
    {
        ColorSourceBlend = Blend.Zero, ColorDestinationBlend = Blend.SourceAlpha,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.SourceAlpha,
    };

    // Clears an area of the current target (a transparent quad, Opaque: a Clear would clear all of it).
    internal static void Clear(SpriteBatch b, Rectangle area)
    {
        b.Begin(SpriteSortMode.Immediate, BlendState.Opaque, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        b.Draw(White, area, Color.Transparent);
        b.End();
    }

    internal static Rectangle PlacedOf(a2f layer) { return Layers.Bounds(layer); }
    internal static string IdOf(a2f layer) { return Layers.Id(layer); }

    // Floor and ground: many floor tiles around a character are drawn after it. Never cover.
    internal static bool Flat(a2f layer) { return Layers.IsFlat(layer); }

    // The game's batches, begun and ended through us, so an error can close whichever is open.
    static ob gameOpen;
    static bool ownOpen;
    static void BeginGame(ob game, SpriteSortMode sort, RasterizerState rasterizer, Effect effect)
    {
        DrawContexts.Begin(game, sort, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, rasterizer, effect);
        gameOpen = game;
    }
    static void EndGame() { var g = gameOpen; gameOpen = null; g?.an(); }
    static void BeginOwn(BlendState blend, SamplerState sampler, RasterizerState rasterizer = null)
    {
        batch.Begin(SpriteSortMode.Immediate, blend, sampler, DepthStencilState.None, rasterizer ?? RasterizerState.CullNone);
        ownOpen = true;
    }
    static void EndOwn() { ownOpen = false; batch.End(); }
    internal static void BeginBatch(ob game, SpriteSortMode sort, RasterizerState rasterizer) { BeginGame(game, sort, rasterizer, null); }

    // After an error: whichever batch we left open is closed.
    internal static void CloseOpen()
    {
        if (ownOpen) { ownOpen = false; try { batch.End(); } catch { } }
        if (gameOpen != null) { var g = gameOpen; gameOpen = null; try { g.an(); } catch { } }
    }

    static void Draw(a7m view, cp9 context, View state)
    {
        var g = DrawContexts.Device(context);
        if (!Fits(g)) return;
        int w = g.PresentationParameters.BackBufferWidth, h = g.PresentationParameters.BackBufferHeight;
        var screen = new Rectangle(0, 0, w, h);

        // Each layer's place, once (the reflection read boxes).
        var drawn = state.Drawn;
        var placed = new Rectangle[drawn.Count];
        var cover = new bool[drawn.Count];     // could cover a character: scenery, not floor, not a character
        for (int i = 0; i < drawn.Count; i++)
        {
            var layer = drawn[i];
            if (layer.d == null || !layer.b) continue;
            placed[i] = PlacedOf(layer);
            cover[i] = !Flat(layer) && Character(layer.n) == null;
        }
        var work = new List<KeyValuePair<a2f, List<a2f>>>();
        foreach (var o in state.Masked)
        {
            var covering = Covering(o, drawn, placed, cover);
            if (covering.Count > 0) work.Add(new KeyValuePair<a2f, List<a2f>>(o, covering));
        }
        if (work.Count == 0) return;

        Targets(g);
        if (batch == null || batch.GraphicsDevice != g) batch = new SpriteBatch(g);
        var targets = g.GetRenderTargets();
        var viewport = g.Viewport;
        var scissor = g.ScissorRectangle;
        bool clipped = scissor.X != 0 || scissor.Y != 0;     // as a7m.b decides
        var clipState = clipped ? WorldViews.Clip(view) : null;

        DrawContexts.EndBatch(context);     // end whichever batch is active, as a7m.b does before its own drawing
        try
        {
            ob game = context.b();
            cmk shader = b6l.a("Outline");
            var effect = shader == null ? null : DrawContexts.ShaderEffect(shader, context, view);
            int cell = Math.Max(1, (int)Math.Round(WorldViews.Zoom(view) * UiScaleFix.Scale));
            foreach (var item in work)
            {
                var chosen = EffectFor(item.Key.n);
                bool lines = chosen == CoveredEffect.Lines, dots = chosen == CoveredEffect.Dither;
                bool fill = lines || dots || chosen == CoveredEffect.Fill;
                // Everything below stays within the creature's own area.
                var around = PlacedOf(item.Key);
                around.Inflate(8, 8);
                var area = Rectangle.Intersect(UiScaleFix.ToPixels(around), screen);
                if (area.Width <= 0 || area.Height <= 0) continue;

                // 1. Coverage: the scenery drawn after the creature, over it.
                g.SetRenderTarget(Mask);
                Clear(batch, area);
                BeginGame(game, SpriteSortMode.Deferred, RasterizerState.CullNone, null);
                foreach (var layer in item.Value) Silhouette(context, layer);
                EndGame();

                // 2. The outline, as the game's shader pass draws it; or the creature itself in the outline's colour.
                g.SetRenderTarget(Work);
                Clear(batch, area);
                if (fill)
                {
                    BeginGame(game, SpriteSortMode.Deferred, RasterizerState.CullNone, null);
                    foreach (var layer in drawn)
                        if (layer.n == item.Key.n && layer.d != null && layer.b && !NotBody(IdOf(layer))) Silhouette(context, layer);
                    EndGame();
                    object colour;
                    var c = item.Key.v != null && item.Key.v.TryGetValue("OutlineColor", out colour) && colour is Color ? (Color)colour : Color.White;
                    BeginOwn(Recolour, SamplerState.PointClamp);
                    batch.Draw(White, area, c);
                    EndOwn();
                }
                else
                {
                    BeginGame(game, SpriteSortMode.Immediate, RasterizerState.CullNone, effect);
                    DrawContexts.ShaderParameters(shader, effect, item.Key, view);
                    item.Key.f3(context, view);
                    EndGame();
                }

                // 3. x coverage, x the pattern; erased where a keyhole is open; then onto the screen.
                BeginOwn(Multiply, SamplerState.PointClamp);
                batch.Draw(Mask, area, area, Color.White);
                EndOwn();
                if (lines) Lines(area, cell);
                else if (dots) Dots(g, area, cell);
                if (Keyhole.Holes.Count > 0)
                {
                    BeginOwn(Multiply, SamplerState.LinearClamp);
                    foreach (var hole in Keyhole.Holes) if (hole.Intersects(area)) batch.Draw(Keyhole.Erase(g), hole, Color.White);
                    EndOwn();
                }
                if (targets.Length == 0) g.SetRenderTarget(null); else g.SetRenderTargets(targets);
                g.Viewport = viewport;
                g.ScissorRectangle = scissor;
                BeginOwn(BlendState.AlphaBlend, SamplerState.PointClamp, clipState);
                batch.Draw(Work, area, area, Color.White * opacity);
                EndOwn();
            }
        }
        catch (Exception ex) { CloseOpen(); Failed("drawing", ex); }
        finally
        {
            if (targets.Length == 0) g.SetRenderTarget(null); else g.SetRenderTargets(targets);
            g.Viewport = viewport;
            g.ScissorRectangle = scissor;
            DrawContexts.ResumeBatch(context, clipState);     // as a7m.b ends: the normal batch again
        }
    }

    // Line fill: one line in three (1 on, 2 off), one world pixel high (cell screen pixels), drifting down at LineSpeed
    // world pixels a second. Drawn as rectangles: on Microsoft XNA a tiled 1x3 texture isn't allowed (Reach).
    const double LineSpeed = 3;
    const int LineRows = 3;
    static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();

    static void Lines(Rectangle area, int cell)
    {
        int period = LineRows * cell, shift = (int)(Clock.Elapsed.TotalSeconds * LineSpeed * cell) % period;
        int y = area.Y - (((area.Y - shift) % period) + period) % period;     // the first line at or above the area
        BeginOwn(Multiply, SamplerState.PointClamp);
        for (; y < area.Bottom; y += period)
        {
            batch.Draw(White, new Rectangle(area.X, y, area.Width, cell), Color.White);
            batch.Draw(White, new Rectangle(area.X, y + cell, area.Width, period - cell), Color.Transparent);
        }
        EndOwn();
    }

    // Dithered fill: spaced-out dots, two per 4x4 tile (a power of two, so it may be tiled on XNA too), each pattern
    // pixel one world pixel (cell screen pixels), lined up with the screen.
    static Texture2D dots;
    static void Dots(GraphicsDevice g, Rectangle area, int cell)
    {
        if (dots == null || dots.IsDisposed)
        {
            var data = new Color[16];
            for (int i = 0; i < 16; i++) data[i] = Color.Transparent;
            data[0] = Color.White; data[2 * 4 + 2] = Color.White;
            dots = new Texture2D(g, 4, 4);
            dots.SetData(data);
        }
        int tile = 4 * cell, x = area.X / tile * tile, y = area.Y / tile * tile;
        var cover = new Rectangle(x, y, area.Right - x, area.Bottom - y);
        BeginOwn(Multiply, SamplerState.PointWrap);
        batch.Draw(dots, cover, new Rectangle(x / cell, y / cell, (cover.Width + cell - 1) / cell, (cover.Height + cell - 1) / cell), Color.White);
        EndOwn();
    }

    // A creature's layers that aren't its body: the game's marker, shield and shadow (as eg0 leaves them out of highlights).
    internal static bool NotBody(string id) { return id == "marker" || id == "shield" || id == "SHD"; }

    // The scenery drawn after the creature's last layer that overlaps its outline.
    static List<a2f> Covering(a2f outlineLayer, List<a2f> drawn, Rectangle[] placed, bool[] cover)
    {
        var result = new List<a2f>();
        var owner = outlineLayer.n;
        if (owner == null) return result;
        int last = -1;
        for (int i = 0; i < drawn.Count; i++) if (drawn[i].n == owner) last = i;
        if (last < 0) return result;
        var area = PlacedOf(outlineLayer);
        area.Inflate(8, 8);
        for (int i = last + 1; i < drawn.Count; i++)
            if (cover[i] && drawn[i].n != owner && placed[i].Intersects(area)) result.Add(drawn[i]);
        return result;
    }

    // A layer's shape in white (its own transparency kept), through the game's own draw path, as a2f.f3 draws it.
    internal static void Silhouette(cp9 context, a2f layer)
    {
        var tint = Layers.Tint(layer);
        Layers.Draw(layer, context, Color.White * ((tint?.A ?? 255) / 255f));
    }
}

// Keyhole (a Show Covered effect for the player, hostile or other characters), after Neverwinter Nights: EE's
// "keyholing": a soft round hole, around each covered target, in the scenery standing in front of it.
// Targets: characters the game would outline (ShowCovered.HasOutline) with the Keyhole effect. See AGENTS.md ("World
// drawing", "The keyhole walking-floor bug") for why it works this way, and what was tried and failed.
// - Covered: a standing layer (not floor or ground, not a character, not a shadow or marker) drawn after a target over
//   its body, noted in Draw every frame and used the next frame. Frames where nobody was covered (and no hole is still
//   fading) cost nothing more.
// - Split: from the first layer after such a target, or earlier, from the first standing layer in front of last frame's
//   hole (InFront: a walking character is drawn on the tile it walks to, after the wall piece on the square it's leaving),
//   the world pass continues into a front picture instead of the screen (the game's world batch, restarted there with
//   a7m.iu as by8 does, so lighting and shaders are unchanged). One screen-sized render target, or on Microsoft XNA
//   above 2048 (Reach) a grid of tiles of at most 2048 a side (KeyholeTiles=1 forces that anywhere, for testing): each
//   layer goes into every tile it reaches (ShowCovered.DrawLayer, and TileExtras for a7m's per-tile extras), the first
//   time by the game's call, again from its sprite as drawn (chf.avw without avv: a character's composite, by8, isn't
//   rebuilt). UiScaleFix maps the drawing into the bound target (Into).
// - Floor drawn after the split near last frame's holes goes onto the screen as the game draws it (AlsoOnScreen), with
//   its tile's extras; in the front picture it only clears what's under it (EraseInFront, Cut blend), so a partly
//   see-through floor isn't counted twice. A hole then shows the game's own floor from the screen. Floor is never
//   redrawn later.
// - At the end of the pass (WorldDone), for each covered target, in drawing order:
//   * cover: the standing scenery in its circle drawn after it, or before it but in front of it (InFront, by map
//     position); only scenery drawn after it opens the hole;
//   * behind: everything else drawn in the circle since the split (the target, characters, scenery behind it), redrawn
//     in order with the tint it was drawn with; under it, the screen (what came before the split, and the floor);
//   * mask: the cover's silhouettes x the hole's shape (KeyholeTransparency at the centre, fading out) x its fade-in;
//   * the front picture becomes front x (1 - mask) + behind x mask.
//   Then the front picture goes onto the screen. Behind and mask are circle-sized.
// KeyholeSize scales the circle (100 % = 0.85 x the character's height as radius). The circles cut are kept in Holes so
// ShowCovered.Draw erases the other effects there.
static class Keyhole
{
    sealed class Target { public bym Owner; public Rectangle Body; public bool HasBody; public int Last; }
    // A layer drawn after the split. Kind: 0 not drawn, 1 floor or ground, 2 standing scenery, 3 anything else.
    sealed class Drawn { public a2f Layer; public Rectangle Placed, Pixels; public byte Kind; public int Index; public Color? Tint; }
    sealed class Front { public Rectangle Area; public RenderTarget2D Target; }
    // Last frame's hole, for the early split: its circle and the target's map depth.
    struct LastHole { public Rectangle Circle; public Vector2 Map; }

    // This pass.
    static a7m pass; static cp9 passContext;
    static readonly List<Target> Targets = new List<Target>();
    static Target open;                        // the target whose layers are being drawn
    static int index;                          // layers seen this pass
    static bool split, splitNext, capturePass;
    static readonly List<Drawn> After = new List<Drawn>();
    static bool floorJustDrawn;                // a floor near a hole was drawn: its tile's extras (TileExtras) follow
    // The front picture, and the screen it stands in for.
    static readonly List<Front> Tiles = new List<Front>();
    static Front bound;                        // the tile the world pass draws into now
    static RenderTarget2D intoTarget; static Vector2 intoAt;     // the target UiScaleFix maps into, and its place on screen
    static RenderTargetBinding[] screen; static Viewport screenViewport; static Rectangle screenScissor;
    // Across frames, keyed by entity (a creature's bym is new every frame).
    static HashSet<object> covered = new HashSet<object>(), coveredNow = new HashSet<object>();
    static Dictionary<object, float> strength = new Dictionary<object, float>(), strengthNow = new Dictionary<object, float>();
    static List<LastHole> lastHoles = new List<LastHole>(), holesNow = new List<LastHole>();
    static readonly System.Diagnostics.Stopwatch clock = System.Diagnostics.Stopwatch.StartNew();
    static double lastDone;
    const float FadeSeconds = 0.2f;
    static float step, holeAlpha = 1f;
    // Drawing.
    static RenderTarget2D behind, mask;
    static SpriteBatch batch;
    static Texture2D hole, erase;
    static int holeStrength = -1;
    const int MaxTile = 2048;
    static int loggedTiles;
    static readonly bool Xna = typeof(GraphicsDevice).Assembly.GetName().Name == "Microsoft.Xna.Framework.Graphics";
    static readonly RasterizerState Clipped = new RasterizerState { ScissorTestEnable = true, CullMode = CullMode.None };
    // Reach-safe (colour and alpha the same): dest x source alpha, and dest x (1 - source alpha).
    static readonly BlendState Multiply = new BlendState
    {
        ColorSourceBlend = Blend.Zero, ColorDestinationBlend = Blend.SourceAlpha,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.SourceAlpha,
    };
    static readonly BlendState Cut = new BlendState
    {
        ColorSourceBlend = Blend.Zero, ColorDestinationBlend = Blend.InverseSourceAlpha,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.InverseSourceAlpha,
    };
    // The circles cut this frame (screen pixels), for ShowCovered.Draw to erase the covered effects there.
    internal static readonly List<Rectangle> Holes = new List<Rectangle>();

    static GraphicsDevice G { get { return DrawContexts.Device(passContext); } }
    static Rectangle ScreenRect { get { var g = G; return new Rectangle(0, 0, g.PresentationParameters.BackBufferWidth, g.PresentationParameters.BackBufferHeight); } }
    static object Who(bym owner) { return (object)owner.b ?? owner; }
    static float Strength(bym owner) { float v; return strength.TryGetValue(Who(owner), out v) ? v : 0f; }
    static bool IsTarget(bym owner) { return owner != null && ShowCovered.HasOutline(owner) && ShowCovered.EffectFor(owner) == CoveredEffect.Keyhole; }

    // Standing in front of others: not flat, not a character, not a shadow or marker.
    static bool Standing(a2f layer)
    {
        return !ShowCovered.Flat(layer) && ShowCovered.Character(layer.n) == null && !ShowCovered.NotBody(Layers.Id(layer));
    }

    // Map position (b0b.d). In this view a square is nearer the camera the larger its X and Y; by map position, not by
    // the sprite's bottom edge: walls are stacked pieces, and an upper piece's sprite ends higher on screen though it
    // stands on the same square.
    static Vector2 Map(bym owner)
    {
        var e = owner == null ? null : owner.b;
        if (e == null) return new Vector2(float.NaN);
        var d = Entities.MapPosition(e);
        return new Vector2(d.X, d.Y);
    }

    // A piece drawn before a target that still stands in front of it: in its circle, and in front by map position on
    // both axes (at least the target's square in X and in Y: X + Y alone also took pieces diagonally beside it, along a
    // wall running south-west to north-east). The target's own square counts (Floor): a walking character between
    // squares is drawn after the wall piece on the square it's leaving, which stands in front of it.
    static bool InFront(a2f layer, Rectangle pixels, Rectangle circle, Vector2 at)
    {
        if (!pixels.Intersects(circle) || float.IsNaN(at.X)) return false;
        var own = Map(layer.n);
        if (float.IsNaN(own.X)) return false;
        return own.X > (float)Math.Floor(at.X) - 0.01f && own.Y > (float)Math.Floor(at.Y) - 0.01f;
    }

    // While a front tile (or behind, or the mask) is the render target, UiScaleFix draws the screen's pixels shifted by
    // its place.
    internal static bool Into(out Vector2 from)
    {
        from = intoAt;
        if (intoTarget == null || passContext == null) return false;
        var targets = G.GetRenderTargets();
        return targets.Length > 0 && targets[0].RenderTarget == intoTarget;
    }

    static Rectangle Circle(Target t, out Rectangle round)
    {
        var p = UiScaleFix.ToPixels(t.Body);
        int radius = Math.Max(16, (int)(p.Height * 0.85f * CoveredSettings.KeyholeSize / 100f));
        radius = Math.Min(radius, MaxTile / 2);
        round = new Rectangle(p.Center.X - radius, p.Center.Y - radius, radius * 2, radius * 2);
        return Rectangle.Intersect(round, ScreenRect);
    }

    // --- The world pass ---------------------------------------------------------------------------------------------

    // First layer of a world pass.
    internal static void Start(a7m view, cp9 context)
    {
        pass = view; passContext = context;
        capturePass = SaveScreenshot.Capturing(G);
        Targets.Clear(); After.Clear(); Holes.Clear();
        if (!capturePass) coveredNow.Clear();
        open = null; index = 0; split = false; splitNext = false; bound = null; intoTarget = null;
        floorJustDrawn = false;
    }

    // Every world layer, in drawing order (ShowCovered.Drawn), before it's drawn.
    internal static void Layer(a2f layer)
    {
        int i = index++;
        var owner = layer.n;
        if (open != null && open.Owner == owner) { Grow(open, layer, i); return; }
        if (open != null)
        {
            if (!split && open.HasBody && (covered.Contains(Who(open.Owner)) || Strength(open.Owner) > 0f)) splitNext = true;
            open = null;
        }
        if (IsTarget(owner))
        {
            open = Targets.Find(x => x.Owner == owner);
            if (open == null) { open = new Target { Owner = owner }; Targets.Add(open); }
            Grow(open, layer, i);
            return;
        }
        if (split || (!splitNext && !InFrontOfLastHole(layer))) return;
        if (SaveScreenshot.Capturing(G)) return;     // the save picture: no keyhole (its mapping is the picture's)
        Split();
    }

    static bool InFrontOfLastHole(a2f layer)
    {
        if (lastHoles.Count == 0 || layer.d == null || !layer.b || layer.u != null || !Standing(layer)) return false;
        var pixels = UiScaleFix.ToPixels(Layers.Bounds(layer));
        foreach (var h in lastHoles) if (InFront(layer, pixels, h.Circle, h.Map)) return true;
        return false;
    }

    static void Grow(Target t, a2f layer, int i)
    {
        t.Last = i;
        if (ShowCovered.NotBody(Layers.Id(layer))) return;     // the body only: not the shadow or markers
        var r = Layers.Bounds(layer);
        t.Body = t.HasBody ? Rectangle.Union(t.Body, r) : r;
        t.HasBody = true;
    }

    // From here on the world pass draws into the front picture.
    static void Split()
    {
        var g = G;
        var s = ScreenRect;
        bool tiled = (Xna && (s.Width > MaxTile || s.Height > MaxTile)) || CoveredSettings.KeyholeTiles;
        int nx = tiled ? (s.Width + MaxTile - 1) / MaxTile : 1, ny = tiled ? (s.Height + MaxTile - 1) / MaxTile : 1;
        int tw = (s.Width + nx - 1) / nx, th = (s.Height + ny - 1) / ny;
        if (nx * ny != loggedTiles)
        {
            loggedTiles = nx * ny;
            Log.Write($"keyhole: front picture in {nx * ny} tile(s) of {tw}x{th}" + (tiled ? (Xna ? " (Microsoft XNA above 2048)" : " (KeyholeTiles=1)") : ""));
        }
        int n = 0;
        for (int y = 0; y < ny; y++)
            for (int x = 0; x < nx; x++, n++)
            {
                var area = Rectangle.Intersect(new Rectangle(x * tw, y * th, tw, th), s);
                if (n == Tiles.Count) Tiles.Add(new Front());
                Tiles[n].Area = area;
                Tiles[n].Target = Sized(g, Tiles[n].Target, area.Width, area.Height, false);
            }
        while (Tiles.Count > n) { Tiles[Tiles.Count - 1].Target?.Dispose(); Tiles.RemoveAt(Tiles.Count - 1); }
        DrawContexts.EndBatch(passContext);
        screen = g.GetRenderTargets(); screenViewport = g.Viewport; screenScissor = g.ScissorRectangle;
        foreach (var t in Tiles) { g.Textures[0] = null; g.SetRenderTarget(t.Target); g.Clear(Color.Transparent); }
        split = true;
        BindTile(Tiles[0], false);
        WorldBatch(DepthStencilState.Default);
    }

    static void BindTile(Front t, bool restart)
    {
        var g = G;
        if (restart) DrawContexts.EndBatch(passContext);
        g.Textures[0] = null;     // XNA refuses a target still bound as a texture
        g.SetRenderTarget(t.Target);
        var clip = new Rectangle(screenScissor.X - t.Area.X, screenScissor.Y - t.Area.Y, screenScissor.Width, screenScissor.Height);
        g.ScissorRectangle = Rectangle.Intersect(clip, new Rectangle(0, 0, t.Area.Width, t.Area.Height));
        bound = t; intoTarget = t.Target; intoAt = new Vector2(t.Area.X, t.Area.Y);
        if (restart) WorldBatch(DepthStencilState.Default);
    }

    static void ToScreen()
    {
        var g = G;
        g.Textures[0] = null;
        if (screen == null || screen.Length == 0) g.SetRenderTarget(null); else g.SetRenderTargets(screen);
        g.Viewport = screenViewport;
        g.ScissorRectangle = screenScissor;
        bound = null; intoTarget = null;
    }

    static void WorldBatch(DepthStencilState depth) { WorldBatch(depth, BlendState.AlphaBlend); }
    static void WorldBatch(DepthStencilState depth, BlendState blend)
    {
        WorldViews.BeginWorldBatch(pass, passContext, SpriteSortMode.Deferred, blend, SamplerState.PointClamp, depth, Clipped);
    }

    // Draws into every front tile the area reaches with the Cut blend (the drawing's alpha clears the front), then the world
    // batch goes back to normal. draw(first): first = by the game's call, else again from the sprite.
    static void EraseInFront(Rectangle pixels, Action<bool> draw)
    {
        bool first = true;
        foreach (var t in Tiles)
        {
            if (Tiles.Count > 1 && !t.Area.Intersects(pixels)) continue;
            DrawContexts.EndBatch(passContext);
            if (bound != t) BindTile(t, false);
            WorldBatch(DepthStencilState.Default, Cut);
            try { draw(first); first = false; }
            finally { DrawContexts.EndBatch(passContext); WorldBatch(DepthStencilState.Default); }
        }
        if (first) draw(true);     // reaches no tile: as the game would
    }

    // Only floor near a hole needs the screen copy (each copy costs a target switch and two batch restarts). Last frame's
    // holes, a floor tile's size around them: a new hole fades in from 0, so its first frame doesn't need one.
    static bool NearHole(Rectangle pixels)
    {
        foreach (var h in lastHoles)
        {
            var r = h.Circle;
            r.Inflate(r.Width / 2, r.Height / 4);
            if (r.Intersects(pixels)) return true;
        }
        return false;
    }

    // Draws onto the screen, then back into the front tile that was bound. No depth test: the screen's depth buffer holds
    // what the game drew before the split.
    static void AlsoOnScreen(Action draw)
    {
        var was = bound;
        DrawContexts.EndBatch(passContext);
        ToScreen();
        WorldBatch(DepthStencilState.None);
        try { draw(); }
        finally { DrawContexts.EndBatch(passContext); BindTile(was ?? Tiles[0], false); WorldBatch(DepthStencilState.Default); }
    }

    // ShowCovered.DrawLayer: notes covered targets; after the split, draws the layer into every front tile it reaches
    // (true: drawn here).
    internal static bool Draw(a2f layer, cp9 context, bkx view)
    {
        bool drawable = layer.d != null && layer.b;
        var placed = drawable ? Layers.Bounds(layer) : Rectangle.Empty;
        if (!capturePass && drawable && Targets.Count > 0 && (open == null || open.Owner != layer.n) && layer.u == null)
        {
            bool standing = false, checkedStanding = false;
            foreach (var t in Targets)
            {
                if (t == open || !t.HasBody || !t.Body.Intersects(placed)) continue;
                if (!checkedStanding) { standing = Standing(layer); checkedStanding = true; }
                if (!standing) break;
                coveredNow.Add(Who(t.Owner));
            }
        }
        if (!split) return false;

        var pixels = UiScaleFix.ToPixels(placed);
        pixels.Inflate(2, 2);
        bool floor = drawable && ShowCovered.Flat(layer);
        byte kind = !drawable ? (byte)0 : floor ? (byte)1 : Standing(layer) ? (byte)2 : (byte)3;
        After.Add(new Drawn { Layer = layer, Placed = placed, Pixels = pixels, Kind = kind, Index = index - 1, Tint = drawable ? Layers.Tint(layer) : null });
        if (floor)
        {
            floorJustDrawn = layer.u == null && NearHole(pixels);
            if (floorJustDrawn)
            {
                // Onto the screen as drawn; in the front picture it only clears what's under it (Cut: front x (1 - its
                // alpha)). Drawn normally there too, a partly see-through floor counted twice once the front went over
                // the screen.
                AlsoOnScreen(() => layer.f3(context, view));
                EraseInFront(pixels, byGame => { if (byGame) layer.f3(context, view); else Again(layer, context); });
                return true;
            }
        }

        if (!drawable || Tiles.Count == 1) { layer.f3(context, view); return true; }
        bool first = true;
        foreach (var t in Tiles)
        {
            if (!t.Area.Intersects(pixels)) continue;
            if (bound != t) BindTile(t, true);
            if (first) { layer.f3(context, view); first = false; }
            else Again(layer, context);
        }
        if (first) layer.f3(context, view);
        return true;
    }

    // A layer drawn again, from its sprite as drawn: chf.avw without avv.
    static void Again(a2f layer, cp9 context)
    {
        var entity = layer.n == null ? null : layer.n.b;
        Layers.Draw(layer, context, Layers.Tint(layer) ?? Color.White);
    }

    // ShowCovered.TileExtras: what a7m draws over a tile right after its floor goes where that floor went (true: drawn here).
    internal static bool Tile(a7m view, cp9 context, ak1 tile)
    {
        if (!split) return false;
        // After a floor that went onto the screen: only there (its front copy is an eraser; drawn into the front as well,
        // a see-through overlay such as transition red counted twice).
        if (floorJustDrawn)
        {
            floorJustDrawn = false;
            AlsoOnScreen(() => WorldDraw.TileExtrasStep(view, context, tile));
            return true;
        }
        var area = Rectangle.Empty;
        if (Tiles.Count == 1) { WorldDraw.TileExtrasStep(view, context, tile); return true; }
        bool any = false;
        foreach (var t in Tiles)
        {
            if (!area.IsEmpty && !t.Area.Intersects(area)) continue;
            if (bound != t) BindTile(t, true);
            WorldDraw.TileExtrasStep(view, context, tile);
            any = true;
        }
        if (!any) WorldDraw.TileExtrasStep(view, context, tile);
        return true;
    }

    // a7m's world pass, after its loop (the world batch has been ended), right before it sets the screen again.
    internal static void Done()
    {
        open = null;
        if (capturePass) { Reset(); return; }     // the save picture's extra pass: leaves the holes' state alone
        var swap = covered; covered = coveredNow; coveredNow = swap; coveredNow.Clear();
        double now = clock.Elapsed.TotalSeconds;
        step = (float)Math.Max(0, Math.Min(0.1, now - lastDone)) / FadeSeconds;
        lastDone = now;
        try
        {
            if (split) CutHoles();
        }
        catch
        {
            // The world batch has ended here; the game sets the screen next. Put it back and close our own batch.
            try { batch?.End(); } catch { }
            try { ToScreen(); } catch { }
            throw;
        }
        finally
        {
            var s = strength; strength = strengthNow; strengthNow = s; strengthNow.Clear();
            var h = lastHoles; lastHoles = holesNow; holesNow = h; holesNow.Clear();
            Reset();
        }
    }

    // KeyholeDebug=1 in settings.ini: every few seconds, each layer in a hole with whether it was cut and why.
    static readonly System.Diagnostics.Stopwatch DebugClock = System.Diagnostics.Stopwatch.StartNew();
    static long debugLast;
    static bool debugNow;

    // KeyholeDebug=2: once, 30 s in, the pictures of one frame as PNGs in the game folder (keyhole-*.png).
    static bool dumped;
    static bool DumpNow { get { return CoveredSettings.KeyholeDumps && !dumped && DebugClock.ElapsedMilliseconds > 30000; } }
    static void Dump(string name, Texture2D t, Rectangle? part = null)
    {
        try
        {
            var r = part ?? new Rectangle(0, 0, t.Width, t.Height);
            var data = new Color[r.Width * r.Height];
            t.GetData(0, r, data, 0, data.Length);
            var copy = new Texture2D(t.GraphicsDevice, r.Width, r.Height);
            copy.SetData(data);
            using (var f = System.IO.File.Create(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "keyhole-" + name + ".png"))) copy.SaveAsPng(f, r.Width, r.Height);
            copy.Dispose();
        }
        catch (Exception ex) { UnderRailFixesMod.Note("dump " + name + " failed: " + ex.Message); }
    }

    static void CutHoles()
    {
        debugNow = CoveredSettings.KeyholeDebug && DebugClock.ElapsedMilliseconds - debugLast > 5000;
        if (debugNow) debugLast = DebugClock.ElapsedMilliseconds;
        bool dump = DumpNow && Targets.Count > 0;
        if (dump) for (int i = 0; i < Tiles.Count; i++) Dump("1-front-before-tile" + i, Tiles[i].Target);
        var g = G;
        if (batch == null || batch.GraphicsDevice != g) batch = new SpriteBatch(g);
        Targets.Sort((a, b) => a.Last.CompareTo(b.Last));
        foreach (var t in Targets)
        {
            if (!t.HasBody) continue;
            Rectangle round, circle = Circle(t, out round);
            if (circle.Width <= 0 || circle.Height <= 0) continue;
            var at = Map(t.Owner);
            var cover = new List<a2f>();
            var rest = new List<Drawn>();
            bool covered = false;
            foreach (var d in After)
            {
                if (d.Kind == 0 || d.Kind == 1 || !d.Pixels.Intersects(circle)) continue;     // floor: on the screen
                bool after = d.Index > t.Last;
                bool c = d.Kind == 2 && d.Layer.n != t.Owner && (after || InFront(d.Layer, d.Pixels, circle, at));
                // Only what's drawn after the target opens a hole: a piece on its own row beside it (InFront) overlaps its
                // sprite while walking along the front of a wall, and opened a hole there.
                if (c) { cover.Add(d.Layer); if (after && d.Placed.Intersects(t.Body)) covered = true; }
                else rest.Add(d);
                if (debugNow)
                {
                    var own = Map(d.Layer.n);
                    var tex = d.Layer.d == null ? "-" : d.Layer.d.GetType().Name;
                    UnderRailFixesMod.Note("keyhole layer " + d.Index + " (target last " + t.Last + ") kind " + d.Kind + " " + (c ? "CUT" : "kept")
                        + " map " + own.X.ToString("0.##") + "," + own.Y.ToString("0.##") + " (target " + at.X.ToString("0.##") + "," + at.Y.ToString("0.##") + ")"
                        + " id " + (Layers.Id(d.Layer) ?? "-") + " tex " + tex + " px " + d.Pixels + " entity " + (Layers.Entity(d.Layer) == null ? "-" : Layers.Entity(d.Layer).GetType().Name)
                        + (d.Layer.n == t.Owner ? " (target's own)" : ""));
                }
            }
            if (debugNow) UnderRailFixesMod.Note("keyhole circle " + circle + " cover " + cover.Count + " rest " + rest.Count);
            // Ease in while covered, out after; a fading-out hole is still cut.
            float alpha = Math.Max(0f, Math.Min(1f, Strength(t.Owner) + (covered ? step : -step)));
            if (alpha <= 0f) continue;
            strengthNow[Who(t.Owner)] = alpha;
            holesNow.Add(new LastHole { Circle = circle, Map = at });
            holeAlpha = alpha;
            Cut1(g, rest, cover, round, circle);
            if (dump)
            {
                Dump("2-mask", mask, new Rectangle(0, 0, circle.Width, circle.Height));
                Dump("3-behind", behind, new Rectangle(0, 0, circle.Width, circle.Height));
                foreach (var tile in Tiles) { var part = Rectangle.Intersect(tile.Area, circle); if (part.Width > 0) Dump("4-front-after", tile.Target, new Rectangle(part.X - tile.Area.X, part.Y - tile.Area.Y, part.Width, part.Height)); }
                UnderRailFixesMod.Note("dumped keyhole pictures, circle " + circle);
                dumped = true;
            }
            Holes.Add(round);
        }
        // The front picture onto the screen.
        ToScreen();
        batch.Begin(SpriteSortMode.Immediate, BlendState.AlphaBlend, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        foreach (var tile in Tiles) batch.Draw(tile.Target, tile.Area, Color.White);
        batch.End();
    }

    static void Reset()
    {
        split = false; bound = null; intoTarget = null; open = null; Targets.Clear(); After.Clear();
    }

    // After an error in the middle of the world pass: the screen back as the target and the world batch begun again there,
    // as the game expects (it goes on drawing; an un-begun batch would throw in the game's own Draw).
    internal static void Abort(bool midPass)
    {
        if (!split) { Reset(); return; }
        try { DrawContexts.EndBatch(passContext); } catch { }
        try { ToScreen(); } catch { }
        if (midPass) { try { WorldBatch(DepthStencilState.Default); } catch { } }
        Reset();
    }

    // --- One hole -------------------------------------------------------------------------------------------------

    static void Into(RenderTarget2D t, Rectangle area)
    {
        var g = G;
        g.Textures[0] = null;
        g.SetRenderTarget(t);
        g.ScissorRectangle = new Rectangle(0, 0, area.Width, area.Height);
        intoTarget = t; intoAt = new Vector2(area.X, area.Y);
    }

    // destination x source alpha (colour and alpha alike, as XNA's Reach profile needs).
    static readonly BlendState ByAlpha = new BlendState
    {
        ColorSourceBlend = Blend.Zero, ColorDestinationBlend = Blend.SourceAlpha,
        AlphaSourceBlend = Blend.Zero, AlphaDestinationBlend = Blend.SourceAlpha,
    };

    static void Cut1(GraphicsDevice g, List<Drawn> rest, List<a2f> cover, Rectangle round, Rectangle circle)
    {
        behind = Sized(g, behind, circle.Width, circle.Height, true);
        mask = Sized(g, mask, circle.Width, circle.Height, true);
        var size = new Rectangle(0, 0, circle.Width, circle.Height);
        var local = new Rectangle(round.X - circle.X, round.Y - circle.Y, round.Width, round.Height);

        // Behind: the rest, in order, with the tint each was drawn with (a7m fades layers flagged a2f.q after drawing
        // them, for its own second pass).
        Into(behind, circle);
        g.Clear(Color.Transparent);
        if (rest.Count > 0)
        {
            WorldBatch(DepthStencilState.Default);
            foreach (var d in rest)
            {
                var drawnTint = Layers.Tint(d.Layer);
                Layers.SetTint(d.Layer, d.Tint);
                try { if (d.Layer.d is e4m) d.Layer.f3(passContext, pass); else Again(d.Layer, passContext); }     // characters: no by8 rebuild
                finally { Layers.SetTint(d.Layer, drawnTint); }
            }
            DrawContexts.EndBatch(passContext);
        }

        // Mask: the cover's silhouettes, four times (a sprite's soft edge pixels, alpha a, count as almost fully covered:
        // 1 - (1 - a)^4; once, the edges of what stands in front still showed at 100%), x the hole x its fade-in. Drawn
        // with the world pass's own batch (lighting shader and transform), so each silhouette covers exactly the pixels
        // its sprite did: a plain batch rounds positions differently, up to a game unit off, which left the very edge
        // of walls (their top highlight) uncut at any transparency. Only the mask's alpha is used (the shader tints it).
        Into(mask, circle);
        g.Clear(Color.Transparent);
        WorldBatch(DepthStencilState.None);
        for (int n = 0; n < 4; n++)
            foreach (var layer in cover) ShowCovered.Silhouette(passContext, layer);
        DrawContexts.EndBatch(passContext);
        g.ScissorRectangle = size;
        batch.Begin(SpriteSortMode.Immediate, Multiply, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
        batch.Draw(Hole(g), local, Color.White * holeAlpha);
        batch.End();

        // Behind x mask; then in the front picture: front x (1 - mask) + behind x mask (premultiplied: Cut, then Additive).
        Into(behind, circle);
        batch.Begin(SpriteSortMode.Immediate, ByAlpha, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
        batch.Draw(mask, size, size, Color.White);
        batch.End();
        foreach (var tile in Tiles)
        {
            var part = Rectangle.Intersect(tile.Area, circle);
            if (part.Width <= 0 || part.Height <= 0) continue;
            var dest = new Rectangle(part.X - tile.Area.X, part.Y - tile.Area.Y, part.Width, part.Height);
            var src = new Rectangle(part.X - circle.X, part.Y - circle.Y, part.Width, part.Height);
            g.Textures[0] = null;
            g.SetRenderTarget(tile.Target);
            g.ScissorRectangle = new Rectangle(0, 0, tile.Area.Width, tile.Area.Height);
            batch.Begin(SpriteSortMode.Immediate, Cut, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            batch.Draw(mask, dest, src, Color.White);
            batch.End();
            batch.Begin(SpriteSortMode.Immediate, BlendState.Additive, SamplerState.PointClamp, DepthStencilState.None, RasterizerState.CullNone);
            batch.Draw(behind, dest, src, Color.White);
            batch.End();
        }
        ToScreen();
    }

    // PreserveContents: these are switched away from and back to, and FNA clears DiscardContents targets then.
    static RenderTarget2D Sized(GraphicsDevice g, RenderTarget2D t, int w, int h, bool atLeast)
    {
        if (t != null && !t.IsDisposed && (atLeast ? t.Width >= w && t.Height >= h : t.Width == w && t.Height == h)) return t;
        t?.Dispose();
        return new RenderTarget2D(g, w, h, false, SurfaceFormat.Color, DepthFormat.None, 0, RenderTargetUsage.PreserveContents);
    }

    // Alpha = how much of what stands in front is removed: KeyholeTransparency at the centre, falling smoothly to 0 at
    // the edge.
    static Texture2D Hole(GraphicsDevice g)
    {
        int strengthSetting = CoveredSettings.KeyholeTransparency;
        if (hole != null && !hole.IsDisposed && holeStrength == strengthSetting) return hole;
        hole?.Dispose();
        float see = strengthSetting / 100f;
        hole = Round(g, t => see * (1 - t));
        holeStrength = strengthSetting;
        return hole;
    }

    // Alpha = how much of a covered effect stays: 0 at the centre, rising smoothly to 1 at the edge (the hole's shape).
    internal static Texture2D Erase(GraphicsDevice g)
    {
        if (erase != null && !erase.IsDisposed) return erase;
        erase = Round(g, t => t);
        return erase;
    }

    // A round texture: value(t), t = 0 inside 55 % of the radius rising smoothly to 1 at the edge.
    static Texture2D Round(GraphicsDevice g, Func<float, float> value)
    {
        const int size = 128;
        var data = new Color[size * size];
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float dx = (x + 0.5f) / size * 2 - 1, dy = (y + 0.5f) / size * 2 - 1;
                float t = Math.Max(0f, Math.Min(1f, ((float)Math.Sqrt(dx * dx + dy * dy) - 0.55f) / 0.45f));
                float v = value(t * t * (3 - 2 * t));
                data[y * size + x] = new Color(v, v, v, v);
            }
        var texture = new Texture2D(g, size, size);
        texture.SetData(data);
        return texture;
    }
}
