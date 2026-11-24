// UnderRailWorks: characters with a whole player sheet (stats, skills, feats, inventory, look, XP) besides the player:
// co-op partners, a second protagonist. A sheet comes from a .char file, the game's own character export (what a new
// game's Import reads):
//   var buddy = World.SpawnFromCharacterFile(path, x, y);    // built as the game builds the player, standing there
//   buddy.ExportTo(path);                                     // its sheet as it is now, as a .char
//   World.MakePlayer(buddy);                                  // this machine's player is now that character (HUD...)
// The game's side: the player's sheet is an au2 (cyt.v(), saved as "TG:PC"); d6f.a(au2) builds the player's body
// (amc with a dsx provider = "whatever cyt.v() is", sight observer c6g on the sheet's own fog bag, faction "player");
// au2.a(body) links the sheet to its body; the IsPlayer flag is e2b.d in c5x.g, set from amc.h(true). An exported .char
// is an ajk (version, DLC list, the sheet a, date, difficulty d), written by ajk.b(path), read by ajk.a(path).
// A body here gets the game's StandAloneProvider holding its own sheet instead of dsx, so it isn't the player.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Ouroboros.Common;
using Ouroboros.Common.Data;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public static partial class World
    {
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        static readonly Type GameType = typeof(cnk).Assembly.GetType("d6f");     // internal: by reflection
        static readonly MethodInfo BuildBody = GameType == null ? null : GameType.GetMethod("a", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(au2) }, null);
        static readonly MethodInfo AddEntityToArea = typeof(aud).GetMethod("a", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(b0b) }, null);
        static readonly PropertyInfo LocationProp = typeof(b0b).GetProperty("Location"), FacingProp = typeof(b0b).GetProperty("VisualDirection");
        internal static readonly FieldInfo FileSheet = typeof(ajk).GetField("a", Inst | BindingFlags.DeclaredOnly), FileVersion = typeof(ajk).GetField("b", Inst | BindingFlags.DeclaredOnly),
            FileDate = typeof(ajk).GetField("c", Inst | BindingFlags.DeclaredOnly), FileDifficulty = typeof(ajk).GetField("d", Inst | BindingFlags.DeclaredOnly), FileDlcs = typeof(ajk).GetField("e", Inst | BindingFlags.DeclaredOnly);
        internal static readonly MethodInfo ReadFile = typeof(ajk).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null),
            WriteFile = typeof(ajk).GetMethod("b", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
        internal static readonly FieldInfo SheetProvider = typeof(amc).GetField("m_m", Inst) ?? typeof(amc).GetFields(Inst).FirstOrDefault(f => f.FieldType == typeof(IProvider<efk>));
        static readonly MethodInfo SetEntityId = typeof(cae).GetMethod("h", Inst | BindingFlags.DeclaredOnly, null, new[] { typeof(Guid) }, null);
        static readonly FieldInfo UidField = typeof(efk).GetField("m_c", Inst | BindingFlags.DeclaredOnly) ?? typeof(efk).GetFields(Inst | BindingFlags.DeclaredOnly).FirstOrDefault(f => f.Name == "c" && f.FieldType == typeof(Guid));
        static readonly FieldInfo Flags = typeof(c5x).GetField("g", Inst | BindingFlags.DeclaredOnly);
        static readonly FieldInfo Footprint = typeof(b0b).GetFields(Inst | BindingFlags.DeclaredOnly).FirstOrDefault(f => f.Name == "z" && f.FieldType == typeof(Vector2?));

        // A character built from a .char file as the game builds the player (its look, sheet, inventory), standing at
        // tile x, y of the player's area, facing south. Not the player: its sheet is its own, and its AI runs unless a mod
        // takes it (CharacterControl). Like Spawn it's never saved (taken out around saves) and stays in this zone.
        // Null: no such file, not a player sheet, or no room there.
        [Preview("multiplayer: built for Co-op so far")] public static Character SpawnFromCharacterFile(string path, int x, int y)
        {
            if (Simulation.Refuses("SpawnFromCharacterFile")) return null;
            var game = bpo.GetService<cnk>();
            var player = Player; var area = player == null ? null : player.Area;
            if (game == null || area == null || BuildBody == null || SheetProvider == null || AddEntityToArea == null || !System.IO.File.Exists(path)) return null;
            var file = ReadFile.Invoke(null, new object[] { path });
            var sheet = file == null ? null : FileSheet.GetValue(file) as au2;
            if (sheet == null) { Log.Write("world: " + path + " has no player sheet"); return null; }
            // A sheet copied from a character here (an import of this game's player) has its id: who-did-what (the blow's
            // attacker, recorded by this id) would find the other one. A new id then.
            if (UidField != null && Characters.Any(other => other.GameData != null && other.GameData.Uid == sheet.Uid)) sheet.Uid = Guid.NewGuid();
            var body = (c5x)BuildBody.Invoke(game, new object[] { sheet });
            // The game builds every player body with the player's one fixed entity id (aju.a): its scripts find the
            // player by it, and AI memory, targets and references go by entity id. A second body with it was the same
            // entity to the game (an enemy that noticed one never noticed the other). A new id, before it's in an area.
            if (SetEntityId != null) SetEntityId.Invoke(body, new object[] { Guid.NewGuid() });
            var aspect = body.GetAspect<amc>();
            SheetProvider.SetValue(aspect, new StandAloneProvider<efk>(sheet));
            aspect.h(false);
            SetPlayerFlag(body, false);
            sheet.a((b0b)body);
            LocationProp.SetValue(body, new Vector3(x, y, 0), null);
            FacingProp.SetValue(body, Ouroboros.Playfield.Space.HorizontalDirection.South, null);
            AddEntityToArea.Invoke(area.Game, new object[] { body });
            var c = Character.Of(body);
            Adopt(c, "sheet:" + sheet.Name);
            return c;
        }

        // This machine's player becomes c (its HUD, inventory and character windows, quick bar, camera, fog of war); the
        // old player's body stays as a character with its own sheet. For a game shown from elsewhere (co-op): saving it
        // here afterwards would save c as the player.
        [Preview("multiplayer: built for Co-op so far")] public static bool MakePlayer(Character c)
        {
            var game = bpo.GetService<cnk>();
            var state = game == null ? null : game.aqu();
            var newBody = c == null ? null : c.Entity as c5x;
            var newAspect = newBody == null ? null : newBody.GetAspect<amc>();
            var newSheet = newAspect == null ? null : newAspect.aa() as au2;
            if (state == null || newSheet == null || SheetProvider == null) return false;
            var oldSheet = state.v();
            if (oldSheet == newSheet) return true;
            // The HUD's quick-bar slots (dvx) subscribe to the player's sheet and never let go: unbound first (as the
            // game's export does, ps.amy), or the old sheet keeps handlers that can't be saved (a zone stored with that
            // character fails: "Zone transition failed").
            var hud = bpo.GetService<ps>();
            if (hud != null && Hud.Exists) hud.amy();
            var oldBody = oldSheet == null ? null : oldSheet.b() as c5x;
            var oldAspect = oldBody == null ? null : oldBody.GetAspect<amc>();
            if (oldAspect != null)
            {
                SheetProvider.SetValue(oldAspect, new StandAloneProvider<efk>(oldSheet));
                oldAspect.h(false);
                SetPlayerFlag(oldBody, false);
            }
            state.a(newSheet);
            newSheet.a((b0b)newBody);
            SheetProvider.SetValue(newAspect, new dsx());
            newAspect.h(true);
            SetPlayerFlag(newBody, true);
            var screens = bpo.GetService<ps>();
            if (screens != null && Hud.Exists) screens.anc();      // the HUD binds the sheet when it's built
            Log.Write("world: the player is now " + c.Name);
            return true;
        }

        // On the main menu: the game's New Game (its rules screen, then character creation, then the first zone), as the
        // menu's button. False: not on the main menu.
        [Preview("multiplayer: built for Co-op so far")] public static bool StartNewGame()
        {
            var menu = UiPopups.Screen() as a8k;
            var controller = menu == null ? null : typeof(a8k).GetFields(Inst | BindingFlags.DeclaredOnly).Where(f => f.FieldType == typeof(azl)).Select(f => f.GetValue(menu) as azl).FirstOrDefault(c => c != null);
            if (controller == null) return false;
            controller.aty();
            return true;
        }

        // On the main menu: the game's character creation alone, for a character made for elsewhere (Co-op: for the host's
        // game). The new game is made behind it as New Game makes it (cnk.arf), then StartCharacterGeneration runs; the
        // Rules screen (difficulty, XP system: the game's, not the character's) is skipped. Pair it with
        // GameEvents.CharacterCreated (BackToMainMenu = true), else Accept starts that new game. False: not on the main menu.
        [Preview("multiplayer: built for Co-op so far")] public static bool OpenCharacterCreation()
        {
            if (!(UiPopups.Screen() is a8k)) return false;
            CreationOnly = true;     // the new game behind it isn't a game (yet): mods' NewGame / GameStarted wait (Saves.CreationClosing)
            bpo.GetServiceOrThrow<cnk>().arf();
            kl.a();
            bpo.GetServiceOrThrow<d4m>().avp(dla.a(TimelapseVertigo.Common.TimelapseWorkflows.StartCharacterGeneration));
            Log.Write("world: character creation (no rules screen)");
            return true;
        }

        internal static bool CreationOnly;
        internal static string StartAs;

        // On the main menu: the game's New Game (its Rules screen: difficulty, XP system), then the game starts with the
        // character in this .char (made earlier, e.g. with OpenCharacterCreation) instead of character creation. False:
        // not on the main menu, or no such file.
        [Preview("multiplayer: built for Co-op so far")] public static bool StartNewGameAs(string characterFile)
        {
            if (string.IsNullOrEmpty(characterFile) || !System.IO.File.Exists(characterFile)) return false;
            StartAs = characterFile;
            if (StartNewGame()) return true;
            StartAs = null;
            return false;
        }

        // h1.a(): character creation about to open. With StartAs: the file's sheet becomes the new game's player and the
        // game starts (as creation's Accept does: cnk.aqu().a(sheet), StartGameInLocale).
        internal static bool CreationOpening()
        {
            var file = StartAs;
            StartAs = null;
            if (file == null) return true;
            try
            {
                var f = ReadFile.Invoke(null, new object[] { file });
                var sheet = f == null ? null : FileSheet.GetValue(f) as au2;
                if (sheet == null) { Log.Write("world: " + file + " has no player sheet: character creation instead"); return true; }
                bpo.GetServiceOrThrow<cnk>().aqu().a(sheet);
                Log.Write("world: new game started as " + sheet.Name + " (" + System.IO.Path.GetFileName(file) + ")");
                bpo.GetServiceOrThrow<d4m>().avp(dla.a(TimelapseVertigo.Common.TimelapseWorkflows.StartGameInLocale));
                return false;
            }
            catch (Exception ex) { Log.Write("world: starting as " + file + ": " + ex.Message + "; character creation instead"); return true; }
        }

        // What a character file (.char) has that a game using only these expansions wouldn't: feats and skills of another
        // expansion (their DLC), a level above 25 or specializations without Expedition (the game's veteran levels),
        // items this game's data doesn't have, a newer game version. Read from the file; nothing is built. Empty: it
        // fits. Lines for a player, e.g. "the feat Last Stand (Expedition)".
        // False: character creation offers no Import (the button is hidden, and its dialog doesn't open). Co-op turns it
        // off while hosting or joined: only characters made in creation, nothing from another game or modded setup.
        [Preview("multiplayer: built for Co-op so far")] public static bool CreationImport = true;

        // newOnly: also refuse a character that isn't fresh from creation (level above 1, or any XP).
        [Preview("multiplayer: built for Co-op so far")] public static IList<string> CharacterFileProblems(string path, IEnumerable<string> expansions, bool newOnly = false)
        {
            var problems = new List<string>();
            var on = new HashSet<string>((expansions ?? new string[0]).Select(x => x.ToLowerInvariant()));
            object file;
            try { file = System.IO.File.Exists(path) ? ReadFile.Invoke(null, new object[] { path }) : null; }
            catch (Exception ex) { problems.Add("the character file can't be read here (" + (ex.InnerException ?? ex).Message + ")"); return problems; }
            var sheet = file == null ? null : FileSheet.GetValue(file) as au2;
            if (sheet == null) { problems.Add("not a character file this game can read"); return problems; }
            if (newOnly)
            {
                var xp = sheet is e3q ? ((e3q)sheet).i() : 0;
                if (sheet.Level > 1 || xp > 0) problems.Add("not a new character (level " + sheet.Level + ", " + xp + " XP): only characters made in creation can join");
            }
            var made = FileVersion.GetValue(file) as Version;
            if (made != null && made > dm9.d) problems.Add("made with a newer game version (" + made + ")");
            Func<string, string> dlc = id => Expansions.Name(id);
            foreach (var feat in sheet.Feats)
                if (!string.IsNullOrEmpty(feat.DLC) && !on.Contains(feat.DLC.ToLowerInvariant())) problems.Add("the feat " + FeatName(feat) + " (" + dlc(feat.DLC) + ")");
            foreach (var skill in sheet.Skills)
                if (!string.IsNullOrEmpty(skill.DLC) && !on.Contains(skill.DLC.ToLowerInvariant()) && StatPoints(skill) > 0) problems.Add("the skill " + skill.GetType().Name + " (" + dlc(skill.DLC) + ")");
            if (!on.Contains("xpbl"))
            {
                if (sheet.Level > aiy.bk) problems.Add("level " + sheet.Level + " (above " + aiy.bk + " needs Expedition)");
                if (sheet.Specializations != null && sheet.Specializations.Count > 0 && sheet.Specializations.Values.Sum(v => v.Value) > 0) problems.Add("specializations (Expedition)");
            }
            var unknown = sheet.Bag.z2().Concat(sheet.Gear.i()).Where(i => i != null && i.Definition == null).ToList();
            if (unknown.Count > 0) problems.Add(unknown.Count + " item(s) this game doesn't have");
            return problems;
        }
        static string FeatName(ai8 feat)
        {
            var p = feat.GetType().GetProperty("Name", BindingFlags.Instance | BindingFlags.Public);
            var n = p == null ? null : p.GetValue(feat, null) as string;
            return string.IsNullOrEmpty(n) ? feat.GetType().Name : n;
        }
        // A skill's own points (Stat<int>'s value, "S4:V").
        static int StatPoints(object stat)
        {
            for (var t = stat.GetType(); t != null; t = t.BaseType)
            {
                var f = t.GetField("b", BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly);
                if (f != null && f.FieldType == typeof(int)) return (int)f.GetValue(stat);
            }
            return 0;
        }

        // A sheet with no body yet (character creation's) written as a .char, as Character.ExportTo writes a body's.
        internal static bool WriteSheet(au2 sheet, string path, DateTime made)
        {
            var game = bpo.GetService<cnk>();
            if (sheet == null || game == null || game.aqu() == null) return false;
            var file = new ajk();
            FileVersion.SetValue(file, (Version)dm9.d.Clone());
            FileDlcs.SetValue(file, new System.Collections.Generic.List<string>(dk2.b()));
            FileSheet.SetValue(file, sheet);
            FileDate.SetValue(file, made);
            FileDifficulty.SetValue(file, game.aqu().m());
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            // Creation's sheet still has the creation screen's handlers on it (its events), which can't be written (the game's
            // own export takes a body's handlers off first, as ExportTo does): every handler in the sheet comes off while
            // it's written and goes back after.
            var detached = DetachHandlers(sheet);
            try { WriteFile.Invoke(file, new object[] { path }); }
            catch (TargetInvocationException ex) { throw ex.InnerException ?? ex; }
            finally { foreach (var d in detached) d.Key.Key.SetValue(d.Key.Value, d.Value); }
            return true;
        }

        // Every delegate field in an object graph set to null; returned (field, owner) -> the delegate, to put back.
        static List<KeyValuePair<KeyValuePair<FieldInfo, object>, object>> DetachHandlers(object root)
        {
            var found = new List<KeyValuePair<KeyValuePair<FieldInfo, object>, object>>();
            var seen = new HashSet<object>(ReferenceEquality.Instance);
            var todo = new Stack<object>();
            todo.Push(root);
            while (todo.Count > 0 && seen.Count < 200000)
            {
                var o = todo.Pop();
                if (o == null || o is string || o is Delegate || o.GetType().IsPrimitive || o.GetType().IsEnum || !seen.Add(o)) continue;
                var arr = o as Array;
                if (arr != null) { if (!arr.GetType().GetElementType().IsPrimitive) foreach (var e in arr) if (e != null && !e.GetType().IsValueType) todo.Push(e); continue; }
                for (var t = o.GetType(); t != null && t != typeof(object); t = t.BaseType)
                    foreach (var f in t.GetFields(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly))
                    {
                        if (f.IsNotSerialized || f.FieldType.IsPrimitive || f.FieldType.IsEnum || f.FieldType == typeof(string) || f.FieldType.IsValueType) continue;
                        var v = f.GetValue(o);
                        if (v == null) continue;
                        if (v is Delegate) { found.Add(new KeyValuePair<KeyValuePair<FieldInfo, object>, object>(new KeyValuePair<FieldInfo, object>(f, o), v)); f.SetValue(o, null); }
                        else todo.Push(v);
                    }
            }
            return found;
        }
        sealed class ReferenceEquality : IEqualityComparer<object>
        {
            internal static readonly ReferenceEquality Instance = new ReferenceEquality();
            public new bool Equals(object a, object b) { return ReferenceEquals(a, b); }
            public int GetHashCode(object o) { return System.Runtime.CompilerServices.RuntimeHelpers.GetHashCode(o); }
        }

        // In a game: back to the main menu as the in-game menu's Main Menu does (the game's QuitGameFromLocale workflow,
        // without saving). False: not in a game.
        [Preview("multiplayer: built for Co-op so far")] public static bool QuitToMainMenu()
        {
            if (Player == null) return false;
            bpo.GetServiceOrThrow<d4m>().avp(dla.a(TimelapseVertigo.Common.TimelapseWorkflows.QuitGameFromLocale));
            Log.Write("world: quitting to the main menu");
            return true;
        }

        internal static void SetPlayerFlag(c5x e, bool on)
        {
            if (Flags == null) return;
            var f = (e2b)Flags.GetValue(e);
            Flags.SetValue(e, on ? f | e2b.d : f & ~e2b.d);
        }
    }

    public sealed partial class Character
    {
        // Takes it out of the world now (as the game removes a character: c5x.w(), an()). Not the player.
        [Preview("multiplayer: built for Co-op so far")] public bool RemoveFromWorld()
        {
            var e = Entity as c5x;
            if (e == null || e.IsPlayer || e.Area == null || Simulation.Refuses("RemoveFromWorld")) return false;
            e.w(); e.an();
            return true;
        }

        // Swaps in the sheet from a .char file (its items, XP, skills, level...) in place: the same body, where it stands,
        // its health kept. For a character from SpawnFromCharacterFile (not the player: for the player the HUD's portrait
        // and some views stayed on the old sheet; build a new body and MakePlayer instead). Cheaper than a new body.
        // False: not such a character, or the file has no sheet.
        [Preview("multiplayer: built for Co-op so far")] public bool ReplaceSheet(string path)
        {
            var body = Entity as c5x; var aspect = body == null ? null : body.GetAspect<amc>();
            var old = aspect == null ? null : aspect.aa() as au2;
            if (old == null || IsPlayer || !System.IO.File.Exists(path) || World.SheetProvider == null || Simulation.Refuses("ReplaceSheet")) return false;
            var file = World.ReadFile.Invoke(null, new object[] { path });
            var sheet = file == null ? null : World.FileSheet.GetValue(file) as au2;
            if (sheet == null) return false;
            float health = Health;
            sheet.Uid = old.Uid;     // the same character to the game (who did what)
            sheet.a((b0b)body);
            World.SheetProvider.SetValue(aspect, new StandAloneProvider<efk>(sheet));
            Health = Math.Min(health, MaxHealth);
            return true;
        }

        // It has a whole player sheet (the player, or a character from SpawnFromCharacterFile).
        [Preview("multiplayer: built for Co-op so far")] public bool HasPlayerSheet { get { var a = (Entity as c5x) == null ? null : ((c5x)Entity).GetAspect<amc>(); return a != null && a.aa() is au2; } }

        // Writes its sheet as it is now to a .char file (the game's export format; a new game's Import reads it). False:
        // it has no player sheet.
        [Preview("multiplayer: built for Co-op so far")] public bool ExportTo(string path) { return ExportTo(path, DateTime.Now); }
        // With the date the file says it was made (the import dialog shows it): a fixed one gives the same bytes for the same
        // sheet, to tell whether it changed.
        [Preview("multiplayer: built for Co-op so far")] public bool ExportTo(string path, DateTime made)
        {
            var body = Entity as c5x;
            var aspect = body == null ? null : body.GetAspect<amc>();
            var sheet = aspect == null ? null : aspect.aa() as au2;
            var game = bpo.GetService<cnk>();
            if (sheet == null || game == null || game.aqu() == null) return false;
            var file = new ajk();
            World.FileVersion.SetValue(file, (Version)dm9.d.Clone());
            World.FileDlcs.SetValue(file, new System.Collections.Generic.List<string>(dk2.b()));
            World.FileSheet.SetValue(file, sheet);
            World.FileDate.SetValue(file, made);
            World.FileDifficulty.SetValue(file, game.aqu().m());
            var dir = System.IO.Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) System.IO.Directory.CreateDirectory(dir);
            // As the game's own export (d6f.arv): the HUD's and the body's handlers on the sheet come off while it's written
            // (BinaryFormatter would try to write them), and go back on after.
            // Only the player's sheet carries the HUD's handlers: another character's export leaves the HUD (and the open windows
            // it rebinds) alone; rebinding failed under an open barter window.
            var screens = body.IsPlayer ? bpo.GetService<ps>() : null;
            var su = body.GetAspect<su>();
            bool live = body.Initialized;
            if (screens != null) screens.amy();
            try
            {
                if (live) { aspect.al(); if (su != null) su.l(); }
                try { World.WriteFile.Invoke(file, new object[] { path }); }
                finally { if (live) { aspect.w(); if (su != null) su.n(); } }
            }
            finally { if (screens != null) screens.amx(); }
            return true;
        }
    }
}
