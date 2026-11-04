// UnderRailWorks: readable names for the game's scrambled members (the drawing area of the naming map). Each name is
// checked once at startup against this game version (Check): a game update that moves one fails loudly in the log
// instead of quietly breaking mods. Static accessors, no allocations: these run for every layer of every frame.
// Members typed with XNA types are reached by reflection or compiled delegates (AGENTS.md, "XNA type identity").
using System;
using System.Collections.Generic;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// One sprite layer of the world pass (a2f): a creature's body, a wall piece, a floor tile, a shadow...
public static class Layers
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    internal static readonly FieldInfo BoundsField = typeof(a2f).GetField("m"), SourceField = typeof(a2f).GetField("c"),
        TintField = typeof(a2f).GetField("e"), RotationField = typeof(a2f).GetField("i"), FlipField = typeof(a2f).GetField("k"),
        IdField = typeof(a2f).GetField("a");     // these share names with methods, hence reflection

    // Where it is drawn (layout units; UiScaleFix.ToPixels gives screen pixels).
    public static Rectangle Bounds(a2f layer) { return (Rectangle)BoundsField.GetValue(layer); }
    // Its part of the texture.
    public static Rectangle Source(a2f layer) { return (Rectangle)SourceField.GetValue(layer); }
    // Its tint (null: none).
    public static Color? Tint(a2f layer) { return (Color?)TintField.GetValue(layer); }
    public static void SetTint(a2f layer, Color? tint) { TintField.SetValue(layer, tint); }
    public static float Rotation(a2f layer) { return (float)RotationField.GetValue(layer); }
    public static SpriteEffects Flip(a2f layer) { return (SpriteEffects)FlipField.GetValue(layer); }
    // Its id: "SHD" a shadow, "marker", "shield"..., or null for a body or a piece of scenery.
    public static string Id(a2f layer) { return IdField.GetValue(layer) as string; }
    // The layers of one entity this frame (bym: new every frame), and the entity itself.
    public static bym Owner(a2f layer) { return layer.n; }
    public static b0b Entity(a2f layer) { return layer.n == null ? null : layer.n.b; }
    // Floor or ground: flat on the floor (many floor tiles around a character are drawn after it).
    public static bool IsFlat(a2f layer) { var e = Entity(layer); return e != null && (Entities.IsFloor(e) || Entities.IsGround(e)); }
    // The texture's draw mode, as a2f.f3 passes it.
    public static byte DrawMode(a2f layer) { return (byte)(layer.n == null || !layer.n.f ? 1 : 2); }

    // The layer's sprite drawn as a2f.f3 draws it, with these values instead (chf.avw: context, dest, source, colour,
    // rotation, origin, flip, depth, mode, scale).
    public delegate void SpriteDraw(chf sprite, cp9 context, Rectangle dest, Rectangle source, Color colour, float rotation, Vector2 origin, SpriteEffects flip, float depth, byte mode, Vector2 scale);
    public static readonly SpriteDraw DrawSprite = CompileDraw();
    static SpriteDraw CompileDraw()
    {
        var method = typeof(chf).GetMethod("avw");
        var ps = method.GetParameters();
        var self = System.Linq.Expressions.Expression.Parameter(typeof(chf), "s");
        var args = new System.Linq.Expressions.ParameterExpression[ps.Length];
        for (int i = 0; i < ps.Length; i++) args[i] = System.Linq.Expressions.Expression.Parameter(ps[i].ParameterType, "a" + i);
        var all = new List<System.Linq.Expressions.ParameterExpression> { self };
        all.AddRange(args);
        return System.Linq.Expressions.Expression.Lambda<SpriteDraw>(System.Linq.Expressions.Expression.Call(self, method, args), all).Compile();
    }

    // The layer's own sprite with another colour (e.g. white for a silhouette) and its entity's scale.
    public static void Draw(a2f layer, cp9 context, Color colour)
    {
        var e = Entity(layer);
        DrawSprite(layer.d, context, Bounds(layer), Source(layer), colour, Rotation(layer), Vector2.Zero, Flip(layer), 0f, DrawMode(layer), e == null ? Vector2.One : Entities.Scale(e));
    }
}

