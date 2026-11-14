// UnderRailWorks: characters, items and placed characters for mods, in Works' own types. What the game does, measured
// from its code (UnderRailWorks.md, "Characters and items"):
// - A character is an entity (c5x) with the character aspect (amc), whose data (efk) holds name, level, faction,
//   health (Defense), base abilities (bn2), skills (ech), feats (an2), the bag (dcw); the player's (e3q) also XP.
//   The game edits stats like its console commands do: set the base value, then recalculate (efk.bl()).
// - Items: definitions in data/Rules/Items/<path>.item (ez2), instances (coh) with stacks; the game checks and takes
//   items by code (e.g. "currency.sc" = charons) and creates them by path ("currency\\StygianCoin").
// - Placed characters: the game's spawn helper (vq.a: blueprint, zone, location), as its spawnEntity command.
//   Works' placements are never saved: Works spawns them whenever their zone loads and takes them out right before
//   the game stores the zone (Saves.cs), so the stock game never sees them.
//   World.Place(new Placement { Id = "mymod_stranger", Zone = "...", Area = "...", X = 10, Y = 12,
//       Blueprint = "creatures\\generichumans\\foundryworker", Name = "Stranger", Dialog = "mymod/stranger",
//       When = Condition.Not(Condition.Quest("mymod_crate", QuestState.Completed)) });
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class Inventory
{
    readonly Func<efk> owner;
    internal Inventory(Func<efk> owner) { this.owner = owner; }

    internal efk Owner { get { var o = owner(); if (o == null) throw new InvalidOperationException("the character is gone"); return o; } }
    bool IsPlayer { get { return owner() is e3q; } }

    // Puts an item that exists (taken out of a container or another bag: its condition, ammo and stacks kept) into the bag.
    public void Put(Item item) { if (item != null && !Simulation.Refuses("Inventory.Put")) Owner.Bag.z3(item.Game, true); }

    // Items in the bag (not equipped ones).
    public IList<Item> Items { get { return Owner.Bag.z2().Select(i => new Item(i)).ToList(); } }

    // How many of an item (by code, e.g. "currency.sc") the character has, bag and (optionally) gear.
    public int Count(string code, bool includeGear = false) { return Owner.b(code, int.MaxValue, includeGear).Sum(p => p.StackPortion); }
    public bool Has(string code, int stacks = 1, bool includeGear = false) { return Count(code, includeGear) >= stacks; }

    // Adds stacks of an item, by path ("currency\\StygianCoin", "armor\\gasMask"), as the game's GiveItemsJob does
    // (for the player: lootable, and the "item received" line). False if the path is unknown.
    public bool Add(string path, int stacks = 1, bool notify = true)
    {
        if (stacks <= 0 || Simulation.Refuses("Inventory.Add")) return false;     // no empty or negative stacks into a save
        var item = ez2.a(path);
        if (item == null) return false;
        item.Stacks = stacks;
        if (IsPlayer) { item.Lootable = true; item.Stealable = true; }
        Owner.Bag.z3(item);
        if (IsPlayer && notify) a7v.a(new bou(item));
        return true;
    }

    // Takes stacks of an item by code (bag first, then gear, as the game's RemoveItemFromPlayerAction). Returns how
    // many were taken.
    public int Take(string code, int stacks = 1, bool notify = true)
    {
        if (stacks <= 0 || Simulation.Refuses("Inventory.Take")) return 0;
        var taken = Owner.a(code, stacks, true);
        if (taken == null) return 0;
        if (IsPlayer && notify) a7v.a(new aub(taken));
        return taken.Stacks;
    }

    // Charons (Stygian coins).
    public int Money { get { return Count(ItemCodes.Charons); } }
    public bool AddMoney(int amount, bool notify = true) { return Add("currency\\StygianCoin", amount, notify); }
    // False (nothing taken) for an amount not above 0, or more than the character has.
    public bool TakeMoney(int amount, bool notify = true) { if (amount <= 0 || Money < amount) return false; return Take(ItemCodes.Charons, amount, notify) == amount; }
}

public static class ItemCodes { public const string Charons = "currency.sc"; }

