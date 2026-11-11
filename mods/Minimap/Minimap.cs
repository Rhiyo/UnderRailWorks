// Minimap: an UnderRailWorks mod. A panel on the HUD shows the area around the player in the same isometric
// orientation as the screen (up-right on the minimap is up-right in the game): what the player has discovered (the
// game's shroud): floors, walls, props, doors, exits in the game's transition colours,
// the player as a gold dot and the characters the player can currently see as dots in the colour holding Tab gives
// them (the game's own outline colours). Characters the game isn't drawing (out of sight, stealthed) are not shown.
// A white box shows what the screen is looking at; left-click or drag on the map to look elsewhere (as an area map
// does); right-click the minimap for a large map of the whole area (same box, same clicks).
// It is rebuilt a few times a second, not every frame. Settings: Options -> Mods -> Minimap settings.
// Built from Works' API only (World, Area, Character, Ui.Custom): it compiles without the game, so no scrambled names.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.4.0")]

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
    [Label("Large map: tiles across"), Range(0, 300), Increment(10), Tooltip("Click the minimap for a large map of the area: how many tiles it spans around the player.\n0: the whole area, centred.")]
    public int LargeTilesAcross = 0;
    [Label("Show markers"), Tooltip("Places mods mark (quest targets, notes): a diamond, at the map's edge when farther away.")] public bool ShowMarkers = true;
    [Label("Show merchants"), Tooltip("Every merchant in the area as a small square (seen or not, as long as their spot is discovered).")]
    public bool ShowMerchants = true;
    [Label("Show exits"), Tooltip("Exits in the game's transition colours: blue to another area of this map, red to another map.")]
    public bool ShowExits = true;

    public override void OnChanged() { Minimap.Relayout = true; }
}

public enum Corner { [Label("Top right")] TopRight, [Label("Bottom right")] BottomRight, [Label("Top left")] TopLeft, [Label("Bottom left")] BottomLeft }

public sealed class Minimap : WorksMod
{
    internal static bool Relayout;
    MinimapConfig config;
    Keybind toggleKey;
    UiElement hud;
    UiPanel panel;
    readonly MapPicture map = new MapPicture(), large = new MapPicture();
    UiWindow largeWindow;
    UiElement mapView, largeView;
    int floor = -1;     // the large map's area: an index into World.ZoneAreas (-1: the player's)
    MapPicture dragging;
    double sinceDraw;
    List<Character> merchants = new List<Character>();
    IList<MapMarker> markers = new List<MapMarker>();
    string markersFor; bool markersDirty = true;
    string merchantsFor;
    bool hidden;
    int sinceMerchants;

    public override void Load()
    {
        config = GetConfig<MinimapConfig>();
        Markers.Changed.Add(this, () => markersDirty = true);
        toggleKey = Keybinds.Register(this, "toggle", "Show/hide", Keys.F7);
        UiEvents.HudCreated.Add(this, root => { hud = root; Build(); });
        UiEvents.HudDestroyed.Add(this, () => hud = null);     // back to the main menu
        GameEvents.Update.Add(this, seconds =>
        {
            if (panel == null) return;
            if (Relayout) { Relayout = false; Build(); return; }
            if (toggleKey.Pressed) { hidden = panel.Visible; Ui.Show(panel, !hidden); }     // kept across HUD rebuilds (loads, resolution changes)
            // Like an area map: left-click (or drag) on the map looks there (on the player's own area); right-click on the minimap opens the large map.
            if (panel.Visible && Input.MousePressed(MouseButton.Right) && Ui.IsHot(panel)) { if (largeWindow != null && largeWindow.IsOpen) largeWindow.Close(); else OpenLarge(); }
            if (Input.MousePressed(MouseButton.Left))
                dragging = panel.Visible && mapView != null && Ui.IsHot(mapView) ? map : largeWindow != null && largeWindow.IsOpen && largeView != null && Ui.IsHot(largeView) && floor < 0 ? large : null;
            if (dragging != null)
            {
                if (!Input.MouseDown(MouseButton.Left)) dragging = null;
                else { LookAt(dragging, dragging == map ? mapView : largeView); sinceDraw = 1; }
            }
            sinceDraw += seconds;
            if (!panel.Visible || sinceDraw < 0.25) return;
            sinceDraw = 0;
            if (config.ShowMerchants && (merchantsFor != World.ZoneId || sinceMerchants++ > 20)) { merchants = World.Characters.Where(c => c.IsMerchant && !c.IsPlayer).ToList(); merchantsFor = World.ZoneId; sinceMerchants = 0; }
            if (config.ShowMarkers && (markersDirty || markersFor != World.ZoneId)) { markers = Markers.Here; markersFor = World.ZoneId; markersDirty = false; }
            map.Update(World.Player, config.ShowCharacters ? World.Visible : null, config.TilesAcross / 2, config.ShowExits, config.ShowMerchants ? merchants : null, config.ShowMarkers ? markers : null);
            if (largeWindow != null && largeWindow.IsOpen)
            {
                var areas = World.ZoneAreas;
                var a = floor >= 0 && floor < areas.Count ? areas[floor] : World.Player == null ? null : World.Player.Area;
                bool fit = config.LargeTilesAcross == 0 && a != null;
                // The whole area: an isometric map W x H spans W + H tiles across the screen; centred on its middle.
                large.Update(World.Player, config.ShowCharacters ? World.Visible : null, fit ? (a.Width + a.Height) / 2 + 2 : config.LargeTilesAcross / 2, config.ShowExits,
                    config.ShowMerchants ? merchants : null, config.ShowMarkers ? Markers.All.Where(m => m.Zone == World.ZoneId).ToList() : null,
                    fit ? new Vector3(a.Width / 2f, a.Height / 2f, 0) : (Vector3?)null, a);
            }
            if (map.Note != null) { Log.Info(map.Note); map.Note = null; }
        });
    }

