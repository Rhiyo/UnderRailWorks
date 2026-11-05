// UnderRailWorks: keeps every save loadable by the stock game, and the game's lifecycle events. What the game does,
// measured from its code (UnderRailWorks.md, "Saves and lifecycle"): a save folder is global.dat (the game state
// cyt, written by d6f.b(folder, transitional)) plus bulk storage, where each visited zone is stored by bl1.yp (on a
// save, and when the player leaves the zone); the stock binder throws on any type it doesn't know.
// So Works:
// - takes the characters mods placed (World.Place) out of a zone right before bl1.yp stores it, and puts them back
//   after a save (after leaving, they come back when the zone loads again);
// - takes mod quests out of the quest lists while global.dat is written, and keeps their state in a global variable
//   per quest ("works_quest_<id>" = "active|completed|failed,read,index": plain strings the stock game ignores),
//   rebuilding them after a load;
// - refuses, at the binder, to write any type from Works or a mod (SaveGuard): a save fails rather than becoming
//   unloadable.
// Mods keep their own state in Variables (bool, int, float, string): that is saved, and the stock game ignores it.
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public static partial class GameEvents
{
    // A new game was created (before character creation).
    public static readonly Hook<Action> NewGame = new Hook<Action>("NewGame");
    // A save was loaded (the game state, the current zone and the player are there).
    public static readonly Hook<Action> GameLoaded = new Hook<Action>("GameLoaded");
    // A zone was loaded (entering it, or loading a save), with the player in it: its id (World.ZoneId) and whether
    // this is the player's first visit.
    public static readonly Hook<Action<string, bool>> ZoneLoaded = new Hook<Action<string, bool>>("ZoneLoaded");
    // A conversation opened (its name, and the character talked to) / closed (its name).
    public static readonly Hook<Action<string, Character>> DialogStarted = new Hook<Action<string, Character>>("DialogStarted");
    public static readonly Hook<Action<string>> DialogEnded = new Hook<Action<string>>("DialogEnded");
    // An item went into the player's bag (any way: loot, trade, reward).
    public static readonly Hook<Action<Item>> ItemAdded = new Hook<Action<Item>>("ItemAdded");
    // The player gained XP (before the game's own multipliers for the classic system are known: the amount given).
    public static readonly Hook<Action<int>> ExperienceGained = new Hook<Action<int>>("ExperienceGained");
    // Before and after the game writes a save; the argument is the save's folder name ("quicksave", "autosave"...).
    public static readonly Hook<Action<string>> Saving = new Hook<Action<string>>("Saving");
    public static readonly Hook<Action<string>> Saved = new Hook<Action<string>>("Saved");
    // A quest changed state (started, completed, failed): its id and new state.
    public static readonly Hook<Action<string, QuestState>> QuestChanged = new Hook<Action<string, QuestState>>("QuestChanged");
    // The barter screen opened with a merchant.
    public static readonly Hook<Action<Character>> TradeOpened = new Hook<Action<Character>>("TradeOpened");
    // A character died.
    // The player character is about to be exported (path: the .char being written): write what should travel with it
    // into CharacterData. And a new game started with an imported character: its CharacterData is there now.
    public static readonly Hook<Action<string>> CharacterExporting = new Hook<Action<string>>("CharacterExporting");
    public static readonly Hook<Action> CharacterImported = new Hook<Action>("CharacterImported");
    public static readonly Hook<Action<Character>> CharacterDied = new Hook<Action<Character>>("CharacterDied");
    // Just before the game handles a death: the character still has its status effects (the game clears them while
    // it dies: measured, none left at CharacterDied) and gear. Killer: who dealt the blow, when known.
    public static readonly Hook<Action<Character, Character>> CharacterDying = new Hook<Action<Character, Character>>("CharacterDying");
    // Combat started / ended.
    public static readonly Hook<Action> CombatStarted = new Hook<Action>("CombatStarted");
    public static readonly Hook<Action> CombatEnded = new Hook<Action>("CombatEnded");
}

