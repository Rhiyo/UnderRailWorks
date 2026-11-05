// UnderRailWorks: item definitions (stats), new items, tooltips. The game's side (measured from its code;
// ~/UnderRailWorks-research/specs/items-stores.md):
// - Every stat is on the item's definition (bk3: weapons bke, armour eya...). Items made from a data file ("library"
//   items: stores, loot, scripts) share one cached definition per path (ez2's cache, filled by ez2.b(path, bool)) and
//   save only the path; crafted items carry their own copy (saved whole).
// - So editing a library definition reaches every such item and is never saved (Items.EditDefinition); a crafted copy
//   or an item made unique (Item.MakeUnique) keeps its own definition, saved as stock types.
// - New items (Items.Define): a definition cloned from a stock one, answered by ez2.b for the path works\<mod>\<id>. A
//   save holds that path only; without the mod the stock game still loads it (the item shows as "Unknown" with no
//   stats), and keeps the path, so reinstalling the mod brings the item back.
// - Tooltips: cq4.a(item, viewer, ...) builds every item tooltip (inventory, trade, loot, crafting preview) as lines of
//   coloured sections (ecs / dl6); Items.ModifyTooltip adds lines.
//   Items.EditDefinition("weapons\\pistols\\...", d => { d.CritChance += 0.05; d.Name = "Lucky Pistol"; });
//   var path = Items.Define(this, "stimpack", "consumables\\medicine\\healthhypo", d => { d.Name = "Stim"; d.Value = 300; });
//   Items.ModifyTooltip(this, (item, tip) => tip.AddLine("Sells for " + item.Value, Color.Gold));
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using TimelapseVertigo.Rules.Combat;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public struct DamageRange
    {
        public string Type { get; set; }     // Mechanical, Heat, Cold, Electricity, Acid, Energy, Bio
        public int Min { get; set; }
        public int Max { get; set; }
    }

    // An item's definition: what every item of its kind shares (a library definition), or one item's own copy.
    public sealed class ItemDefinition
    {
        internal readonly bk3 Game;
        internal ItemDefinition(bk3 d) { Game = d; }
        public string Code { get { return Game.a5(); } set { Game.k(value); } }
        public string Name { get { return Game.az(); } set { Game.g(value); } }
        public string Description { get { return Game.a0(); } set { Game.i(value); } }
        // The icon's name ("Atlas.cell" or an Icons.Register name).
        public string Icon { get { return Game.a3(); } set { Game.f(value); } }
        public double? Value { get { return Game.bf(); } set { Game.b(value); } }
        public double Weight { get { return Game.a4(); } set { Game.h(value); } }
        public int MaxStack { get { return Game.be(); } set { Game.f(value); } }
        public bool IsWeapon { get { return Game is bke; } }
        public bool IsArmor { get { return Game is eya; } }

        // Weapons (0 / empty otherwise).
        public IList<DamageRange> Damage
        {
            get { var w = Game as bke; return w == null ? new List<DamageRange>() : w.l().Select(a => new DamageRange { Type = a.DamageType.ToString(), Min = a.LowerBoundary, Max = a.UpperBoundary }).ToList(); }
            set
            {
                var w = Game as bke; if (w == null || value == null) return;
                var list = w.l(); list.Clear();
                foreach (var r in value) { DamageType t; Enum.TryParse(r.Type, out t); list.Add(new a1q { DamageType = t, LowerBoundary = r.Min, UpperBoundary = r.Max }); }
            }
        }
        public int ActionPoints { get { var w = Game as bke; return w == null ? 0 : w.v(); } set { var w = Game as bke; if (w != null) w.a(value); } }
        public double CritChance { get { var w = Game as bke; return w == null ? 0 : w.ai(); } set { var w = Game as bke; if (w != null) w.f(value); } }
        public double CritDamage { get { var w = Game as bke; return w == null ? 0 : w.y(); } set { var w = Game as bke; if (w != null) w.c(value); } }
        public int MinStrength { get { var w = Game as bke; return w == null ? 0 : w.i(); } set { var w = Game as bke; if (w != null) w.b(value); } }

        // Armour: threshold and resistance (percent) per damage type, and the armour penalty (percent).
        public IDictionary<string, Resistance> Resistances
        {
            get
            {
                var a = Game as eya; var r = new Dictionary<string, Resistance>();
                if (a != null) foreach (var d in a.i()) r[d.b().ToString()] = new Resistance { Threshold = d.a(), Percent = (int)Math.Round(d.c() * 100) };
                return r;
            }
        }
        public void SetResistance(string type, int threshold, int percent)
        {
            var a = Game as eya; if (a == null) return;
            DamageType t; if (!Enum.TryParse(type, out t)) throw new ArgumentException("no damage type " + type);
            var list = a.i();
            int i = list.FindIndex(d => d.b() == t);
            var v = i >= 0 ? list[i] : new du2();
            v.a(t); ResistanceThreshold.Invoke(v, threshold, out v); v.a(percent / 100.0);
            if (i >= 0) list[i] = v; else list.Add(v);
        }
        public double ArmorPenalty { get { var a = Game as eya; return a == null ? 0 : a.h() * 100; } set { var a = Game as eya; if (a != null) a.b(value / 100.0); } }
    }

    public sealed class Tooltip
    {
        internal ecs Game;
        // ecs.a (the lines) and dl6.a (a line's sections) clash with method names: read by reflection.
        static readonly FieldInfo LinesField = typeof(ecs).GetField("a");
        static readonly FieldInfo SectionsField = typeof(dl6).GetField("a");
        List<dl6> Lines { get { return (List<dl6>)LinesField.GetValue(Game); } }
        // Whether it is shown for a merchant's goods.
        public bool InTrade { get; internal set; }
        public int LineCount { get { return Lines.Count; } }
        // The text of a line (its sections joined).
        public string Line(int index) { var s = SectionsField.GetValue(Lines[index]) as System.Collections.IEnumerable; return s == null ? "" : string.Concat(s.Cast<bph>().Select(x => x.b)); }
        // In the game's light grey (its tooltip text colour), or a colour of the mod's.
        public void AddLine(string text) { AddLine(text, Color.LightGray); }
        public void InsertLine(int index, string text) { InsertLine(index, text, Color.LightGray); }
        public void AddLine(string text, Color colour) { Lines.Add(cq4.a(text, colour, null)); }
        public void InsertLine(int index, string text, Color colour) { var l = Lines; l.Insert(Math.Max(0, Math.Min(index, l.Count)), cq4.a(text, colour, null)); }
        // "label: value" in the game's two colours (grey label, white value).
        public void AddValue(string label, string value) { Lines.Add(cq4.a(label + ": ", Color.LightGray, value, Color.White)); }
        public void AddSeparator() { Lines.Add(new dl6()); }
        public void RemoveLine(int index) { var l = Lines; if (index >= 0 && index < l.Count) l.RemoveAt(index); }
        // The first line containing text (-1: none), e.g. to insert after the value line.
        public int Find(string text) { for (int i = 0; i < Lines.Count; i++) if (Line(i).IndexOf(text, StringComparison.OrdinalIgnoreCase) >= 0) return i; return -1; }
    }

    public static partial class Items
    {
        // The shared definition of the items made from path (loaded if needed). Edits here reach every such item and
        // last until the game closes; use EditDefinition to apply them every start.
        public static ItemDefinition Definition(string path) { var d = ez2.b(path); return d == null ? null : new ItemDefinition(d); }

        // Edits path's shared definition whenever the game loads it (and now, if it has). Never saved.
        public static void EditDefinition(WorksMod mod, string path, Action<ItemDefinition> edit) { ItemHooks.Edit(mod.Id, path, edit); }

        // A new kind of item: a copy of basePath's definition, edited, with the code "works.<mod>.<id>". Returns its
        // path ("works\<mod>\<id>") for Inventory.Add, stores, loot and content packs. See the file's header on saves.
        public static string Define(WorksMod mod, string id, string basePath, Action<ItemDefinition> edit) { return ItemHooks.Define(mod.Id, id, basePath, edit); }

        // Lines added to item tooltips (every window that shows them).
        public static void ModifyTooltip(WorksMod mod, Action<Item, Tooltip> edit) { ItemHooks.Tooltips.Add(mod, edit); }

        // Every item path in the game's data (and the mods' defined ones), optionally of one kind.
        public static IList<string> All(ItemKind? kind = null)
        {
            var root = System.IO.Path.Combine(System.IO.Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data"), "rules", "items");
            var paths = (System.IO.Directory.Exists(root) ? System.IO.Directory.GetFiles(root, "*.item", System.IO.SearchOption.AllDirectories)
                .Select(f => f.Substring(root.Length + 1, f.Length - root.Length - 6).Replace('/', '\\')) : Enumerable.Empty<string>()).Concat(ItemHooks.Defined.Keys);
            if (kind == null) return paths.ToList();
            return paths.Where(p => { bk3 d; try { d = ez2.b(p); } catch { d = null; } return d != null && Item.KindOf(d) == kind; }).ToList();
        }

        // Every new item's path the mods defined.
        public static IList<string> Defined { get { return ItemHooks.Defined.Keys.ToList(); } }
    }

    public enum ItemKind { Other, Weapon, Armor, Shield, Component, Ammo, Currency, Throwable, Consumable }

    public sealed partial class Item
    {
        // What sort of item it is (from the definition's class).
        public ItemKind Kind
        {
            get { return KindOf(Game.Definition); }
        }
        internal static ItemKind KindOf(bk3 d)
        {
            {
                if (d is bke) return ItemKind.Weapon;
                if (d is eya) return ItemKind.Armor;
                if (d is b3n) return ItemKind.Shield;
                if (d is a1k) return ItemKind.Component;
                if (d is dgr) return ItemKind.Ammo;
                if (d is a54) return ItemKind.Currency;
                if (d is cdk) return ItemKind.Throwable;
                if (d != null && d.bg()) return ItemKind.Consumable;
                return ItemKind.Other;
            }
        }
        // Quality tier 0 (junk) .. 5 (the game's cr1), and how worn it is (0..1; 1 for items without durability).
        public int Tier { get { return Game.Definition == null ? 0 : (int)Game.Definition.bc(); } }
        public double Condition
        {
            get
            {
                // Only gear wears (the game checks durability on worn and wielded items, skipping indestructible ones, a9());
                // other items keep MaxDurability at 1 and Durability at 0, which isn't "broken".
                var d = Game.Definition;
                var k = Kind;
                if (d == null || d.a9() || (k != ItemKind.Weapon && k != ItemKind.Armor && k != ItemKind.Shield)) return 1;
                var max = Game.MaxDurability;
                return max <= 0 ? 1 : Math.Max(0, Math.Min(1, Game.Durability / max));
            }
        }
        // A plain value kept on this one item (saved with it; the stock game keeps and ignores it). Primitive values.
        // What it can be used as: the names of the game's capabilities on its definition ("Food", "Medicine"...).
        public IList<string> Uses
        {
            get
            {
                var d = Game.Definition;
                var list = d == null ? null : d.a2();
                return list == null ? new List<string>() : list.Where(kv => kv.Value != null).Select(kv => kv.Value.Name).ToList();
            }
        }
        public object GetValue(string key) { var p = Game.CustomProperties; return p == null || !p.HasProperty(key) ? null : p[key]; }
        // Saved inside the item (II:CP), so the stock game reads it back: only plain values (a number, bool, string,
        // DateTime, Guid); anything else would make the save unloadable without the mod.
        public void SetValue(string key, object value)
        {
            if (value == null) { Game.CustomProperties.RemoveProperty(key); return; }
            var t = value.GetType();
            if (!(t.IsPrimitive || t == typeof(string) || t == typeof(decimal) || t == typeof(DateTime) || t == typeof(Guid)))
                throw new ArgumentException("Item.SetValue: only plain values (numbers, bool, string, DateTime, Guid), not " + t.FullName);
            Game.CustomProperties[key] = value;
        }
        public ItemDefinition Definition { get { return Game.Definition == null ? null : new ItemDefinition(Game.Definition); } }
        // This one item gets its own copy of the definition (as crafted items have), to edit without touching others.
        // Saved whole, with stock types only: the stock game shows it as edited.
        public void MakeUnique() { Game.c(); }
    }
}

