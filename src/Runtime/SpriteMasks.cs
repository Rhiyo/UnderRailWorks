// UnderRailWorks: where a layer's sprite actually shows (not just its rectangle), for Layer.IsSolidAt / SolidOverlap.
// The game picks things under the mouse the same way (a7m.a(a2f, Point)): the point mapped into the layer's part of its
// texture, then the texture's own pixels (read once per texture), or where those can't be read the game's mask (cne.u0:
// ecg, 8-pixel cells, loaded with "Sprite Target Mask"; too coarse at edges).
// A character's composite (by8) is its base sprite plus up to two overlays (f, g), each with its own rectangle; its
// cached picture is a render target, so the parts are tested, as the game's picking does. A texture with neither a mask
// nor readable pixels counts as solid everywhere in the rectangle (the old rectangle test).
using System;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

namespace UnderRailWorks.Internal
{
static class SpriteMasks
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly FieldInfo BaseField = typeof(by8).GetField("d", Any), Over1 = typeof(by8).GetField("f", Any), Over2 = typeof(by8).GetField("g", Any);
    static readonly FieldInfo NoMask = typeof(ecg).GetField("a", Any);     // shares its name with a method

    internal static bool Solid(a2f layer, int x, int y)
    {
        if (layer == null || layer.d == null) return false;
        return Hit(layer.d, Layers.Bounds(layer), Layers.Source(layer), Layers.Flip(layer), x, y, 0);
    }

    static bool Hit(chf sprite, Rectangle dest, Rectangle source, SpriteEffects flip, int x, int y, int depth)
    {
        if (sprite == null || depth > 3) return false;
        var composite = sprite as by8;
        if (composite != null)
        {
            if (Hit(BaseField.GetValue(composite) as chf, dest, source, flip, x, y, depth + 1)) return true;
            foreach (var f in new[] { Over1, Over2 })
            {
                var part = f.GetValue(composite) as a2f;
                if (part != null && part.d != null && Hit(part.d, Layers.Bounds(part), Layers.Source(part), Layers.Flip(part), x, y, depth + 1)) return true;
            }
            return false;
        }
        if (dest.Width <= 0 || dest.Height <= 0 || source.Width <= 0 || source.Height <= 0) return false;
        if (x < dest.Left || x >= dest.Right || y < dest.Top || y >= dest.Bottom) return false;
        int fx = (int)((long)(x - dest.X) * source.Width / dest.Width), fy = (int)((long)(y - dest.Y) * source.Height / dest.Height);
        if ((flip & SpriteEffects.FlipHorizontally) != 0) fx = source.Width - 1 - fx;
        if ((flip & SpriteEffects.FlipVertically) != 0) fy = source.Height - 1 - fy;
        Texture2D texture;
        try { texture = sprite.a(); } catch { return true; }
        if (texture == null) return false;
        return Solid(texture, source.X + fx, source.Y + fy);
    }

    // One texture pixel: the texture's alpha (read once per texture), else the game's mask (8-pixel cells: too coarse at
    // edges, a railing beside a character's arm counted as over it), else solid.
    static bool Solid(Texture2D texture, int tx, int ty)
    {
        if (tx < 0 || ty < 0 || tx >= texture.Width || ty >= texture.Height) return false;
        var bits = Pixels(texture);
        if (bits != null) return bits.Get(ty * texture.Width + tx);
        var mask = Mask(texture);
        if (mask == null) return true;
        int cell = mask.c <= 0 ? 8 : mask.c;
        int cx = tx / cell, cy = ty / cell;
        if (cx >= mask.d || cy >= mask.e) return false;
        int bit = cy * mask.d + cx;
        return bit / 8 < mask.b.Length && (mask.b[bit / 8] & (128 >> (bit % 8))) != 0;
    }

    static ecg Mask(Texture2D texture)
    {
        try
        {
            var resources = bpo.GetService<cne>();
            var m = resources == null ? null : resources.u0(texture);
            return m == null || (bool)NoMask.GetValue(m) || m.b == null ? null : m;     // ecg.a: no mask file
        }
        catch { return null; }
    }

    sealed class Bits { public System.Collections.BitArray Value; }
    static readonly ConditionalWeakTable<Texture2D, Bits> read = new ConditionalWeakTable<Texture2D, Bits>();
    // Null: unreadable (a compressed format, too big, a render target): solid everywhere.
    static System.Collections.BitArray Pixels(Texture2D texture)
    {
        Bits b;
        if (read.TryGetValue(texture, out b)) return b.Value;
        b = new Bits();
        try
        {
            long n = (long)texture.Width * texture.Height;
            if (!(texture is RenderTarget2D) && texture.Format == SurfaceFormat.Color && n <= 4096L * 4096L)
            {
                var data = new Color[n];
                texture.GetData(data);
                var bits = new System.Collections.BitArray((int)n);
                for (int i = 0; i < data.Length; i++) if (data[i].A >= 32) bits[i] = true;
                b.Value = bits;
            }
        }
        catch { b.Value = null; }
        read.Add(texture, b);
        return b.Value;
    }

    // Solid points of both, every `step` layout units over where their rectangles meet.
    internal static int Overlap(a2f a, a2f b, int step)
    {
        if (a == null || b == null) return 0;
        var r = Rectangle.Intersect(Layers.Bounds(a), Layers.Bounds(b));
        if (r.Width <= 0 || r.Height <= 0) return 0;
        if (step < 1) step = 1;
        int n = 0;
        for (int y = r.Top + step / 2; y < r.Bottom; y += step)
            for (int x = r.Left + step / 2; x < r.Right; x += step)
                if (Solid(a, x, y) && Solid(b, x, y)) n++;
        return n;
    }
}
}
