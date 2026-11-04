// UnderRailWorks: the drawing area of the API in Works' own types, so mods never hold the game's (a7m, cp9, a2f...).
// The handles are structs around the game's objects: the world pass runs these for every layer of every frame, and
// structs cost no allocations. Equality is the game object's identity; Key gives it as an object for identity tables
// (ConditionalWeakTable); IsNull replaces "== null". What each member does in the game: GameNames.cs (Layers,
// WorldViews, DrawContexts) and UnderRailWorks.md, "Drawing".
using System;
using System.Collections.Generic;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;

// The world view (a7m): the map's drawing for one frame.
public struct WorldView : IEquatable<WorldView>
{
    internal readonly a7m Game;
    internal WorldView(a7m view) { Game = view; }
    public bool IsNull { get { return Game == null; } }
    public object Key { get { return Game; } }
    public float Zoom { get { return WorldViews.Zoom(Game); } }
    // The view's clipping rasterizer state.
    public RasterizerState Clip { get { return WorldViews.Clip(Game); } }
    // Starts the world batch the way the world pass does (lighting shader included).
    public void BeginWorldBatch(DrawContext context, SpriteSortMode sort, BlendState blend, SamplerState sampler, DepthStencilState depth, RasterizerState rasterizer)
    {
        WorldViews.BeginWorldBatch(Game, context.Game, sort, blend, sampler, depth, rasterizer);
    }
    // Layers with their own shader, drawn after the world (key: the shader's name, e.g. "Outline").
    public IDictionary<string, IList<Layer>> ShaderLayers
    {
        get
        {
            var all = WorldViews.ShaderLayers(Game);
            var result = new Dictionary<string, IList<Layer>>();
            if (all != null) foreach (var kv in all) { var list = new List<Layer>(kv.Value.Count); foreach (var l in kv.Value) list.Add(new Layer(l)); result[kv.Key] = list; }
            return result;
        }
    }
    public bool Equals(WorldView o) { return ReferenceEquals(Game, o.Game); }
    public override bool Equals(object o) { return o is WorldView && Equals((WorldView)o); }
    public override int GetHashCode() { return RuntimeHelpers.GetHashCode(Game); }
    public static bool operator ==(WorldView a, WorldView b) { return ReferenceEquals(a.Game, b.Game); }
    public static bool operator !=(WorldView a, WorldView b) { return !ReferenceEquals(a.Game, b.Game); }
}

// The game's draw context (cp9) for the frame.
public struct DrawContext : IEquatable<DrawContext>
{
    internal readonly cp9 Game;
    internal DrawContext(cp9 context) { Game = context; }
    public bool IsNull { get { return Game == null; } }
    public GraphicsDevice Device { get { return DrawContexts.Device(Game); } }
    // Ends whichever batch is active.
    public void EndBatch() { DrawContexts.EndBatch(Game); }
    // Begins the plain batch again (as the shader pass ends).
    public void ResumeBatch(RasterizerState rasterizer) { DrawContexts.ResumeBatch(Game, rasterizer); }
    // The batch the context draws with now (the world's while one is set).
    public GameBatch Batch { get { return new GameBatch(Game.b()); } }
    public bool Equals(DrawContext o) { return ReferenceEquals(Game, o.Game); }
    public override bool Equals(object o) { return o is DrawContext && Equals((DrawContext)o); }
    public override int GetHashCode() { return RuntimeHelpers.GetHashCode(Game); }
    public static bool operator ==(DrawContext a, DrawContext b) { return ReferenceEquals(a.Game, b.Game); }
    public static bool operator !=(DrawContext a, DrawContext b) { return !ReferenceEquals(a.Game, b.Game); }
}

// One of the game's sprite batches (ob).
public struct GameBatch
{
    internal readonly ob Game;
    internal GameBatch(ob batch) { Game = batch; }
    public bool IsNull { get { return Game == null; } }
    public void End() { Game.an(); }
    public void Begin(SpriteSortMode sort, BlendState blend, SamplerState sampler, DepthStencilState depth, RasterizerState rasterizer, Effect effect)
    {
        DrawContexts.Begin(Game, sort, blend, sampler, depth, rasterizer, effect);
    }
}

// A layer shader of the game (cmk), e.g. "Outline".
public struct LayerShader
{
    internal readonly cmk Game;
    internal LayerShader(cmk shader) { Game = shader; }
    public bool IsNull { get { return Game == null; } }
    public static LayerShader Find(string name) { return new LayerShader(b6l.a(name)); }
    // Its effect and its parameters for one layer, as the shader pass uses them.
    public Effect Effect(DrawContext context, WorldView view) { return DrawContexts.ShaderEffect(Game, context.Game, view.Game); }
    public void SetParameters(Effect effect, Layer layer, WorldView view) { DrawContexts.ShaderParameters(Game, effect, layer.Game, view.Game); }
}