    // Moves the view to the map position under the mouse on a picture.
    static void LookAt(MapPicture picture, UiElement view)
    {
        if (view == null) return;
        var b = view.Bounds; var m = Input.Mouse;
        var at = picture.ToMap((m.X - b.X) * (float)picture.Width / b.Width, (m.Y - b.Y) * (float)picture.Height / b.Height);
        if (at != null) Camera.Centre = at;
    }

    // The large map: the same picture over more of the area, in a window (drag it by the title, Escape closes it).
    void OpenLarge()
    {
        const int w = 800, h = 440;
        largeWindow = Ui.Window(this, "large", "Map: " + (World.Player != null && World.Player.Area != null ? World.Player.Area.Name : ""), w, h);
        if (largeWindow == null) return;
        int ph = h - largeWindow.Top - 8;
        largeView = Ui.Custom(w - 16, ph, large.Draw);
        Ui.Add(largeWindow, largeView, 8, largeWindow.Top + 2);
        // Other areas of this map (a building's floors, a station's levels): step through them.
        floor = -1;
        if (World.ZoneAreas.Count > 1)
        {
            var prev = Ui.Button("<", () => Step(-1)); var next = Ui.Button(">", () => Step(1));
            Ui.Add(largeWindow, prev, 14, largeWindow.Top + 8);
            Ui.Add(largeWindow, next, 14 + prev.Width + 6, largeWindow.Top + 8);
        }
        large.Resize(w - 16, ph);
        largeWindow.Closed += () => largeWindow = null;
        sinceDraw = 1;
    }

    void Step(int by)
    {
        var areas = World.ZoneAreas;
        if (areas.Count == 0 || largeWindow == null) return;
        int i = floor >= 0 ? floor : Math.Max(0, areas.IndexOf(World.Player.Area));
        floor = ((i + by) % areas.Count + areas.Count) % areas.Count;
        bool mine = areas[floor] == World.Player.Area;
        Ui.SetText(largeWindow, "Map: " + areas[floor].Name + (mine ? "" : "  (" + (floor + 1) + " of " + areas.Count + ")"));
        if (mine) floor = -1;
        sinceDraw = 1;
    }