// Things on the map (b0b): walls, props, creatures (c5x), floor tiles.
public static class Entities
{
    static readonly FieldInfo FloorField = typeof(b0b).GetField("f"), GroundField = typeof(b0b).GetField("g"), MapField = typeof(b0b).GetField("d"),
        ScaleXField = typeof(b0b).GetField("ac"), ScaleYField = typeof(b0b).GetField("ad");

    public static bool IsFloor(b0b e) { return (bool)FloorField.GetValue(e); }
    public static bool IsGround(b0b e) { return (bool)GroundField.GetValue(e); }
    // Map position: X and Y in squares (larger = nearer the camera), Z height.
    public static Vector3 MapPosition(b0b e) { return (Vector3)MapField.GetValue(e); }
    public static Vector2 Scale(b0b e) { return new Vector2(Math.Max(0f, (float)ScaleXField.GetValue(e)), Math.Max(0f, (float)ScaleYField.GetValue(e))); }
    // A character: a c5x with stats (amc). Walls and props are c5x too.
    public static c5x Character(b0b e) { var c = e as c5x; return c != null && c.GetAspect<amc>() != null ? c : null; }
    public static bool IsPlayer(b0b e) { var c = Character(e); return c != null && c.IsPlayer; }
    // How a character stands towards the player (as eg0 decides the Tab outline's colour); None for non-characters.
    public static Attitude AttitudeOf(b0b e)
    {
        try
        {
            var c = Character(e);
            if (c == null) return Attitude.None;
            var who = c.GetAspect<amc>().aa();
            var game = bpo.GetService<cnk>();
            var party = game == null ? null : game.aqu();
            if (who == null || party == null) return Attitude.Neutral;
            var a = who.a(party.ac(), asp.a);
            return a == global::h.a ? Attitude.Hostile : a == global::h.b ? Attitude.Unfriendly : a == global::h.d ? Attitude.Friendly : a == global::h.e ? Attitude.Ally : Attitude.Neutral;
        }
        catch { return Attitude.Neutral; }
    }

    // The colour holding Tab outlines a character in, as eg0 picks it: gold for the player's character (character data
    // e3q), sandy brown for passive ones, else the game's colour for its attitude (adi.ae: red hostile, orange unfriendly,
    // grey neutral, green friendly, blue ally). (eg0 also uses white for the hovered or targeted one.)
    static readonly FieldInfo OutlineColours = typeof(adi).GetField("ae", BindingFlags.Static | BindingFlags.Public);
    public static Color OutlineColor(b0b e)
    {
        var c = Character(e);
        if (c != null && (c.IsPlayer || c.GetAspect<amc>().aa() is e3q)) return Color.Gold;
        if (c != null && c.GetAspect<amc>().aa() != null && c.GetAspect<amc>().aa().Passive) return Color.SandyBrown;
        var colours = OutlineColours.GetValue(null) as System.Collections.IDictionary;
        var a = AttitudeOf(e);
        object key = a == Attitude.Hostile ? global::h.a : a == Attitude.Unfriendly ? global::h.b : a == Attitude.Friendly ? global::h.d : a == Attitude.Ally ? global::h.e : (object)global::h.c;
        return colours != null && colours.Contains(key) ? (Color)colours[key] : Color.Gray;
    }

    // Fog of war: whether an observer (WorldViews.Observer) has discovered the entity (the shroud lifted there), and
    // whether it sees it right now (b0b.b / b0b.c, as the world view decides what to draw).
    public static bool Discovered(b0b e, long observer) { return e.b(observer); }
    public static bool SeenNow(b0b e, long observer) { return e.c(observer); }

    // Hostile or unfriendly to the player (red and orange outlines), not passive.
    public static bool IsHostile(b0b e)
    {
        try
        {
            var c = Character(e);
            var who = c == null ? null : c.GetAspect<amc>().aa();
            var game = bpo.GetService<cnk>();
            var party = game == null ? null : game.aqu();
            if (who == null || party == null || who.Passive) return false;
            var attitude = who.a(party.ac(), asp.a);
            return attitude == global::h.a || attitude == global::h.b;
        }
        catch { return false; }
    }
}

