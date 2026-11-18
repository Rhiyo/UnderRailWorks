// Lantern: an example lighting mod for UnderRailWorks (docs/Drawing.md). It replaces the game's lighting with its own:
// light from the game's light sources and from a lantern the player carries, stopped by walls (shadows, worked out
// per tile), darker where no light reaches. Built only from Works' composing drawing API, so it works with every other
// mod's drawing and with Wall Vision's keyhole:
//   - Lights.GameLighting: the game's own lighting off while Lantern is on (things drawn at full light);
//   - Lights.Sources / Lights.Ambient and Area.KindAt: where light comes from, what blocks it;
//   - WorldDraw.LayerColour: every layer multiplied by the light on its tile (as the game itself lights);
//   - WorldDraw.PostProcess: a soft glow around each light, over the finished world.
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class LanternConfig : ModConfig
{
    [Header("Lantern")]
    [Label("Lantern lighting")] public bool On = true;
    [Label("Darkness where no light reaches (%)"), Range(0, 95), Increment(5), EnabledIf("On")] public int Darkness = 60;
    [Label("Lantern reach (tiles)"), Range(0, 12), Increment(1), EnabledIf("On")] public int Reach = 6;
    [Label("Glow around lights"), EnabledIf("On")] public bool Glow = true;
}

public sealed class Lantern : WorksMod
{
    LanternConfig config;
    // The light on each tile near the player (0-1 per channel), worked out a few times a second.
    Dictionary<Point, Vector3> light = new Dictionary<Point, Vector3>();
    Vector3 ambient = Vector3.One;
    readonly List<KeyValuePair<Vector2, Color>> glows = new List<KeyValuePair<Vector2, Color>>();
    Area area; readonly Dictionary<Point, bool> blocks = new Dictionary<Point, bool>();
    double since, sinceWalls;
    Texture2D blob;
    const int Near = 28;     // tiles around the player that get light worked out

    bool Active { get { return config.On && World.Player != null; } }

    public override void Load()
    {
        config = GetConfig<LanternConfig>();
        Lights.GameLighting(this, () => Active ? 0f : 1f);
        GameEvents.Update.Add(this, Tick);
        WorldDraw.LayerColour.Add(this, Colour);
        WorldDraw.PostProcess.Add(this, GlowEffect);
    }

    void Tick(double seconds)
    {
        since += seconds; sinceWalls += seconds;
        if (since < 0.1) return;
        since = 0;
        var p = World.Player;
        if (!Active || p == null) { light.Clear(); glows.Clear(); return; }
        var a = p.Area;
        if (a != area || sinceWalls > 2) { area = a; blocks.Clear(); sinceWalls = 0; }     // doors open and close
        if (a == null) return;

        float dark = 1f - config.Darkness / 100f;
        ambient = Lights.Ambient(a).ToVector3() * dark;
        var map = new Dictionary<Point, Vector3>();
        int px = (int)Math.Floor(p.Position.X), py = (int)Math.Floor(p.Position.Y);
        glows.Clear();
        foreach (var s in Lights.Sources(a))
        {
            if (s.Darkens || Vector2.Distance(s.Centre, new Vector2(px, py)) > Near + s.Radius) continue;
            Shine(map, s.Centre, Math.Max(1.5f, s.Radius), s.Colour.ToVector3());
            glows.Add(new KeyValuePair<Vector2, Color>(s.Centre, s.Colour));
        }
        if (config.Reach > 0) Shine(map, new Vector2(px + 0.5f, py + 0.5f), config.Reach + 0.5f, new Vector3(1f, 0.85f, 0.6f));
        light = map;
    }

