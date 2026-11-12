// UnderRailWorks: new base abilities and skills from mods. The game keeps its abilities and skills as fixed lists of
// its own classes inside the character (bn2 / ech), saved in saves and exported .char files, so new ones there would
// break both for the stock game. Mod ones are Works' own, for the player character:
// - values in the owner mod's CharacterData (saved with the game, carried into exports and imports);
// - points from the game's own counters (ability points / skill points, plain saved numbers), so the game's "spend
//   all your points" checks and level ups count them;
// - an "Other" tab on the character screen (iw): editable at creation and level up, with the game's per-level skill cap
//   (csn.a(level)); Cancel on a level up puts the values back as they were when the screen opened (the game restores
//   its own points from its snapshot, agw);
// - a skill's value as the game computes one: points x (1 + the best related ability's weight x the game's ability
//   factor c2h.d), with game or mod abilities.
// Their effects are the mods' (Stats.Modify bonuses, checks in their own dialogs...): the game's checks don't know them.
//   var survival = CustomStats.AddSkill(this, "survival", "Survival", "Foraging, tracking, enduring.",
//       new Dictionary<string, float> { { "Constitution", 1f }, { "Perception", 0.5f } });
//   if (survival.Value >= 40) ...
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public abstract class CustomStat
    { internal CustomStat() { }
        internal WorksMod Owner;
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string Description { get; internal set; }
        internal abstract string Key { get; }
        internal abstract int Default { get; }
        // The player's points in it (what was spent).
        public int Base
        {
            get { var v = SaveData.CharacterOf(Owner).Get<object>(Key, null); return v == null ? Default : Convert.ToInt32(v); }
            set { SaveData.CharacterOf(Owner).Set(Key, value); CustomStats.Raise(); }
        }
        public abstract int Value { get; }
    }

    public sealed class ModBaseAbility : CustomStat
    { internal ModBaseAbility() { }
        internal int Start;
        internal override string Key { get { return "works_ability_" + Id; } }
        internal override int Default { get { return Start; } }
        public override int Value { get { return Base; } }
    }

    public sealed class ModSkill : CustomStat
    { internal ModSkill() { }
        internal Dictionary<string, float> Abilities;
        internal override string Key { get { return "works_skill_" + Id; } }
        internal override int Default { get { return 0; } }
        // Points x (1 + the best related ability's weight x the game's factor for its value), as the game's skills.
        public override int Value { get { var p = World.Player; return ValueFor(p == null ? null : p.GameData); } }
        // For a character's data (the creation screen's character isn't World.Player yet).
        internal int ValueFor(efk data)
        {
            int b = Base;
            if (b <= 0 || data == null || Abilities == null || Abilities.Count == 0) return b;
            float best = 0;
            foreach (var kv in Abilities) best = Math.Max(best, kv.Value * c2h.d(CustomStats.AbilityValue(data, kv.Key)));
            return (int)(b * (1 + best));
        }
    }

    public static class CustomStats
    {
        internal static readonly List<ModBaseAbility> AbilityList = new List<ModBaseAbility>();
        internal static readonly List<ModSkill> SkillList = new List<ModSkill>();
        public static IList<ModBaseAbility> Abilities { get { return AbilityList.ToList(); } }
        public static IList<ModSkill> SkillsAdded { get { return SkillList.ToList(); } }
        // A new base ability (start: its value for a new character; the game's start at 5).
        public static ModBaseAbility AddBaseAbility(WorksMod mod, string id, string name, string description, int start = 5)
        {
            var a = new ModBaseAbility { Owner = mod, Id = mod.Id + "." + id, Name = name, Description = description, Start = start };
            AbilityList.Add(a);
            return a;
        }
        // A new skill; abilities: the game's (Strength...) or mod abilities (their id), with weights as the game's (1, 0.5...).
        public static ModSkill AddSkill(WorksMod mod, string id, string name, string description, IDictionary<string, float> abilities = null)
        {
            var s = new ModSkill { Owner = mod, Id = mod.Id + "." + id, Name = name, Description = description,
                Abilities = abilities == null ? new Dictionary<string, float>() : new Dictionary<string, float>(abilities, StringComparer.OrdinalIgnoreCase) };
            SkillList.Add(s);
            return s;
        }
        // After a value changed (the character screen, a mod).
        public static readonly Hook<Action> Changed = new Hook<Action>("CustomStatsChanged");
        internal static void Raise() { if (Changed.Any) Changed.Run(h => h()); }

        internal static int AbilityValue(efk data, string name)
        {
            var mod = AbilityList.FirstOrDefault(a => string.Equals(a.Id, name, StringComparison.OrdinalIgnoreCase) || string.Equals(a.Name, name, StringComparison.OrdinalIgnoreCase));
            if (mod != null) return mod.Value;
            var a2 = data.BaseAbilities.a(name);
            return a2 == null ? 0 : a2.g();
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class CustomStatsTab
    {
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly FieldInfo Tabs = typeof(iw).GetField("m_c", Inst) ?? typeof(iw).GetField("c", Inst);
        // The point counters as the game's own properties (their setters raise the change events the screen's tabs
        // refresh on; writing the fields left the Skills tab's "remaining" stale). Found by their getters: g() ability
        // points, h() skill points (obfuscated property names).
        // Getter / setter pairs: g()/b(int) ability points, h()/d(int) skill points.
        sealed class PointCounter
        {
            readonly MethodInfo get, set;
            internal PointCounter(string getter, string setter)
            {
                get = typeof(e3q).GetMethod(getter, Inst | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                set = typeof(e3q).GetMethod(setter, Inst | BindingFlags.DeclaredOnly, null, new[] { typeof(int) }, null);
            }
            internal int GetValue(e3q c, object _) { return (int)get.Invoke(c, null); }
            internal void SetValue(e3q c, int v, object _) { set.Invoke(c, new object[] { v }); }
        }
        static readonly PointCounter AbilityPoints = new PointCounter("g", "b"), SkillPoints = new PointCounter("h", "d");
        static readonly Dictionary<CustomStat, int> opened = new Dictionary<CustomStat, int>();

        // iw.c(): the screen's tabs were added (Base, Skills, Feats...): ours after them.
        static void Built(iw __instance)
        {
            if (CustomStats.AbilityList.Count == 0 && CustomStats.SkillList.Count == 0) return;
            try
            {
                var tabs = Tabs.GetValue(__instance) as dpg;
                if (tabs == null) return;
                opened.Clear();
                foreach (var s in All()) opened[s] = s.Base;     // also after an import rebuilt the tabs: the floor is the imported value
                var page = new cvh { Text = "Other" };
                tabs.n().Add(page);
                Fill(__instance, UiElement.Of(page));
            }
            catch (Exception ex) { Log.Write("custom stats: tab: " + ex); }
        }

        // The screen opened: the values to go back to on Cancel (and the floor for minus).
        static void Opened(iw __instance)
        { try {
            screen = __instance;
            opened.Clear();
            foreach (var s in All()) opened[s] = s.Base;
        } catch (Exception ex_) { PatchGuard.Failed("CustomStats.Opened", ex_); }
    }

        // iw.a(cku): closing; cku.b is Cancel.
        static void Closing(object __instance, cku __0)
        { try {
            if (!(__instance is iw) || __0 != cku.b) return;     // amm.a(cku) closes every window: the character screen's Cancel
            if (Hud.Current == null) CharacterFiles.CreationCancelled();     // no game on screen: the creation screen (an import is a9m.b at any level)
            foreach (var kv in opened) if (kv.Key.Base != kv.Value) kv.Key.Base = kv.Value;
        } catch (Exception ex_) { PatchGuard.Failed("CustomStats.Closing", ex_); }
    }

        static IEnumerable<CustomStat> All() { return CustomStats.AbilityList.Cast<CustomStat>().Concat(CustomStats.SkillList); }

        static void Fill(iw screen, UiElement page)
        {
            var c = screen.g();
            bool editable = screen.h() != a9m.a;
            bool creating = screen.h() == a9m.c;
            int y = Ui.S(16), line = Ui.LineHeight;
            Ui.Add(page, Ui.Custom(Ui.S(600), line + 4, cv =>
            {
                var t = "Ability points: " + AbilityPoints.GetValue(c, null) + "     Skill points: " + SkillPoints.GetValue(c, null);
                cv.Text(t, cv.Bounds.X, cv.Bounds.Y + 2, new Color(215, 215, 205));
            }), Ui.S(20), y);
            y += line + Ui.S(14);
            Action<string> heading = text => { Ui.Add(page, Ui.Custom(Ui.S(600), line + 4, cv => cv.Text(text, cv.Bounds.X, cv.Bounds.Y + 2, new Color(230, 160, 60))), Ui.S(20), y); y += line + Ui.S(8); };
            if (CustomStats.AbilityList.Count > 0)
            {
                heading("Base abilities");
                foreach (var a in CustomStats.AbilityList) Row(page, c, a, true, editable, creating, ref y);
                y += Ui.S(10);
            }
            if (CustomStats.SkillList.Count > 0)
            {
                heading("Skills");
                foreach (var s in CustomStats.SkillList) Row(page, c, s, false, editable, creating, ref y);
            }
        }

        static void Row(UiElement page, e3q c, CustomStat stat, bool ability, bool editable, bool creating, ref int y)
        {
            int line = Ui.LineHeight;
            var label = Ui.Custom(Ui.S(360), line + 6, cv =>
            {
                int value = stat is ModSkill ? ((ModSkill)stat).ValueFor(c) : stat.Value;
                var v = stat.Base == value ? stat.Base.ToString() : stat.Base + "  [" + value + "]";
                cv.Text(stat.Name, cv.Bounds.X, cv.Bounds.Y + 3, new Color(181, 120, 40));
                cv.Text(v, cv.Bounds.X + Ui.S(260), cv.Bounds.Y + 3, Color.White);
            });
            Ui.Add(page, label, Ui.S(30), y);
            Ui.Tooltip(label, stat.Name + "\n" + stat.Description);
            if (editable)
            {
                Ui.Add(page, Ui.SmallButton("+", Ui.S(24), line + 4, () => Change(c, stat, ability, creating, +1)), Ui.S(400), y);
                Ui.Add(page, Ui.SmallButton("-", Ui.S(24), line + 4, () => Change(c, stat, ability, creating, -1)), Ui.S(428), y);
            }
            y += line + Ui.S(10);
        }

        static void Change(e3q c, CustomStat stat, bool ability, bool creating, int step)
        {
            try
            {
                var counter = ability ? AbilityPoints : SkillPoints;
                if (counter == null) { Log.Write("custom stats: no point counter"); return; }
                int points = counter.GetValue(c, null), now = stat.Base;
                int floor; opened.TryGetValue(stat, out floor);
                if (creating && ability) floor = CharacterRules.AbilityMinAtCreation;
                else if (creating) floor = 0;
                if (step > 0)
                {
                    if (points <= 0) return;
                    if (ability && creating && now >= CharacterRules.AbilityMaxAtCreation) return;
                    if (!ability && now >= csn.a(c.Level)) return;     // the game's per-level skill cap
                    counter.SetValue(c, points - 1, null);
                }
                else
                {
                    if (now <= floor) return;
                    counter.SetValue(c, points + 1, null);
                }
                stat.Base = now + step;
                RefreshTabs();
            }
            catch (Exception ex) { Log.Write("custom stats: " + ex.Message); }
        }

        // The game's Base (e44) and Skills (dh5) tabs show "remaining points" refreshed only on their own clicks: their
        // refresh a() after a change here.
        static iw screen;
        static readonly Type BaseTab = typeof(iw).Assembly.GetType("e44"), SkillTab = typeof(iw).Assembly.GetType("dh5");
        static void RefreshTabs()
        {
            var tabs = screen == null ? null : Tabs.GetValue(screen) as dpg;
            if (tabs == null) return;
            foreach (var page in tabs.n())
                foreach (var child in page.Controls)
                {
                    var t = child.GetType();
                    if (t != BaseTab && t != SkillTab) continue;
                    var refresh = t.GetMethod("a", Inst | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null);
                    if (refresh != null) try { refresh.Invoke(child, null); } catch (Exception ex) { Log.Write("custom stats: refresh: " + ex.Message); }
                }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(CustomStatsTab).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            harmony.Patch(typeof(iw).GetMethod("c", Inst | BindingFlags.DeclaredOnly, null, Type.EmptyTypes, null), postfix: m("Built"));
            harmony.Patch(typeof(iw).GetConstructor(new[] { typeof(e3q), typeof(a9m), typeof(bool) }), postfix: m("Opened"));
            harmony.Patch(typeof(amm).GetMethod("a", Inst | BindingFlags.DeclaredOnly, null, new[] { typeof(cku) }, null), prefix: m("Closing"));
        }
    }
}
