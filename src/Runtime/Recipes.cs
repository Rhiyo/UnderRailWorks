// UnderRailWorks: crafting recipes from mods. A game recipe is a blueprint class (cwp) with named component slots
// (cgo: a requirement, cb6, a label and a count) that builds its result in by(); the character knows blueprints by
// type, saved in the save (PC1:B), so a mod blueprint learned the game's way would break the save for the stock
// game. A mod recipe is Works' own instead: one blueprint object per recipe, added to the crafting window's lists
// when the window builds them (eed.c), known per save in the mod's save data. Crafting it runs the game's own
// crafting (skill checks, components used up, ItemCrafted).
//   var r = Crafting.AddRecipe(this, "stim", "Field Stim", "Medicine", "consumables\\medicine\\healthhypo",
//       RecipeSlot.Item("Syringe", "syringe", 20), RecipeSlot.Item("Herb", "cavehopperleg", 10, 2));
//   r.Known = true;            // or r.Learn() from a quest, a book...
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using TimelapseVertigo.Rules.Crafting;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class RecipeSlot
    {
        internal string Label; internal string[] Codes; internal int Skill, Count; internal bool Optional;
        // A component by item code (Item.Code, e.g. "syringe"), the crafting skill it needs, how many.
        public static RecipeSlot Item(string label, string code, int skill = 0, int count = 1) { return new RecipeSlot { Label = label, Codes = new[] { code }, Skill = skill, Count = Math.Max(1, count) }; }
        public static RecipeSlot AnyOf(string label, string[] codes, int skill = 0, int count = 1) { return new RecipeSlot { Label = label, Codes = codes, Skill = skill, Count = Math.Max(1, count) }; }
        public RecipeSlot AsOptional() { Optional = true; return this; }
    }

    public sealed class ModRecipe
    {
        internal WorksMod Owner;
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string Category { get; internal set; }
        public string Result { get; internal set; }
        public int Count { get; set; } = 1;
        public bool KnownByDefault { get; set; } = true;
        internal RecipeSlot[] Slots;
        internal object Blueprint;
        string Key { get { return "works_recipe_" + Id; } }
        // Known in the current game (per save; KnownByDefault until learned or forgotten).
        public bool Known
        {
            get { var v = SaveData.Of(Owner).Get<object>(Key, null); return v == null ? KnownByDefault : Convert.ToBoolean(v); }
            set { SaveData.Of(Owner).Set(Key, value); }
        }
        public void Learn() { Known = true; }
        public void Forget() { Known = false; }
    }

    public static partial class Crafting
    {
        internal static readonly List<ModRecipe> Recipes = new List<ModRecipe>();
        // category: Weapons, Armor, Grenades, Ammo, Medicine, Traps, Devices, Processing. result: an item path.
        public static ModRecipe AddRecipe(WorksMod mod, string id, string name, string category, string result, params RecipeSlot[] slots)
        {
            BlueprintCategory cat;
            if (!Enum.TryParse(category ?? "", true, out cat)) throw new ArgumentException("unknown recipe category " + category);
            var r = new ModRecipe { Owner = mod, Id = mod.Id + "." + id, Name = name, Category = cat.ToString(), Result = result, Slots = slots ?? new RecipeSlot[0] };
            r.Blueprint = WorksBlueprint.Make(r, cat);
            Recipes.Add(r);
            Text.Set("ItemBlueprints", ((cwp)r.Blueprint).TechnicalName, name);
            return r;
        }
        public static IList<ModRecipe> ModRecipes { get { return Recipes.ToList(); } }
    }
}

namespace UnderRailWorks.Internal
{
    // The blueprint: an emitted subclass supplies the abstract properties (their getters have obfuscated names, so C#
    // can't override them by name); the rest is here.
    public abstract class WorksBlueprint : cwp
    {
        internal ModRecipe Recipe;
        internal BlueprintCategory Cat;
        public string RecipeName() { return Recipe.Name; }
        public string RecipeTechnicalName() { return "works_" + Recipe.Id.Replace('.', '_'); }
        public cr1 RecipeQuality() { return cr1.c; }
        public BlueprintCategory RecipeCategory() { return Cat; }

