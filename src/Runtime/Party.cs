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
                var p = World.RealPlayer;     // not World.Player: while a member acts, that is the member
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

        // A multiplayer mod: who reached a level on another machine; GameEvents.LeveledUp runs here with them as the player.
        [Preview("multiplayer: built for Co-op so far")] public static void LeveledUp(Character who, int level) { if (GameEvents.LeveledUp.Any) As(who, () => GameEvents.LeveledUp.Run(h => h(level))); }

        // The map's player-only triggers also fire for the party's other members: story and quest triggers, trigger barks,
        // dialog and music areas, the scripted triggers marked player-only (the game's ya triggers on stepping in, out and
        // around). For that moment the member counts as the player (its IsPlayer flag and player aspect set, then put
        // back); what the trigger then does to "the player" itself (a dialog job, a cutscene) is the player's. NPCs' own
        // reactions to the player (walking up to talk, guards, attacking on sight) still see only the player.
        [Preview("multiplayer: built for Co-op so far")] public static bool TriggersCountMembers { get; set; }

        // The game's faction-armour conditions (map data: "wearing CAU armor", "wearing Tchortist armor", "not wearing the
        // Gray Army uniform"; they read the player's gear) count the whole party: "wearing" if any member wears it, "not
        // wearing" only if none does. One disguised member is enough for the group (and members can split factions).
        [Preview("multiplayer: built for Co-op so far")] public static bool FactionArmorCountsAnyMember { get; set; }

        // NPCs' own reactions to the player (walking up to talk, guards warning trespassers, attacking on sight,
        // noticing a sneaking player, idle comments: the AI steps whose code asks for the player's sheet) also react to
        // the party's other members: such a step is asked again with the player's body taken to be the member's (the
        // sheet, so stats and gear, stays the player's), and its action then runs for the member it passed for. Random
        // steps that also look at the player are asked once per member, so they come up a little more often.
        [Preview("multiplayer: built for Co-op so far")] public static bool ReactionsCountMembers { get; set; }

        // Spotting and disarming traps work for the party's other members as for the player: a trap a member spots is
        // revealed (the game's reveal and "Trap detected!" are the player's), and disarming one gives them its XP. For
        // the scan or the disarm the member counts as the player (as for triggers).
        [Preview("multiplayer: built for Co-op so far")] public static bool TrapsCountMembers { get; set; }
        // Safeguards for members the game doesn't expect: scripts that remove every non-player character in a place
        // (the lift cutscenes kill whoever rides along) spare them, and a member's pickpocketing is refused (the game's
        // window for it opens for the player, with the player's bag).
        [Preview("multiplayer: built for Co-op so far")] public static bool Safeguards { get; set; }

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

        // A save is loading: the characters added belong to the world that's going away (a co-op mod adds its players
        // again once they're placed in the loaded one).
        internal static void ForgetMembers() { lock (others) others.Clear(); }

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

        // ya.e/f/g/h(entity): a trigger's enter / leave / move / step for an entity. A party member counts as the player
        // for the call (Party.TriggersCountMembers).
        sealed class AsPlayerState { public c5x Body; public amc Aspect; public bool WasPlayer, WasAspect; }
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<ya, object> logged = new System.Runtime.CompilerServices.ConditionalWeakTable<ya, object>();
        static bool TriggerStarting(ya __instance, b0b __0, out AsPlayerState __state)
        {
            __state = null;
            // A copy of another game (a co-op joiner's): the map's triggers are the real game's to run (they start
            // conversations, cutscenes, quests there); here they'd run a second time, out of step.
            if (Simulation.IsMirror && !Simulation.Applying) { Simulation.Refuses("a map trigger"); return false; }
            try
            {
                var body = __0 as c5x;
                if (!Party.TriggersCountMembers || body == null || body.IsPlayer) return true;
                var c = Character.Of(body);
                if (c == null || !Party.Contains(c)) return true;
                var aspect = body.GetAspect<amc>();
                __state = new AsPlayerState { Body = body, Aspect = aspect, WasPlayer = false, WasAspect = aspect != null && aspect.@as() };
                World.SetPlayerFlag(body, true);
                if (aspect != null) aspect.h(true);
                object seen;
                if (!logged.TryGetValue(__instance, out seen)) { logged.Add(__instance, c); Log.Write("party: " + c.Name + " counts as the player for a trigger (" + __instance.GetType().Name + ")"); }
            }
            catch (Exception ex) { Log.Write("party: trigger: " + ex.Message); }
            return true;
        }
        static Exception TriggerDone(Exception __exception, AsPlayerState __state)
        {
            if (__state != null)
            {
                try { World.SetPlayerFlag(__state.Body, __state.WasPlayer); if (__state.Aspect != null) __state.Aspect.h(__state.WasAspect); }
                catch (Exception ex) { Log.Write("party: trigger restore: " + ex.Message); }
            }
            return __exception;
        }

        // cb2 / enj ("wearing CAU / Tchortist armor") and e7c ("not wearing the Gray Army uniform"): each member's sheet in
        // turn as the player's (the swap TalkAs uses), the answers combined.
        [ThreadStatic] static bool inArmor;
        static bool ArmorCheck(object __instance, object __0, ref bool __result)
        {
            if (inArmor || !Party.FactionArmorCountsAnyMember) return true;
            try
            {
                var state = bpo.GetService<cnk>() == null ? null : bpo.GetService<cnk>().aqu();
                var saved = state == null ? null : state.v();
                if (saved == null) return true;
                var sheets = new List<au2>();
                foreach (var m in Party.Members)
                {
                    var body = m.Entity as c5x; var aspect = body == null ? null : body.GetAspect<amc>();
                    var sheet = aspect == null ? null : aspect.aa() as au2;
                    if (sheet != null && !sheets.Contains(sheet)) sheets.Add(sheet);
                }
                if (!sheets.Contains(saved)) sheets.Insert(0, saved);
                if (sheets.Count < 2) return true;
                bool wearing = !(__instance is e7c);     // e7c is the "not wearing" one: all must pass
                bool result = !wearing;
                inArmor = true;
                try
                {
                    foreach (var sheet in sheets)
                    {
                        state.a(sheet);
                        bool r = ((dth)__instance).jh(__0);
                        result = wearing ? result || r : result && r;
                    }
                }
                finally { state.a(saved); inArmor = false; }
                __result = result;
                return false;
            }
            catch (Exception ex) { Log.Write("party: armor check: " + ex.Message); inArmor = false; return true; }
        }

        // works.armorcheck: each condition as the game asks it (party rule included), then for the player alone.
        internal static string ArmorReport()
        {
            Func<dth, string> both = d =>
            {
                bool party = d.jh(null);
                bool was = Party.FactionArmorCountsAnyMember; Party.FactionArmorCountsAnyMember = false;
                bool alone; try { alone = d.jh(null); } finally { Party.FactionArmorCountsAnyMember = was; }
                return party + " (player alone " + alone + ")";
            };
            var r = "armor: CAU " + both(new cb2()) + ", Tchortist " + both(new enj()) + ", not Gray Army " + both(new e7c()) + "; party rule " + (Party.FactionArmorCountsAnyMember ? "on" : "off");
            Log.Write(r);
            return r;
        }

        // --- NPC reactions (Party.ReactionsCountMembers) -------------------------------------------------------------
        // b64.acj: an AI step's condition (its delegate a); b64/byh.aci: its action. A step "reads the player" if its
        // condition's code calls cyt.v() (the player's sheet): read once per method from its IL.
        const System.Reflection.BindingFlags AnyInst = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
        static readonly System.Reflection.FieldInfo Condition = typeof(b64).GetField("a", AnyInst);
        static readonly System.Reflection.FieldInfo SheetBody = typeof(au2).GetFields(AnyInst).FirstOrDefault(f => f.FieldType == typeof(b0b));
        static readonly System.Reflection.MethodInfo PlayerSheet = typeof(cyt).GetMethod("v", AnyInst, null, Type.EmptyTypes, null);
        static readonly Dictionary<System.Reflection.MethodBase, bool> readsPlayer = new Dictionary<System.Reflection.MethodBase, bool>();
        static readonly System.Runtime.CompilerServices.ConditionalWeakTable<b64, b0b> passedFor = new System.Runtime.CompilerServices.ConditionalWeakTable<b64, b0b>();
        [ThreadStatic] static bool inReaction;
        // works.reactions: steps found that read the player, how often they were asked again, passes for a member.
        static long asked, passedMembers;
        static readonly HashSet<string> loggedPass = new HashSet<string>();
        internal static string ReactionReport()
        {
            int found; lock (readsPlayer) found = readsPlayer.Count(kv => kv.Value);
            var r = "reactions: " + (Party.ReactionsCountMembers ? "on" : "off") + ", AI steps reading the player " + found + ", asked for members " + asked + ", passed for a member " + passedMembers;
            Log.Write(r); return r;
        }
        static bool ReadsPlayer(System.Reflection.MethodBase m)
        {
            bool r;
            if (m == null || PlayerSheet == null) return false;
            lock (readsPlayer) if (readsPlayer.TryGetValue(m, out r)) return r;
            try { r = HarmonyLib.PatchProcessor.ReadMethodBody(m).Any(kv => (kv.Key == System.Reflection.Emit.OpCodes.Call || kv.Key == System.Reflection.Emit.OpCodes.Callvirt) && Equals(kv.Value, PlayerSheet)); }
            catch { r = false; }
            lock (readsPlayer) readsPlayer[m] = r;
            return r;
        }
        static au2 HostSheet() { var g = bpo.GetService<cnk>(); var s = g == null ? null : g.aqu(); return s == null ? null : s.v(); }

        static bool ReactionCondition(b64 __instance, TimeSpan __0, double __1, ref bool __result)
        {
            if (inReaction || !Party.ReactionsCountMembers || Condition == null || SheetBody == null) return true;
            try
            {
                var cond = Condition.GetValue(__instance) as Func<TimeSpan, double, bool>;
                if (cond == null || !ReadsPlayer(cond.Method)) return true;
                passedFor.Remove(__instance);
                var sheet = HostSheet();
                var own = sheet == null ? null : SheetBody.GetValue(sheet) as b0b;
                if (own == null) return true;
                inReaction = true;
                try
                {
                    if (cond(__0, __1)) { __result = true; return false; }
                    foreach (var m in Party.Members)
                    {
                        var body = m.Entity as b0b;
                        if (body == null || body == own || m.IsDead) continue;
                        SheetBody.SetValue(sheet, body);
                        bool ok;
                        asked++;
                        try { ok = cond(__0, __1); } finally { SheetBody.SetValue(sheet, own); }
                        if (!ok) continue;
                        passedFor.Add(__instance, body); __result = true; passedMembers++;
                        var key = cond.Method.DeclaringType + "." + cond.Method.Name + "/" + m.Name;
                        lock (loggedPass) if (loggedPass.Add(key)) Log.Write("party: an NPC's AI step (" + (cond.Method.DeclaringType == null ? "?" : cond.Method.DeclaringType.Name) + ") reacts to " + m.Name);
                        return false;
                    }
                }
                finally { inReaction = false; }
                __result = false;
                return false;
            }
            catch (Exception ex) { Log.Write("party: reaction: " + ex.Message); inReaction = false; return true; }
        }
        sealed class ReactionState { public au2 Sheet; public b0b Own; }
        static void ReactionActing(b64 __instance, out ReactionState __state)
        {
            __state = null;
            b0b body;
            if (!Party.ReactionsCountMembers || SheetBody == null || !passedFor.TryGetValue(__instance, out body)) return;
            var sheet = HostSheet(); var own = sheet == null ? null : SheetBody.GetValue(sheet) as b0b;
            if (own == null || body == null) return;
            SheetBody.SetValue(sheet, body);
            __state = new ReactionState { Sheet = sheet, Own = own };
        }
        static Exception ReactionActed(Exception __exception, ReactionState __state)
        {
            if (__state != null) try { SheetBody.SetValue(__state.Sheet, __state.Own); } catch { }
            return __exception;
        }

        // --- Traps and safeguards -----------------------------------------------------------------------------------
        static bool IsMember(c5x body) { if (body == null || body.IsPlayer) return false; var c = Character.Of(body); return c != null && Party.Contains(c); }
        static AsPlayerState AsPlayer(c5x body)
        {
            var aspect = body.GetAspect<amc>();
            var st = new AsPlayerState { Body = body, Aspect = aspect, WasPlayer = false, WasAspect = aspect != null && aspect.@as() };
            World.SetPlayerFlag(body, true);
            if (aspect != null) aspect.h(true);
            return st;
        }
        // amc.a(List<ak1>): a character's trap and hidden-object scan; ds.fr: the disarm action.
        static void ScanStarting(amc __instance, out AsPlayerState __state)
        {
            __state = null;
            try { var b = __instance.a8() as c5x; if (Party.TrapsCountMembers && IsMember(b)) __state = AsPlayer(b); } catch (Exception ex) { Log.Write("party: trap scan: " + ex.Message); }
        }
        static void DisarmStarting(ds __instance, out AsPlayerState __state)
        {
            __state = null;
            try { var b = __instance.vg() == null ? null : __instance.vg().q() as c5x; if (Party.TrapsCountMembers && IsMember(b)) __state = AsPlayer(b); } catch (Exception ex) { Log.Write("party: disarm: " + ex.Message); }
        }
        // amc.a(cwt, bool) with no cause: a script's removal (the lifts). Members are spared.
        static bool ScriptDeath(amc __instance, cwt __0)
        {
            try
            {
                if (__0 != null || !Party.Safeguards) return true;
                var b = __instance.a8() as c5x;
                if (!IsMember(b)) return true;
                Log.Write("party: a script would have killed " + Character.Of(b).Name + " (a party member): spared");
                return false;
            }
            catch (Exception ex) { Log.Write("party: script death: " + ex.Message); return true; }
        }
        // dka.am8(thief, victim, handler): the pickpocket window. Not for members.
        static bool Pickpocket(b0b __0)
        {
            var b = __0 as c5x;
            if (!Party.Safeguards || !IsMember(b)) return true;
            Log.Write("party: " + Character.Of(b).Name + " can't pickpocket (a party member: the window is the player's)");
            Combat.Float(Character.Of(b), "Pickpocketing isn't available in co-op yet.", Color.Orange);
            return false;
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            const System.Reflection.BindingFlags all = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.DeclaredOnly;
            Func<string, HarmonyLib.HarmonyMethod> hm = n => new HarmonyLib.HarmonyMethod(typeof(PartyHooks).GetMethod(n, System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic));
            var scan = typeof(amc).GetMethod("a", all, null, new[] { typeof(List<ak1>) }, null);
            if (scan != null) harmony.Patch(scan, prefix: hm("ScanStarting"), finalizer: hm("TriggerDone")); else Log.Write("party: the trap scan wasn't found");
            var disarm = typeof(ds).GetMethod("fr", all, null, new[] { typeof(TimeSpan), typeof(TimeSpan) }, null);
            if (disarm != null) harmony.Patch(disarm, prefix: hm("DisarmStarting"), finalizer: hm("TriggerDone")); else Log.Write("party: the disarm action wasn't found");
            var death = typeof(amc).GetMethod("a", all, null, new[] { typeof(cwt), typeof(bool) }, null);
            if (death != null) harmony.Patch(death, prefix: hm("ScriptDeath"));
            var pick = typeof(dka).GetMethod("am8", all, null, new[] { typeof(b0b), typeof(object), typeof(csy) }, null);
            if (pick != null) harmony.Patch(pick, prefix: hm("Pickpocket"));
            if (Condition != null && SheetBody != null && PlayerSheet != null)
            {
                var cond = typeof(b64).GetMethod("acj", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, new[] { typeof(TimeSpan), typeof(double) }, null);
                if (cond != null) harmony.Patch(cond, prefix: new HarmonyLib.HarmonyMethod(typeof(PartyHooks).GetMethod("ReactionCondition", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
                foreach (var t in new[] { typeof(b64), typeof(byh) })
                {
                    var act = t.GetMethod("aci", AnyInst, null, new[] { typeof(TimeSpan), typeof(double) }, null);
                    if (act != null) harmony.Patch(act, prefix: new HarmonyLib.HarmonyMethod(typeof(PartyHooks).GetMethod("ReactionActing", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)),
                        finalizer: new HarmonyLib.HarmonyMethod(typeof(PartyHooks).GetMethod("ReactionActed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
                }
            }
            else Log.Write("party: the AI step pieces weren't found: NPCs react to the player only");
            foreach (var t in new[] { typeof(cb2), typeof(enj), typeof(e7c) })
            {
                var check = t.GetMethod("jh", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, new[] { typeof(object) }, null);
                if (check != null) harmony.Patch(check, prefix: new HarmonyLib.HarmonyMethod(typeof(PartyHooks).GetMethod("ArmorCheck", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
                else Log.Write("party: " + t.Name + ".jh not found: its armor check counts only the player");
            }
            foreach (var name in new[] { "e", "f", "g", "h" })
            {
                var t = typeof(ya).GetMethod(name, System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, new[] { typeof(b0b) }, null);
                if (t != null) harmony.Patch(t, prefix: new HarmonyLib.HarmonyMethod(typeof(PartyHooks).GetMethod("TriggerStarting", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)),
                    finalizer: new HarmonyLib.HarmonyMethod(typeof(PartyHooks).GetMethod("TriggerDone", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            }
            var end = typeof(crp).GetMethod("ahs", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, new[] { typeof(bool), typeof(bool) }, null);
            if (end != null) harmony.Patch(end, prefix: new HarmonyLib.HarmonyMethod(typeof(PartyHooks).GetMethod("MayEnd", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            var walk = typeof(ale).GetMethod("a", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public, null, new[] { typeof(ak1), typeof(ya) }, null);
            harmony.Patch(walk, prefix: new HarmonyLib.HarmonyMethod(typeof(PartyHooks).GetMethod("ToExit", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
            Log.Write("party: hooks in place");
        }
    }
}
