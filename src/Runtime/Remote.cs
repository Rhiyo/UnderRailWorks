// UnderRailWorks: pieces for mods that show one game in another (co-op, spectating): saving and loading on command,
// the game's own id for a character (kept in saves), exact placement and facing, the player's orders before they
// happen (to send them elsewhere), a camera that follows any character, and a frozen world (nothing moves by itself,
// so it can be set from outside).
//   SaveGames.SaveNow("coop-host");  SaveGames.Load("coop-host");
//   var c = World.ByEntityId(id);  c.PlaceAt(new Vector3(12.5f, 30, 0), facing);
//   GameEvents.PlayerOrdering.Add(this, o => { if (o.Kind == PlayerOrderKind.Walk) { Send(o.X, o.Y); o.Cancel = true; } });
//   Camera.Follow(myCharacter);  Simulation.Frozen = true;
// The game's side (UnderRail 1.3.1.2):
// - Saving: cnk.arg(folder) writes the save at once on the calling thread (the save workflow al1 queues the same call
//   behind its "Saving..." overlay); cnk.arh() is the game's "may save now" (not in combat, not disabled by a script).
//   Loading: cnk.arm() lists the saves (a3r), the LoadGameFromMainMenu / LoadGameFromLocale workflows load one.
// - Ids: every entity has a Guid (cae.qe(), saved as "E2:I"); aud.a(Guid) finds it in an area. Characters mods spawn
//   are new entities (new ids) every time they're spawned.
// - Orders: every player command (a click on the world: walk atl, attack mm, talk c5o, use buw, ability du, exit f3;
//   the attack key) goes through tf.lj(bad), which only runs it (bad.f1).
// - Camera: dka.akx() (amw, b88) follows an entity (ae0) or nothing (ae1); the game only takes it back for the player
//   when it follows nobody or the player.
// - Simulation: c1a ticks the zone's action manager ix.a(total, delta) (movement steps, attacks, every entity's
//   aspects including the AI), and bl1 ticks combat (crp.aho). Frozen skips both; drawing and the UI go on.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Ouroboros.Playfield.Space;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed partial class Character
    {
        // The game's own id for this character: kept in saves, so a loaded copy of the save has the same one (characters
        // mods spawn get a new one each time they're spawned).
        [Preview("multiplayer: built for Co-op so far")] public string EntityId { get { return Entity.qe().ToString(); } }
        // Which way it faces: 0 north, 1 north-east ... 7 north-west (map directions).
        [Preview("multiplayer: built for Co-op so far")] public int Facing { get { return (int)Entity.VisualDirection; } }
        // Where it is walking to (the tile of its current walk order), or null (not walking somewhere).
        [Preview("multiplayer: built for Co-op so far")] public Point? WalkTarget
        {
            get
            {
                var c = Entity as c5x;
                var walker = c == null ? null : c.GetAspect<ny>(); var order = walker == null ? null : walker.a() as djk;
                var tile = order == null ? null : order.fm() as ak1;
                return tile == null ? (Point?)null : tile.Location;
            }
        }
        // It has a walk order (going somewhere or following someone).
        [Preview("multiplayer: built for Co-op so far")] public bool IsWalking { get { var c = Entity as c5x; var w = c == null ? null : c.GetAspect<ny>(); return w != null && w.a() is em1; } }

        // Walks to the object with that EntityId (a door, lever, container, computer...) and uses it, as the player's click
        // does. False: no such object here, or it can't be used.
        [Preview("multiplayer: built for Co-op so far")] public bool UseObject(string entityId)
        {
            var c = Entity as c5x; var area = Area; Guid g;
            var move = c == null ? null : c.GetAspect<ale>();
            if (move == null || area == null || !Guid.TryParse(entityId, out g)) return false;
            var target = area.Game.a(g) as c5x;
            var use = target == null ? null : target.GetAspect<bim>();
            if (use == null) return false;
            return move.b(target, use.t(), 1, use.y()) != null;
        }

        // Talks to npc as the player's click does (walks there, opens its conversation; j7.a). For the player: the
        // ordinary conversation window. False: either can't talk.
        [Preview("multiplayer: built for Co-op so far")] public bool TalkTo(Character npc)
        {
            var me = Entity as c5x; var them = npc == null ? null : npc.Entity as c5x;
            var mine = me == null ? null : me.GetAspect<j7>(); var theirs = them == null ? null : them.GetAspect<j7>();
            if (mine == null || theirs == null || Simulation.Refuses("TalkTo")) return false;
            mine.a(theirs, false, null, false);
            return true;
        }

        // Uses an ability (a psi power, a feat's ability, an item's use: a grenade thrown, a hypo injected; a toggle such
        // as stealth) as this character, as the game's own AI does (cp.b / cp.a: walking into range, AP, cooldowns, the
        // character's own skills). abilityId: PlayerOrder.AbilityId (the capability's type); itemId: the item in its bag
        // or gear (PlayerOrder.ItemId; matched by id, else by the same kind of item), null for none; target: a
        // Character, a Point (a tile), or null (itself). False: no such ability or item, or not usable now.
        [Preview("ability ids are the game's scrambled type names")] public bool UseAbility(string abilityId, string itemId, object target)
        {
            var c = Entity as c5x; var data = c == null ? null : c.GetAspect<amc>(); var area = Area;
            var type = string.IsNullOrEmpty(abilityId) ? null : typeof(c3n).Assembly.GetType(abilityId);
            var cap = type == null ? null : dgk.c(type);
            if (data == null || cap == null || Simulation.Refuses("UseAbility")) return false;
            coh item = null;
            if (!string.IsNullOrEmpty(itemId))
            {
                Guid g; Guid.TryParse(itemId, out g);
                var sheet = data.aa();
                var all = sheet.Bag.z2().Concat(sheet.Gear.d().Select(s => s.pp()).OfType<coh>()).ToList();
                item = all.FirstOrDefault(i => i.Id == g) ?? all.FirstOrDefault(i => i.Definition != null && i.Definition.a2() != null && i.Definition.a2().Any(u => u.Value == cap));
                if (item == null) return false;
            }
            object at = c;
            if (target is Character) at = ((Character)target).Entity;
            else if (target is Point && area != null) at = Areas.Tile(area.Game, ((Point)target).X, ((Point)target).Y);
            var proxy = ecf.a(cap, new eru(new Ouroboros.Playfield.References.EntityAspectReference<amc, amc>(data)), item);
            var aimed = proxy as eu7;
            if (aimed != null)
            {
                if (!aimed.yw()) { Log.Write("ability " + cap.GetType().Name + " for " + Name + ": not usable now (ready " + aimed.t() + ", allowed " + aimed.r() + ", cooled " + aimed.x() + ", in control " + data.aa().IsInControl + ", item " + (item != null) + ")"); return false; }
                if (cp.b(aimed, c, at) == null) { Log.Write("ability " + cap.GetType().Name + " for " + Name + ": refused at " + (at is ak1 ? "a tile" : "a character")); return false; }
                return true;
            }
            var toggle = proxy as bl6;
            if (toggle != null) return toggle.yw() && cp.a(toggle, c) != null;
            Log.Write("ability " + cap.GetType().Name + ": not an ability that can be used (" + (proxy == null ? "none" : proxy.GetType().Name) + ")");
            return false;
        }

        // Swaps primary and secondary weapon (the X key: dcu.k()).
        [Preview("multiplayer: built for Co-op so far")] public bool SwapWeapons() { var a = (Entity as c5x) == null ? null : ((c5x)Entity).GetAspect<amc>(); if (a == null || Simulation.Refuses("SwapWeapons")) return false; a.aa().Offense.k(); return true; }
        // Reloads the weapon in hand from its bag, as the AI does (d3k.a: ammo or a battery).
        public bool Reload() { var a = (Entity as c5x) == null ? null : ((c5x)Entity).GetAspect<amc>(); if (a == null || Simulation.Refuses("Reload")) return false; return d3k.a(a.aa(), a) != null; }

        // Walks to the zone exit on tile x, y and takes it (as the player's click on an exit). The game then changes zone:
        // for everyone, as when the player leaves. False: no exit there.
        [Preview("multiplayer: built for Co-op so far")] public bool TakeExitAt(int x, int y)
        {
            var c = Entity as c5x; var area = Area;
            var move = c == null ? null : c.GetAspect<ale>();
            if (move == null || area == null || x < 0 || y < 0 || x >= area.Width || y >= area.Height || !Exits.IsEnabled(World.ZoneId, x, y)) return false;
            var tile = Areas.Tile(area.Game, x, y);
            var triggers = tile.a(typeof(b0j));
            if (triggers == null || triggers.Count == 0) triggers = tile.a(typeof(cdo));
            if (triggers == null || triggers.Count == 0) return false;
            move.a(tile, triggers[0]);
            return true;
        }

        // Puts it exactly there (map tiles, fractional allowed) at once, facing that way (-1: as it is).
        [Preview("multiplayer: built for Co-op so far")] public bool PlaceAt(Vector3 at, int facing = -1)
        {
            var c = Entity as c5x;
            var area = Area;
            if (c == null || area == null) return false;
            a0l.a(c, area.Game, at, facing < 0 || facing > 7 ? (HorizontalDirection?)null : (HorizontalDirection)facing, false);
            return true;
        }
    }

    // A door in the player's area. IsOpen set: opens or closes it at once (passability too), quietly, without its
    // scripts; Toggle: as a character using it would (sound, scripts).
    [Preview("multiplayer: built for Co-op so far")] public sealed class Door
    {
        internal readonly c5x Entity; internal readonly cnb Aspect;
        internal Door(c5x e, cnb a) { Entity = e; Aspect = a; }
        public string EntityId { get { return Entity.qe().ToString(); } }
        public Vector3 Position { get { return Entities.MapPosition(Entity); } }
        public bool IsOpen { get { return Aspect.q(); } set { if (Aspect.q() != value) Aspect.d(value); } }
        public void Toggle() { Aspect.b(!Aspect.q()); }
        public bool IsLocked { get { var l = Entity.GetAspect<d2b>(); return l != null && l.n(); } }
    }

    public static partial class World
    {
        // The tiles of the player's area with a zone exit (what the player clicks to leave; Character.TakeExitAt).
        [Preview("multiplayer: built for Co-op so far")] public static IList<Point> ExitTiles
        {
            get
            {
                var p = Player; var area = p == null ? null : p.Area;
                var list = new List<Point>();
                if (area == null) return list;
                for (int x = 0; x < area.Width; x++)
                    for (int y = 0; y < area.Height; y++)
                    {
                        var t = Areas.Tile(area.Game, x, y);
                        if (t == null) continue;
                        var a = t.a(typeof(b0j)); var b = a != null && a.Count > 0 ? a : t.a(typeof(cdo));
                        if (b != null && b.Count > 0) list.Add(new Point(x, y));
                    }
                return list;
            }
        }

        // The doors in the player's area.
        [Preview("multiplayer: built for Co-op so far")] public static IList<Door> Doors
        {
            get
            {
                var p = Player; var area = p == null ? null : p.Area;
                var list = new List<Door>();
                if (area == null) return list;
                foreach (var e in area.Game.Entities.OfType<c5x>()) { var d = e.GetAspect<bim>() as cnb; if (d != null) list.Add(new Door(e, d)); }
                return list;
            }
        }

        // The character with this EntityId in the player's area, or null.
        [Preview("multiplayer: built for Co-op so far")] public static Character ByEntityId(string id)
        {
            Guid g;
            var p = Player;
            var area = p == null ? null : p.Area;
            if (area == null || !Guid.TryParse(id, out g)) return null;
            var e = area.Game.a(g) as c5x;
            return e == null ? null : Character.Of(e);
        }
    }

    public static partial class Camera
    {
        // The camera keeps that character in view (as it keeps the player); null: back to the player.
        [Preview("multiplayer: built for Co-op so far")] public static void Follow(Character c)
        {
            var game = bpo.GetService<ps>();
            var follow = game == null ? null : game.akx();
            if (follow == null) return;
            if (c == null || c.Entity == null) { follow.ae1(); var p = World.Player; if (p != null) follow.ae0(p.Entity, 3f, false); }
            else follow.ae0(c.Entity, 3f, false);
        }
    }

    [Preview("multiplayer: built for Co-op so far")] public enum PlayerOrderKind { Walk, Attack, Talk, Use, Ability, Travel, Other }
    // The player ordered their character to do something (a click on the world, the attack key). Cancel: it doesn't.
    // Walk: X, Y the tile; Attack / Talk: Target.
    // Walk / Travel: X, Y the tile (Travel: the exit's tile). Attack / Talk: Target. Use: TargetEntityId (a door, lever,
    // container...; also set for Attack and Talk) and X, Y its tile.
    [Preview("multiplayer: built for Co-op so far")] public sealed class PlayerOrder : Cancellable
    {
        public PlayerOrderKind Kind { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public Character Target { get; internal set; }
        public string TargetEntityId { get; internal set; }
        // Ability: the capability (Character.UseAbility's id: its type's full name), the item it's used from (a grenade,
        // a hypo: the item's id in the user's bag or gear; null: none), and whether it targets a tile (X, Y) or itself.
        public string AbilityId { get; internal set; }
        public string ItemId { get; internal set; }
        public bool TargetsTile { get; internal set; }
        public bool TargetsSelf { get; internal set; }
    }

    public static partial class GameEvents
    {
        [Preview("multiplayer: built for Co-op so far")] public static readonly Hook<Action<PlayerOrder>> PlayerOrdering = new Hook<Action<PlayerOrder>>("PlayerOrdering");
    }

    [Preview("multiplayer: built for Co-op so far")] public static class SaveGames
    {
        static cnk Game { get { return bpo.GetService<cnk>(); } }
        // The game would let the player save now (not in combat, not stopped by a script).
        public static bool CanSave { get { var g = Game; return g != null && World.Player != null && g.arh(); } }
        // Writes the current game into the save folder name now (replacing it). False: not in a game, or no save came out.
        public static bool SaveNow(string name)
        {
            var g = Game;
            if (g == null || World.Player == null || string.IsNullOrEmpty(name)) return false;
            g.arg(name);
            return System.IO.File.Exists(System.IO.Path.Combine(Folder(name), "global.dat"));
        }
        // Deletes that save as the Load Game window's Delete does (cnk.arn). False: no such save, or it couldn't be deleted.
        public static bool Delete(string name)
        {
            var g = Game;
            if (g == null || string.IsNullOrEmpty(name) || !System.IO.Directory.Exists(Folder(name))) return false;
            try { g.arn(name); }
            catch (Exception ex) { Log.Write("saves: deleting " + name + ": " + ex.Message); }
            return !System.IO.Directory.Exists(Folder(name));
        }
        // The save's folder on disk (it may not exist).
        public static string Folder(string name) { return d5x.d(name); }
        // Loads that save (from the main menu or in game). False: there's no such save, or this game can't load it
        // (another major version, or DLC it doesn't have). The load happens over the next frames.
        public static bool Load(string name)
        {
            var g = Game;
            var save = g == null ? null : g.arm().FirstOrDefault(s => s.FolderName == name);
            if (save == null || ast.a(save.GameVersion, save.DLCs)) return false;
            bpo.GetServiceOrThrow<d4m>().avp(dla.a(World.Player != null ? TimelapseVertigo.Common.TimelapseWorkflows.LoadGameFromLocale : TimelapseVertigo.Common.TimelapseWorkflows.LoadGameFromMainMenu, save));
            return true;
        }
    }

    // Frozen: nothing in the zone moves or acts by itself (no walking, attacks, AI, combat turns), while it is still drawn
    // and the UI works: for a world set from elsewhere (PlaceAt, Health).
    [Preview("multiplayer: built for Co-op so far")] public static class Simulation
    {
        public static bool Frozen { get; set; }

        // This game is a copy of another one, set from there (a co-op joiner's): Works' calls that change the world
        // (Spawn, Place, SpawnFromCharacterFile, items in and out of bags and containers, GiveExperience, TeleportTo,
        // Travel, Use) do nothing here, so other mods running on this machine don't make things only it would see. The
        // mod that keeps the copy makes its changes inside Apply.
        public static bool IsMirror { get; set; }
        public static void Apply(Action changes)
        {
            scope++;
            try { changes(); }
            finally { scope--; }
        }
        [ThreadStatic] static int scope;
        internal static bool Applying { get { return scope > 0; } }
        // A world-changing call now: refused (once logged per caller) when this is a mirror outside Apply.
        // Mods whose code was refused a world change (Keybinds: their key needs the real game).
        internal static readonly HashSet<string> RefusedBy = new HashSet<string>();
        internal static bool Refuses(string what)
        {
            if (!IsMirror || scope > 0) return false;
            if (ModScope.Current != null) RefusedBy.Add(ModScope.Current);
            lock (logged) if (logged.Add(what)) Log.Write("mirror: " + what + " refused (this game is a copy of another; logged once)");
            return true;
        }
        static readonly HashSet<string> logged = new HashSet<string>();
    }
}