namespace UnderRailWorks.Internal
{
    static class ItemHooks
    {
        sealed class Edit_ { public string Mod; public Action<ItemDefinition> Edit; }
        static readonly Dictionary<string, List<Edit_>> edits = new Dictionary<string, List<Edit_>>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, KeyValuePair<string, Action<ItemDefinition>>> Defined = new Dictionary<string, KeyValuePair<string, Action<ItemDefinition>>>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Hook<Action<Item, Tooltip>> Tooltips = new Hook<Action<Item, Tooltip>>("ModifyTooltip");
        [ThreadStatic] static bool inside;

        static string Norm(string p) { return (p ?? "").Replace('/', '\\').Trim('\\'); }

        internal static void Edit(string mod, string path, Action<ItemDefinition> edit)
        {
            List<Edit_> list;
            if (!edits.TryGetValue(Norm(path), out list)) edits[Norm(path)] = list = new List<Edit_>();
            list.Add(new Edit_ { Mod = mod, Edit = edit });
            // Already loaded: the cached definition(s) under any spelling of the path.
            foreach (var d in Cached(path)) Apply(mod, edit, d, path);
        }

        internal static string Define(string mod, string id, string basePath, Action<ItemDefinition> edit)
        {
            var path = "works\\" + mod + "\\" + id;
            Defined[path] = new KeyValuePair<string, Action<ItemDefinition>>(Norm(basePath), d => { d.Code = "works." + mod + "." + id; if (edit != null) edit(d); });
            return path;
        }

