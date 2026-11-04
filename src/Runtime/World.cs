// UnderRailWorks: the game world for mods, in Works' own types. Mods never hold the game's scrambled classes: those
// change between game versions and need the game exe to compile against. What the game's data means (which objects are
// walls, what the shroud hides, which exits are open, who the player can see) is decided here, once, from the game's
// own code, so every mod agrees with the game and with each other.
//   World.Player              the player's character (null on the main menu)
//   World.Visible             the characters the game drew last frame (in sight, not stealthed), not the player
//   World.FogOfWar            the game's fog of war is on (undiscovered parts hidden under the shroud)
//   character.Area            its area: Width, Height, KindAt(x, y), Exits
//   character.Position, Attitude, OutlineColor (its Tab outline colour), Name, IsPlayer
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

// What a tile holds, as a map would show it.
public enum TileKind
{
    Nothing,    // no floor, or not discovered yet while the fog of war is on
    Floor,
    Wall,       // unnamed solid scenery: the rooms' outlines
    Prop,       // named solid objects standing on floor: desks, shelves, barrels, lockers
    Door,
}

public struct Exit
{
    public Rectangle Tiles;      // the transition's tiles (map units)
    public bool ToOtherZone;     // to another map (zone), else another area of this one
    // The colour the game draws its tiles in (a23): blue to another area, red to another map.
    public Color GameColour { get { return ToOtherZone ? World.ZoneExitColour : World.AreaExitColour; } }
}

public sealed class Character
{
    internal readonly b0b Entity;
    Character(b0b entity) { Entity = entity; }

    static readonly ConditionalWeakTable<b0b, Character> Wrappers = new ConditionalWeakTable<b0b, Character>();
    // One wrapper per game character, so == and dictionaries work across frames.
    internal static Character Of(b0b entity) { return entity == null ? null : Wrappers.GetValue(entity, e => new Character(e)); }

    public bool IsPlayer { get { return Entities.IsPlayer(Entity); } }
    public string Name { get { return Entities.Name(Entity); } }
    // Map position (tiles; fractional while moving).
    public Vector3 Position { get { return Entities.MapPosition(Entity); } }
    public Area Area { get { return Area.Of(Entities.AreaOf(Entity)); } }
    public Attitude Attitude { get { return Entities.AttitudeOf(Entity); } }
    // The colour holding Tab outlines it in (gold for the player, sandy brown if passive, else by attitude).
    public Color OutlineColor { get { return Entities.OutlineColor(Entity); } }
}

public sealed class Area
{
    internal readonly aud Game;
    Area(aud game) { Game = game; }
    static readonly ConditionalWeakTable<aud, Area> Wrappers = new ConditionalWeakTable<aud, Area>();
    internal static Area Of(aud area) { return area == null ? null : Wrappers.GetValue(area, a => new Area(a)); }

    public int Width { get { return Areas.Width(Game); } }
    public int Height { get { return Areas.Height(Game); } }

    // What tile x, y holds. With the fog of war on, only what the player has discovered counts (as the world view draws
    // it); discoveredOnly: false shows everything.
    public TileKind KindAt(int x, int y, bool discoveredOnly = true)
    {
        if (x < 0 || y < 0 || x >= Width || y >= Height) return TileKind.Nothing;
        bool shroud = discoveredOnly && World.FogOfWar;
        long observer = World.Observer;
        var kind = TileKind.Nothing;
        foreach (var e in Areas.On(Areas.Tile(Game, x, y)))
        {
            if (shroud && !Entities.Discovered(e, observer)) continue;
            if (Entities.IsFloor(e) || Entities.IsGround(e)) { if (kind == TileKind.Nothing) kind = TileKind.Floor; continue; }
            var solid = Entities.Solid(e);
            // Doors win over walls (a door stands in a wall's line), walls over props.
            if (solid == TileKind.Door) kind = TileKind.Door;
            else if (solid == TileKind.Wall && kind != TileKind.Door) kind = TileKind.Wall;
            else if (solid == TileKind.Prop && kind != TileKind.Door && kind != TileKind.Wall) kind = TileKind.Prop;
        }
        return kind;
    }

    // The exits the game currently allows (their condition holds).
    public IList<Exit> Exits { get { return new List<Exit>(Areas.Exits(Game)); } }
}

public static class World
{
    // The player's character, as the game finds it (cnk.aqu(): the player's data au2, its entity).
    public static Character Player
    {
        get
        {
            var game = bpo.GetService<cnk>();
            var data = game == null ? null : game.aqu();
            var player = data == null || data.v() == null ? null : data.v().b();
            return Character.Of(player);
        }
    }

    // The characters the game drew in the last frame, the player excepted: the ones in sight (stealthed or out of sight
    // characters aren't drawn). Updated at the end of each world pass.
    public static IList<Character> Visible { get { tracking = true; return visible; } }

    // The game's fog of war is on: undiscovered parts are hidden under the shroud.
    public static bool FogOfWar { get { var v = View; return v != null && WorldViews.Shroud(v); } }

    public static readonly Color AreaExitColour = new Color(0, 0, 150), ZoneExitColour = new Color(150, 0, 0);

    // --- Fed by the drawing hooks (WorldDrawHooks) ----------------------------------------------------------------
    // The world view: the in-game screen's map (ps.akw(), the game's own way to it; null on the main menu).
    static a7m View { get { var screen = bpo.GetService<ps>(); return screen == null ? null : screen.akw(); } }
    static readonly HashSet<b0b> drawing = new HashSet<b0b>();
    static IList<Character> visible = new Character[0];
    static bool tracking;     // only once a mod has asked for Visible (a lookup per drawn layer otherwise)
    internal static long Observer { get { var v = View; return v == null ? 1 : WorldViews.Observer(v); } }

    internal static void LayerDrawn(a2f layer)
    {
        if (!tracking) return;
        var e = Layers.Entity(layer);
        if (e != null && Entities.Character(e) != null && !Entities.IsPlayer(e)) drawing.Add(e);
    }

    internal static void WorldDone()
    {
        if (!tracking) return;
        var list = new List<Character>(drawing.Count);
        foreach (var e in drawing) list.Add(Character.Of(e));
        visible = list.AsReadOnly();
        drawing.Clear();
    }
}
