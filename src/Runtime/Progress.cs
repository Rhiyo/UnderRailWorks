// UnderRailWorks: status effect events (with a veto), the level-up event, per-save mod data, and a dump of the game's
// content ids. What the game does, measured from its code:
// - A character's status effects are its br5 (owner: its character data, field a); br5.a(effect, ...) applies one
//   (the game's stacking rules), br5.d(effect) removes one; removal, expiry and replacement all go through br5.c(effect).
// - Levelling up: the character screen iw in level-up mode (a9m.b) closes through amm.a(cku) (its base): cku.a is Accept (the
//   level stands), cku.b Cancel.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Text;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class StatusChange : Cancellable
{ internal StatusChange() { }
    public Character Character { get; internal set; }
    public string Code { get; internal set; }
    public string Name { get; internal set; }
    // On StatusEffects.Applying: true stops the effect (it isn't applied).
    // Cancel: Cancellable
}

public static partial class GameEvents
{
    // A status effect is about to be applied (set Cancel to refuse it), was applied, or was removed (also on expiry).
    public static readonly Hook<Action<StatusChange>> StatusApplying = new Hook<Action<StatusChange>>("StatusApplying");
    public static readonly Hook<Action<StatusChange>> StatusApplied = new Hook<Action<StatusChange>>("StatusApplied");
    public static readonly Hook<Action<StatusChange>> StatusRemoved = new Hook<Action<StatusChange>>("StatusRemoved");
    // The player levelled up (the level-up screen closed without Cancel): the new level.
    public static readonly Hook<Action<int>> LeveledUp = new Hook<Action<int>>("LeveledUp");
}

static class ProgressHooks
{
    static readonly FieldInfo Owner = typeof(br5).GetField("m_a", BindingFlags.Instance | BindingFlags.NonPublic) ?? typeof(br5).GetField("a", BindingFlags.Instance | BindingFlags.NonPublic);

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(ProgressHooks).GetMethod(n, any));
        harmony.Patch(typeof(br5).GetMethods(any).First(x => x.Name == "a" && x.ReturnType == typeof(ea4) && x.GetParameters().Length == 4), prefix: m("Applying"), postfix: m("Applied"));
        harmony.Patch(typeof(br5).GetMethod("c", any, null, new[] { typeof(els) }, null), postfix: m("Removed"));     // every removal: removed, expired, replaced
        harmony.Patch(typeof(amm).GetMethods(any).First(x => x.Name == "a" && x.DeclaringType == typeof(amm) && x.GetParameters().Length == 1 && x.GetParameters()[0].ParameterType == typeof(cku)), postfix: m("ScreenClosed"));     // amm: every window's close-with-result
    }

    static StatusChange Change(br5 effects, els e)
    {
        return new StatusChange { Character = Combat.OfData(Owner.GetValue(effects) as efk), Code = e.Code, Name = e.LocalizedName ?? e.Name };
    }

    static bool Applying(br5 __instance, els __0, ref ea4 __result)
    {
        if (!GameEvents.StatusApplying.Any || __0 == null) return true;
        try
        {
            var c = Change(__instance, __0);
            GameEvents.StatusApplying.Run(h => h(c));
            if (c.Cancel) { __result = ea4.d; return false; }     // refused, as the game reports one it won't take
        }
        catch (Exception ex) { Log.Write("effects: " + ex.Message); }
        return true;
    }

    static void Applied(br5 __instance, els __0, ea4 __result)
    {
        // Only when it was applied (ea4.a new, ea4.c replaced): not refused (d), nor kept out by a stronger one already on (b).
        if (!GameEvents.StatusApplied.Any || __0 == null || (__result != ea4.a && __result != ea4.c)) return;
        try { var c = Change(__instance, __0); GameEvents.StatusApplied.Run(h => h(c)); } catch (Exception ex) { Log.Write("effects: " + ex.Message); }
    }

    static void Removed(br5 __instance, els __0)
    {
        if (!GameEvents.StatusRemoved.Any || __0 == null) return;
        try { var c = Change(__instance, __0); GameEvents.StatusRemoved.Run(h => h(c)); } catch (Exception ex) { Log.Write("effects: " + ex.Message); }
    }

    // iw.a(result): the character screen closes with cku.a (Accept) or cku.b (Cancel/Close). Accept on the level-up
    // screen (iw.h() == a9m.b) is a level-up.
    static int lastLevel;
    static void ScreenClosed(amm __instance, cku __0)
    {
        try
        {
            var screen = __instance as iw;
            var p = World.Player;
            if (screen == null || p == null || !p.IsCharacter || __0 != cku.a || screen.h() != a9m.b) return;
            int level = p.Level;
            if (level != lastLevel && GameEvents.LeveledUp.Any) GameEvents.LeveledUp.Run(h => h(level));
            lastLevel = level;
        }
        catch (Exception ex) { Log.Write("levels: " + ex.Message); }
    }
    internal static void Known(int level) { lastLevel = level; }
}

// The game's content, listed for modders: item paths with their codes and names, character blueprints, stores,
// dialogs, zones. Written to UnderRailWorks-content.txt in the game folder by ContentIndex.Write (WorksTest has a key;
// Works writes it when UnderRailWorks.ini has DumpContent=1).
public static class ContentIndex
{
    public static string Write()
    {
        var data = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "data");
        var sb = new StringBuilder();
        sb.Append("UnderRail content ids, written by UnderRailWorks ").Append(FixVersion.Value).Append('\n');
        Func<string, string, IEnumerable<string>> files = (dir, ext) => Directory.Exists(Path.Combine(data, dir))
            ? Directory.GetFiles(Path.Combine(data, dir), "*" + ext, SearchOption.AllDirectories).Select(f => f.Substring(Path.Combine(data, dir).Length + 1, f.Length - Path.Combine(data, dir).Length - 1 - ext.Length).Replace('/', '\\')).OrderBy(x => x, StringComparer.OrdinalIgnoreCase)
            : Enumerable.Empty<string>();
        sb.Append("\n[items] path = code (name): Inventory.Add takes the path, Has/Take the code\n");
        foreach (var path in files("rules/items", ".item")) sb.Append(path).Append(" = ").Append(Items.Code(path) ?? "?").Append(" (").Append(Items.Name(path) ?? "?").Append(")\n");
        sb.Append("\n[blueprints] for placements (creatures, objects...)\n");
        foreach (var path in files("meta/playfield/blueprints/locale", ".upeb")) sb.Append(path).Append('\n');
        sb.Append("\n[stores] for Placement.Store / MakeMerchant\n");
        foreach (var path in files("rules/stores", ".store")) sb.Append(path).Append('\n');
        sb.Append("\n[dialogs] the game's conversations (Dialogs.Override can point a character at one)\n");
        foreach (var path in files("dialogs", ".udlg")) sb.Append(path).Append('\n');
        sb.Append("\n[status effects] codes for StatusEffects.Apply\n");
        foreach (var code in StatusEffects.Codes) sb.Append(code).Append('\n');
        var file = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "UnderRailWorks-content.txt");
        File.WriteAllText(file, sb.ToString());
        Log.Write("content index: " + file);
        return file;
    }
}
}