// How a character stands towards the player (the game's own levels, h.a-h.e).
public enum Attitude { None, Hostile, Unfriendly, Neutral, Friendly, Ally }

// Zones (c1a: a map file) and their areas (aud: one floor of it, a grid of tiles ak1), as the game holds them.
public static class Areas
{
    static readonly PropertyInfo AreaSize = typeof(aud).GetProperty("Size");
    static readonly PropertyInfo TriggerLocation = typeof(ya).GetProperty("Location"), TriggerSize = typeof(ya).GetProperty("Size");

    // The zone the party is in (null on the main menu).
    public static c1a CurrentZone() { var game = bpo.GetService<cnk>(); return game == null ? null : game.aqw(); }
    // The area an entity is on (null if none).
    public static aud Of(b0b entity) { return entity == null ? null : entity.Area; }
    public static int Width(aud area) { return UnderrailDropdownScroll.W((esj)AreaSize.GetValue(area, null)); }
    public static int Height(aud area) { return UnderrailDropdownScroll.H((esj)AreaSize.GetValue(area, null)); }
    // The tile at x, y (null outside the area) and what stands on it (floor, walls, props, characters, items).
    public static ak1 Tile(aud area, int x, int y) { return area.b(x, y); }
    public static IList<b0b> On(ak1 tile) { return tile == null ? (IList<b0b>)new b0b[0] : tile.Entities; }
    // Exits: area transitions within the zone (b0j, "ATT") and zone transitions to another map (cdo, "ZTT") whose
    // condition currently allows them (as the game decides whether to draw their tiles, a23), with their tile rectangles.
    public static IEnumerable<Exit> Exits(aud area)
    {
        foreach (var t in area.Triggers)
        {
            bool zone = t is cdo;
            if (!zone && !(t is b0j)) continue;
            bool open;
            try { open = zone ? (((cdo)t).c() == null || ((cdo)t).c().jh(null)) : (((b0j)t).Condition == null || ((b0j)t).Condition.jh(null)); }
            catch { open = true; }
            if (!open) continue;
            object at = TriggerLocation.GetValue(t, null), size = TriggerSize.GetValue(t, null);
            yield return new Exit { ToOtherZone = zone, Tiles = new Rectangle(UnderrailDropdownScroll.X(at), UnderrailDropdownScroll.Y(at), UnderrailDropdownScroll.W((esj)size), UnderrailDropdownScroll.H((esj)size)) };
        }
    }

    // The colours the game draws transition tiles in (a23): blue to another area of the zone, red to another zone.
    public static readonly Color AreaExitColour = new Color(0, 0, 150), ZoneExitColour = new Color(150, 0, 0);
}

public struct Exit
{
    public Rectangle Tiles;
    public bool ToOtherZone;     // to another map (zone), else another area of this one
}

// The world view (a7m, a UI control: mv : a7m : dnn).
public static class WorldViews
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly FieldInfo ZoomField = typeof(dnn).GetField("y"), ShaderField = typeof(a7m).GetField("x", Any), ClipField = typeof(a7m).GetField("aq", BindingFlags.Static | BindingFlags.NonPublic);     // static
    static readonly MethodInfo BeginMethod = typeof(a7m).GetMethod("iu", new[] { typeof(cp9), typeof(SpriteSortMode), typeof(BlendState), typeof(SamplerState), typeof(DepthStencilState), typeof(RasterizerState) });

    public static float Zoom(a7m view) { return (float)ZoomField.GetValue(view); }
    // Fog of war on the world view: true while undiscovered parts are hidden (the shroud; dnn.h.a == edg.b), and whose
    // discoveries count (dnn.c.d; 1 without one, as a7m uses).
    static readonly FieldInfo FogField = typeof(dnn).GetField("h"), ObserverField = typeof(dnn).GetField("c");
    public static bool Shroud(a7m view) { var fog = FogField.GetValue(view) as bnd; return fog != null && fog.a == edg.b; }
    public static long Observer(a7m view) { var o = ObserverField.GetValue(view) as bag; return o == null ? 1 : o.d; }
    // Layers with their own shader, drawn after the world (key: the shader's name, e.g. "Outline").
    public static Dictionary<string, List<a2f>> ShaderLayers(a7m view) { return ShaderField.GetValue(view) as Dictionary<string, List<a2f>>; }
    // The view's clipping rasterizer state.
    public static RasterizerState Clip(a7m view) { return (RasterizerState)ClipField.GetValue(null); }
    // Starts the world batch the way the world pass does (lighting shader included).
    public static void BeginWorldBatch(a7m view, cp9 context, SpriteSortMode sort, BlendState blend, SamplerState sampler, DepthStencilState depth, RasterizerState rasterizer)
    {
        BeginMethod.Invoke(view, new object[] { context, sort, blend, sampler, depth, rasterizer });
    }
}

