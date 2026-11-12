// UnderRailWorks: containers, loot and locks (crates, lockers, corpses, doors). The game's side, measured from its code
// (details: UnderRailWorks.md, "Containers, loot and locks"):
// - A container is the c7j aspect of a map entity; its items are c7j.ItemContainer (dbi). Opening runs c7j.mz(opener);
//   items the player takes go through c7j.kl(container, item, taker).
// - Loot is rolled once per entity, at its first deploy (the first visit of the zone, or a spawn), in ery.mx(), into
//   the container (or an NPC's bag). It is saved with the entity: mods may only add stock items.
// - Locks (doors and containers alike) are the d2b aspect. Lockpicking and hacking are d2b.a(who, kind, bonus): no
//   dice, the effective skill plus the tool's bonus against the lock's difficulty.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using TimelapseVertigo.Playfield.Locale;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class Container
{
    internal readonly c7j Game;
    internal Container(c7j c) { Game = c; }
    internal dbi Items_ { get { return Game.ItemContainer; } }
    // The game's id for its object (kept in saves; World.ByEntityId's kind of id).
    public string EntityId { get { return Game.a8().qe().ToString(); } }
    [Preview("drawing types")] public MapObject Entity { get { return new MapObject(Game.a8()); } }
    // Opened before (the game's Inspected flag).
    public bool Searched { get { return Game.Inspected; } }
    public IList<Item> Items { get { return Items_ == null ? new List<Item>() : Items_.z2().Select(i => new Item(i)).ToList(); } }
    // Adds a stock item by path (it is saved with the container: stock items only).
    public bool Add(string path, int stacks = 1) { if (Simulation.Refuses("Container.Add")) return false; return Containers.AddTo(Items_, path, stacks); }
    public bool Remove(Item item) { if (Simulation.Refuses("Container.Remove")) return false; return item != null && Items_ != null && Items_.z5(item.Game); }
    public Microsoft.Xna.Framework.Vector3 Position { get { return Entities.MapPosition(Game.a8()); } }
    // Its shown name (the hover label, e.g. "Locker").
    public string Name { get { return Entities.Name(Game.a8()); } }
    // The area it's in (null if not placed).
    public Area Area { get { return Area.Of(Entities.AreaOf(Game.a8())); } }
    // Opens it for the player now, as a click on it would after walking there (its lock and the game's checks
    // are skipped; owners still notice a theft).
    public bool Open() { var p = World.Player; return p != null && Game.a((c5x)p.Entity, null, true, true); }
    // Someone owns it: taking from it (and, for some, opening it) is theft if you aren't an owner (c3g ownership,
    // c7j's protection mode).
    public bool Owned { get { return Game.ContainerProtectionMode != ctz.a && Game.a8().GetAspect<c3g>() != null; } }
    public bool IsOwner(Character who)
    {
        var o = Game.a8().GetAspect<c3g>();
        var data = who == null ? null : ((c5x)who.Entity).GetAspect<amc>();
        return o == null || (data != null && data.aa() != null && o.a(data.aa()));
    }
    // Who would turn hostile if thief took from it now, by the game's rule: protectors (by id or faction) in the area
    // who can see the thief; for zone- or faction-wide protection, every protector in the zone (seen or not).
    public IList<Character> Witnesses(Character thief) { return Owned && !IsOwner(thief) ? Ownership.Witnesses(Game.a8(), thief) : new List<Character>(); }
    // Its lock (null: none).
    public Lock Lock { get { var l = Game.a8().GetAspect<d2b>(); return l == null ? null : new Lock(l); } }
}

// Loot just rolled for a container or a character (once per entity per game). Add or remove stock items.
public sealed class LootTarget
{ internal LootTarget() { }
    internal dbi Bag;
    public Container Container { get; internal set; }
    public Character Character { get; internal set; }
    public IList<Item> Added { get; internal set; }
    public IList<Item> Items { get { return Bag.z2().Select(i => new Item(i)).ToList(); } }
    public bool Add(string path, int stacks = 1) { return Containers.AddTo(Bag, path, stacks); }
    public bool Remove(Item item) { return item != null && Bag.z5(item.Game); }
}

public enum LockKind { Mechanical, Electronic }

