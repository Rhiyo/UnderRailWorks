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
        static readonly FieldInfo UidField = typeof(efk).GetField("m_c", Inst | BindingFlags.DeclaredOnly) ?? typeof(efk).GetFields(Inst | BindingFlags.DeclaredOnly).FirstOrDefault(f => f.Name == "c" && f.FieldType == typeof(Guid));
        static readonly FieldInfo Flags = typeof(c5x).GetField("g", Inst | BindingFlags.DeclaredOnly);
        static readonly FieldInfo Footprint = typeof(b0b).GetFields(Inst | BindingFlags.DeclaredOnly).FirstOrDefault(f => f.Name == "z" && f.FieldType == typeof(Vector2?));

        // A character built from a .char file as the game builds the player (its look, sheet, inventory), standing at
        // tile x, y of the player's area, facing south. Not the player: its sheet is its own, and its AI runs unless a mod
        // takes it (CharacterControl). Like Spawn it's never saved (taken out around saves) and stays in this zone.
        // Null: no such file, not a player sheet, or no room there.
        public static Character SpawnFromCharacterFile(string path, int x, int y)
        {
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
        public static bool MakePlayer(Character c)
        {
            var game = bpo.GetService<cnk>();
            var state = game == null ? null : game.aqu();
            var newBody = c == null ? null : c.Entity as c5x;
            var newAspect = newBody == null ? null : newBody.GetAspect<amc>();
            var newSheet = newAspect == null ? null : newAspect.aa() as au2;
            if (state == null || newSheet == null || SheetProvider == null) return false;
            var oldSheet = state.v();
            if (oldSheet == newSheet) return true;
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
        public static bool StartNewGame()
        {
            var menu = UiPopups.Screen() as a8k;
            var controller = menu == null ? null : typeof(a8k).GetFields(Inst | BindingFlags.DeclaredOnly).Where(f => f.FieldType == typeof(azl)).Select(f => f.GetValue(menu) as azl).FirstOrDefault(c => c != null);
            if (controller == null) return false;
            controller.aty();
            return true;
        }

        static void SetPlayerFlag(c5x e, bool on)
        {
            if (Flags == null) return;
            var f = (e2b)Flags.GetValue(e);
            Flags.SetValue(e, on ? f | e2b.d : f & ~e2b.d);
        }
    }

    public sealed partial class Character
    {
        // It has a whole player sheet (the player, or a character from SpawnFromCharacterFile).
        public bool HasPlayerSheet { get { var a = (Entity as c5x) == null ? null : ((c5x)Entity).GetAspect<amc>(); return a != null && a.aa() is au2; } }

        // Writes its sheet as it is now to a .char file (the game's export format; a new game's Import reads it). False:
        // it has no player sheet.
        public bool ExportTo(string path) { return ExportTo(path, DateTime.Now); }
        // With the date the file says it was made (the import dialog shows it): a fixed one gives the same bytes for the same
        // sheet, to tell whether it changed.
        public bool ExportTo(string path, DateTime made)
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
            var screens = bpo.GetService<ps>();
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
