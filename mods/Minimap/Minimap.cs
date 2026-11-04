// Minimap: an UnderRailWorks mod. A panel on the HUD shows the area around the player in the same isometric
// orientation as the screen (up-right on the minimap is up-right in the game): what the player has discovered (the
// game's shroud): floors, walls, props, doors, exits in the game's transition colours,
// the player as a gold dot and the characters the player can currently see as dots in the colour holding Tab gives
// them (the game's own outline colours). Characters the game isn't drawing (out of sight, stealthed) are not shown.
// It is rebuilt a few times a second, not every frame. Settings: Options -> Mods -> Minimap settings.
using System;
using System.Collections.Generic;
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
    euz hud;
    UiPanel panel;
    MapView view;
    double sinceDraw;
    b0b player;
    a7m worldView;
    readonly HashSet<b0b> seenNow = new HashSet<b0b>();
    List<b0b> seen = new List<b0b>();

    public override void Load()
    {
        config = GetConfig<MinimapConfig>();
        UiEvents.HudCreated.Add(this, root => { hud = root; Build(); });
        // Who the game draws this frame: the player and the characters in sight.
        WorldDraw.LayerStarting.Add(this, (worldView, context, layer, shaderPass) =>
        {
            if (shaderPass) return;
            var e = Layers.Entity(layer);
            var c = e == null ? null : Entities.Character(e);
            if (c == null) return;     // any of its layers (body, shadow...): it is being drawn
            if (c.IsPlayer) { player = e; this.worldView = worldView; } else seenNow.Add(e);
        });
        WorldDraw.WorldDone.Add(this, (worldView, context) => { seen = new List<b0b>(seenNow); seenNow.Clear(); });
        GameEvents.Update.Add(this, seconds =>
        {
            if (panel == null) return;
            if (Relayout) { Relayout = false; Build(); return; }
            if (Input.Pressed(KeyOf(config.Key))) Ui.Show(panel, !panel.Visible);
            sinceDraw += seconds;
            if (!panel.Visible || sinceDraw < 0.25) return;
            sinceDraw = 0;
            try
            {
                bool shroud = worldView != null && WorldViews.Shroud(worldView);
                view.Render(player, config.ShowCharacters ? seen : null, config.TilesAcross / 2, config.ShowExits, shroud, worldView == null ? 1 : WorldViews.Observer(worldView));
                if (view.Note != null) { Log.Info(view.Note); view.Note = null; }
            }
            catch (Exception ex) { Log.Warn("render failed: " + ex.Message); }
        });
    }

    void Build()
    {
        if (hud == null) return;
        if (panel != null) Ui.Remove(panel);
        int w = config.Size, h = config.Size / 2 + 12;
        panel = Ui.Panel(w, h);
        panel.Background = new Color(8, 8, 8) * (config.Opacity / 100f);
        view = new MapView { Size = new esj(w - 8, h - 8) };
        Ui.Add(panel, view, 4, 4);
        // Clear of the HUD's own parts (interface units, the same at every interface scale): the portrait and the icon bar
        // at the top, the side bar and the menu buttons in the bottom corners; the bottom edge level with the action bar's.
        bool left = config.Corner == Corner.TopLeft || config.Corner == Corner.BottomLeft, top = config.Corner == Corner.TopRight || config.Corner == Corner.TopLeft;
        int x = left ? (top ? 12 : 56) : Ui.Width(hud) - w - (top ? 12 : 52);
        int y = top ? (left ? 145 : 62) : Ui.Height(hud) - h - 12;
        Ui.Add(hud, panel, x, y);
        Ui.Show(panel, config.Show);
        sinceDraw = 1;
    }

    static Keys KeyOf(ToggleKey k)
    {
        switch (k) { case ToggleKey.F6: return Keys.F6; case ToggleKey.F8: return Keys.F8; case ToggleKey.F9: return Keys.F9; case ToggleKey.M: return Keys.M; default: return Keys.F7; }
    }
}

// The map itself: a texture rebuilt by Render, drawn over the control's area.
public sealed class MapView : euz
{
    Texture2D picture;
    aud lastArea;
    internal string Note;     // once per area, for the log
    GraphicsDevice device;
    Microsoft.Xna.Framework.Color[] pixels;
    int tw, th;


    static readonly Microsoft.Xna.Framework.Color Floor = new Microsoft.Xna.Framework.Color(70, 74, 70), Wall = new Microsoft.Xna.Framework.Color(150, 150, 140),
        AreaExit = Areas.AreaExitColour, ZoneExit = Areas.ZoneExitColour, Outline = Microsoft.Xna.Framework.Color.Black,
        Furniture = new Microsoft.Xna.Framework.Color(92, 96, 90), Door = new Microsoft.Xna.Framework.Color(140, 100, 60);