// The game's draw context (cp9) and its batches (ob).
public static class DrawContexts
{
    static readonly FieldInfo DeviceField = typeof(cp9).GetField("c"), ResourcesField = typeof(cp9).GetField("a");
    static readonly MethodInfo EndMethod = typeof(cp9).GetMethod("a", Type.EmptyTypes), ResumeMethod = typeof(cp9).GetMethod("a", new[] { typeof(RasterizerState) }),
        BeginMethod = typeof(ob).GetMethod("w3"), ShaderMethod = typeof(cmk).GetMethod("d3"), ParamsMethod = typeof(cmk).GetMethod("d4");

    public static GraphicsDevice Device(cp9 context) { return (GraphicsDevice)DeviceField.GetValue(context); }
    // Ends whichever batch is active.
    public static void EndBatch(cp9 context) { EndMethod.Invoke(context, null); }
    // Begins the plain batch again (as a7m.b ends).
    public static void ResumeBatch(cp9 context, RasterizerState rasterizer) { ResumeMethod.Invoke(context, new object[] { rasterizer }); }
    // Begins a game batch (ob.w3).
    public static void Begin(ob batch, SpriteSortMode sort, BlendState blend, SamplerState sampler, DepthStencilState depth, RasterizerState rasterizer, Effect effect)
    {
        BeginMethod.Invoke(batch, new object[] { sort, blend, sampler, depth, rasterizer, effect });
    }
    // A layer shader's effect (cmk.d3) and its parameters (cmk.d4), as the shader pass uses them.
    public static Effect ShaderEffect(cmk shader, cp9 context, a7m view) { return (Effect)ShaderMethod.Invoke(shader, new object[] { ResourcesField.GetValue(context), view }); }
    public static void ShaderParameters(cmk shader, Effect effect, object key, a7m view) { ParamsMethod.Invoke(shader, new object[] { effect, key, view }); }
}

static class GameNames
{
    // At startup: every game member Works looks up (any static FieldInfo / MethodInfo / PropertyInfo / delegate field in
    // this assembly) still resolves in this game version. A null one is a member that moved, or a lookup with the wrong
    // flags (1.x had two: a7m.aq is static, aw9.b() private): logged loudly instead of failing later in a mod.
    internal static void Check()
    {
        var missing = new List<string>();
        int ok = 0;
        foreach (var t in typeof(GameNames).Assembly.GetTypes())
        {
            if (t.ContainsGenericParameters || t.Name.Contains("<")) continue;
            foreach (var f in t.GetFields(BindingFlags.Static | BindingFlags.NonPublic | BindingFlags.Public))
            {
                if (f.Name.Contains("<") || !(typeof(MemberInfo).IsAssignableFrom(f.FieldType) || (typeof(Delegate).IsAssignableFrom(f.FieldType) && f.IsInitOnly))) continue;
                object v;
                try { v = f.GetValue(null); }
                catch (Exception ex) { missing.Add(t.Name + "." + f.Name + " (" + (ex.InnerException ?? ex).Message + ")"); continue; }
                if (v == null) missing.Add(t.Name + "." + f.Name); else ok++;
            }
        }
        if (missing.Count > 0) Log.Always("UnderRailWorks: game members not found in this game version: " + string.Join(", ", missing.ToArray()));
        else Log.Write("mods: " + ok + " game members checked");
    }
}
