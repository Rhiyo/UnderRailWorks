// UnderRailWorks: tile marks (coloured tiles on the floor: a range, an area, a path) and map-to-screen positions. The
// composing part of the drawing API: marks are drawn in each tile's extras step, where the game draws its own tile
// grid, so they lie on the floor under characters and scenery, and Wall Vision's keyhole carries them into its picture
// like the game's own. Every mod's marks show together.
using System;
using System.Collections.Generic;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public enum TileMarkStyle { Fill = 0, Outline = 1 }

// A mod's set of marked tiles in the area on screen (tile coordinates as World.HoveredTile; a character's tile is its Position rounded down). Keep one
// per kind of mark; it stays until changed or cleared (a zone change doesn't clear it), and goes with its mod.
public sealed class TileMarks
{
    readonly string mod;
    Dictionary<Point, Color> tiles = new Dictionary<Point, Color>();
    bool shown = true;
    TileMarkStyle style;

    public TileMarks(WorksMod mod)
    {
        if (mod == null) throw new ArgumentNullException("mod");
        this.mod = mod.Id;
        lock (all) { all.Add(this); Publish(); }
    }

    // A colour per tile; its alpha is the mark's strength (Color * 0.5f: half). Set replaces that tile's mark.
    public void Set(Point tile, Color colour) { Change(d => d[tile] = colour); }
    public void Set(IEnumerable<Point> tiles, Color colour) { if (tiles == null) return; Change(d => { foreach (var t in tiles) d[t] = colour; }); }
    // All marks replaced by these at once.
    public void Replace(IEnumerable<Point> tiles, Color colour)
    {
        var d = new Dictionary<Point, Color>();
        if (tiles != null) foreach (var t in tiles) d[t] = colour;
        this.tiles = d;
    }
    public void Remove(Point tile) { Change(d => d.Remove(tile)); }
    public void Clear() { tiles = new Dictionary<Point, Color>(); }
    public int Count { get { return tiles.Count; } }
    public bool Has(Point tile) { return tiles.ContainsKey(tile); }
    // Hidden marks are kept, not drawn.
    public bool Shown { get { return shown; } set { shown = value; } }
    // Fill: a soft fill with a bright edge; Outline: the edge only.
    public TileMarkStyle Style { get { return style; } set { style = value; } }

    // Copy on write: the world pass reads the dictionary while a mod changes it from its update.
    void Change(Action<Dictionary<Point, Color>> change)
    {
        var d = new Dictionary<Point, Color>(tiles);
        change(d);
        tiles = d;
    }

    static readonly List<TileMarks> all = new List<TileMarks>();
    static TileMarks[] snapshot = new TileMarks[0];
    static void Publish() { snapshot = all.ToArray(); }
    sealed class Owner : IModOwned { public void RemoveOwner(string mod) { Forget(mod); } }
    static TileMarks() { lock (ModScope.Hooks) ModScope.Hooks.Add(new Owner()); }
    internal static void Forget(string mod) { lock (all) { all.RemoveAll(m => string.Equals(m.mod, mod, StringComparison.OrdinalIgnoreCase)); Publish(); } }

    // The game's tile size at zoom 1, half of it each way (a7m.as, an esj: the grid texture's source rectangle is
    // as.c * 2 by as.d * 2, and vq projects with it). The same for every view; read once.
    static readonly System.Reflection.FieldInfo SizeField = typeof(a7m).GetField("as", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic),
        SizeW = typeof(esj).GetField("c"), SizeH = typeof(esj).GetField("d");     // fields share names with methods
    static int halfW, halfH;
    internal static bool Size(a7m view, out int hw, out int hh)
    {
        if (halfW == 0 && view != null)
        {
            var size = SizeField.GetValue(view);
            halfW = (int)SizeW.GetValue(size); halfH = (int)SizeH.GetValue(size);
        }
        hw = halfW; hh = halfH;
        return hw > 0 && hh > 0;
    }

    static Texture2D fill, outline;
    static bool broken;
    static Texture2D Make(GraphicsDevice device, bool filled, int hw, int hh)
    {
        int w = hw * 2, h = hh * 2;
        var data = new Color[w * h];
        float cx = w / 2f, cy = h / 2f;
        for (int y = 0; y < h; y++)
            for (int x = 0; x < w; x++)
            {
                // 1 at the diamond's edge, 0 at its middle; the edge band ~1.5 px wide across the short side.
                float d = Math.Abs(x + 0.5f - cx) / cx + Math.Abs(y + 0.5f - cy) / cy;
                if (d > 1f) continue;
                float edge = (1f - d) * cy;     // px from the edge, roughly
                float a = edge < 1.5f ? 1f : filled ? 0.35f : 0f;
                if (a > 0) data[y * w + x] = Color.White * a;     // premultiplied
            }
        var t = new Texture2D(device, w, h);
        t.SetData(data);
        return t;
    }

    // a7m's tile-extras step, after the game's: this tile's marks, with the world batch (begun), as the game draws its grid.
    internal static void DrawOn(a7m view, cp9 context, ak1 tile)
    {
        var list = snapshot;
        if (list.Length == 0 || broken || tile == null) return;
        try
        {
            int hw, hh;
            if (!Size(view, out hw, out hh)) return;
            var p = tile.Location;
            Vector2 at = Vector2.Zero; bool placed = false; float zoom = 1f;
            for (int i = 0; i < list.Length; i++)
            {
                var m = list[i];
                Color c;
                if (!m.shown || !m.tiles.TryGetValue(p, out c)) continue;
                if (!placed) { at = view.vq(p.X, p.Y, 0f); zoom = WorldViews.Zoom(view); placed = true; }
                var device = DrawContexts.Device(context);
                Texture2D t;
                if (m.style == TileMarkStyle.Outline) t = outline != null && !outline.IsDisposed ? outline : (outline = Make(device, false, hw, hh));
                else t = fill != null && !fill.IsDisposed ? fill : (fill = Make(device, true, hw, hh));
                context.b().w7(t, at, null, c, 0f, Vector2.Zero, zoom, SpriteEffects.None, 0f);
                WorldDraw.MarksDrawn++; if (Passes.InModPicture) WorldDraw.MarksInPicture++;
            }
        }
        catch (Exception ex) { broken = true; Log.Always("tile marks: " + ex.GetType().Name + ": " + ex.Message + " (off until restart)"); }
    }
}

public static partial class World
{
    // Where a map position is on screen, in interface units (as Ui elements and Character.ScreenBounds): a whole-number
    // position is the middle of that tile (a character's Position: the middle of the tile it stands on), Z lifts it.
    // Valid for the frame the world was last drawn in; null without a world on screen.
    public static Vector2? ToScreen(Vector3 position)
    {
        var v = View; int hw, hh;
        if (v == null || !TileMarks.Size(v, out hw, out hh)) return null;
        var z = WorldViews.Zoom(v);
        var at = v.vq(position.X, position.Y, position.Z);
        return new Vector2(at.X + hw * z, at.Y + hh * z);
    }
    // The rectangle a tile's floor diamond fills on screen (interface units); null without a world on screen.
    public static Rectangle? TileBounds(Point tile)
    {
        var v = View; int hw, hh;
        if (v == null || !TileMarks.Size(v, out hw, out hh)) return null;
        var z = WorldViews.Zoom(v);
        var at = v.vq(tile.X, tile.Y, 0f);
        return new Rectangle((int)at.X, (int)at.Y, (int)Math.Ceiling(hw * 2 * z), (int)Math.Ceiling(hh * 2 * z));
    }
}
}