        protected override coh by(efk A_0, Dictionary<string, coh> A_1, out string A_2)
        {
            A_2 = null;
            if (!b(A_1)) return null;
            var item = ez2.a(Recipe.Result);
            if (item == null) { Log.Write("recipes: " + Recipe.Id + ": no item " + Recipe.Result); return null; }
            if (Recipe.Count > 1) item.Stacks = Recipe.Count;
            return item;
        }

        static Type impl;
        internal static object Make(ModRecipe r, BlueprintCategory cat)
        {
            if (impl == null) impl = Emit();
            var bp = (WorksBlueprint)Activator.CreateInstance(impl);
            bp.Recipe = r; bp.Cat = cat;
            foreach (var s in r.Slots)
                bp.Slots[s.Label] = new cgo(new eh0(s.Codes, s.Skill), s.Label, s.Optional, s.Count);
            return bp;
        }

        static Type Emit()
        {
            var module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("UnderRailWorks.Recipes"), AssemblyBuilderAccess.Run).DefineDynamicModule("UnderRailWorks.Recipes");
            var tb = module.DefineType("WorksBlueprintImpl", TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, typeof(WorksBlueprint));
            Action<string, string> over = (property, helper) =>
            {
                var getter = typeof(cwp).GetProperty(property, BindingFlags.Instance | BindingFlags.Public).GetGetMethod();
                var m = tb.DefineMethod(getter.Name, MethodAttributes.Public | MethodAttributes.Virtual | MethodAttributes.HideBySig | MethodAttributes.Final, getter.ReturnType, Type.EmptyTypes);
                var il = m.GetILGenerator();
                il.Emit(OpCodes.Ldarg_0);
                il.Emit(OpCodes.Call, typeof(WorksBlueprint).GetMethod(helper));
                il.Emit(OpCodes.Ret);
                tb.DefineMethodOverride(m, getter);
            };
            over("Name", "RecipeName");
            over("TechnicalName", "RecipeTechnicalName");
            over("Quality", "RecipeQuality");
            over("Category", "RecipeCategory");
            tb.DefineDefaultConstructor(MethodAttributes.Public);
            return tb.CreateType();
        }
    }

    static class RecipeHooks
    {
        const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public;
        static readonly FieldInfo All = typeof(eed).GetField("i", Inst), Craftable = typeof(eed).GetField("j", Inst), Rows = typeof(eed).GetField("k", Inst);
        static readonly MethodInfo CanCraft = typeof(eed).GetMethod("a", Inst, null, new[] { typeof(cwp) }, null);
        static readonly MethodInfo Row = typeof(eed).GetMethod("c", Inst, null, new[] { typeof(cwp) }, null);

        // After eed.c() built the lists from the known blueprints: the known mod recipes, in name order.
        static void Built(eed __instance)
        {
            if (Crafting.Recipes.Count == 0) return;
            try
            {
                var all = (List<cwp>)All.GetValue(__instance);
                var craftable = (List<cwp>)Craftable.GetValue(__instance);
                var rows = (Dictionary<cwp, zt>)Rows.GetValue(__instance);
                foreach (var r in Crafting.Recipes.Where(x => x.Known).OrderBy(x => x.Name, StringComparer.Ordinal))
                {
                    var bp = (cwp)r.Blueprint;
                    if (all.Contains(bp)) continue;
                    int at = all.FindIndex(x => string.CompareOrdinal(x.Name, bp.Name) > 0);
                    all.Insert(at < 0 ? all.Count : at, bp);
                    if ((bool)CanCraft.Invoke(__instance, new object[] { bp })) craftable.Add(bp);
                    rows[bp] = (zt)Row.Invoke(__instance, new object[] { bp });
                }
            }
            catch (Exception ex) { Log.Write("recipes: " + ex.Message); }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            harmony.Patch(typeof(eed).GetMethod("c", Inst, null, Type.EmptyTypes, null), postfix: new HarmonyLib.HarmonyMethod(typeof(RecipeHooks).GetMethod("Built", BindingFlags.Static | BindingFlags.NonPublic)));
        }
    }
}