static class Saves
{
    const string QuestVariable = "works_quest_";

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(Saves).GetMethod(n, any));
        // Zone storage: bl1.yp(c2w where, c1a zone, bool leaving).
        harmony.Patch(typeof(bl1).GetMethod("yp", any), prefix: m("ZoneStoring"), finalizer: m("ZoneStored"));
        // global.dat: d6f.b(string folder, bool transitional).
        harmony.Patch(Game("d6f").GetMethod("b", any, null, new[] { typeof(string), typeof(bool) }, null), prefix: m("GlobalWriting"), finalizer: m("GlobalWritten"));
        // Lifecycle: arg (save), ari (load), arf (new game), arp (zone loaded).
        harmony.Patch(Game("d6f").GetMethod("arg", any), prefix: m("SaveStarting"), postfix: m("SaveDone"));
        harmony.Patch(Game("d6f").GetMethod("ari", any), prefix: m("Loading"), finalizer: m("LoadFinished"));
        harmony.Patch(Game("d6f").GetMethod("arf", any), postfix: m("NewGameCreated"));
        harmony.Patch(Game("d6f").GetMethod("arp", any), postfix: m("ZoneLoadedHook"));
        harmony.Patch(Game("d6f").GetMethod("a", any, null, new[] { typeof(bfc) }, null), postfix: m("FirstVisitHook"));
        // Quests: ek2.atb / atc / atd (start, complete, fail).
        foreach (var n in new[] { "atb", "atc", "atd" }) harmony.Patch(typeof(ek2).GetMethod(n, any), postfix: m("QuestHook"));
        // Dialog window: zq.n1(e89) opens, zq.n2 closes.
        harmony.Patch(typeof(zq).GetMethod("n1", any), postfix: m("DialogOpened"));
        harmony.Patch(typeof(zq).GetMethod("n2", any), postfix: m("DialogClosed"));
        // Items: dcw.g(coh), the one notifier for items added to a container.
        harmony.Patch(typeof(dcw).GetMethod("g", any, null, new[] { typeof(coh) }, null), postfix: m("ItemAddedHook"));
        // XP: amc.a(int, string), how the game gives XP (kills, quests, the console).
        harmony.Patch(typeof(amc).GetMethod("a", any, null, new[] { typeof(int), typeof(string) }, null), postfix: m("XpHook"));
        // Trade: dka.amw(player, merchant) opens barter.
        harmony.Patch(typeof(dka).GetMethods(any).First(x => x.Name == "amw" && x.GetParameters().Length == 2), postfix: m("TradeHook"));
        // Death: amc.a(cwt attack, bool shattered).
        harmony.Patch(typeof(amc).GetMethod("a", any, null, new[] { typeof(cwt), typeof(bool) }, null), prefix: m("Dying"), postfix: m("Died"));
        // Combat: crp.ahn (start) / crp.ahr (end); crp.ahg() = in combat.
        harmony.Patch(typeof(crp).GetMethod("ahn", any), prefix: m("CombatBefore"), postfix: m("CombatStartedHook"));
        harmony.Patch(typeof(crp).GetMethod("ahr", any), prefix: m("CombatEnding"));
    }

    // Game types that are internal to the game (not nameable from C#): by name.
    static Type Game(string name) { var t = typeof(cnk).Assembly.GetType(name); if (t == null) throw new MissingMemberException(name); return t; }

    static void Run(string what, Action call) { try { call(); } catch (Exception ex) { Log.Write("saves: " + what + " failed: " + ex); } }

    // --- Zones -------------------------------------------------------------------------------------------------

    static void ZoneStoring(c1a __1, out List<KeyValuePair<c5x, aud>> __state)
    {
        Run("taking mod statuses out", ModStatusStore.Strip);
        List<KeyValuePair<c5x, aud>> removed = null;
        Run("removing placed characters", () => removed = World.RemovePlaced(__1));
        __state = removed;
        if (removed != null && removed.Count > 0) Log.Write("saves: kept " + removed.Count + " placed character(s) out of the stored zone");
    }

    static Exception ZoneStored(bool __2, List<KeyValuePair<c5x, aud>> __state, Exception __exception)
    {
        Run("putting mod statuses back", ModStatusStore.PutBack);
        if (__state != null && __state.Count > 0)
        {
            if (!__2) Run("putting placed characters back", () => World.PutBack(__state));     // a save: the zone stays loaded
            else Run("forgetting placed characters", () => World.Forget(null));                 // leaving: they come back on the next load
        }
        return __exception;
    }

    // While a save loads, ari loads the zone (arp) before Works restores mod quests: nothing zone-related before its end.
    static bool loading, statusPending;

    // The player is placed after the zone loads (ars, after arp; for a save, at its end): the zone's placements and
    // event wait for the first update with the zone and the player there.
    static bool zonePending;

    // First visit: arp calls d6f.a(bfc), which copies the map's own layer into the save's storage and returns true
    // only when the zone has never been stored (the game's own test).
    static bool firstVisit;
    static void FirstVisitHook(bool __result) { if (__result) firstVisit = true; }

    static void ZoneLoadedHook() { zonePending = true; }

    internal static void Tick()
    {
        if (!zonePending || loading) return;
        if (Areas.CurrentZone() == null || World.Player == null || World.Player.Area == null) return;
        zonePending = false;
        ZoneReady();
        if (statusPending) { statusPending = false; Run("restoring mod statuses", ModStatusStore.Restore); }
    }

    static void ZoneReady()
    {
        var zone = Areas.CurrentZone();
        string id = World.ZoneId;
        bool first = firstVisit; firstVisit = false;
        Log.Write("saves: zone loaded: " + id + (first ? " (first visit)" : ""));
        Run("placing characters", () => World.SpawnPlacements(zone, id));
        Run("imported character", CharacterFiles.ZoneReady);
        if (GameEvents.ZoneLoaded.Any) GameEvents.ZoneLoaded.Run(h => h(id, first));
    }

    // --- global.dat: mod quests out while it's written -----------------------------------------------------------

    sealed class Taken { public List<eza> List; public eza Entry; public int Index; }

    static void GlobalWriting(out List<Taken> __state)
    {
        Run("taking mod statuses out", ModStatusStore.Strip);
        var taken = new List<Taken>();
        __state = taken;
        Run("keeping mod quests out of the save", () =>
        {
            var log = Quests.Log;
            if (log == null) return;
            foreach (var list in new[] { log.c(), log.a(), log.b() })
                for (int i = list.Count - 1; i >= 0; i--)
                {
                    var e = list[i];
                    if (!Quests.IsModQuest(e.Name)) continue;
                    Variables.Global.Set(QuestVariable + e.Name.ToLowerInvariant(), StateName(e.State) + "," + (e.Read ? 1 : 0) + "," + i);
                    list.RemoveAt(i);
                    taken.Insert(0, new Taken { List = list, Entry = e, Index = i });
                }
        });
    }

    static Exception GlobalWritten(List<Taken> __state, Exception __exception)
    {
        Run("putting mod statuses back", ModStatusStore.PutBack);
        if (__state != null) Run("putting mod quests back", () => { foreach (var t in __state) t.List.Insert(Math.Min(t.Index, t.List.Count), t.Entry); });
        return __exception;
    }

    static string StateName(e3c s) { return s == e3c.b ? "completed" : s == e3c.c ? "failed" : "active"; }

    // After a load: rebuild the mod quests from their variables (only those whose mod is here; the rest stay saved).
    static void RestoreQuests()
    {
        var log = Quests.Log;
        var vars = bpo.GetService<cnk>().aqu().r();
        foreach (var id in Quests.ModQuestIds)
        {
            var value = vars.GetPropertyValue(QuestVariable + id.ToLowerInvariant()) as string;
            if (value == null || Quests.State(id) != QuestState.NotStarted) continue;
            var parts = value.Split(',');
            var def = ao5.a(id);
            if (def == null) continue;
            var e = new eza(def);
            e.State = parts[0] == "completed" ? e3c.b : parts[0] == "failed" ? e3c.c : e3c.a;
            e.Read = parts.Length > 1 && parts[1] == "1";
            int index; int.TryParse(parts.Length > 2 ? parts[2] : "0", out index);
            var list = e.State == e3c.b ? log.a() : e.State == e3c.c ? log.b() : log.c();
            list.Insert(Math.Max(0, Math.Min(index, list.Count)), e);
        }
    }

    // --- Lifecycle -----------------------------------------------------------------------------------------------

    static void SaveStarting(string __0) { Run("recording mod statuses", ModStatusStore.Record); Log.Write("saves: saving " + __0); if (GameEvents.Saving.Any) GameEvents.Saving.Run(h => h(__0)); }
    static void SaveDone(string __0) { Run("writing mod save data", () => SaveFiles.Save(__0)); Run("re-applying placements", World.ReapplyAll); if (GameEvents.Saved.Any) GameEvents.Saved.Run(h => h(__0)); }
    static void NewGameCreated() { SaveFiles.NewGame(); World.ForgetAll(); Run("imported character data", CharacterFiles.NewGame); if (GameEvents.NewGame.Any) GameEvents.NewGame.Run(h => h()); }

    static void Loading(string __0) { loading = true; CharacterFiles.Forget(); SaveFiles.Load(__0); World.ForgetAll(); Log.Write("saves: loading " + __0); }

    static Exception LoadFinished(Exception __exception)
    {
        loading = false;
        if (__exception != null) return __exception;
        Run("restoring mod quests", RestoreQuests);
        statusPending = true;
        Run("level", () => { var p = World.Player; ProgressHooks.Known(p == null || !p.IsCharacter ? 0 : p.Level); });
        zonePending = true;
        if (GameEvents.GameLoaded.Any) GameEvents.GameLoaded.Run(h => h());
        Markers.Raise();     // another game's markers
        return null;
    }

    static void QuestHook(string __0) { if (GameEvents.QuestChanged.Any) { var s = Quests.State(__0); GameEvents.QuestChanged.Run(h => h(__0, s)); } }

    static string openDialog;
    static void DialogOpened(bool __result, e89 __0)
    {
        if (!__result) return;
        openDialog = __0.d() == null ? null : __0.d().e();
        if (GameEvents.DialogStarted.Any) { var npc = Character.Of(__0.e()); var name = openDialog; GameEvents.DialogStarted.Run(h => h(name, npc)); }
    }
    static void DialogClosed()
    {
        var name = openDialog; openDialog = null;
        if (GameEvents.DialogEnded.Any) GameEvents.DialogEnded.Run(h => h(name));
    }

    static void TradeHook(bool __result, b0b __1)
    {
        if (__result && GameEvents.TradeOpened.Any) { var merchant = Character.Of(__1); GameEvents.TradeOpened.Run(h => h(merchant)); }
    }

    static void ItemAddedHook(dcw __instance, coh __0)
    {
        if (!GameEvents.ItemAdded.Any || __0 == null) return;
        var player = World.Player;
        if (player == null || !player.IsCharacter || __instance != PlayerBag()) return;
        var item = new Item(__0);
        GameEvents.ItemAdded.Run(h => h(item));
    }
    static dcw PlayerBag() { var game = bpo.GetService<cnk>(); var p = game == null || game.aqu() == null ? null : game.aqu().v(); return p == null ? null : p.Bag; }

    static void XpHook(amc __instance, int __0)
    {
        if (GameEvents.ExperienceGained.Any && __instance.aa() is e3q) GameEvents.ExperienceGained.Run(h => h(__0));
    }

    static void Dying(amc __instance, cwt __0)
    {
        if (!GameEvents.CharacterDying.Any || __instance.aa() == null) return;     // (Dead is already set by the blow)
        try
        {
            var c = Character.Of(__instance.a8());
            // The blow's attacker is recorded by value (ap9: name, faction, Uid): found again by Uid among the zone's characters.
            Character killer = null;
            if (__0 != null && __0.i != null && __0.i.d != Guid.Empty)
            {
                var uid = __0.i.d;
                killer = World.Characters.FirstOrDefault(x => x.GameData != null && x.GameData.Uid == uid);
            }
            GameEvents.CharacterDying.Run(h => h(c, killer));
        }
        catch (Exception ex) { Log.Write("dying: " + ex.Message); }
    }
    static void Died(amc __instance) { if (GameEvents.CharacterDied.Any) { var c = Character.Of(__instance.a8()); GameEvents.CharacterDied.Run(h => h(c)); } }

    static void CombatBefore(crp __instance, out bool __state) { __state = __instance.ahg(); }
    static void CombatStartedHook(crp __instance, bool __state) { if (!__state && __instance.ahg() && GameEvents.CombatStarted.Any) GameEvents.CombatStarted.Run(h => h()); }
    static void CombatEnding(crp __instance) { if (__instance.ahg() && GameEvents.CombatEnded.Any) GameEvents.CombatEnded.Run(h => h()); }
}

// The last line of defence: the binder (e6e.av5 -> FnaMode.SavedName) sees every type the game writes. A type from
// Works or a mod there would make the save unloadable in the stock game, so the write fails instead (the game's own
// save code catches it and writes SaveError-*.txt).
static class SaveGuard
{
    static readonly Assembly Works = typeof(SaveGuard).Assembly;
    static readonly string ModsFolder = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "Mods");

    internal static void Check(Type type)
    {
        if (type == null) return;
        var a = type.Assembly;
        bool bad = a == Works || a.GetName().Name == "0Harmony";
        if (!bad && !a.IsDynamic)
        {
            string at = "";
            try { at = a.Location; } catch { }
            bad = at.Length > 0 && Path.GetFullPath(at).StartsWith(ModsFolder, StringComparison.OrdinalIgnoreCase);
        }
        if (!bad && type.IsGenericType) foreach (var t in type.GetGenericArguments()) Check(t);
        if (bad)
        {
            Log.Write("saves: refused to write " + type.FullName + " (" + a.GetName().Name + "): the stock game couldn't load this save");
            throw new InvalidOperationException("UnderRailWorks: a mod's object (" + type.FullName + ") would be written into the save");
        }
    }
}
}
