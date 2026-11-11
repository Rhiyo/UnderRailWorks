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
        public string EntityId { get { return Entity.qe().ToString(); } }
        // Which way it faces: 0 north, 1 north-east ... 7 north-west (map directions).
        public int Facing { get { return (int)Entity.VisualDirection; } }
        // Puts it exactly there (map tiles, fractional allowed) at once, facing that way (-1: as it is).
        public bool PlaceAt(Vector3 at, int facing = -1)
        {
            var c = Entity as c5x;
            var area = Area;
            if (c == null || area == null) return false;
            a0l.a(c, area.Game, at, facing < 0 || facing > 7 ? (HorizontalDirection?)null : (HorizontalDirection)facing, false);
            return true;
        }
    }

    public static partial class World
    {
        // The character with this EntityId in the player's area, or null.
        public static Character ByEntityId(string id)
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
        public static void Follow(Character c)
        {
            var game = bpo.GetService<ps>();
            var follow = game == null ? null : game.akx();
            if (follow == null) return;
            if (c == null || c.Entity == null) { follow.ae1(); var p = World.Player; if (p != null) follow.ae0(p.Entity, 3f, false); }
            else follow.ae0(c.Entity, 3f, false);
        }
    }

    public enum PlayerOrderKind { Walk, Attack, Talk, Use, Ability, Travel, Other }
    // The player ordered their character to do something (a click on the world, the attack key). Cancel: it doesn't.
    // Walk: X, Y the tile; Attack / Talk: Target.
    public sealed class PlayerOrder : Cancellable
    {
        public PlayerOrderKind Kind { get; internal set; }
        public int X { get; internal set; }
        public int Y { get; internal set; }
        public Character Target { get; internal set; }
    }

    public static partial class GameEvents
    {
        public static readonly Hook<Action<PlayerOrder>> PlayerOrdering = new Hook<Action<PlayerOrder>>("PlayerOrdering");
    }

    public static class SaveGames
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
    public static class Simulation
    {
        public static bool Frozen { get; set; }
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
                else if (__0 is du) o.Kind = PlayerOrderKind.Ability;
                else if (__0 is f3) o.Kind = PlayerOrderKind.Travel;
                var e = target as c5x;
                if (e != null && e.GetAspect<amc>() != null) o.Target = Character.Of(e);
                GameEvents.PlayerOrdering.Run(h => h(o));
                return !o.Cancel;
            }
            catch (Exception ex) { Log.Write("player order: " + ex.Message); return true; }
        }

        static bool Tick() { return !Simulation.Frozen; }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(RemoteHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(tf).GetMethod("lj", Any, null, new[] { typeof(bad) }, null), prefix: m("Order"));
            harmony.Patch(typeof(ix).GetMethod("a", Any, null, new[] { typeof(TimeSpan), typeof(TimeSpan) }, null), prefix: m("Tick"));
            harmony.Patch(typeof(crp).GetMethod("aho", Any, null, new[] { typeof(e6d) }, null), prefix: m("Tick"));
            Log.Write("remote: hooks in place");
        }
    }
}
