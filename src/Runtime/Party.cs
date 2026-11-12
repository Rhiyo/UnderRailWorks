// UnderRailWorks: the party (the player and the characters other people play: a co-op mod adds them) and rules that
// need it together, and zone exits switched off. Mods that mean "the players" use Party.Members instead of
// World.Player, and so work in co-op when a co-op mod fills the party.
//   Party.Add(this, buddy);                                  // a co-op mod: this character is a player's
//   foreach (var c in Party.Members) c.Health = c.MaxHealth;  // any mod: everyone, not just this machine's player
//   Party.NeedTogether(this, PartyNeed.EnterZone, "cc_ph");   // everyone near the one leaving, to enter that zone
//   Party.NeedTogether(this, PartyNeed.Talk, "dialogs\\core city\\jyles");   // ...to start that conversation
//   Party.NeedTogether(this, PartyNeed.Use, lever.EntityId); // ...to use that object
//   Exits.Disable(this, World.ZoneId, 9, 5);                 // that exit tile does nothing until Enable
// A fight doesn't end while a hostile combatant has detected any party member (the game checks only the player).
// The rules are checked where the game does those things (a zone change a0l.a(c5x, c2w), the start of a conversation
// dka.amu, an object's use bim.a, the walk to an exit ale.a(tile, trigger)), whoever starts them; refused with a
// floating line over the character, as the game refuses. Nothing here is saved: mods set their rules in Load.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public enum PartyNeed { EnterZone, Talk, Use }

    public static class Party
    {
        sealed class Rule { public string Mod; public PartyNeed Need; public string Key; public float Distance; }
        static readonly List<Rule> rules = new List<Rule>();
        static readonly Dictionary<Character, string> others = new Dictionary<Character, string>();

        // The player and the characters added (living, in this zone).
        public static IList<Character> Members
        {
            get
            {
                var list = new List<Character>();
                var p = World.Player;
                if (p != null) list.Add(p);
                lock (others) foreach (var c in others.Keys.ToList()) { if (c.Area == null || c.IsDead) continue; if (!list.Contains(c)) list.Add(c); }
                return list;
            }
        }
        // The party member whose action is running (Party.As): World.Player is that character meanwhile, so mods written
        // for one player do for whoever acted what they'd do for the player. Null: the player's own.
        public static Character Acting { get { return acting; } }
        [ThreadStatic] static Character acting;
        // Runs a with who as the acting player (World.Player), back after (nests).
        public static void As(Character who, Action a)
        {
            var before = acting;
            acting = who != null && !who.IsPlayer && Contains(who) ? who : before;
            try { a(); } finally { acting = before; }
        }
        // As As, for a game step that starts and ends in two hooks (a prefix and a finalizer): Begin returns the previous.
        internal static Character Begin(Character who) { var before = acting; acting = who != null && !who.IsPlayer && Contains(who) ? who : before; return before; }
        internal static void End(Character before) { acting = before; }

        // A multiplayer mod: who reached a level on another machine; GameEvents.LevelledUp runs here with them as the player.
        [Preview("multiplayer: built for Co-op so far")] public static void LevelledUp(Character who, int level) { if (GameEvents.LevelledUp.Any) As(who, () => GameEvents.LevelledUp.Run(h => h(level))); }

        public static void Add(WorksMod mod, Character c) { if (c != null) lock (others) others[c] = mod.Id; }
        public static void Remove(Character c) { if (c != null) lock (others) others.Remove(c); }
        public static bool Contains(Character c) { if (c == null) return false; if (c.IsPlayer) return true; lock (others) return others.ContainsKey(c); }

        // That (entering a zone by its id, a conversation by its name, an object by its EntityId; key null: every one)
        // needs every party member within distance tiles of the character doing it.
        public static void NeedTogether(WorksMod mod, PartyNeed need, string key, float distance = 8)
        {
            lock (rules) { rules.RemoveAll(r => r.Mod == mod.Id && r.Need == need && r.Key == key); rules.Add(new Rule { Mod = mod.Id, Need = need, Key = key, Distance = distance }); }
        }
        public static void DontNeedTogether(WorksMod mod, PartyNeed need, string key) { lock (rules) rules.RemoveAll(r => r.Mod == mod.Id && r.Need == need && r.Key == key); }

        // The members farther than distance from who (empty: everyone's there).
        public static IList<Character> Missing(Character who, float distance)
        {
            if (who == null) return new List<Character>();
            var at = who.Position;
            return Members.Where(c => c != who && Vector2.Distance(new Vector2(c.Position.X, c.Position.Y), new Vector2(at.X, at.Y)) > distance).ToList();
        }

        // Whether who may do it now (a party member doing it with the others away: no). Says why over who when not.
        internal static bool Allows(PartyNeed need, string key, Character who)
        {
            if (who == null || !Contains(who) || Members.Count < 2) return true;
            Rule rule;
            lock (rules) rule = rules.Where(r => r.Need == need && (r.Key == null || string.Equals(r.Key, key, StringComparison.OrdinalIgnoreCase))).OrderBy(r => r.Distance).FirstOrDefault();
            if (rule == null) return true;
            var missing = Missing(who, rule.Distance);
            if (missing.Count == 0) return true;
            Combat.Float(who, "Everyone needs to be here (" + string.Join(", ", missing.Select(c => c.Name).ToArray()) + " isn't)", Color.Orange);
            return false;
        }

        internal static void Forget(string mod)
        {
            lock (rules) rules.RemoveAll(r => r.Mod == mod);
            lock (others) foreach (var c in others.Where(kv => kv.Value == mod).Select(kv => kv.Key).ToList()) others.Remove(c);
        }
    }

    // Zone exits (the tiles World.ExitTiles lists) switched off and on. An exit that's off refuses the walk to it (the
    // player's click and Character.TakeExitAt alike). The game's own exits can also be closed by its map scripts (each
    // exit trigger has a condition); this is on top of that.
    public static class Exits
    {
        static readonly Dictionary<string, string> off = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);     // "zone|x|y" -> mod
        static string Key(string zone, int x, int y) { return (zone ?? "") + "|" + x + "|" + y; }
        public static void Disable(WorksMod mod, string zone, int x, int y) { lock (off) off[Key(zone, x, y)] = mod.Id; }
        public static void Enable(string zone, int x, int y) { lock (off) off.Remove(Key(zone, x, y)); }
        public static bool IsEnabled(string zone, int x, int y) { lock (off) return !off.ContainsKey(Key(zone, x, y)); }
        internal static void Forget(string mod) { lock (off) foreach (var k in off.Where(kv => kv.Value == mod).Select(kv => kv.Key).ToList()) off.Remove(k); }
    }
}

