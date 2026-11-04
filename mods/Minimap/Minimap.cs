// Minimap: an UnderRailWorks mod. A panel on the HUD shows the area around the player in the same isometric
// orientation as the screen (up-right on the minimap is up-right in the game): what the player has discovered (the
// game's shroud): floors, walls, props, doors, exits in the game's transition colours,
// the player as a gold dot and the characters the player can currently see as dots in the colour holding Tab gives
// them (the game's own outline colours). Characters the game isn't drawing (out of sight, stealthed) are not shown.
// It is rebuilt a few times a second, not every frame. Settings: Options -> Mods -> Minimap settings.
// Built from Works' API only (World, Area, Character, Ui.Custom): it compiles without the game, so no scrambled names.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class MinimapConfig : ModConfig
{
    [Header("Minimap")]
    [Label("Show the minimap")] public bool Show = true;
    [Label("Size"), Range(120, 400), Increment(10), Tooltip("Width of the minimap in interface units (its height is half: the map is isometric).")]
    public int Size = 260;
    [Label("Tiles across"), Range(20, 120), Increment(5), Tooltip("How many tiles the minimap spans, side to side.")]
    public int TilesAcross = 50;
    [Label("Corner"), Tooltip("Bottom right: beside the action bar, left of the menu button (out of the way of what lies ahead).")]
    public Corner Corner = Corner.BottomRight;
    [Label("Background opacity"), Range(0, 100), Increment(5)] public int Opacity = 85;
    [Label("Show characters"), Tooltip("Characters the player can currently see, as dots in their Tab outline colour.")]
    public bool ShowCharacters = true;
    [Label("Show merchants"), Tooltip("Every merchant in the area as a small square (seen or not, as long as their spot is discovered).")]
    public bool ShowMerchants = true;
    [Label("Show exits"), Tooltip("Exits in the game's transition colours: blue to another area of this map, red to another map.")]
    public bool ShowExits = true;
    [Label("Key that shows or hides it")] public ToggleKey Key = ToggleKey.F7;

    public override void OnChanged() { Minimap.Relayout = true; }
}

public enum Corner { [Label("Top right")] TopRight, [Label("Bottom right")] BottomRight, [Label("Top left")] TopLeft, [Label("Bottom left")] BottomLeft }
public enum ToggleKey { F6, F7, F8, F9, M }

public sealed class Minimap : WorksMod
{
    internal static bool Relayout;
    MinimapConfig config;
    UiElement hud;
    UiPanel panel;
    readonly MapPicture map = new MapPicture();
    double sinceDraw;
    List<Character> merchants = new List<Character>();
    string merchantsFor;
    int sinceMerchants;

    public override void Load()
    {
        config = GetConfig<MinimapConfig>();
        UiEvents.HudCreated.Add(this, root => { hud = root; Build(); });
        GameEvents.Update.Add(this, seconds =>
        {
            if (panel == null) return;
            if (Relayout) { Relayout = false; Build(); return; }
            if (Input.Pressed(KeyOf(config.Key))) Ui.Show(panel, !panel.Visible);
            sinceDraw += seconds;
            if (!panel.Visible || sinceDraw < 0.25) return;
            sinceDraw = 0;
            if (config.ShowMerchants && (merchantsFor == null || sinceMerchants++ > 20)) { merchants = World.Characters.Where(c => c.IsMerchant && !c.IsPlayer).ToList(); merchantsFor = World.ZoneId; sinceMerchants = 0; }
            map.Update(World.Player, config.ShowCharacters ? World.Visible : null, config.TilesAcross / 2, config.ShowExits, config.ShowMerchants ? merchants : null);
            if (map.Note != null) { Log.Info(map.Note); map.Note = null; }
        });
    }

    void Build()
    {
        if (hud == null) return;
        if (panel != null) Ui.Remove(panel);
        int w = config.Size, h = config.Size / 2 + 12;
        panel = Ui.Panel(w, h);
        panel.Background = new Color(8, 8, 8) * (config.Opacity / 100f);
        Ui.Add(panel, Ui.Custom(w - 8, h - 8, map.Draw), 4, 4);
        map.Resize(w - 8, h - 8);
        // Clear of the HUD's own parts (interface units, the same at every interface scale): the portrait and the icon bar
        // at the top, the side bar and the menu buttons in the bottom corners; the bottom edge level with the action bar's.
        bool left = config.Corner == Corner.TopLeft || config.Corner == Corner.BottomLeft, top = config.Corner == Corner.TopRight || config.Corner == Corner.TopLeft;
        int x = left ? (top ? 12 : 56) : hud.Width - w - (top ? 12 : 52);
        int y = top ? (left ? 145 : 62) : hud.Height - h - 12;
        Ui.Add(hud, panel, x, y);
        Ui.Show(panel, config.Show);
        sinceDraw = 1;
    }

    static Keys KeyOf(ToggleKey k)
    {
        switch (k) { case ToggleKey.F6: return Keys.F6; case ToggleKey.F8: return Keys.F8; case ToggleKey.F9: return Keys.F9; case ToggleKey.M: return Keys.M; default: return Keys.F7; }
    }
}

// The map itself: pixels rebuilt by Update a few times a second, put into a texture and drawn by the custom element.
sealed class MapPicture
{
    internal string Note;     // once per area, for the log
    Texture2D picture;
    Color[] pixels;
    bool changed;
    int tw = 16, th = 8;
    Area lastArea;

    static readonly Color Floor = new Color(70, 74, 70), Wall = new Color(150, 150, 140), Prop = new Color(92, 96, 90),
        Door = new Color(140, 100, 60), Outline = Color.Black;

