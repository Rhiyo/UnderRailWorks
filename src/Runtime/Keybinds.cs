// UnderRailWorks: key bindings for mods, in the game's own Controls tab. A mod registers an action with a default key;
// it appears in Options -> Controls after the game's bindings ("Minimap: show or hide"), where the player rebinds it
// with the game's own key-capture row and conflict check, and the mod reads it (Pressed/Down). Each mod's settings
// page lists its keys. Every mod key should go through this: one place for the player to see and change them.
// How it sits in the game (measured from its code): the Controls list (ar4) holds Hotkey values and builds a row (cbx)
// per value; a row's label is d1o.c(hotkey), its key d1o.a(hotkey), and Accept saves it with cbx.a(settings) into
// config.dat. Mod actions get Hotkey values far outside the game's (100000 + n): Works answers label and key for them
// and saves them in the mod's settings.ini, so config.dat never holds anything of a mod's.
//   var toggle = Keybinds.Add(this, "toggle", "Show or hide the minimap", Keys.F7);
//   if (toggle.Pressed) ...
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework.Input;
using TimelapseVertigo.Common.Settings;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class Keybind
{ internal Keybind() { }
    internal Hotkey Slot;
    internal string Mod, ModName, Id;
    public string Label { get; internal set; }
    public Keys DefaultKey { get; internal set; }
    // The key the player chose (Keys.None: unbound).
    public Keys Key { get; internal set; }
    // True on the update the key went down (not while typing in a text box: the game's own hotkeys don't fire then either).
    // Not while typing, nor while the Options window is open (binding a game key there mustn't run a mod's action).
    public bool Pressed { get { return Keybinds.Replaying == this || (Key != Keys.None && !Input.Typing && !OptionsOpen && Input.Pressed(Key)); } }
    static bool OptionsOpen { get { var o = UiHooks.CurrentOptions; return o != null && o.Parent != null; } }
    public string ModId { get { return Mod; } }
    public string KeyId { get { return Id; } }
    public bool Down { get { return Key != Keys.None && !Input.Typing && Input.Down(Key); } }
    internal string SettingKey { get { return "key." + Id; } }
}

public static class Keybinds
{
    const int First = 100000;
    static readonly List<Keybind> all = new List<Keybind>();

    // On a copy of another game (Simulation.IsMirror): a mod key the player pressed whose handler then tried to change
    // the world (Works refused it here). For a multiplayer mod to do it in the real game (Replay there). Keys that only
    // change this screen (a window, a minimap) aren't reported.
    [Preview("multiplayer: built for Co-op so far")] public static event Action<Keybind> NeedsTheRealGame;
    internal static Keybind Replaying;
    static readonly List<Keybind> pressed = new List<Keybind>();
    internal static void Tick()
    {
        pressed.Clear();
        if (NeedsTheRealGame == null || !Simulation.IsMirror || Input.Typing) return;
        foreach (var k in all) if (k.Key != Keys.None && Input.Pressed(k.Key)) pressed.Add(k);
        if (pressed.Count > 0) Simulation.RefusedBy.Clear();
    }
    // After the mods' Update handlers.
    internal static void After()
    {
        if (pressed.Count == 0) return;
        var h = NeedsTheRealGame;
        foreach (var k in pressed.ToList()) if (h != null && Simulation.RefusedBy.Contains(k.Mod)) try { h(k); } catch (Exception ex) { Log.Write("keybinds: " + ex.Message); }
        pressed.Clear();
    }

    // Runs the mod's Update handlers once (0 s passed) with that keybind pressed and who as the player (World.Player):
    // a key another player pressed on their machine, done here (the real game).
    [Preview("multiplayer: built for Co-op so far")] public static bool Replay(string modId, string keyId, Character who)
    {
        var k = all.FirstOrDefault(x => x.Mod == modId && x.Id == keyId);
        if (k == null) return false;
        Replaying = k;
        try { Party.As(who, () => GameEvents.Update.RunFor(modId, h => h(0))); }
        finally { Replaying = null; }
        return true;
    }

    // Registers an action (call from Load). id: unique within the mod; label: what the Controls tab shows after the mod's
    // name ("Minimap: Show/hide"): keep it short, the column is narrow. The player's key is kept in the mod's
    // settings.ini (key.<id>).
    public static Keybind Add(WorksMod mod, string id, string label) { return Add(mod, id, label, Keys.None); }     // unbound by default
    // (Not an optional parameter: an enum default value makes the build's retargeting resolve XNA itself.)
    public static Keybind Add(WorksMod mod, string id, string label, Keys defaultKey)
    {
        var existing = all.FirstOrDefault(k => k.Mod == mod.Id && k.Id == id);
        if (existing != null) { existing.Label = label; existing.DefaultKey = defaultKey; return existing; }     // a reloaded mod: same slot, its new texts
        var k2 = new Keybind { Slot = (Hotkey)(First + all.Count), Mod = mod.Id, ModName = mod.Info.Name, Id = id, Label = label, DefaultKey = defaultKey };
        Keys key;
        var saved = mod.Settings.Get(k2.SettingKey);
        k2.Key = saved != null && Enum.TryParse(saved, true, out key) ? key : defaultKey;
        var clash = all.FirstOrDefault(k => k.Key != Keys.None && k.Key == k2.Key);
        if (clash != null && saved == null)
        {
            // Two mods' defaults: the first keeps it, this one starts unbound (one key, one action).
            Log.Write("keys: " + k2.ModName + " (" + k2.Label + ") wanted " + k2.Key + ", already " + clash.ModName + "'s (" + clash.Label + "): unbound, set one in Options -> Controls");
            k2.Key = Keys.None;
        }
        else if (clash != null) Log.Write("keys: " + k2.ModName + " (" + k2.Label + ") and " + clash.ModName + " (" + clash.Label + ") both use " + k2.Key + ": change one in Options -> Controls");
        all.Add(k2);
        return k2;
    }