public sealed class Lock
{
    internal readonly d2b Game;
    internal Lock(d2b l) { Game = l; }
    public bool Locked { get { return Game.n(); } }
    public LockKind Kind { get { return Game.j() == LockType.Mechanical ? LockKind.Mechanical : LockKind.Electronic; } }
    // The skill it needs (lockpicking or hacking).
    public int Difficulty { get { return Game.p(); } }
    // False: only a key opens it.
    public bool Pickable { get { return Game.e(); } }
    public string KeyCode { get { return Game.g(); } }
    [Preview("drawing types")] public MapObject Entity { get { return new MapObject(Game.a8()); } }
    // Picking or hacking it provokes its owners (its protection mode, d2b.o(), isn't none and someone owns the door
    // or container: c3g); Witnesses: who would turn hostile now.
    public bool Guarded { get { return Game.o() != d1c.a && Game.a8().GetAspect<c3g>() != null; } }
    public IList<Character> Witnesses(Character thief) { return Guarded ? Ownership.Witnesses(Game.a8(), thief) : new List<Character>(); }
    // A pick (mechanical) or hack (electronic) attempt by a character with its own skill and no tool, through the
    // game's check (Locks.ModifyAttempt applies). True if it opened. The game's XP and messages for picking with a
    // tool are not given; guards who see it may turn hostile, as with a real pick.
    public bool TryPick(Character who)
    {
        if (who == null || !Locked) return !Locked;
        var a = ((c5x)who.Entity).GetAspect<amc>();
        return a != null && Game.a(a, Game.j(), 0) == c9b.a;
    }
}

public sealed class LockAttempt
{ internal LockAttempt() { }
    public Lock Lock { get; internal set; }
    public Character Character { get; internal set; }
    public LockKind Kind { get; internal set; }
    // The character's lockpicking or hacking, and the tool's bonus as the game counts them.
    public int Skill { get; internal set; }
    public int ToolBonus { get; internal set; }
    // Added to Skill + ToolBonus for this attempt (the lock itself stays as saved).
    public int Bonus { get; set; }
    // null: the game decides; true: it opens; false: it fails.
    public bool? Force { get; set; }
    // After the attempt (LockAttempted): whether it opened.
    public bool Success { get; internal set; }
}

public static class Locks
{
    // Before a pick or hack: change the odds (Bonus) or decide (Force).
    public static readonly Hook<Action<LockAttempt>> ModifyAttempt = new Hook<Action<LockAttempt>>("ModifyAttempt");
}

public static partial class GameEvents
{
    public static readonly Hook<Action<Container, Character, bool>> ContainerOpened = new Hook<Action<Container, Character, bool>>("ContainerOpened");     // container, opener, first time
    public static readonly Hook<Action<Container, Item, Character>> ItemTaken = new Hook<Action<Container, Item, Character>>("ItemTaken");
    public static readonly Hook<Action<LootTarget>> LootGenerated = new Hook<Action<LootTarget>>("LootGenerated");
    public static readonly Hook<Action<LockAttempt>> LockAttempted = new Hook<Action<LockAttempt>>("LockAttempted");
}

public static class Containers
{
    internal static bool AddTo(dbi bag, string path, int stacks)
    {
        if (bag == null) return false;
        var item = ez2.a(path);
        if (item == null) return false;
        item.Stacks = stacks;
        item.Lootable = true; item.Stealable = true;
        bag.z3(item);
        return true;
    }
}

// The game's ownership rule (c3g): owners never provoke; protectors by id (g()) or faction (d()) do, in mode c2n.b
// only those in the thief's area whose vision (bm6.b(thief, false)) sees the thief. c3g.a(amc) is not a check (it
// provokes); the owner test is c3g.a(efk).
static class Ownership
{
    internal static IList<Character> Witnesses(c5x entity, Character thief)
    {
        var result = new List<Character>();
        var o = entity == null ? null : entity.GetAspect<c3g>();
        var thiefData = thief == null ? null : ((c5x)thief.Entity).GetAspect<amc>();
        if (o == null || thiefData == null || thiefData.aa() == null || o.a(thiefData.aa())) return result;
        bool needSight = o.a() == c2n.b;
        foreach (var c in World.Characters)
        {
            if (c == thief || c.IsPlayer) continue;
            var e = (c5x)c.Entity;
            var d = e.GetAspect<amc>() == null ? null : e.GetAspect<amc>().aa();
            if (d == null || !(o.g().Contains(d.Id ?? "") || o.d().Contains(d.FactionId ?? ""))) continue;
            var eyes = needSight ? e.GetAspect<bm6>() : null;     // e.t.b: a field/method name clash
            if (needSight && (e.Area != thief.Entity.Area || eyes == null || !eyes.b(thiefData, false))) continue;
            result.Add(c);
        }
        return result;
    }
}