    public void Resize(int width, int height) { tw = Math.Max(16, width); th = Math.Max(8, height); }

    public void Update(Character player, IList<Character> characters, int radius, bool exits, IList<Character> merchants = null)
    {
        if (pixels == null || pixels.Length != tw * th) pixels = new Color[tw * th];
        Array.Clear(pixels, 0, pixels.Length);
        changed = true;
        var area = player == null ? null : player.Area;
        if (area == null) return;
        var at = player.Position;
        float cx = at.X + 0.5f, cy = at.Y + 0.5f;
        int r = radius + radius / 2 + 2, size = 2 * r + 1, ox = (int)cx - r, oy = (int)cy - r;
        // What each tile around the player shows (Works applies the shroud: undiscovered tiles are Nothing).
        var colour = new Color?[size, size];
        int hidden = 0;
        for (int i = 0; i < size; i++)
            for (int j = 0; j < size; j++)
            {
                var kind = area.KindAt(ox + i, oy + j);
                colour[i, j] = kind == TileKind.Floor ? Floor : kind == TileKind.Wall ? Wall : kind == TileKind.Prop ? Prop : kind == TileKind.Door ? Door : (Color?)null;
                if (kind == TileKind.Nothing && area.KindAt(ox + i, oy + j, false) != TileKind.Nothing) hidden++;
            }
        if (area != lastArea) { lastArea = area; Note = "area " + area.Width + "x" + area.Height + ", fog of war " + (World.FogOfWar ? "on: " + hidden + " tiles around the player still under the shroud" : "off"); }
        // Exits in the game's own colours, only where their tiles have been discovered.
        if (exits)
            foreach (var ex in area.Exits)
                for (int x = ex.Tiles.Left; x < ex.Tiles.Right; x++)
                    for (int y = ex.Tiles.Top; y < ex.Tiles.Bottom; y++)
                    {
                        int i = x - ox, j = y - oy;
                        if (i >= 0 && j >= 0 && i < size && j < size && colour[i, j] != null) colour[i, j] = ex.GameColour;
                    }
        // Isometric: a map step in X goes right and down on screen, in Y left and down (as the game draws).
        // Screen offset (u, v) from the centre -> map offset: x - y = u / k, x + y = 2v / k.
        float k2 = tw / (2f * radius);
        for (int py = 0; py < th; py++)
            for (int px = 0; px < tw; px++)
            {
                float u = (px - tw / 2f) / k2, v = 2 * (py - th / 2f) / k2;
                int i = (int)Math.Floor(cx + (u + v) / 2) - ox, j = (int)Math.Floor(cy + (v - u) / 2) - oy;
                if (i < 0 || j < 0 || i >= size || j >= size || colour[i, j] == null) continue;
                pixels[py * tw + px] = colour[i, j].Value;
            }
        if (characters != null)
            foreach (var c in characters)
                if (c.Area == area) Dot(c.Position, cx, cy, k2, c.OutlineColor, 2);
        // Merchants: a square in gold-brown over their dot, where the map around them is discovered.
        if (merchants != null)
            foreach (var m in merchants)
            {
                if (m.Area != area) continue;
                int mi = (int)m.Position.X - ox, mj = (int)m.Position.Y - oy;
                if (mi < 0 || mj < 0 || mi >= size || mj >= size || colour[mi, mj] == null) continue;
                Square(m.Position, cx, cy, k2, Merchant);
            }
        Dot(at, cx, cy, k2, player.OutlineColor, 3);     // the player last, on top (gold, as Tab)
    }

    static readonly Color Merchant = new Color(225, 175, 60);
    void Square(Vector3 map, float cx, float cy, float k2, Color colour)
    {
        float dx = map.X + 0.5f - cx, dy = map.Y + 0.5f - cy;
        int sx = (int)Math.Round(tw / 2f + (dx - dy) * k2), sy = (int)Math.Round(th / 2f + (dx + dy) * k2 / 2);
        for (int y = -4; y <= 4; y++)
            for (int x = -4; x <= 4; x++)
            {
                int px = sx + x, py = sy + y;
                if (px < 0 || py < 0 || px >= tw || py >= th) continue;
                pixels[py * tw + px] = Math.Abs(x) == 4 || Math.Abs(y) == 4 ? Outline : colour;
            }
    }

    // A round dot with a dark rim at a map position.
    void Dot(Vector3 map, float cx, float cy, float k2, Color colour, int radius)
    {
        float dx = map.X + 0.5f - cx, dy = map.Y + 0.5f - cy;
        int sx = (int)Math.Round(tw / 2f + (dx - dy) * k2), sy = (int)Math.Round(th / 2f + (dx + dy) * k2 / 2);
        for (int y = -radius - 1; y <= radius + 1; y++)
            for (int x = -radius - 1; x <= radius + 1; x++)
            {
                int px = sx + x, py = sy + y;
                if (px < 0 || py < 0 || px >= tw || py >= th) continue;
                int d2 = x * x + y * y;
                if (d2 <= radius * radius) pixels[py * tw + px] = colour;
                else if (d2 <= (radius + 1) * (radius + 1)) pixels[py * tw + px] = Outline;
            }
    }

    public void Draw(UiCanvas canvas)
    {
        if (pixels == null) return;
        if (picture == null || picture.IsDisposed || picture.Width != tw || picture.Height != th || picture.GraphicsDevice != canvas.Device)
        {
            if (picture != null) picture.Dispose();
            picture = new Texture2D(canvas.Device, tw, th);
            changed = true;
        }
        if (changed) { picture.SetData(pixels); changed = false; }
        canvas.DrawTexture(picture, canvas.Bounds, Color.White);
    }
}