    // The game's own keys (its Controls tab: cnk.aq1().e().c(), Hotkey -> key), once they are loaded: a mod's default
    // on one of them starts unbound (G is the game's Crafting, H its..., they'd both fire); a key the player set is only
    // reported. Run from Works' update until the game's table exists.
    static int gameChecked;     // how many of all were checked (keys added later, by a reload or Abilities.Add, are checked too)
    internal static void CheckGameKeys()
    {
        if (gameChecked >= all.Count) return;
        var game = ces.a();
        var table = game == null || game.aq1() == null || game.aq1().e() == null ? null : game.aq1().e().c();
        if (table == null || table.Count == 0) return;
        if (gameChecked == 0) Log.Write("keys: the game's: " + string.Join(", ", table.Where(kv => (int)kv.Key < First && kv.Value != Keys.None).OrderBy(kv => kv.Value.ToString()).Select(kv => kv.Value + "=" + kv.Key)));
        var fresh = all.Skip(gameChecked).ToList();
        gameChecked = all.Count;
        foreach (var k in fresh)
        {
            if (k.Key == Keys.None) continue;
            var hit = table.Where(kv => (int)kv.Key < First && kv.Value == k.Key).Select(kv => kv.Key).FirstOrDefault();
            if ((int)hit == 0 && !table.Any(kv => (int)kv.Key < First && kv.Value == k.Key)) continue;
            var mod = ModLoader.Find(k.Mod);
            bool chosen = mod != null && mod.Instance != null && mod.Instance.Settings.Get(k.SettingKey) != null;
            if (chosen) { Log.Write("keys: " + k.ModName + " (" + k.Label + ") uses " + k.Key + ", also the game's " + hit + ": both act"); continue; }
            Log.Write("keys: " + k.ModName + " (" + k.Label + ") wanted " + k.Key + ", the game's " + hit + ": unbound, set one in Options -> Controls");
            k.Key = Keys.None;
        }
    }

    // A mod's keys (for its settings page).
    public static IList<Keybind> Of(string modId) { return all.Where(k => k.Mod == modId).ToList(); }

    // A key's name as the game writes it.
    public static string Name(Keys key) { return key == Keys.None ? "(none)" : key.ToString(); }

    static Keybind Find(Hotkey h) { int i = (int)h - First; return i >= 0 && i < all.Count ? all[i] : null; }

    // --- The hooks ------------------------------------------------------------------------------------------------
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(Keybinds).GetMethod(n, any));
        harmony.Patch(typeof(d1o).GetMethod("c", any, null, new[] { typeof(Hotkey) }, null), prefix: m("LabelOf"));
        harmony.Patch(typeof(d1o).GetMethod("a", any, null, new[] { typeof(Hotkey) }, null), prefix: m("KeyOf"));
        harmony.Patch(typeof(cbx).GetMethod("a", any, null, new[] { typeof(ls) }, null), prefix: m("Save"));
        harmony.Patch(typeof(ar4).GetMethods(any).First(x => x.Name == "a" && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(esj)), prefix: m("AddRows"));
    }

    // ar4.a(size) builds the rows from its list (field b): the mods' actions go at its end.
    static readonly FieldInfo ListField = typeof(ar4).GetField("b", BindingFlags.Instance | BindingFlags.NonPublic);
    static void AddRows(ar4 __instance)
    { try {
        var list = ListField.GetValue(__instance) as List<Hotkey>;
        if (list == null) return;
        foreach (var k in all) if (!list.Contains(k.Slot)) list.Add(k.Slot);
    } catch (Exception ex_) { PatchGuard.Failed("Keybinds.AddRows", ex_); }
    }

    static bool LabelOf(Hotkey __0, ref string __result)
    { try {
        var k = Find(__0);
        if (k == null) return true;
        __result = k.ModName + ": " + k.Label;
        return false;
    } catch (Exception ex_) { PatchGuard.Failed("Keybinds.LabelOf", ex_); return true; }
    }

    static bool KeyOf(Hotkey __0, ref Keys __result)
    { try {
        var k = Find(__0);
        if (k == null) return true;
        __result = k.Key;
        return false;
    } catch (Exception ex_) { PatchGuard.Failed("Keybinds.KeyOf", ex_); return true; }
    }

    // cbx.a(settings) on Accept: a mod's row is saved in the mod's settings.ini, not in config.dat.
    static readonly FieldInfo RowHotkey = typeof(cbx).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).First(f => f.FieldType == typeof(Hotkey));
    static readonly MethodInfo RowKey = typeof(cbx).GetMethod("d", BindingFlags.Instance | BindingFlags.Public, null, Type.EmptyTypes, null);
    static bool Save(cbx __instance)
    { try {
        var k = Find((Hotkey)RowHotkey.GetValue(__instance));
        if (k == null) return true;
        k.Key = (Keys)Convert.ToInt32(RowKey.Invoke(__instance, null));     // cbx.d(): XNA's Keys in its signature
        ModSettings.For(k.Mod).Set(k.SettingKey, k.Key.ToString());
        return false;
    } catch (Exception ex_) { PatchGuard.Failed("Keybinds.Save", ex_); return true; }
    }
}
}
