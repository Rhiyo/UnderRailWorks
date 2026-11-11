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
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

// What a tile holds, as a map would show it.
public enum TileKind
{
    Nothing,    // no floor, or not discovered yet while the fog of war is on
    Floor,
    Wall,       // unnamed solid scenery: the rooms' outlines
    Prop,       // named solid objects standing on floor: desks, shelves, barrels, lockers
    Door,
}

public struct Resistance { public int Threshold { get; set; } public int Percent { get; set; } }

public struct Exit
{
    public Rectangle Tiles { get; set; }      // the transition's tiles (map units)
    public bool ToOtherZone { get; set; }     // to another map (zone), else another area of this one
    // The colour the game draws its tiles in (a23): blue to another area, red to another map.
    public Color GameColour { get { return ToOtherZone ? World.ZoneExitColour : World.AreaExitColour; } }
}

public sealed partial class Character
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
    // Keeps near leader (within distance tiles) out of combat; null stops it. For spawned allies, pets, escorts.
    public void Follow(Character leader, int distance = 3) { World.SetFollow(this, leader, distance); }
    // travels: a spawned follower of the player comes along to other zones (spawned again next to the player, with its
    // health; GameEvents.FollowerArrived gives the new character). Never saved: a loaded game has none.
    public void Follow(Character leader, int distance, bool travels) { World.SetFollow(this, leader, distance, travels); }
    public void StopFollowing() { World.SetFollow(this, null, 0); }
    public bool IsFollowing { get { return World.IsFollowing(this); } }
    public Area Area { get { return Area.Of(Entities.AreaOf(Entity)); } }
    // Where it was drawn on screen in the last frame (interface units, as Ui positions), or null (not drawn).
    public Rectangle? ScreenBounds { get { return World.ScreenBoundsOf(Entity); } }
    public Attitude Attitude { get { return Entities.AttitudeOf(Entity); } }
    // The colour holding Tab outlines it in (gold for the player, sandy brown if passive, else by attitude).
    public Color OutlineColor { get { return Entities.OutlineColor(Entity); } }
    // --- Character data (efk), as the game's own console commands read and change it ---------------------------
    internal efk GameData { get { return Data; } }
    efk Data { get { var c = Entities.Character(Entity); return c == null ? null : c.GetAspect<amc>().aa(); } }
    efk Need { get { var d = Data; if (d == null) throw new InvalidOperationException("not a character"); return d; } }

    // A character (has character data), not a placed object such as a turret's base or a prop.
    public bool IsCharacter { get { return Data != null; } }
    // It can be talked to (has the dialog aspect the game's Talk uses).
    public bool CanTalk { get { var c = Entity as c5x; return c != null && c.GetAspect<j7>() != null && IsCharacter && !IsDead; } }
    public string Id { get { return Need.Id; } }
    // A stable key for remembering this character: its id, or its name when it has none (characters spawned from a
    // blueprint have no template id).
    public string Key { get { var id = Need.Id; return string.IsNullOrEmpty(id) ? Name : id; } }
    public string Name { get { var d = Data; return d == null ? Entities.Name(Entity) : d.LocalizedName ?? d.Name; } set { Need.Name = value; ((c5x)Entity).q(value); } }     // the data's name and the entity's (the hover label)
    public int Level { get { return Need.Level; } }
    public string Faction { get { return Need.FactionId; } set { Need.FactionId = value; } }
    public bool Passive { get { return Need.Passive; } set { Need.Passive = value; } }
    public bool IsDead { get { var d = Data; return d != null && d.Dead; } }
    // In the fight: its own flag, or a combatant in the combat manager's list (characters with a player sheet that
    // aren't the player don't get the flag).
    public bool IsInCombat { get { if (Need.IsInCombat) return true; var m = Combat.Manager; return m != null && m.ahg() && m.ahu(Data) != null; } }
    public void Kill() { Need.a((cwt)null, (cru)null, false); }

    // How far it sees (tiles): its sheet's SightRange, what its AI's vision uses.
    public float SightRange { get { return Need.SightRange; } }
    // Its AI has detected other (the game's own awareness: unaware -> suspicious -> detected; bm6.e). What makes
    // characters join a fight. False for one without an AI.
    public bool HasNoticed(Character other)
    {
        var me = Entity as c5x; var them = other == null ? null : other.Entity as c5x;
        var ai = me == null ? null : me.GetAspect<bm6>();
        var target = them == null ? null : them.GetAspect<amc>();
        return ai != null && target != null && ai.e(target);
    }

    public float Health { get { return Need.Defense.q().zo(); } set { Need.Defense.q().zp(value); } }
    public float MaxHealth { get { return Need.Defense.q().zm(); } }
    // Damage threshold (flat, subtracted first) and resistance (percent) per damage type, as combat uses them
    // (efk.Defense.af()[type]: du2.a() threshold, du2.c() resistance fraction). Keys: Mechanical, Heat, Cold,
    // Electricity, Acid, Energy, Bio (the names Combat's damage parts use).
    public IDictionary<string, Resistance> Resistances
    {
        get
        {
            var result = new Dictionary<string, Resistance>();
            foreach (var kv in Need.Defense.af()) result[kv.Key.ToString()] = new Resistance { Threshold = kv.Value.a(), Percent = (int)Math.Round(kv.Value.c() * 100) };
            return result;
        }
    }
    // How well it notices sneaking characters (compared with their Stealth).
    public int Detection { get { return Need.Detection; } }

    // Base abilities by name (Strength, Dexterity, Agility, Constitution, Perception, Will, Intelligence) and skills by
    // name (Guns, Persuasion, Mercantile...): the value with bonuses, and the base value, which can be set.
    public int Ability(string name) { var s = Need.BaseAbilities.a(name); return s == null ? 0 : s.g(); }
    public int BaseAbility(string name) { var s = Need.BaseAbilities.a(name); return s == null ? 0 : s.f(); }
    public void SetBaseAbility(string name, int value) { var s = Need.BaseAbilities.a(name); if (s == null) throw new ArgumentException("no ability " + name); s.a(value); Need.bl(); }
    public int Skill(string name) { var s = Need.Skills.a(name); return s == null ? 0 : s.g(); }
    public int BaseSkill(string name) { var s = Need.Skills.a(name); return s == null ? 0 : s.f(); }
    public void SetBaseSkill(string name, int value) { var s = Need.Skills.a(name); if (s == null) throw new ArgumentException("no skill " + name); ((TimelapseVertigo.Rules.Characters.Stat<int>)s).a(value); Need.bl(); }
    // The names of its skills (as Skill/SetBaseSkill take them).
    public IList<string> SkillNames { get { return Need.Skills.Select(k => k.Name).ToList(); } }
    // The player's unspent points (spent on the game's level-up screen): skills, abilities, feats (0 for others).
    public int SkillPoints { get { var p = Data as e3q; return p == null ? 0 : p.h(); } set { var p = Data as e3q; if (p != null) p.d(Math.Max(0, value)); } }
    public int AbilityPoints { get { var p = Data as e3q; return p == null ? 0 : p.g(); } set { var p = Data as e3q; if (p != null) p.b(Math.Max(0, value)); } }
    public int FeatPoints { get { var p = Data as e3q; return p == null ? 0 : p.j(); } set { var p = Data as e3q; if (p != null) p.f(Math.Max(0, value)); } }
    public IList<string> Feats { get { return Need.Feats.Select(f => f.Name).ToList(); } }
    // By display name or key (the game's internal name, or a mod feat's ModFeat.Key).
    public bool HasFeat(string name) { return Need.Feats.Any(f => string.Equals(f.Name, name, StringComparison.OrdinalIgnoreCase) || string.Equals(ck.a(f.GetType()), name, StringComparison.OrdinalIgnoreCase)); }

    // The player's XP (0 for others) and the game's way to award it (Intelligence factor, notice, level-up).
    internal e3q PlayerData { get { return Data as e3q; } }
    public int Experience { get { var p = Data as e3q; return p == null ? 0 : p.i(); } }
    public void GiveExperience(int amount, string reason = null) { if (IsPlayer) awq.a(amount, reason); }

    public Inventory Inventory { get { return inventory ?? (inventory = new Inventory(() => Data)); } }
    // What it wears and wields; equip and unequip.
    public Equipment Equipment { get { return equipment ?? (equipment = new Equipment(() => Data)); } }
    Equipment equipment;
    internal bool HasGear(acd gear) { var d = Data; return d != null && ReferenceEquals(d.Gear, gear); }
    Inventory inventory;

    // Its personal attitude to the player (overrides its faction's), as the game's scripts set it.
    public override string ToString() { try { return Name ?? "?"; } catch { return "?"; } }
    public Attitude AttitudeToPlayer { get { return Attitude; } }
    // How it stands towards another character (its own override for the other's faction, else the faction table),
    // e.g. whether an enemy would fight the player's ally. None for non-characters.
    public Attitude AttitudeTo(Character other)
    {
        try
        {
            var me = Entities.Character(Entity); var them = other == null ? null : Entities.Character(other.Entity);
            if (me == null || them == null) return Attitude.None;
            var game = bpo.GetService<cnk>();
            var table = game == null || game.aqu() == null ? null : game.aqu().ac();     // the game state's faction table
            var a = me.GetAspect<amc>().aa().a(table, them.GetAspect<amc>().aa().FactionId);
            return ToAttitude(a);
        }
        catch { return Attitude.Neutral; }
    }
    // Its own attitude to everyone of a faction ("player", "playerNativeAlly", a faction id as Faction shows),
    // whatever the faction table says. None: back to the table.
    public void SetAttitudeToFaction(string faction, Attitude attitude)
    {
        if (attitude == Attitude.None) { Need.c(faction); return; }
        Need.a(faction, (h)((int)attitude - 1));
    }
    static Attitude ToAttitude(h a) { return a == global::h.a ? Attitude.Hostile : a == global::h.b ? Attitude.Unfriendly : a == global::h.d ? Attitude.Friendly : a == global::h.e ? Attitude.Ally : Attitude.Neutral; }
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
        if (e == null || Entities.Character(e) == null) return;
        if (!Entities.IsPlayer(e)) drawing.Add(e);
        var id = Layers.Id(layer);
        if (id == "SHD" || id == "marker") return;     // shadows and markers reach past the body
        var r = Layers.Bounds(layer);
        Rectangle had;
        boundsNow[e] = boundsNow.TryGetValue(e, out had) ? Rectangle.Union(had, r) : r;
    }

    internal static void WorldDone()
    {
        if (!tracking) return;
        var list = new List<Character>(drawing.Count);
        foreach (var e in drawing) list.Add(Character.Of(e));
        visible = list.AsReadOnly();
        drawing.Clear();
        var t = boundsLast; boundsLast = boundsNow; boundsNow = t; boundsNow.Clear();
    }
    static Dictionary<b0b, Rectangle> boundsNow = new Dictionary<b0b, Rectangle>(), boundsLast = new Dictionary<b0b, Rectangle>();
    // Where a character was drawn in the last frame (interface units, as Ui elements; null: not drawn, e.g. out of
    // sight). The first call starts the tracking, so it answers from the next frame on.
    internal static Rectangle? ScreenBoundsOf(b0b e) { tracking = true; Rectangle r; return e != null && boundsLast.TryGetValue(e, out r) ? r : (Rectangle?)null; }
}
}