// Item definitions by path (data/Rules/Items/<path>.item, e.g. "consumables\\medicine\\bandage"): the code the game
// counts and takes items by, and the shown name.
public static partial class Items
{
    // The game's class for an item and its quick-slot flag (for tests).
    [Preview("the game's scrambled type name")] public static string DebugKind(Item item) { return item == null ? "" : item.DebugKind; }
    public static bool Exists(string path) { return RawDefinition(path) != null; }
    public static string Code(string path) { var d = RawDefinition(path); return d == null ? null : d.a5(); }
    public static string Name(string path) { var d = RawDefinition(path); return d == null ? null : d.az(); }
    static readonly Dictionary<string, bk3> cache = new Dictionary<string, bk3>(StringComparer.OrdinalIgnoreCase);
    static bk3 RawDefinition(string path)
    {
        bk3 d;
        if (path == null) return null;
        if (!cache.TryGetValue(path, out d)) { try { d = ez2.b(path, false); } catch { d = null; } cache[path] = d; }
        return d;
    }
}

public sealed partial class Item
{
    internal readonly coh Game;
    // Two wrappers of the same game object are equal (Works makes a new one on each call): fine as dictionary keys.
    public override bool Equals(object obj) { var o = obj as Item; return o != null && ReferenceEquals(o.Game, Game); }
    public override int GetHashCode() { return Game == null ? 0 : System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(Game); }
    public static bool operator ==(Item a, Item b) { return ReferenceEquals(a, b) || (!ReferenceEquals(a, null) && a.Equals(b)); }
    public static bool operator !=(Item a, Item b) { return !(a == b); }
    internal Item(coh item) { Game = item; }
    public string Code { get { return Game.Definition == null ? "" : Game.Definition.a5(); } }
    public string Name { get { return Game.Definition == null ? "" : Game.Definition.az(); } }
    public int Stacks { get { return Game.Stacks; } set { Game.Stacks = Math.Max(1, value); } }     // at least 1: take an item away with Inventory.Take / Remove
    // Base value of one item and of the stack (the game's ItemValue; merchants multiply it by their markup).
    public int Value { get { return Game.SingleItemValue; } }
    public int StackValue { get { return Game.ItemValue; } }
    public double Weight { get { return Game.Weight; } }
    internal string DebugKind { get { return Game.GetType().Name + (Game.Definition != null && Game.Definition.v ? " quickslot" : ""); } }
    public override string ToString() { return Name + (Stacks > 1 ? " x" + Stacks : ""); }
    // Its use as an ability (Character.UseAbility's id), null if it has none. Its id in its owner's bag or gear.
    [Preview("the game's scrambled type name")] public string AbilityId { get { var u = Game.Definition == null ? null : Game.Definition.a2(); return u == null || u.Count == 0 || u[0].Value == null ? null : u[0].Value.GetType().FullName; } }
    public string Id { get { return Game.Id.ToString(); } }
}

public enum SpawnSide { Neutral, Hostile, PlayerOnly, Ally }

// A character placed by a mod: re-created each time its zone loads, while When holds; never saved.
public sealed partial class Placement
{
    public string Id { get; set; }                // unique (prefix with the mod id)
    public string Zone { get; set; }              // zone id (World.ZoneId shows the current one)
    public string Area { get; set; }              // area name in the zone (Area.Name)
    public int X { get; set; }
    public int Y { get; set; }                 // tile (X, Y)
    public string Blueprint { get; set; }         // entity blueprint, e.g. "creatures\\generichumans\\foundryworker"
    public string Name { get; set; }              // shown name (null: the blueprint's)
    public string Dialog { get; set; }            // Works or game dialog Talk opens (null: the blueprint's)
    public bool Friendly { get; set; } = true;     // neutral and passive towards the player (else as the blueprint's faction)
    public Condition When { get; set; }           // present only while this holds (checked when the zone loads)
    public string Store { get; set; }             // makes it a merchant with this game store (data/Rules/Stores, e.g. "bigbret")
    public Action<Character> Setup { get; set; }  // anything else, after it's placed
    internal bool Temporary;         // World.Spawn: once only, forgotten when the zone goes
}