    // Light from a point onto the tiles it can see, fading to nothing at its reach.
    void Shine(Dictionary<Point, Vector3> map, Vector2 from, float reach, Vector3 colour)
    {
        int fx = (int)Math.Floor(from.X), fy = (int)Math.Floor(from.Y), r = (int)Math.Ceiling(reach);
        for (int y = fy - r; y <= fy + r; y++)
            for (int x = fx - r; x <= fx + r; x++)
            {
                float d = Vector2.Distance(from, new Vector2(x + 0.5f, y + 0.5f));
                if (d > reach || !Sees(fx, fy, x, y)) continue;
                float f = 1f - d / reach; f *= f;
                var t = new Point(x, y);
                Vector3 had; map.TryGetValue(t, out had);
                map[t] = had + colour * f;
            }
    }

    // A straight line of tiles from a to b with no wall or door between (b itself may be one: a lit wall).
    bool Sees(int ax, int ay, int bx, int by)
    {
        int dx = Math.Abs(bx - ax), dy = Math.Abs(by - ay), sx = ax < bx ? 1 : -1, sy = ay < by ? 1 : -1, err = dx - dy, x = ax, y = ay;
        while (x != bx || y != by)
        {
            if ((x != ax || y != ay) && Blocks(x, y)) return false;
            int e2 = 2 * err;
            if (e2 > -dy) { err -= dy; x += sx; }
            if (e2 < dx) { err += dx; y += sy; }
        }
        return true;
    }
    bool Blocks(int x, int y)
    {
        var t = new Point(x, y);
        bool b;
        if (!blocks.TryGetValue(t, out b)) { var k = area.KindAt(x, y, false); blocks[t] = b = k == TileKind.Wall || k == TileKind.Door; }
        return b;
    }

    // Every layer of the world multiplied by the light on its tile (selection marks and outlines stay as they are).
    void Colour(Layer layer, ref Color colour)
    {
        if (!Active || light.Count == 0) return;
        var kind = layer.Kind;
        if (kind == LayerKind.Marker || kind == LayerKind.Outline || kind == LayerKind.Shield) return;
        var e = layer.Entity;
        if (e.IsNull) return;
        var at = e.MapPosition;
        Vector3 l;
        if (!light.TryGetValue(new Point((int)Math.Floor(at.X), (int)Math.Floor(at.Y)), out l)) l = Vector3.Zero;
        l = Vector3.Min(Vector3.One, l + ambient);
        var c = colour.ToVector4();
        colour = new Color(c.X * l.X, c.Y * l.Y, c.Z * l.Z, c.W);
    }

    // After the world (Wall Vision's keyhole included): a soft glow around each light, added over it.
    bool GlowEffect(WorldPicture picture)
    {
        if (!Active || !config.Glow || glows.Count == 0) return false;
        if (blob == null || blob.IsDisposed) blob = Blob(picture.Device, 64);
        picture.CopyInput();
        var tile = World.TileBounds(Point.Zero);
        float tilePixels = tile == null ? 64 : picture.ToPixels(tile.Value).Width;
        picture.Batch.Begin(SpriteSortMode.Deferred, BlendState.Additive, SamplerState.LinearClamp, DepthStencilState.None, RasterizerState.CullNone);
        foreach (var g in glows)
        {
            var at = picture.ToPixels(new Vector3(g.Key.X - 0.5f, g.Key.Y - 0.5f, 0));
            if (at == null) continue;
            float size = tilePixels * 2.5f;
            picture.Batch.Draw(blob, new Rectangle((int)(at.Value.X - size / 2), (int)(at.Value.Y - size / 4), (int)size, (int)(size / 2)), g.Value * 0.35f);
        }
        picture.Batch.End();
        return true;
    }

    // A round soft spot, white in the middle (premultiplied).
    static Texture2D Blob(GraphicsDevice device, int size)
    {
        var data = new Color[size * size];
        float r = size / 2f;
        for (int y = 0; y < size; y++)
            for (int x = 0; x < size; x++)
            {
                float d = Vector2.Distance(new Vector2(x + 0.5f, y + 0.5f), new Vector2(r, r)) / r;
                float a = d >= 1 ? 0 : (1 - d) * (1 - d);
                data[y * size + x] = Color.White * a;
            }
        var t = new Texture2D(device, size, size);
        t.SetData(data);
        return t;
    }
}