namespace UnderRailWorks.Internal
{
    static class PartyHooks
    {
        // ale.a(tile, trigger): a character's walk to an exit (the player's click, TakeExitAt).
        static bool ToExit(ale __instance, ak1 __0)
        {
            try
            {
                if (__0 == null || Exits.IsEnabled(World.ZoneId, __0.Location.X, __0.Location.Y)) return true;
                var who = __instance.a8() as c5x;
                if (who != null) Combat.Float(Character.Of(who), "That way is closed.", Color.Orange);
                return false;
            }
            catch (Exception ex) { Log.Write("exits: " + ex.Message); return true; }
        }

        // crp.ahs(silent, strict): may the fight end? The game refuses while a hostile combatant has detected the player
        // (aqu().v() only); the party's other members count the same way, so a fight doesn't end while enemies are after
        // one of them.
        static bool MayEnd(bool __0, ref bool __result)
        {
            try
            {
                var members = Party.Members.Where(c => !c.IsPlayer && !c.IsDead && c.Area != null).ToList();
                if (members.Count == 0) return true;
                foreach (var m in members)
                {
                    var foe = World.CharactersNear(m.Position, 200).FirstOrDefault(e => e != m && !e.Passive && e.AttitudeTo(m) == Attitude.Hostile && e.HasNoticed(m));
                    if (foe == null) continue;
                    if (!__0) Combat.Float(m, "Cannot end combat at this time.", Color.Orange);
                    __result = false;
                    return false;
                }
            }
            catch (Exception ex) { Log.Write("party: " + ex.Message); }
            return true;
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            var end = typeof(crp).GetMethod("ahs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, new[] { typeof(bool), typeof(bool) }, null);
            if (end != null) harmony.Patch(end, prefix: new HarmonyLib.HarmonyMethod(typeof(PartyHooks).GetMethod("MayEnd", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            var walk = typeof(ale).GetMethod("a", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, new[] { typeof(ak1), typeof(ya) }, null);
            harmony.Patch(walk, prefix: new HarmonyLib.HarmonyMethod(typeof(PartyHooks).GetMethod("ToExit", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            Log.Write("party: hooks in place");
        }
    }
}