public static partial class World
{
    static readonly ConstructorInfo LocationCtor = typeof(bt4).GetConstructors().First(c => c.GetParameters().Length == 2 && c.GetParameters()[0].ParameterType == typeof(string));
    static readonly MethodInfo AddToArea = typeof(aud).GetMethod("a", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(b0b) }, null);
    static readonly List<Placement> placements = new List<Placement>();
    // Placed entities currently in the world, by placement id.
    static readonly Dictionary<string, c5x> placed = new Dictionary<string, c5x>();

    // Every living character in the current zone (all its areas), the player included.
    public static IList<Character> Characters
    {
        get
        {
            var list = new List<Character>();
            var zone = Areas.CurrentZone();
            if (zone == null || zone.Areas == null) return list;
            foreach (var area in zone.Areas)
                if (area != null && area.Entities != null) foreach (var e in area.Entities)
                    if (e is c5x && ((c5x)e).GetAspect<amc>() != null) { var c = Character.Of(e); if (!c.IsDead) list.Add(c); }
            return list;
        }
    }

    // Living characters in the current zone within radius tiles of a point (distance on the map, levels ignored),
    // nearest first (the player included if in range; filter with Where). at: e.g. World.Player.Position.
    public static IList<Character> CharactersNear(Vector3 at, float radius)
    {
        return Characters.Select(c => new { c, d = Vector2.Distance(new Vector2(c.Position.X, c.Position.Y), new Vector2(at.X, at.Y)) }).Where(x => x.d <= radius).OrderBy(x => x.d).Select(x => x.c).ToList();
    }

    // The character under the mouse in the world (null: none, or not a character): the one the game outlines white.
    // (The world view mv is a dnn; dnn.an2() is what the mouse is over.)
    public static Character Hovered
    {
        get
        {
            var view = Hud.Current == null ? null : WorldViewField.GetValue(Hud.Current) as dnn;
            var e = view == null ? null : view.an2();
            if (!(e is c5x) || ((c5x)e).GetAspect<amc>() == null) return null;
            var c = Character.Of(e);
            return c.IsDead ? null : c;
        }
    }
    // When the current zone finished loading (UTC).
    public static DateTime ZoneLoadedAt { get; internal set; }

    // The areas of the current zone (a building's floors, a station's levels...), in the zone's order.
    public static IList<Area> ZoneAreas
    {
        get
        {
            var zone = Areas.CurrentZone();
            return zone == null || zone.Areas == null ? new List<Area>() : zone.Areas.Select(Area.Of).Where(a => a != null).ToList();
        }
    }

    // The map tile under the mouse (null: the mouse is over the interface, or no world): what a click would walk to.
    public static Point? HoveredTile
    {
        get
        {
            var view = Hud.Current == null ? null : WorldViewField.GetValue(Hud.Current) as dnn;
            var tile = view == null ? null : view.an1();
            return tile == null ? (Point?)null : tile.Location;
        }
    }
    static readonly System.Reflection.FieldInfo WorldViewField = typeof(ci3).GetField("m_t", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic) ?? typeof(ci3).GetField("t", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic);

    // Every container in the current zone (crates, lockers, corpses...).
    public static IList<Container> Containers
    {
        get
        {
            var list = new List<Container>();
            var zone = Areas.CurrentZone();
            if (zone == null || zone.Areas == null) return list;
            foreach (var area in zone.Areas)
                if (area != null && area.Entities != null) foreach (var e in area.Entities)
                {
                    var c = e is c5x ? ((c5x)e).GetAspect<bim>() as c7j : null;
                    if (c != null) list.Add(new Container(c));
                }
            return list;
        }
    }

    // The current zone's id, for Placement.Zone (null outside a game).
    public static string ZoneId { get { var game = bpo.GetService<cnk>(); return game == null || game.aqu() == null || game.aqu().c() == null ? null : game.aqu().c().b(); } }

    static readonly Dictionary<string, string> placementOwners = new Dictionary<string, string>();
    public static void Place(Placement p)
    {
        if (Simulation.Refuses("World.Place")) return;
        if (p == null || p.Id == null || p.Zone == null || p.Blueprint == null) throw new ArgumentException("a placement needs Id, Zone and Blueprint");
        var owner = ModScope.Current ?? "";
        string had;
        if (placementOwners.TryGetValue(p.Id, out had) && !string.Equals(had, owner, StringComparison.OrdinalIgnoreCase))
            Log.Write("world: " + (owner.Length == 0 ? "?" : owner) + " replaces " + (had.Length == 0 ? "another" : had + "'s") + " placement " + p.Id + " (prefix ids with the mod's id)");
        placementOwners[p.Id] = owner;
        lock (placements) { placements.RemoveAll(x => x.Id == p.Id); placements.Add(p); }
        if (string.Equals(ZoneId, p.Zone, StringComparison.OrdinalIgnoreCase)) SpawnPlacements(Areas.CurrentZone(), p.Zone);
    }

    // --- Temporary characters (arena waves, ambushes, summons) ------------------------------------------------------
    // A character from a blueprint, now, in the player's area at a tile (see FreeTilesNear). Unlike a placement it
    // is created once: never saved (taken out around saves like placements), gone when the zone is left or another
    // game loaded, never re-created. hostile: attacks the player (else neutral and passive).
    internal static Character Spawn(string blueprint, int x, int y, bool hostile) { return Spawn(blueprint, x, y, hostile ? SpawnSide.Hostile : SpawnSide.Neutral); }
    // side: PlayerOnly fights the player and nobody else (a duel, an arena: a station's guards stay out of it). How:
    // a character's attitude to a faction is its own override (efk.an), else the faction table (oj.abu), else neutral;
    // the spawn gets a faction of its own that no table knows (everyone is neutral to it) and an override making it
    // hostile to "player". (Giving it the player's own faction instead turned the whole station on the player.)
    public static Character Spawn(string blueprint, int x, int y, SpawnSide side)
    {
        if (Simulation.Refuses("World.Spawn")) return null;
        bool hostile = side != SpawnSide.Neutral;
        var player = Player;
        var zone = ZoneId;
        if (player == null || zone == null || player.Area == null) return null;
        var id = "spawn:" + (++spawned);
        var p = new Placement { Id = id, Zone = zone, Area = player.Area.Name, X = x, Y = y, Blueprint = blueprint, Friendly = !hostile, Temporary = true };
        if (side == SpawnSide.PlayerOnly) p.Setup = c => { c.Faction = "works_spawn"; c.SetAttitudeToPlayer(Attitude.Hostile); };
        // Ally: the game's own faction for what the player brings into a fight (its force fields: playerNativeAlly),
        // so the faction table makes it friends with the player and enemies with the player's enemies, both ways.
        else if (side == SpawnSide.Ally) p.Setup = c => c.Faction = "playerNativeAlly";
        // Hostile: to the player and to the player's allies (playerNativeAlly: companions, a co-op partner), as the
        // game's own enemies are.
        else if (hostile) p.Setup = c => { c.SetAttitudeToPlayer(Attitude.Hostile); c.SetAttitudeToFaction("playerNativeAlly", Attitude.Hostile); };
        Place(p);
        p.When = Condition.Code(() => false);     // created: never again
        return Placed(id);
    }
    static int spawned;

    // A character made in code (not from a blueprint) treated as a Spawn: taken out around saves, forgotten with the zone.
    internal static void Adopt(Character c, string name)
    {
        var e = c.Entity as c5x;
        var zone = ZoneId; var area = c.Area;
        if (e == null || zone == null || area == null) return;
        var id = "spawn:" + (++spawned);
        // Friendly false: put back after a save (Apply), it's left as it is (the default made it passive and neutral).
        var p = new Placement { Id = id, Zone = zone, Area = area.Name, X = (int)c.Position.X, Y = (int)c.Position.Y, Blueprint = name, Temporary = true, Friendly = false };
        p.When = Condition.Code(() => false);
        lock (placements) placements.Add(p);
        placed[id] = e;
    }

    // --- Followers -----------------------------------------------------------------------------------------------
    // Out of combat, a follower that falls more than distance tiles behind is sent (the game's own move order, as its
    // AI moves: ale.a(tile)) to a free tile near its leader; in combat its AI fights as it would. Spawned characters
    // stay in their zone (they are never saved), so a follower is left behind at an exit.
    sealed class Follow { public Character Who, Leader; public int Distance; public bool Travels; public double Wait; public Vector3? OrderedFor; public Vector3 LastSeen; public int Still; }
    // Travelling followers of the player, taken along at a zone change (spawns stay in their zone otherwise).
    sealed class Traveller { public Placement From; public string Name; public float Health; public int Distance; public Character Old; }
    static readonly List<Traveller> travelling = new List<Traveller>();

    // The player leaves the zone (GameEvents.ZoneLeaving went through): note the travelling followers.
    internal static void PackFollowers()
    {
        travelling.Clear();
        foreach (var f in followers.Values.ToList())
        {
            if (!f.Travels || f.Who.IsDead || f.Leader == null || !f.Leader.IsPlayer) continue;
            var p = PlacementOf(f.Who.Entity);
            if (p == null) continue;     // only Works' spawns can be re-created
            travelling.Add(new Traveller { From = p, Name = f.Who.Name, Health = f.Who.Health, Distance = f.Distance, Old = f.Who });
        }
        if (travelling.Count > 0) Log.Write("follow: " + travelling.Count + " follower(s) come along");
    }

    // The new zone is ready: the followers next to the player again.
    internal static void UnpackFollowers()
    {
        var list = travelling.ToList(); travelling.Clear();
        var p = Player;
        if (p == null || list.Count == 0) return;
        var at = p.Position;
        var tiles = FreeTilesNear((int)Math.Round(at.X), (int)Math.Round(at.Y), 3).OrderBy(t => Math.Abs(t.X - at.X) + Math.Abs(t.Y - at.Y)).Skip(1).ToList();
        for (int i = 0; i < list.Count && i < tiles.Count; i++)
        {
            var t = list[i];
            var id = "spawn:" + (++spawned);
            var np = new Placement { Id = id, Zone = ZoneId, Area = p.Area.Name, X = tiles[i].X, Y = tiles[i].Y, Blueprint = t.From.Blueprint, Name = t.From.Name,
                Friendly = t.From.Friendly, Setup = t.From.Setup, Dialog = t.From.Dialog, Store = t.From.Store, OnUse = t.From.OnUse, Temporary = true };
            Place(np);
            np.When = Condition.Code(() => false);
            var c = Placed(id);
            if (c == null) continue;
            try { c.Health = Math.Min(c.MaxHealth, t.Health); } catch { }
            SetFollow(c, p, t.Distance, true);
            Log.Write("follow: " + t.Name + " arrived with the player");
            if (GameEvents.FollowerArrived.Any) GameEvents.FollowerArrived.Run(h => h(c, t.Old));
        }
    }
    static readonly Dictionary<b0b, Follow> followers = new Dictionary<b0b, Follow>();     // by entity
    internal static void SetFollow(Character who, Character leader, int distance, bool travels = false)
    {
        if (who == null || who.Entity == null) return;
        if (leader == null) followers.Remove(who.Entity); else followers[who.Entity] = new Follow { Who = who, Leader = leader, Distance = Math.Max(1, distance), Travels = travels };
    }
    internal static bool IsFollowing(Character who) { return who != null && who.Entity != null && followers.ContainsKey(who.Entity); }

    internal static void FollowTick(double seconds)
    {
        if (followers.Count == 0) return;
        foreach (var kv in followers.ToList())
        {
            var f = kv.Value; var who = f.Who;
            if (who.IsDead || who.Area == null || f.Leader.IsDead) { followers.Remove(kv.Key); continue; }
            if (Combat.InCombat || (f.Wait -= seconds) > 0) continue;
            f.Wait = 0.5;
            var a = who.Position; var b = f.Leader.Position;
            if (who.Area.Game != f.Leader.Area?.Game) continue;
            double far = Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y));
            bool moving = (a - f.LastSeen).Length() > 0.05f;
            f.LastSeen = a;
            f.Still = moving ? 0 : f.Still + 1;
            if (far <= f.Distance + 1) { f.OrderedFor = null; continue; }
            // An order is a new path (and replaces whatever it was doing): only when the leader has moved on since the
            // last one, or the follower stopped short (blocked, or arrived where the leader was).
            if (f.OrderedFor != null && (b - f.OrderedFor.Value).Length() < 2 && f.Still < 2) continue;
            f.OrderedFor = b;
            var to = FreeTilesNear((int)Math.Round(b.X), (int)Math.Round(b.Y), f.Distance)
                .OrderBy(t => (t.X - b.X) * (t.X - b.X) + (t.Y - b.Y) * (t.Y - b.Y) + 0.1 * ((t.X - a.X) * (t.X - a.X) + (t.Y - a.Y) * (t.Y - a.Y))).Cast<Point?>().FirstOrDefault();
            var c = who.Entity as c5x;
            var move = c == null ? null : c.GetAspect<ale>();
            if (to == null || move == null) continue;
            try { move.a(Areas.Tile(who.Area.Game, to.Value.X, to.Value.Y), 0.0, 1, 0, true); }
            catch (Exception ex) { Log.Write("follow: " + ex.Message); followers.Remove(kv.Key); }
        }
    }

    // Removes a spawned (or placed) character from the world now.
    public static void Despawn(Character c)
    {
        if (c == null) return;
        var kv = placed.FirstOrDefault(x => x.Value == c.Entity);
        if (kv.Key == null) return;
        placed.Remove(kv.Key);
        lock (placements) placements.RemoveAll(x => x.Id == kv.Key && x.Temporary);
        if (kv.Value.Area != null) kv.Value.an();
    }

    // Floor tiles in the player's area within radius, nearest first, not taken by a character or a solid object.
    public static IList<Point> FreeTilesNear(int x, int y, int radius)
    {
        var result = new List<Point>();
        var player = Player;
        var area = player == null ? null : player.Area;
        if (area == null) return result;
        var taken = new HashSet<Point>(Characters.Select(c => new Point((int)Math.Round(c.Position.X), (int)Math.Round(c.Position.Y))));
        for (int dy = -radius; dy <= radius; dy++)
            for (int dx = -radius; dx <= radius; dx++)
            {
                int tx = x + dx, ty = y + dy;
                if (tx < 0 || ty < 0 || tx >= area.Width || ty >= area.Height || dx * dx + dy * dy > radius * radius) continue;
                var at = new Point(tx, ty);
                if (!taken.Contains(at) && area.KindAt(tx, ty, false) == TileKind.Floor) result.Add(at);
            }
        return result.OrderBy(t => (t.X - x) * (t.X - x) + (t.Y - y) * (t.Y - y)).ToList();
    }

    // The character a placement put in the world (null if it isn't there now).
    public static Character Placed(string id) { c5x e; return placed.TryGetValue(id, out e) && e.Area != null ? Character.Of(e) : null; }

    internal static bool IsPlaced(b0b e) { return e is c5x && placed.ContainsValue((c5x)e); }
    internal static b0b PlacedEntity(string id) { c5x e; return placed.TryGetValue(id, out e) && e.Area != null ? e : null; }
    internal static Placement PlacementOf(b0b e)
    {
        if (!(e is c5x)) return null;
        var kv = placed.FirstOrDefault(x => x.Value == e);
        if (kv.Key == null) return null;
        lock (placements) return placements.FirstOrDefault(p => p.Id == kv.Key);
    }

    // The zone was loaded (also when loading a save): spawn its placements whose condition holds.
    internal static void SpawnPlacements(c1a zone, string zoneId)
    {
        if (zone == null || zoneId == null) return;
        if (Simulation.IsMirror && !Simulation.Applying) return;     // a copy of another game: its placed characters come from there (the copy's own mirroring places them)
        foreach (var p in placements.Where(x => string.Equals(x.Zone, zoneId, StringComparison.OrdinalIgnoreCase)).ToList())
        {
            c5x existing;
            if (placed.TryGetValue(p.Id, out existing) && existing.Area != null && existing.Area.Zone == zone) continue;
            bool wanted;
            try { wanted = p.When == null || p.When.Test(); } catch (Exception ex) { Log.Write("world: " + p.Id + ": condition failed: " + ex.Message); continue; }
            if (!wanted) continue;
            try
            {
                var at = (bt4)LocationCtor.Invoke(new object[] { p.Area, new Vector3(p.X, p.Y, 0) });     // bt4(area, Vector3): XNA in its signature
                var e = vq.a(p.Blueprint, zone, at);
                if (e == null) { Log.Write("world: " + p.Id + ": no blueprint " + p.Blueprint + " or no area " + p.Area); continue; }
                placed[p.Id] = e;
                Apply(p, e);
                Log.Write("world: placed " + p.Id + " in " + zoneId + "/" + p.Area + " at " + p.X + "," + p.Y);
            }
            catch (Exception ex) { Log.Write("world: " + p.Id + ": spawn failed: " + ex); }
        }
    }

    // The placement's name, dialog, attitude and setup. Again after Works puts the character back after a save:
    // deploying it again can reload its data from the blueprint's template (AutoUpdate).
    static void Apply(Placement p, c5x e)
    {
                var c = Character.Of(e);
                if (c.IsCharacter)
                {
                    if (p.Name != null) c.Name = p.Name;
                    if (p.Dialog != null)
                    {
                        // As the game's scripts give a character a dialog (a8): the dialog aspect, then its name. Only on
                        // placed characters, which are never saved; game characters use Dialogs.Override.
                        var talk = e.GetAspect<j7>() as bn5;
                        if (talk == null) { talk = new bn5(e); e.SetAspect((j7)talk); }
                        talk.b(p.Dialog);
                    }
                    if (p.Friendly) { c.Passive = true; c.SetAttitudeToPlayer(Attitude.Neutral); }
                }
                else if (p.Name != null) e.q(p.Name);     // an object: its hover label
                if (p.Store != null && !c.IsMerchant) c.MakeMerchant(p.Store);
                // An object with a mod use but none of its own: a plain use aspect (never saved: placed entities aren't).
                if (p.OnUse != null && e.GetAspect<bim>() == null) e.SetAspect(new bim(e));
                if (p.Setup != null) p.Setup(c);
    }

    // Before the game stores a zone (saving, or leaving it): take the placed entities out. Returns them with their
    // area, to put back after a save.
    internal static List<KeyValuePair<c5x, aud>> RemovePlaced(c1a zone)
    {
        var removed = new List<KeyValuePair<c5x, aud>>();
        foreach (var kv in placed.ToList())
        {
            var e = kv.Value;
            var area = e.Area;
            if (area == null || area.Zone != zone) continue;
            e.an();     // undeploy and remove from the area, as the game does when it stores a zone
            removed.Add(new KeyValuePair<c5x, aud>(e, area));
        }
        return removed;
    }

    internal static void PutBack(List<KeyValuePair<c5x, aud>> removed)
    {
        foreach (var kv in removed)
        {
            try
            {
                AddToArea.Invoke(kv.Value, new object[] { kv.Key });
                var id = placed.First(x => x.Value == kv.Key).Key;
                var p = placements.FirstOrDefault(x => x.Id == id);
                if (p != null) Apply(p, kv.Key);
            } catch (Exception ex) { Log.Write("world: putting back a placed character failed: " + ex.Message); } }
    }

    // After a save: the game redeploys the zone at its end (d6f.arg's finally), which can reload placed characters'
    // data from their templates again.
    internal static void ReapplyAll()
    {
        foreach (var kv in placed.ToList())
        {
            var p = placements.FirstOrDefault(x => x.Id == kv.Key);
            if (p != null && kv.Value.Area != null) { try { Apply(p, kv.Value); } catch (Exception ex) { Log.Write("world: " + p.Id + ": " + ex.Message); } }
        }
    }

    // A game is loading or starting: the placed entities belong to the game state being replaced.
    internal static void ForgetAll() { placed.Clear(); travelling.Clear(); followers.Clear(); lock (placements) placements.RemoveAll(p => p.Temporary); Party.ForgetMembers(); World.ForgetDrawn(); }

    internal static void Forget(c1a zone)
    {
        foreach (var kv in placed.ToList()) if (kv.Value.Area == null) placed.Remove(kv.Key);
        lock (placements) placements.RemoveAll(p => p.Temporary && !placed.ContainsKey(p.Id));     // spawns don't follow the player out
    }
}
}