    // kind of each tile around the player: 0 nothing (or undiscovered), 1 floor, 2 wall, 3 exit to another area, 4 prop
    // (furniture, containers), 5 door, 6 exit to another map.
    static readonly System.Reflection.FieldInfo NameField = typeof(cae).GetField("b", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic);
    static string NameOf(c5x c) { return NameField.GetValue(c) as string; }
    // shroud: hide what the observer hasn't discovered yet (the game's fog of war is on).
    public void Render(b0b player, IList<b0b> characters, int radius, bool exits, bool shroud, long observer)
    {
        tw = Math.Max(16, Ui.Width(this)); th = Math.Max(8, Ui.Height(this));
        if (pixels == null || pixels.Length != tw * th) pixels = new Microsoft.Xna.Framework.Color[tw * th];
        Array.Clear(pixels, 0, pixels.Length);
        var area = Areas.Of(player);
        if (area != null)
        {
            var at = Entities.MapPosition(player);
            float cx = at.X + 0.5f, cy = at.Y + 0.5f;
            int r = radius + radius / 2 + 2, size = 2 * r + 1, ox = (int)cx - r, oy = (int)cy - r;
            var kind = new byte[size, size];
            int hidden = 0;
            int aw = Areas.Width(area), ah = Areas.Height(area);
            for (int i = 0; i < size; i++)
                for (int j = 0; j < size; j++)
                {
                    int x = ox + i, y = oy + j;
                    if (x < 0 || y < 0 || x >= aw || y >= ah) continue;
                    // Undiscovered parts stay dark under the shroud, as in the game: only what the player has seen counts.
                    byte k = 0;
                    foreach (var e in Areas.On(Areas.Tile(area, x, y)))
                    {
                        if (shroud && !Entities.Discovered(e, observer)) continue;
                        if (Entities.IsFloor(e) || Entities.IsGround(e)) { if (k == 0) k = 1; continue; }
                        var c = e as c5x;
                        if (c == null || Entities.Character(e) != null) continue;
                        // Solid objects (the cd0 aspect: walls, furniture, doors...): doors (cnb) have their own colour,
                        // unnamed ones are the walls (they form the rooms' outlines), named ones are props (desks,
                        // shelves, barrels) and are shown as floor, slightly lighter.
                        if (c.GetAspect<cnb>() != null) k = 5;
                        else if (c.GetAspect<cd0>() != null && k != 5) { if (string.IsNullOrEmpty(NameOf(c))) k = 2; else if (k != 2) k = 4; }
                    }
                    kind[i, j] = k;
                    if (shroud && k == 0) hidden++;
                }
            if (area != lastArea) { lastArea = area; Note = "area " + Areas.Width(area) + "x" + Areas.Height(area) + ", shroud " + (shroud ? "on: " + hidden + " of " + size * size + " tiles around the player hidden" : "off"); }
            // Exits in the game's own colours (blue: another area of this map, red: another map), only on discovered tiles.
            if (exits)
                foreach (var ex in Areas.Exits(area))
                    for (int x = ex.Tiles.Left; x < ex.Tiles.Right; x++)
                        for (int y = ex.Tiles.Top; y < ex.Tiles.Bottom; y++)
                        {
                            int i = x - ox, j = y - oy;
                            if (i >= 0 && j >= 0 && i < size && j < size && kind[i, j] != 0) kind[i, j] = ex.ToOtherZone ? (byte)6 : (byte)3;
                        }
            // Isometric: a map step in X goes right and down on screen, in Y left and down (as the game draws).
            // Screen offset (u, v) from the centre -> map offset: x - y = u / k, x + y = 2v / k.
            float k2 = tw / (2f * radius);
            for (int py = 0; py < th; py++)
                for (int px = 0; px < tw; px++)
                {
                    float u = (px - tw / 2f) / k2, v = 2 * (py - th / 2f) / k2;
                    int i = (int)Math.Floor(cx + (u + v) / 2) - ox, j = (int)Math.Floor(cy + (v - u) / 2) - oy;
                    if (i < 0 || j < 0 || i >= size || j >= size) continue;
                    byte k = kind[i, j];
                    if (k != 0) pixels[py * tw + px] = k == 1 ? Floor : k == 4 ? Furniture : k == 2 ? Wall : k == 5 ? Door : k == 6 ? ZoneExit : AreaExit;
                }
            if (characters != null)
                foreach (var c in characters)
                    if (Areas.Of(c) == area) Dot(Entities.MapPosition(c), cx, cy, k2, Entities.OutlineColor(c), 2);
            Dot(at, cx, cy, k2, Entities.OutlineColor(player), 3);     // the player last, on top (gold, as Tab)
        }
        if (picture == null || picture.IsDisposed || picture.Width != tw || picture.Height != th)
        {
            picture?.Dispose();
            if (device == null) return;     // not drawn yet
            picture = new Texture2D(device, tw, th);
        }
        picture.SetData(pixels);
    }

    // A round dot with a dark rim at a map position.
    void Dot(Vector3 map, float cx, float cy, float k2, Microsoft.Xna.Framework.Color colour, int radius)
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

    public override void iv(e6d time, cp9 context)
    {
        device = DrawContexts.Device(context);
        if (picture != null && !picture.IsDisposed) Ui.DrawTexture(context, picture, Ui.Bounds(this), Microsoft.Xna.Framework.Color.White);
        base.iv(time, context);
    }
}
