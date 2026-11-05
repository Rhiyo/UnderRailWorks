// UnderRailWorks: feats defined by mods (tModLoader's ModPlayer perks, RimWorld's traits). The game's side (measured
// from its code; ~/UnderRailWorks-research/specs/music-console-feats-ai.md section 3):
// - A feat is a class deriving from ai8, found by the registry ck (a dictionary key -> instance, built from the game's
//   own classes). The level-up screen lists the registry; taking a feat stores its type in the character's feats.
// - A save keeps a feat as its registry key (a string). An unknown key is skipped by the game, so a save with a mod
//   feat loads in the stock game (the feat is just absent) and keeps the key; with the mod back, the feat is back.
// Works defines one class WorksFeat : ai8 (its overrides read the mod's definition) and, per mod feat, emits a tiny
// subclass at startup whose name is the save key ("mod_<mod>_<id>", lower case), registered in ck.
// A mod feat's effects are the mod's: OnGain / OnLose (no lasting side effects there: they would stay in the save),
// and Works' events and hooks checking Character.HasFeat(feat.Key). Requirements: abilities, skills and a minimum
// level, as the game shows them.
//   var f = Feats.Add(this, "packrat", "Pack Rat", "Carry 50 more weight.", requires: new FeatRequirements { Abilities = { ["Strength"] = 6 } });
//   if (World.Player.HasFeat(f.Key)) ...
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Reflection.Emit;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class FeatRequirements
    {
        public Dictionary<string, int> Abilities { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);     // e.g. Strength, Dexterity
        public Dictionary<string, int> Skills { get; } = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);        // e.g. Guns, Stealth
        public int Level { get; set; }
    }

    public sealed class ModFeat
    {
        public string Mod { get; internal set; }
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string Description { get; internal set; }
        // The key the game (and a save) knows it by, for Character.HasFeat.
        public string Key { get { return ("mod_" + Mod + "_" + Id).ToLowerInvariant(); } }
        public FeatRequirements Requires { get; internal set; }
        public event Action<Character> OnGain, OnLose;
        internal void Gained(Character c) { var a = OnGain; if (a != null) try { a(c); } catch (Exception ex) { ModErrors.Report(Mod, "feat " + Id, ex); } }
        internal void Lost(Character c) { var a = OnLose; if (a != null) try { a(c); } catch (Exception ex) { ModErrors.Report(Mod, "feat " + Id, ex); } }
        internal Type Type;
    }

    public static class Feats
    {
        internal static readonly Dictionary<string, ModFeat> All = new Dictionary<string, ModFeat>(StringComparer.OrdinalIgnoreCase);
        // A new feat on the level-up screen (call in Load). icon: a PNG in the mod's folder (optional).
        public static ModFeat Add(WorksMod mod, string id, string name, string description, FeatRequirements requires = null, string icon = null)
        {
            var f = new ModFeat { Mod = mod.Id, Id = id, Name = name, Description = description, Requires = requires ?? new FeatRequirements() };
            All[f.Key] = f;
            FeatRegistry.Register(f, icon == null ? null : System.IO.Path.Combine(mod.Info.Folder, icon));
            return f;
        }
        // Gives a feat (a mod's or the game's, by key) to a character, as the game does (no feat point is spent).
        public static bool Give(Character who, string key)
        {
            var feat = ck.a(key);
            var data = who == null ? null : (who.Entity as c5x)?.GetAspect<amc>()?.aa();
            if (feat == null || data == null) return false;
            data.Feats.d(feat.GetType());
            data.bl();
            return true;
        }
    }
}

namespace UnderRailWorks.Internal
{
    // The game's feat with its behaviour taken from a ModFeat (the emitted subclass only names it).
    public abstract class WorksFeat : ai8
    {
        protected WorksFeat(string name) : base(name) { }
        ModFeat Def { get { ModFeat f; return Feats.All.TryGetValue(ck.a(GetType()), out f) ? f : null; } }
        public override List<brl> j0()
        {
            var list = new List<brl>();
            var d = Def;
            if (d == null) return list;
            foreach (var kv in d.Requires.Abilities) { var t = FeatRegistry.StatType(kv.Key, true); if (t != null) list.Add(new awr(t, kv.Value)); }
            foreach (var kv in d.Requires.Skills) { var t = FeatRegistry.StatType(kv.Key, false); if (t != null) list.Add(new bap(kv.Value, t)); }
            return list;
        }
        public override void ar(efk A_0) { var d = Def; if (d != null) d.Gained(Combat.OfData(A_0)); }
        public override void @as(efk A_0) { var d = Def; if (d != null) d.Lost(Combat.OfData(A_0)); }
    }

    static class FeatRegistry
    {
        static ModuleBuilder module;
        static readonly FieldInfo Registry = typeof(ck).GetField("m_a", BindingFlags.Static | BindingFlags.NonPublic) ?? typeof(ck).GetField("a", BindingFlags.Static | BindingFlags.NonPublic);

        internal static void Register(ModFeat f, string iconFile)
        {
            if (module == null) module = AssemblyBuilder.DefineDynamicAssembly(new AssemblyName("UnderRailWorks.Feats"), AssemblyBuilderAccess.Run).DefineDynamicModule("UnderRailWorks.Feats");
            // The type's name is the registry key (ck.a(Type): no d29 attribute -> the type name, lower case).
            var tb = module.DefineType(f.Key, TypeAttributes.Public | TypeAttributes.Sealed | TypeAttributes.Class, typeof(WorksFeat));
            var ctor = tb.DefineConstructor(MethodAttributes.Public, CallingConventions.Standard, Type.EmptyTypes);
            var il = ctor.GetILGenerator();
            il.Emit(OpCodes.Ldarg_0);
            il.Emit(OpCodes.Ldstr, f.Name);
            il.Emit(OpCodes.Call, typeof(WorksFeat).GetConstructor(BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(string) }, null));
            il.Emit(OpCodes.Ret);
            f.Type = tb.CreateType();
            var feat = (ai8)Activator.CreateInstance(f.Type);
            var dict = (Dictionary<string, ai8>)Registry.GetValue(null);
            dict[ck.a(f.Type)] = feat;
            // Its texts: the screen asks qv.b("FeatNames/" + Name) and qv.a("Feats/" + wn(), args).
            Text.Set("FeatNames", f.Name, f.Name);
            Text.Set("Feats", feat.wn(), f.Description);
            if (iconFile != null) AssetStore.IconAlias("Feat." + ck.a(f.Type), iconFile);
            Log.Write("feats: " + f.Mod + " adds " + f.Name + " (" + ck.a(f.Type) + ")");
        }

        // The game's stat type for an ability or skill name (for requirements).
        internal static Type StatType(string name, bool ability)
        {
            var p = World.Player;
            var data = p == null ? null : (p.Entity as c5x)?.GetAspect<amc>()?.aa();
            if (data == null) return null;
            object s = ability ? (object)data.BaseAbilities.a(name) : data.Skills.a(name);
            return s == null ? null : s.GetType();
        }

        // Gained / lost through any path (the screen, scripts, Feats.Give): an2.d(Type) / an2.b(Type).
        internal static void Install(HarmonyLib.Harmony harmony) { }
    }
}
