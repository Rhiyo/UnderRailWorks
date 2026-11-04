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
using System.Linq;
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
    // Hostile or unfriendly to the player, not passive (red and orange outlines).
    public bool IsHostile { get { return Entities.IsHostile(Entity); } }
    // Map position (tiles; fractional while moving).
    public Vector3 Position { get { return Entities.MapPosition(Entity); } }
    public Area Area { get { return Area.Of(Entities.AreaOf(Entity)); } }
    public Attitude Attitude { get { return Entities.AttitudeOf(Entity); } }
    // The colour holding Tab outlines it in (gold for the player, sandy brown if passive, else by attitude).
    public Color OutlineColor { get { return Entities.OutlineColor(Entity); } }
    // --- Character data (efk), as the game's own console commands read and change it ---------------------------
    efk Data { get { var c = Entities.Character(Entity); return c == null ? null : c.GetAspect<amc>().aa(); } }
    efk Need { get { var d = Data; if (d == null) throw new InvalidOperationException("not a character"); return d; } }

    // A character (has character data), not a placed object such as a turret's base or a prop.
    public bool IsCharacter { get { return Data != null; } }
    // It can be talked to (has the dialog aspect the game's Talk uses).
    public bool CanTalk { get { var c = Entity as c5x; return c != null && c.GetAspect<j7>() != null && IsCharacter && !IsDead; } }
    public string Id { get { return Need.Id; } }
    public string Name { get { var d = Data; return d == null ? Entities.Name(Entity) : d.LocalizedName ?? d.Name; } set { Need.Name = value; ((c5x)Entity).q(value); } }     // the data's name and the entity's (the hover label)
    public int Level { get { return Need.Level; } }
    public string Faction { get { return Need.FactionId; } set { Need.FactionId = value; } }
    public bool Passive { get { return Need.Passive; } set { Need.Passive = value; } }
    public bool IsDead { get { var d = Data; return d != null && d.Dead; } }
    public bool IsInCombat { get { return Need.IsInCombat; } }
    public void Kill() { Need.a((cwt)null, (cru)null, false); }

    public float Health { get { return Need.Defense.q().zo(); } set { Need.Defense.q().zp(value); } }
    public float MaxHealth { get { return Need.Defense.q().zm(); } }

    // Base abilities by name (Strength, Dexterity, Agility, Constitution, Perception, Will, Intelligence) and skills by
    // name (Guns, Persuasion, Mercantile...): the value with bonuses, and the base value, which can be set.
    public int Ability(string name) { var s = Need.BaseAbilities.a(name); return s == null ? 0 : s.g(); }
    public int BaseAbility(string name) { var s = Need.BaseAbilities.a(name); return s == null ? 0 : s.f(); }
    public void SetBaseAbility(string name, int value) { var s = Need.BaseAbilities.a(name); if (s == null) throw new ArgumentException("no ability " + name); s.a(value); Need.bl(); }
    public int Skill(string name) { var s = Need.Skills.a(name); return s == null ? 0 : s.g(); }
    public int BaseSkill(string name) { var s = Need.Skills.a(name); return s == null ? 0 : s.f(); }
    public void SetBaseSkill(string name, int value) { var s = Need.Skills.a(name); if (s == null) throw new ArgumentException("no skill " + name); ((TimelapseVertigo.Rules.Characters.Stat<int>)s).a(value); Need.bl(); }
    public IList<string> Feats { get { return Need.Feats.Select(f => f.Name).ToList(); } }
    public bool HasFeat(string name) { return Need.Feats.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase)); }

    // The player's XP (0 for others) and the game's way to award it (Intelligence factor, notice, level-up).
    public int Experience { get { var p = Data as e3q; return p == null ? 0 : p.i(); } }
    public void GiveExperience(int amount, string reason = null) { if (IsPlayer) awq.a(amount, reason); }

    public Inventory Inventory { get { return inventory ?? (inventory = new Inventory(() => Data)); } }
    Inventory inventory;

    // Its personal attitude to the player (overrides its faction's), as the game's scripts set it.
    public Attitude AttitudeToPlayer { get { return Attitude; } }
    public void SetAttitudeToPlayer(Attitude attitude)
    {
        if (attitude == Attitude.None) { Need.c(asp.a); return; }
        Need.a(asp.a, (h)((int)attitude - 1));
    }

    // Says a line over its head, as the game's barks do (c0s: floating speech; the game also puts it in the chat log
    // when the player can see the speaker). interrupt: replace what it is saying now. False if it can't speak (no
    // speech aspect: only characters Works placed get one added, game characters keep theirs as saved).
    public bool Say(string text, bool interrupt = true)
    {
        var c = Entity as c5x;
        if (c == null) return false;
        var speech = c.GetAspect<c0s>();
        if (speech == null && World.IsPlaced(c)) { speech = new c0s(c); c.SetAspect(speech); }
        if (speech == null) return false;
        SayMethod.Invoke(speech, new object[] { text, interrupt, null, null });
        return true;
    }
    static readonly System.Reflection.MethodInfo SayMethod = typeof(c0s).GetMethods().First(m => m.Name == "a" && m.GetParameters().Length == 4 && m.GetParameters()[0].ParameterType == typeof(string));

    // Its status effects (the game's br5), and the shop it runs (null if it isn't a merchant).
    internal br5 Effects { get { return Need.StatusEffects; } }
    public IList<StatusEffect> StatusEffects { get { var b = Need.StatusEffects; return b.d().Select(e => new StatusEffect(e, b)).ToList(); } }
    public bool HasStatusEffect(string code) { return Need.StatusEffects.d().Any(e => string.Equals(e.Code, code, StringComparison.OrdinalIgnoreCase)); }
    public bool IsMerchant { get { var c = Entity as c5x; return c != null && c.GetAspect<dvp>() != null; } }
    // Makes a placed character a merchant with one of the game's stores (data/Rules/Stores/<name>.store, e.g.
    // "bigbret"), as the game's scripts do (die: the merchant aspect, the store name, a restock). Only characters
    // Works placed (they are never saved); false otherwise.
    public bool MakeMerchant(string store)
    {
        var c = Entity as c5x;
        if (c == null || !World.IsPlaced(c)) return false;
        var m = c.GetAspect<dvp>();
        if (m == null) { m = new dvp(c); c.SetAspect(m); }
        // The barter screen also takes the merchant's dialog aspect (dka.amw: new aim(..., j7)): every game merchant has one.
        if (c.GetAspect<j7>() == null) c.SetAspect((j7)new bn5(c));
        m.b(store);
        m.aqc();
        return true;
    }

    // A merchant's stock (what the barter screen lists on their side), and what they charge the player per item value.
    public IList<Item> Stock { get { var c = Entity as c5x; var m = c == null ? null : c.GetAspect<dvp>(); return m == null ? new List<Item>() : m.ap7().z2().Select(i => new Item(i)).ToList(); } }
    public float Markup { get { var c = Entity as c5x; var m = c == null ? null : c.GetAspect<dvp>(); var p = World.Player; var pd = p == null ? null : ((c5x)p.Entity).GetAspect<amc>().aa(); return m == null || pd == null ? 1 : m.aqd(pd); } }

    // Its own variables (the game's per-character CustomProperties, saved with it).
    public Variables Variables { get { return variables ?? (variables = new Variables(() => { var c = Entities.Character(Entity); return c == null || c.GetAspect<amc>().aa() == null ? null : c.GetAspect<amc>().aa().CustomProperties; })); } }
    Variables variables;
}

public sealed class Area
{
    internal readonly aud Game;
    Area(aud game) { Game = game; }
    static readonly ConditionalWeakTable<aud, Area> Wrappers = new ConditionalWeakTable<aud, Area>();
    internal static Area Of(aud area) { return area == null ? null : Wrappers.GetValue(area, a => new Area(a)); }

    public string Name { get { return Game.y8(); } }
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

public static partial class World
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