static class ContainerHooks
{
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(ContainerHooks).GetMethod(n, any));
        harmony.Patch(typeof(c7j).GetMethod("mz", any, null, new[] { typeof(c5x), typeof(string) }, null), prefix: m("OpenBefore"), postfix: m("Opened"));
        harmony.Patch(typeof(c7j).GetMethod("kl", any, null, new[] { typeof(dbi), typeof(coh), typeof(object) }, null), postfix: m("Taken"));
        harmony.Patch(typeof(ery).GetMethod("mx", any, null, Type.EmptyTypes, null), prefix: m("RollBefore"), postfix: m("Rolled"));
        harmony.Patch(typeof(d2b).GetMethod("a", any, null, new[] { typeof(amc), typeof(LockType), typeof(int) }, null), prefix: m("LockBefore"), postfix: m("LockAfter"));
    }

    static void OpenBefore(c7j __instance, out bool __state) { try { __state = !__instance.Inspected; } catch (Exception ex_) { PatchGuard.Failed("Containers.OpenBefore", ex_); __state = default(bool); }
    }
    static void Opened(c7j __instance, c5x __0, bool __state)
    {
        if (!GameEvents.ContainerOpened.Any) return;
        try { var c = new Container(__instance); var who = __0 == null ? null : Character.Of(__0); GameEvents.ContainerOpened.Run(h => h(c, who, __state)); }
        catch (Exception ex) { Log.Write("containers: " + ex.Message); }
    }

    static void Taken(c7j __instance, coh __1, object __2)
    {
        if (!GameEvents.ItemTaken.Any || __1 == null) return;
        try
        {
            var taker = __2 is amc ? Character.Of(((amc)__2).a8()) : World.Player;
            var c = new Container(__instance); var item = new Item(__1);
            GameEvents.ItemTaken.Run(h => h(c, item, taker));
        }
        catch (Exception ex) { Log.Write("containers: " + ex.Message); }
    }

    // ery.mx: rolls once (its flag a() turns true); the target is the character's bag or the container's items.
    static void RollBefore(ery __instance, out HashSet<coh> __state)
    { try {
        __state = null;
        if (!GameEvents.LootGenerated.Any || __instance.a()) return;
        var bag = Target(__instance);
        if (bag != null) __state = new HashSet<coh>(bag.z2());
    } catch (Exception ex_) { PatchGuard.Failed("Containers.RollBefore", ex_); __state = default(HashSet<coh>); }
    }

    static void Rolled(ery __instance, HashSet<coh> __state)
    {
        if (__state == null || !__instance.a()) return;
        try
        {
            var bag = Target(__instance);
            if (bag == null) return;
            var e = __instance.a8();
            var amc = e.GetAspect<amc>();
            var c7 = e.GetAspect<bim>() as c7j;
            var t = new LootTarget
            {
                Bag = bag,
                Character = amc != null && amc.aa() != null ? Character.Of(e) : null,
                Container = c7 != null ? new Container(c7) : null,
                Added = bag.z2().Where(i => !__state.Contains(i)).Select(i => new Item(i)).ToList(),
            };
            GameEvents.LootGenerated.Run(h => h(t));
        }
        catch (Exception ex) { Log.Write("loot: " + ex.Message); }
    }

    static dbi Target(ery g)
    {
        var e = g.a8();
        if (e == null) return null;
        var amc = e.GetAspect<amc>();
        if (amc != null && amc.aa() != null) return amc.aa().Bag;
        var c7 = e.GetAspect<bim>() as c7j;
        return c7 == null ? null : c7.ItemContainer;     // furniture's spawned corpses (czz) are left out
    }

    static bool LockBefore(d2b __instance, amc __0, LockType __1, ref int __2, ref c9b __result, out LockAttempt __state)
    {
        __state = null;
        if (!Locks.ModifyAttempt.Any && !GameEvents.LockAttempted.Any) return true;
        try
        {
            var who = __0 == null ? null : Character.Of(__0.a8());
            var kind = __1 == LockType.Mechanical ? LockKind.Mechanical : LockKind.Electronic;
            var a = new LockAttempt { Lock = new Lock(__instance), Character = who, Kind = kind, ToolBonus = __2, Skill = who == null ? 0 : who.Skill(kind == LockKind.Mechanical ? "Lockpicking" : "Hacking") };
            Locks.ModifyAttempt.Run(h => h(a));
            __state = a;
            if (a.Force == false) { __result = c9b.b; return false; }     // "insufficient skill", as the game says
            if (a.Force == true) __2 += 100000;
            else __2 += a.Bonus;
        }
        catch (Exception ex) { Log.Write("locks: " + ex.Message); }
        return true;
    }

    static void LockAfter(c9b __result, LockAttempt __state)
    {
        if (__state == null || !GameEvents.LockAttempted.Any) return;
        __state.Success = __result == c9b.a;
        try { GameEvents.LockAttempted.Run(h => h(__state)); } catch (Exception ex) { Log.Write("locks: " + ex.Message); }
    }
}
}