namespace UnderRailWorks.Internal
{
    static class RemoteHooks
    {
        static bool Order(bad __0)
        {
            if (__0 == null || !GameEvents.PlayerOrdering.Any) return true;
            try
            {
                var o = new PlayerOrder { Kind = PlayerOrderKind.Other };
                var target = __0.a();
                if (__0 is atl && target is ak1) { var at = ((ak1)target).Location; o.Kind = PlayerOrderKind.Walk; o.X = at.X; o.Y = at.Y; }
                else if (__0 is mm) o.Kind = PlayerOrderKind.Attack;
                else if (__0 is c5o) o.Kind = PlayerOrderKind.Talk;
                else if (__0 is buw) o.Kind = PlayerOrderKind.Use;
                else if (__0 is du || __0 is e06)
                {
                    o.Kind = PlayerOrderKind.Ability;
                    var proxy = ((em8)__0).ati();
                    var cap = proxy == null ? null : proxy.p();
                    o.AbilityId = cap == null ? null : cap.GetType().FullName;
                    var medium = proxy == null ? null : proxy.b9() as coh;
                    o.ItemId = medium == null ? null : medium.Id.ToString();
                    var t = target as ak1;
                    if (t != null) { o.TargetsTile = true; o.X = t.Location.X; o.Y = t.Location.Y; }
                    var me = World.Player;
                    o.TargetsSelf = target == null || (me != null && target == me.Entity);
                }
                else if (__0 is f3) { o.Kind = PlayerOrderKind.Travel; var t = target as ak1; if (t != null) { o.X = t.Location.X; o.Y = t.Location.Y; } }
                var e = target as b0b;
                if (e != null)
                {
                    o.TargetEntityId = e.qe().ToString();
                    var m = Entities.MapPosition(e); if (o.Kind == PlayerOrderKind.Use) { o.X = (int)Math.Round(m.X); o.Y = (int)Math.Round(m.Y); }
                    var ch = e as c5x;
                    if (ch != null && ch.GetAspect<amc>() != null) o.Target = Character.Of(ch);
                }
                GameEvents.PlayerOrdering.Run(h => h(o));
                return !o.Cancel;
            }
            catch (Exception ex) { Log.Write("player order: " + ex.Message); return true; }
        }

