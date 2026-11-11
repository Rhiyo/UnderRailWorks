// UnderRailWorks: reloading a mod's code while the game runs (for modders: change the code, build, see it in game).
//   works.reload <mod id>      the mod's DLL(s) loaded again now
//   works.reload code          every mod whose DLL changed since it was loaded
//   HotReload=1 in UnderRailWorks.ini: changed DLLs are reloaded by themselves (checked every second), and mods load
//   into collectible load contexts from the start, so an old version's memory can be freed.
// A reload: the mod's Unload() (WorksMod), then everything Works knows it registered goes (hook handlers, Harmony
// patches, console commands, area triggers, loot and store rules, dialog patches, interface it added, its windows, HUD
// parts, new-game options, its settings page, provided API, abilities, skills, recipes), the new DLL is loaded from
// memory (the file stays free for the next build) in its own collectible AssemblyLoadContext, Load() and PostLoad()
// run, and a running game catches it up: its GameStarted, ZoneLoaded and HudCreated handlers run once.
// Kept: its save data, settings and key bindings (same ids), and what the game itself already holds: feats (redefined
// texts apply, the type is the first load's), item edits already applied to loaded definitions (new edits apply to
// definitions loaded later), placed characters (re-placed with the new code by id), zones, sounds and pictures (by id).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace UnderRailWorks.Internal
{
    // A registry that keeps its entries by mod (hooks): what a reload removes.
    interface IModOwned { void RemoveOwner(string mod); }

    static class ModScope
    {
        // The mod whose code is running now (its Load, a hook handler, a command): what it adds to the interface is its own.
        [ThreadStatic] internal static string Current;
        // The mod being loaded again (registries skip what mustn't happen twice).
        internal static string Reloading;
        internal static readonly List<IModOwned> Hooks = new List<IModOwned>();

        static readonly Dictionary<string, List<WeakReference>> owned = new Dictionary<string, List<WeakReference>>(StringComparer.OrdinalIgnoreCase);
        internal static void Own(UiElement element)
        {
            var mod = Current;
            if (mod == null || element == null || mod == "UnderRailWorks") return;
            List<WeakReference> list;
            if (!owned.TryGetValue(mod, out list)) owned[mod] = list = new List<WeakReference>();
            if (list.Count > 64) list.RemoveAll(w => { var e = w.Target as UiElement; return e == null || e.Control.Parent == null; });
            list.Add(new WeakReference(element));
        }
        internal static int RemoveUi(string mod)
        {
            List<WeakReference> list;
            if (!owned.TryGetValue(mod, out list)) return 0;
            owned.Remove(mod);
            int n = 0;
            foreach (var w in list)
            {
                var e = w.Target as UiElement;
                if (e == null || e.Control.Parent == null) continue;
                try { Ui.Remove(e); n++; } catch (Exception ex) { Log.Write("reload: " + mod + ": removing an element: " + ex.Message); }
            }
            return n;
        }
    }

    // Mods' assemblies: plain LoadFrom, or (reloads, HotReload=1) from memory into a collectible AssemblyLoadContext.
    static class ModCode
    {
        static readonly Type Alc = typeof(object).Assembly.GetType("System.Runtime.Loader.AssemblyLoadContext");
        static readonly MethodInfo FromStream = Alc == null ? null : Alc.GetMethod("LoadFromStream", new[] { typeof(Stream) });
        static readonly MethodInfo UnloadMethod = Alc == null ? null : Alc.GetMethod("Unload", Type.EmptyTypes);
        internal static bool CanReload { get { return FromStream != null && UnloadMethod != null; } }

        // By simple name, for other assemblies referring to a mod's (the AssemblyResolve handler asks here first).
        internal static readonly Dictionary<string, Assembly> ByName = new Dictionary<string, Assembly>(StringComparer.OrdinalIgnoreCase);
        static readonly Dictionary<string, object> contexts = new Dictionary<string, object>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, List<Assembly>> Assemblies = new Dictionary<string, List<Assembly>>(StringComparer.OrdinalIgnoreCase);
        internal static readonly Dictionary<string, DateTime> Stamps = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        internal static DateTime Stamp(ModInfo mod)
        {
            try { return Directory.GetFiles(mod.Folder, "*.dll").Select(File.GetLastWriteTimeUtc).DefaultIfEmpty(DateTime.MinValue).Max(); }
            catch { return DateTime.MinValue; }
        }

        internal static Assembly Load(ModInfo mod, string dll, bool collectible)
        {
            Assembly a;
            if (collectible && CanReload)
            {
                object ctx;
                if (!contexts.TryGetValue(mod.Id, out ctx))
                    contexts[mod.Id] = ctx = Activator.CreateInstance(Alc, new object[] { "UnderRailWorks." + mod.Id + "." + DateTime.UtcNow.Ticks, true });
                using (var s = new MemoryStream(File.ReadAllBytes(dll))) a = (Assembly)FromStream.Invoke(ctx, new object[] { s });
            }
            else a = Assembly.LoadFrom(dll);
            ByName[a.GetName().Name] = a;
            List<Assembly> list;
            if (!Assemblies.TryGetValue(mod.Id, out list)) Assemblies[mod.Id] = list = new List<Assembly>();
            list.Add(a);
            return a;
        }

        // The mod's code is no longer used: its context may be collected (once nothing refers to its types).
        internal static List<Assembly> Release(ModInfo mod)
        {
            List<Assembly> old;
            if (!Assemblies.TryGetValue(mod.Id, out old)) old = new List<Assembly>();
            Assemblies.Remove(mod.Id);
            foreach (var a in old) { Assembly cur; if (ByName.TryGetValue(a.GetName().Name, out cur) && cur == a) ByName.Remove(a.GetName().Name); }
            object ctx;
            if (contexts.TryGetValue(mod.Id, out ctx))
            {
                contexts.Remove(mod.Id);
                try { UnloadMethod.Invoke(ctx, null); } catch (Exception ex) { Log.Write("reload: " + mod.Id + ": unloading its old code: " + ex.Message); }
            }
            return old;
        }
    }

    static class ModReloader
    {
        static readonly List<string> pending = new List<string>();
        static double since;
        static readonly Dictionary<string, DateTime> seen = new Dictionary<string, DateTime>(StringComparer.OrdinalIgnoreCase);

        internal static void Request(string id) { lock (pending) if (!pending.Contains(id, StringComparer.OrdinalIgnoreCase)) pending.Add(id); }

        // Every update (the game's thread): queued reloads, and with HotReload=1 changed DLLs (after they stopped
        // changing for a second: a build writes in steps).
        internal static void Tick(double seconds)
        {
            List<string> now;
            lock (pending) { now = pending.ToList(); pending.Clear(); }
            foreach (var id in now)
            {
                var r = Reload(id);
                Log.Always("reload: " + r);
                Commands.Print("works.reload: " + r);
            }
            if (!FixSettings.HotReload) return;
            since += seconds;
            if (since < 1) return;
            since = 0;
            foreach (var mod in ModLoader.Mods.Where(m => m.Loaded))
            {
                DateTime loaded, stamp = ModCode.Stamp(mod), last;
                if (!ModCode.Stamps.TryGetValue(mod.Id, out loaded) || stamp <= loaded) continue;
                if (!seen.TryGetValue(mod.Id, out last) || last != stamp) { seen[mod.Id] = stamp; continue; }     // still changing
                seen.Remove(mod.Id);
                Request(mod.Id);
            }
        }

        // Every mod whose DLL changed since it was loaded.
        internal static int RequestChanged()
        {
            int n = 0;
            foreach (var mod in ModLoader.Mods.Where(m => m.Loaded))
            {
                DateTime loaded;
                if (ModCode.Stamps.TryGetValue(mod.Id, out loaded) && ModCode.Stamp(mod) > loaded) { Request(mod.Id); n++; }
            }
            return n;
        }

        internal static string Reload(string id)
        {
            var mod = ModLoader.Find(id);
            if (mod == null) return "no mod " + id;
            if (!mod.Loaded) return mod.Id + " isn't loaded" + (mod.Error != null ? " (" + mod.Error + ")" : "");
            if (!ModCode.CanReload) return "this runtime can't load code again";
            if (!Directory.GetFiles(mod.Folder, "*.dll").Any()) return mod.Id + " has no code";
            int removedUi;
            var was = ModScope.Current;
            try
            {
                ModScope.Current = mod.Id;
                if (mod.Instance != null) try { mod.Instance.Unload(); } catch (Exception ex) { Log.Write("reload: " + mod.Id + ": its Unload failed: " + ex); }
                ModScope.Current = was;
                removedUi = Forget(mod);
                var old = ModCode.Release(mod);
                ModConfig.Forget(old);
                mod.Instance = null; mod.Loaded = false; mod.Error = null;
                ModScope.Reloading = mod.Id;
                ModLoader.LoadCode(mod, true);
                if (mod.Loaded && mod.Instance != null)
                {
                    ModScope.Current = mod.Id;
                    try { mod.Instance.PostLoad(); }
                    catch (Exception ex) { mod.Error = "PostLoad failed: " + ex.Message; Log.Write("mods: " + mod.Id + " PostLoad failed: " + ex); }
                    ModScope.Current = was;
                }
            }
            finally { ModScope.Reloading = null; ModScope.Current = was; }
            if (!mod.Loaded) return mod.Id + " failed to load again: " + mod.Error;
            CatchUp(mod.Id);
            return "reloaded " + mod.Id + " " + mod.VersionText + (removedUi > 0 ? " (" + removedUi + " interface element(s) of the old code removed)" : "");
        }

        // A game is running: the reloaded mod's handlers for how it started.
        static void CatchUp(string id)
        {
            if (World.Player == null) return;
            GameEvents.GameStarted.RunFor(id, h => h());
            var zone = World.ZoneId;
            if (zone != null) GameEvents.ZoneLoaded.RunFor(id, h => h(zone, false));
            var root = Hud.Root;
            if (root != null) UiEvents.HudCreated.RunFor(id, h => h(root));
        }

        // Everything the mod registered through Works. Returns how many interface elements went.
        static int Forget(ModInfo mod)
        {
            var id = mod.Id;
            List<IModOwned> hooks;
            lock (ModScope.Hooks) hooks = ModScope.Hooks.ToList();
            foreach (var h in hooks) h.RemoveOwner(id);
            try { new HarmonyLib.Harmony("UnderRailWorks.reloader").UnpatchAll("UnderRailWorks." + id); }
            catch (Exception ex) { Log.Write("reload: " + id + ": unpatching: " + ex.Message); }
            Action<string, Action> step = (what, a) => { try { a(); } catch (Exception ex) { Log.Write("reload: " + id + ": " + what + ": " + ex.Message); } };
            int ui = 0;
            step("windows", () => { foreach (var w in UiWindows.Open.Where(w => w.Mod != null && w.Mod.Id == id).ToList()) w.Close(); });
            step("HUD parts", () => UnderRailWorks.Hud.Forget(id));
            step("interface", () => ui = ModScope.RemoveUi(id));
            step("commands", () => Commands.Forget(id));
            step("triggers", () => { lock (Triggers.All) Triggers.All.RemoveAll(t => t.Mod == id); });
            step("loot", () => LootHooks.Rules.RemoveAll(r => r.Mod == id));
            step("stores", () => StoreHooks.Forget(id));
            step("dialogs", () => Dialogs.Forget(id));
            step("item edits", () => ItemHooks.Forget(id));
            step("api", () => Mods.Forget(id));
            step("new game options", () => NewGameOptions.Forget(id));
            step("main menu buttons", () => MainMenu.Forget(id));
            step("settings page", () => ModOptions.Forget(id));
            step("abilities", () => { Abilities.All.RemoveAll(a => a.Owner != null && a.Owner.Id == id); AbilityBars.Dirty = true; });
            step("skills", () => { CustomStats.AbilityList.RemoveAll(a => a.Owner != null && a.Owner.Id == id); CustomStats.SkillList.RemoveAll(s => s.Owner != null && s.Owner.Id == id); });
            step("recipes", () => Crafting.Recipes.RemoveAll(r => r.Owner != null && r.Owner.Id == id));
            return ui;
        }
    }
}