    void Build()
    {
        if (hud == null) return;
        if (panel != null) Ui.Remove(panel);
        int w = config.Size, h = config.Size / 2 + 12;
        panel = Ui.Panel(w, h);
        panel.Background = new Color(8, 8, 8) * (config.Opacity / 100f);
        mapView = Ui.Custom(w - 8, h - 8, map.Draw);
        Ui.Add(panel, mapView, 4, 4);
        map.Resize(w - 8, h - 8);
        // Clear of the HUD's own parts (interface units, the same at every interface scale): the portrait and the icon bar
        // at the top, the side bar and the menu buttons in the bottom corners; the bottom edge level with the action bar's.
        bool left = config.Corner == Corner.TopLeft || config.Corner == Corner.BottomLeft, top = config.Corner == Corner.TopRight || config.Corner == Corner.TopLeft;
        var anchor = top ? (left ? HudAnchor.TopLeft : HudAnchor.TopRight) : (left ? HudAnchor.BottomLeft : HudAnchor.BottomRight);
        int dx = top ? 12 : left ? 56 : 52, dy = top ? (left ? 145 : 62) : 12;
        Hud.AddPart(this, "Minimap", panel, anchor, dx, dy);     // an anchored HUD part: layout mods can move it
        Ui.Show(panel, config.Show && !hidden);
        sinceDraw = 1;
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

    public void Update(Character player, IList<Character> characters, int radius, bool exits, IList<Character> merchants = null, IList<MapMarker> markers = null, Vector3? centre = null, Area show = null)
    {
        if (pixels == null || pixels.Length != tw * th) pixels = new Color[tw * th];
        Array.Clear(pixels, 0, pixels.Length);
        changed = true;
        var area = show ?? (player == null ? null : player.Area);
        if (area == null || player == null) return;
        bool here = area == player.Area;     // another floor: no player dot, no view box
        var at = player.Position;
        var mid = centre ?? at;
        float cx = mid.X + 0.5f, cy = mid.Y + 0.5f;
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
        // Markers: a diamond in the marker's colour; one off the map sits on its edge, in its direction.
        labels.Clear();
        if (markers != null)
            foreach (var mk in markers.Where(mk => mk.Area == null ? here : mk.Area == area.Name)) labels.Add(new KeyValuePair<Point, string>(Diamond(new Vector3(mk.X, mk.Y, 0), cx, cy, k2, mk.Colour), mk.Label));
        lastCx = cx; lastCy = cy; lastK2 = k2;
        if (!here) return;
        Dot(at, cx, cy, k2, player.OutlineColor, 3);     // the player last, on top (gold, as Tab)
        ViewBox(cx, cy, k2);
    }

    // What the screen shows: a white box. The world and the map share their isometric layout, so the screen's rectangle
    // is a rectangle here too: one map step moves Camera.TileWidth units on screen and k2 pixels here.
    void ViewBox(float cx, float cy, float k2)
    {
        var c = Camera.Centre; var view = Camera.ViewSize; float tw0 = Camera.TileWidth;
        if (c == null || tw0 <= 0 || view.X == 0) return;
        float dx = c.Value.X - cx, dy = c.Value.Y - cy, s = k2 / tw0;
        int mx = (int)Math.Round(tw / 2f + (dx - dy) * k2), my = (int)Math.Round(th / 2f + (dx + dy) * k2 / 2);
        int hw = (int)Math.Round(view.X / 2f * s), hh = (int)Math.Round(view.Y / 2f * s);
        for (int x = mx - hw; x <= mx + hw; x++) { Put(x, my - hh, Color.White); Put(x, my + hh, Color.White); }
        for (int y = my - hh; y <= my + hh; y++) { Put(mx - hw, y, Color.White); Put(mx + hw, y, Color.White); }
    }
    void Put(int x, int y, Color c) { if (x >= 0 && y >= 0 && x < tw && y < th) pixels[y * tw + x] = c; }

    // The map position under a picture pixel (the inverse of the drawing), for clicks.
    float lastCx, lastCy, lastK2;
    public Vector2? ToMap(float px, float py)
    {
        if (lastK2 <= 0) return null;
        float u = (px - tw / 2f) / lastK2, v = 2 * (py - th / 2f) / lastK2;
        return new Vector2(lastCx + (u + v) / 2 - 0.5f, lastCy + (v - u) / 2 - 0.5f);
    }
    public int Width { get { return tw; } }
    public int Height { get { return th; } }

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

    // Where each marker's diamond is (picture pixels) and its label: shown when the mouse rests on it.
    readonly List<KeyValuePair<Point, string>> labels = new List<KeyValuePair<Point, string>>();

    Point Diamond(Vector3 map, float cx, float cy, float k2, Color colour)
    {
        float dx = map.X + 0.5f - cx, dy = map.Y + 0.5f - cy;
        float fx = tw / 2f + (dx - dy) * k2, fy = th / 2f + (dx + dy) * k2 / 2;
        const int R = 5;
        bool off = fx < R || fy < R || fx > tw - 1 - R || fy > th - 1 - R;
        if (off)
        {
            // Along the line from the centre, to the edge (minus the diamond's size).
            float vx = fx - tw / 2f, vy = fy - th / 2f;
            float s = Math.Min(vx == 0 ? float.MaxValue : (tw / 2f - R - 1) / Math.Abs(vx), vy == 0 ? float.MaxValue : (th / 2f - R - 1) / Math.Abs(vy));
            fx = tw / 2f + vx * s; fy = th / 2f + vy * s;
        }
        int sx = (int)Math.Round(fx), sy = (int)Math.Round(fy), r = off ? R - 1 : R;
        for (int y = -r; y <= r; y++)
            for (int x = -r; x <= r; x++)
            {
                int d = Math.Abs(x) + Math.Abs(y), px = sx + x, py = sy + y;
                if (d > r || px < 0 || py < 0 || px >= tw || py >= th) continue;
                pixels[py * tw + px] = d == r ? Outline : colour;
            }
        return new Point(sx, sy);
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
        // A marker's label while the mouse is on its diamond (the picture is drawn at its own size).
        var b = canvas.Bounds; var m = Input.Mouse;
        if (!b.Contains(m)) return;
        foreach (var l in labels)
        {
            if (string.IsNullOrEmpty(l.Value) || Math.Abs(b.X + l.Key.X * b.Width / tw - m.X) > 6 || Math.Abs(b.Y + l.Key.Y * b.Height / th - m.Y) > 6) continue;
            int w = Ui.TextWidth(l.Value) + 10, x = Math.Max(b.X, Math.Min(b.Right - w, m.X - w / 2)), y = m.Y - 26;
            canvas.Fill(new Rectangle(x, y, w, 18), new Color(8, 8, 8, 230));
            canvas.Frame(new Rectangle(x, y, w, 18), new Color(181, 92, 0));
            canvas.Text(l.Value, x + 5, y + 1, new Color(230, 220, 200));
            break;
        }
    }
}
