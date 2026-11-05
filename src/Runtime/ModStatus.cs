// UnderRailWorks: status effects defined by mods (tModLoader's ModBuff). The game's side (measured from its code;
// ~/UnderRailWorks-research/specs/music-console-feats-ai.md section 5): a status effect is a saved object of a stock
// class; the food buff (c9i) is the one whose name, icon and description are plain data. A mod status is such an
// effect with the code "mod:<mod>:<id>", shown in the status bar like any other; its behaviour is the mod's (OnTick,
// or Works' events and combat hooks checking Has).
// Saves: mod statuses never reach a save file. Works takes them out of every character's effects right before the game
// writes global data or stores a zone and puts them back after; their owner, time left and stacks go into the save's
// UnderRailWorks.json and are re-applied when it loads (the player's, and characters on the loaded map by Key).
// Caveat: eating food removes every food effect in the game, mod statuses included (re-apply in GameEvents.ItemUsed
// if that matters).
//   var bleeding = ModStatuses.Define(this, "bleeding", "Bleeding", "Losing 5 health every turn.", "Status.Poison");
//   bleeding.OnTick += (who, stacks) => who.Health -= 5 * stacks;
//   bleeding.Apply(target, turns: 3);
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class ModStatus
    {
        public string Mod { get; internal set; }
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string Description { get; internal set; }
        public string Icon { get; internal set; }
        // The code the game sees (StatusChange.Code, Character.HasStatusEffect).
        public string Code { get { return "mod:" + Mod + ":" + Id; } }
        // Each turn it is on a character (each owner's turn in combat, every 5 s outside): the character, its stacks.
        public event Action<Character, int> OnTick;
        internal void Tick(Character c, int stacks) { var t = OnTick; if (t != null) try { t(c, stacks); } catch (Exception ex) { ModErrors.Report(Mod, "status " + Id + " tick", ex); } }

        // Puts it on a character for turns (5 s each outside combat); applying again refreshes it.
        public void Apply(Character who, int turns) { ModStatusStore.Apply(this, who, turns * 5000.0, 1); }
        public bool Has(Character who) { return who != null && who.HasStatusEffect(Code); }
        public void Remove(Character who) { ModStatusStore.Remove(this, who); }
    }

    public static class ModStatuses
    {
        // icon: an icon name the game knows ("Status.Poison", "Status.PsiBoost", "Status.5"..., or one from Icons.Register).
        public static ModStatus Define(WorksMod mod, string id, string name, string description, string icon)
        {
            var s = new ModStatus { Mod = mod.Id, Id = id, Name = name, Description = description, Icon = icon };
            ModStatusStore.Types[s.Code] = s;
            return s;
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class ModStatusStore
    {
        internal static readonly Dictionary<string, ModStatus> Types = new Dictionary<string, ModStatus>(StringComparer.Ordinal);
        static readonly FieldInfo CodeField = typeof(els).GetField("m_c", BindingFlags.Instance | BindingFlags.NonPublic) ?? typeof(els).GetField("c", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo OwnerField = typeof(br5).GetField("m_a", BindingFlags.Instance | BindingFlags.NonPublic) ?? typeof(br5).GetField("a", BindingFlags.Instance | BindingFlags.NonPublic);
        static readonly FieldInfo ListField = typeof(br5).GetField("m_c", BindingFlags.Instance | BindingFlags.NonPublic) ?? typeof(br5).GetField("c", BindingFlags.Instance | BindingFlags.NonPublic);

        static br5 Effects(Character who) { var c = who == null ? null : who.Entity as c5x; var a = c == null ? null : c.GetAspect<amc>(); return a == null || a.aa() == null ? null : a.aa().StatusEffects; }

        internal static void Apply(ModStatus s, Character who, double ms, int stacks)
        {
            var fx = Effects(who);
            if (fx == null) return;
            Remove(s, who);     // refresh: one at a time
            var carrier = new c9i("works", s.Name, s.Icon, null, null, s.Description);
            CodeField.SetValue(carrier, s.Code);
            carrier.Duration = (int)Math.Min(int.MaxValue, ms);
            carrier.TimeLeft = ms;
            if (stacks > 1) carrier.Stacks = stacks;
            fx.a(carrier, false, false, false);
        }

        internal static void Remove(ModStatus s, Character who)
        {
            var fx = Effects(who);
            if (fx == null) return;
            foreach (var e in fx.d().Where(e => e.Code == s.Code).ToList()) fx.d(e);
        }

        // Every mod status on the player and the characters of the loaded map, with its owner's effects. Found by code:
        // the game applies a copy of the effect it is given, so the carrier object itself isn't what it keeps.
        static IEnumerable<KeyValuePair<els, br5>> Live()
        {
            var seen = new HashSet<br5>();
            var people = new List<Character>();
            if (World.Player != null && World.Player.IsCharacter) people.Add(World.Player);
            try { people.AddRange(World.Characters); } catch { }
            foreach (var c in people)
            {
                var fx = Effects(c);
                if (fx == null || !seen.Add(fx)) continue;
                foreach (var e in ((List<els>)ListField.GetValue(fx)).ToList())
                    if (e != null && e.Code != null && e.Code.StartsWith("mod:", StringComparison.Ordinal)) yield return new KeyValuePair<els, br5>(e, fx);
            }
        }

        // --- Out of save files and back --------------------------------------------------------------------------
        [ThreadStatic] static List<KeyValuePair<els, br5>> stripped;
        internal static void Strip()
        {
            stripped = Live().ToList();
            foreach (var kv in stripped) ((List<els>)ListField.GetValue(kv.Value)).Remove(kv.Key);     // no events, no stat changes (carriers hold no stat effects)
        }
        internal static void PutBack()
        {
            var s = stripped; stripped = null;
            if (s == null) return;
            foreach (var kv in s) { var list = (List<els>)ListField.GetValue(kv.Value); if (!list.Contains(kv.Key)) list.Add(kv.Key); }
        }

        // Into the save's UnderRailWorks.json (before the files are written), and back after a load.
        internal static void Record()
        {
            var list = new List<object>();
            foreach (var kv in Live())
            {
                var who = Combat.OfData(OwnerField.GetValue(kv.Value) as efk);
                if (who == null) continue;
                list.Add(new Dictionary<string, object> { { "code", kv.Key.Code }, { "owner", who.IsPlayer ? "player" : who.Key }, { "ms", kv.Key.TimeLeft }, { "stacks", (double)kv.Key.Stacks } });
            }
            if (list.Count > 0) SaveFiles.Current["UnderRailWorks.status"] = new Dictionary<string, object> { { "list", list } };
            else SaveFiles.Current.Remove("UnderRailWorks.status");
        }

        internal static void Restore()
        {
            object o;
            if (!SaveFiles.Current.TryGetValue("UnderRailWorks.status", out o) || !(o is Dictionary<string, object>)) return;
            var list = ((Dictionary<string, object>)o)["list"] as List<object>;
            if (list == null) return;
            foreach (Dictionary<string, object> e in list)
            {
                ModStatus s;
                if (!Types.TryGetValue((string)e["code"], out s)) continue;     // its mod isn't installed: the entry stays in the file
                var owner = (string)e["owner"];
                var who = owner == "player" ? World.Player : World.Characters.FirstOrDefault(c => c.Key == owner);
                if (who != null) Apply(s, who, Convert.ToDouble(e["ms"]), Convert.ToInt32(e["stacks"]));
            }
        }

        // --- Ticks: els.o() (an owner's combat turn) and els.e(ms) (real time; true when a tick fired) ----------
        static void TurnTick(els __instance) { Tick(__instance); }
        static void TimeTick(els __instance, bool __result) { if (__result) Tick(__instance); }
        static void Tick(els e)
        {
            ModStatus s;
            if (e.Code == null || !e.Code.StartsWith("mod:", StringComparison.Ordinal) || !Types.TryGetValue(e.Code, out s)) return;
            var owner = Live().FirstOrDefault(kv => ReferenceEquals(kv.Key, e)).Value;     // a tick is rare (one a turn): a scan is fine
            var who = owner == null ? null : Combat.OfData(OwnerField.GetValue(owner) as efk);
            if (who != null) s.Tick(who, e.Stacks);
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(ModStatusStore).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(els).GetMethod("o", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null), postfix: m("TurnTick"));
            harmony.Patch(typeof(els).GetMethod("e", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(double) }, null), postfix: m("TimeTick"));
        }
    }
}