// One sprite layer of the world pass (a2f): a creature's body, a wall piece, a floor tile, a shadow...
public struct Layer : IEquatable<Layer>
{
    internal readonly a2f Game;
    internal Layer(a2f layer) { Game = layer; }
    public bool IsNull { get { return Game == null; } }
    public object Key { get { return Game; } }
    // Where it is drawn (layout units; UiScaleFix.ToPixels gives screen pixels), its part of the texture.
    public Rectangle Bounds { get { return Layers.Bounds(Game); } }
    public Rectangle Source { get { return Layers.Source(Game); } }
    // Its tint (null: none).
    public Color? Tint { get { return Layers.Tint(Game); } }
    public void SetTint(Color? tint) { Layers.SetTint(Game, tint); }
    public float Rotation { get { return Layers.Rotation(Game); } }
    public SpriteEffects Flip { get { return Layers.Flip(Game); } }
    // Its id: "SHD" a shadow, "marker", "shield"..., or null for a body or a piece of scenery.
    public string Id { get { return Layers.Id(Game); } }
    // The layers of its entity this frame, and the entity.
    public LayerSet Owner { get { return new LayerSet(Layers.Owner(Game)); } }
    public MapObject Entity { get { return new MapObject(Layers.Entity(Game)); } }
    // It has a sprite and is shown (a2f.d, a2f.b): only these are drawn.
    public bool Drawable { get { return Game.d != null && (bool)Layers.ShownField.GetValue(Game); } }
    // Drawn with its own shader after the world (e.g. "Outline"; null for ordinary layers), and that shader's values.
    public string ShaderName { get { return Game.u; } }
    public object ShaderParameter(string name) { object v; return Game.v != null && Game.v.TryGetValue(name, out v) ? v : null; }
    // Floor or ground: flat on the floor.
    public bool IsFlat { get { return Layers.IsFlat(Game); } }
    // Its sprite with another colour (e.g. white for a silhouette).
    public void Draw(DrawContext context, Color colour) { Layers.Draw(Game, context.Game, colour); }
    // Drawn the game's way (a2f.f3), e.g. from a DrawLayer handler that draws it somewhere else.
    public void DrawAsGame(DrawContext context, LayerView view) { Game.f3(context.Game, view.Game); }
    // The same in the shader pass, where the world view itself is the view (a7m is a bkx).
    public void DrawAsGame(DrawContext context, WorldView view) { Game.f3(context.Game, view.Game); }
    // A plain sprite (e4m), not a character's composite (by8, which rebuilds its cached picture when drawn).
    public bool IsPlainSprite { get { return Game.d is e4m; } }
    // The sprite source's kind, for logs.
    public string SpriteKind { get { return Game.d == null ? "-" : Game.d.GetType().Name; } }
    public bool Equals(Layer o) { return ReferenceEquals(Game, o.Game); }
    public override bool Equals(object o) { return o is Layer && Equals((Layer)o); }
    public override int GetHashCode() { return RuntimeHelpers.GetHashCode(Game); }
    public static bool operator ==(Layer a, Layer b) { return ReferenceEquals(a.Game, b.Game); }
    public static bool operator !=(Layer a, Layer b) { return !ReferenceEquals(a.Game, b.Game); }
}

// An entity's layers for one frame (bym: new every frame).
public struct LayerSet : IEquatable<LayerSet>
{
    internal readonly bym Game;
    internal LayerSet(bym set) { Game = set; }
    public bool IsNull { get { return Game == null; } }
    public object Key { get { return Game; } }
    public MapObject Entity { get { return new MapObject(Game == null ? null : Game.b); } }
    // Tab would outline this entity this frame.
    public bool HasOutline { get { return Outlines.Has(Game); } }
    public bool Equals(LayerSet o) { return ReferenceEquals(Game, o.Game); }
    public override bool Equals(object o) { return o is LayerSet && Equals((LayerSet)o); }
    public override int GetHashCode() { return RuntimeHelpers.GetHashCode(Game); }
    public static bool operator ==(LayerSet a, LayerSet b) { return ReferenceEquals(a.Game, b.Game); }
    public static bool operator !=(LayerSet a, LayerSet b) { return !ReferenceEquals(a.Game, b.Game); }
}

// Something on the map (b0b): a wall piece, a prop, a creature, a floor tile.
public struct MapObject : IEquatable<MapObject>
{
    internal readonly b0b Game;
    internal MapObject(b0b e) { Game = e; }
    public bool IsNull { get { return Game == null; } }
    public object Key { get { return Game; } }
    public Vector3 MapPosition { get { return Entities.MapPosition(Game); } }
    public bool IsFlat { get { return Entities.IsFloor(Game) || Entities.IsGround(Game); } }
    // The character it is (null for walls, props, floors).
    public Character Character { get { return Entities.Character(Game) == null ? null : global::Character.Of(Game); } }
    public bool Equals(MapObject o) { return ReferenceEquals(Game, o.Game); }
    public override bool Equals(object o) { return o is MapObject && Equals((MapObject)o); }
    public override int GetHashCode() { return RuntimeHelpers.GetHashCode(Game); }
    public static bool operator ==(MapObject a, MapObject b) { return ReferenceEquals(a.Game, b.Game); }
    public static bool operator !=(MapObject a, MapObject b) { return !ReferenceEquals(a.Game, b.Game); }
}

// A map tile (ak1), as the world pass hands it to the tile-extras step.
public struct MapTile { internal readonly ak1 Game; internal MapTile(ak1 tile) { Game = tile; } }

// The view a layer is drawn for (bkx), passed through to DrawAsGame.
public struct LayerView { internal readonly bkx Game; internal LayerView(bkx view) { Game = view; } }