        static void Apply(string mod, Action<ItemDefinition> edit, bk3 d, string path)
        {
            try { edit(new ItemDefinition(d)); }
            catch (Exception ex) { ModErrors.Report(mod, "item definition " + path, ex); }
        }

        static readonly FieldInfo CacheField = typeof(ez2).GetFields(BindingFlags.Static | BindingFlags.NonPublic).FirstOrDefault(f => typeof(System.Collections.IDictionary).IsAssignableFrom(f.FieldType));
        static IEnumerable<bk3> Cached(string path)
        {
            var cache = CacheField == null ? null : CacheField.GetValue(null) as System.Collections.IDictionary;
            if (cache == null) yield break;
            foreach (System.Collections.DictionaryEntry e in cache)
                if (string.Equals(Norm(e.Key as string), Norm(path), StringComparison.OrdinalIgnoreCase) && e.Value is bk3) yield return (bk3)e.Value;
        }

        // ez2.b(path, bool): reads a definition from its file (every load into the cache goes through it).
        static bool Load(string __0, ref bk3 __result)
        {
            KeyValuePair<string, Action<ItemDefinition>> def;
            if (__0 == null || !Defined.TryGetValue(Norm(__0), out def)) return true;
            if (inside) return true;
            inside = true;
            try
            {
                var b = ez2.b(def.Key, false);
                if (b == null) { Log.Write("items: " + __0 + ": no base item " + def.Key); return true; }
                var copy = b.tw();     // the definition's own clone (what crafting uses)
                def.Value(new ItemDefinition(copy));
                __result = copy;
                return false;
            }
            catch (Exception ex) { Log.Write("items: " + __0 + ": " + ex.Message); return true; }
            finally { inside = false; }
        }