        static bool Combat() { return !Simulation.Frozen; }

        // ix.a with the world frozen: only walking runs (the walk orders em1 and their steps qy / bpr / dnf / bsz), so a
        // character told to walk (MoveTo) walks with the game's own animation and speed; everything else (AI, attacks,
        // triggers, every entity's aspects) waits. The rest is ix.a's own loop.
        static bool Tick(ix __instance, TimeSpan __0, TimeSpan __1)
        { try {
            if (!Simulation.Frozen) return true;
            var list = __instance.g8();
            foreach (var item in list.ToList())
            {
                if (item.c) continue;
                var a = item.a;
                // Walking, and dying (d3q: the death animation, then the corpse; ad6 the body fading, c7 its removal), so
                // a death set from elsewhere plays out and leaves the game's corpse.
                if (!(a is em1 || a is qy || a is bpr || a is dnf || a is bsz || a is d3q || a is ad6 || a is c7)) continue;
                var r = a.n(__0, __1);
                if (r != dc4.b && !item.c)
                {
                    if (item.b == atp.a || (r == dc4.e && item.b == atp.c)) list.Remove(item);
                    else a.v(__instance);
                }
            }
            return false;
        } catch (Exception ex_) { PatchGuard.Failed("Remote.Tick", ex_); return true; }
    }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(RemoteHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(tf).GetMethod("lj", Any, null, new[] { typeof(bad) }, null), prefix: m("Order"));
            harmony.Patch(typeof(ix).GetMethod("a", Any, null, new[] { typeof(TimeSpan), typeof(TimeSpan) }, null), prefix: m("Tick"));
            harmony.Patch(typeof(crp).GetMethod("aho", Any, null, new[] { typeof(e6d) }, null), prefix: m("Combat"));
            Log.Write("remote: hooks in place");
        }
    }
}