        static void Loaded(string __0, bk3 __result)
        {
            List<Edit_> list;
            if (__result == null || __0 == null || inside || !edits.TryGetValue(Norm(__0), out list)) return;
            foreach (var e in list) Apply(e.Mod, e.Edit, __result, __0);
        }

        static void Tooltip(coh __0, eg __5, ecs __result)
        {
            if (!Tooltips.Any || __0 == null || __result == null) return;
            var item = new Item(__0);
            var tip = new Tooltip { Game = __result, InTrade = __5 != null };
            Tooltips.Run(h => h(item, tip));
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            const BindingFlags st = BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(ItemHooks).GetMethod(n, st));
            harmony.Patch(typeof(ez2).GetMethod("b", st, null, new[] { typeof(string), typeof(bool) }, null), prefix: m("Load"), postfix: m("Loaded"));
            harmony.Patch(typeof(cq4).GetMethods(st).First(x => x.Name == "a" && x.ReturnType == typeof(ecs) && x.GetParameters().Length == 9), postfix: m("Tooltip"));
        }
    }

    static class ResistanceThreshold
    {
        static readonly MethodInfo Setter = typeof(du2).GetMethods().FirstOrDefault(x => x.Name == "a" && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(int));
        internal static void Invoke(du2 v, int threshold, out du2 result) { object boxed = v; if (Setter != null) Setter.Invoke(boxed, new object[] { threshold }); result = (du2)boxed; }
    }
}
