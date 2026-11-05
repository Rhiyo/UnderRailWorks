// UnderRailWorks: game variables, quests, text and notifications for mods, in Works' own types. What the game does,
// measured from its code (UnderRailWorks.md, "Quests and variables"):
// - Variables: named values in PropertyCollections: the game's global one (cnk.aqu().r(), saved in global.dat), each
//   zone's, each character's. Game scripts and dialogs keep all story state there (quests_*, frag_*, dun_*...).
// - Quests: defined in code at startup (b4m.a: ao5.a(group, name, xp, oddityXp, text)), no stages (a multi-step quest
//   is several quests), run by the quest controller (cnk.aqz(): start atb, complete atc (grants the XP), fail atd),
//   their state saved as name + state in global.dat. The journal's text comes from the knowledge table "quests/<name>".
// - Text: qv.b("category/key") (the language's .txt, then data/Knowledge/<category>.k). Works answers first for text
//   mods add (in memory only: never written anywhere).
//   Variables.Global.Set("mymod_metStranger", true);  Variables.Global.Get("mymod_metStranger", false)
//   Quests.Define("mymod_findTheCrate", "South Gate Station", "Find the stranger's crate.", xp: 100);
//   Quests.Start("mymod_findTheCrate");  Quests.State(...)  Quests.Complete(...)
//   Text.Set("ui", "mymod_title", "...");  Notify.Log("text");  Notify.OverPlayer("text", Color.Gold)
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Ouroboros.Common.Data;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

// A set of named game variables. Values are bool, int, float or string only: the stock game must be able to read
// every save, and these are saved with their owner.
public sealed class Variables
{
    readonly Func<PropertyCollection> source;
    internal Variables(Func<PropertyCollection> source) { this.source = source; }

    // The game's global variables (where story flags and quest progress live); null-safe on the main menu.
    public static readonly Variables Global = new Variables(() => { var game = bpo.GetService<cnk>(); return game == null || game.aqu() == null ? null : game.aqu().r(); });
    // The current zone's own variables (saved with the zone).
    public static readonly Variables Zone = new Variables(() => { var zone = Areas.CurrentZone(); return zone == null ? null : zone.CustomProperties; });

    // There is a game loaded to read and write.
    public bool Available { get { return source() != null; } }

    public bool Has(string name) { var p = source(); return p != null && p.HasProperty(name); }

    public T Get<T>(string name, T fallback = default(T))
    {
        var p = source();
        object v = p == null ? null : p.GetPropertyValue(name);
        if (v is T) return (T)v;
        if (v == null) return fallback;
        try { return (T)Convert.ChangeType(v, typeof(T), System.Globalization.CultureInfo.InvariantCulture); }     // e.g. an int read as float
        catch { return fallback; }
    }

    public void Set(string name, bool value) { Put(name, value); }
    public void Set(string name, int value) { Put(name, value); }
    public void Set(string name, float value) { Put(name, value); }
    public void Set(string name, string value) { Put(name, value); }
    public void Remove(string name) { var p = source(); if (p != null) p.RemoveProperty(name); }

    void Put(string name, object value)
    {
        var p = source();
        if (p == null) throw new InvalidOperationException("no game is loaded");
        p[name] = value;
    }
}

public enum QuestState { NotStarted, Active, Completed, Failed }

public sealed class QuestInfo
{
    public string Id { get; set; }
    public string Group { get; set; }
    public string Text { get; set; }
    public int Xp { get; set; }
    public int OddityXp { get; set; }
    public bool FromMod { get; set; }
}

public static class Quests
{
    static readonly HashSet<string> modQuests = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
    internal static IEnumerable<string> ModQuestIds { get { return modQuests; } }
    internal static bool IsModQuest(string id) { return id != null && modQuests.Contains(id); }

    // A new quest: id (unique: prefix it with the mod's id), the journal heading it's listed under (the game uses area
    // names: "South Gate Station"), its journal text, and the XP completing it gives (classic and oddity systems).
    // Call it from Load: the game resolves quest names once per save it loads.
    public static void Define(string id, string group, string journalText, int xp = 0, int oddityXp = 0)
    {
        if (string.IsNullOrEmpty(id) || string.IsNullOrEmpty(journalText)) throw new ArgumentException("a quest needs an id and journal text");
        if (ao5.a(id) != null) throw new InvalidOperationException("a quest named " + id + " already exists");
        ao5.a(group ?? "", id, xp, oddityXp, journalText);     // the game's own registry (b4m adds the stock quests the same way)
        Text.Set("quests", id, journalText);                    // the journal shows the knowledge text, not the definition's
        modQuests.Add(id);
    }

    static ex7 Controller { get { var game = bpo.GetService<cnk>(); return game == null || game.aqu() == null ? null : game.aqz(); } }

    // As the game's dialogs and scripts do: announce = the chat line ("New journal entry: ...") and, on completion,
    // the XP. Each returns whether the quest changed state.
    public static bool Start(string id, bool announce = true) { var c = Controller; return c != null && c.atb(id, announce); }
    public static bool Complete(string id, bool announce = true)
    {
        var c = Controller;
        if (c == null || State(id) != QuestState.Active) return false;
        c.atc(id, announce);     // always returns false in the game
        return State(id) == QuestState.Completed;
    }
    public static bool Fail(string id, bool announce = true)
    {
        var c = Controller;
        if (c == null || State(id) != QuestState.Active) return false;
        c.atd(id, announce);
        return State(id) == QuestState.Failed;
    }
    // The game's way to "advance": complete one quest, start the next.
    public static void Advance(string fromId, string toId, bool announce = true) { Complete(fromId, announce); Start(toId, announce); }

    public static QuestState State(string id)
    {
        var log = Log;
        if (log == null) return QuestState.NotStarted;
        if (log.c().Any(e => Same(e, id))) return QuestState.Active;
        if (log.a().Any(e => Same(e, id))) return QuestState.Completed;
        if (log.b().Any(e => Same(e, id))) return QuestState.Failed;
        return QuestState.NotStarted;
    }

    public static IList<string> Active { get { return Ids(l => l.c()); } }
    public static IList<string> Completed { get { return Ids(l => l.a()); } }
    public static IList<string> Failed { get { return Ids(l => l.b()); } }

    // One quest's definition (null if unknown).
    public static QuestInfo Info(string id)
    {
        var q = ao5.a(id);
        return q == null ? null : new QuestInfo { Id = q.Name, Group = q.Group, Text = Text.Get("quests/" + q.Name), Xp = q.ClassicExperience, OddityXp = q.OddityExperience, FromMod = IsModQuest(q.Name) };
    }

    // A quest's journal text as the journal shows it (erf: the knowledge text, then $(...) and gender tokens
    // filled in by eb2 with the game's context).
    public static string JournalText(string id)
    {
        var text = Text.Get("quests/" + id);
        var game = bpo.GetService<cnk>();
        if (string.IsNullOrEmpty(text) || game == null || game.aqu() == null) return text ?? "";
        try { return eb2.a(game.aqu().e(), new Ouroboros.Common.Data.PropertyCollection(), text); } catch { return text; }
    }

    // Every quest the game knows (stock and mods').
    public static IList<QuestInfo> All
    {
        get
        {
            return ((Dictionary<string, eei>)Registry.GetValue(null)).Values.Select(q => new QuestInfo { Id = q.Name, Group = q.Group, Text = Text.Get("quests/" + q.Name), Xp = q.ClassicExperience, OddityXp = q.OddityExperience, FromMod = IsModQuest(q.Name) }).ToList();
        }
    }

    // ao5.a: the game's quest registry (a field sharing its name with methods: reflection).
    static readonly FieldInfo Registry = typeof(ao5).GetField("a", BindingFlags.Static | BindingFlags.Public);

    // The save's quest lists (bmw: active c(), completed a(), failed b()).
    internal static bmw Log { get { var game = bpo.GetService<cnk>(); return game == null || game.aqu() == null ? null : game.aqu().t(); } }
    static bool Same(eza e, string id) { return string.Equals(e.Name, id, StringComparison.OrdinalIgnoreCase); }
    static IList<string> Ids(Func<bmw, List<eza>> list) { var log = Log; return log == null ? new string[0] : list(log).Select(e => e.Name).ToArray(); }
}

public static class Text
{
    static readonly Dictionary<string, string> added = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

    // The game's text for "category/key" (e.g. "quests/jonaswatch", "ui/..."), in the player's language; "" if none.
    public static string Get(string path) { return qv.b(path) ?? ""; }

    // "1 turn", "3 turns": a count with its noun, plural with an s unless given (Count(n, "enemy", "enemies")).
    public static string Count(int n, string one, string many = null) { return n + " " + (n == 1 || n == -1 ? one : many ?? one + "s"); }

    // Text a mod adds (or replaces), in every language: "category/key". In memory only.
    public static void Set(string category, string key, string text) { added[category + "/" + key] = text; }

    // The hook at the start of qv.b: Works' text first.
    internal static bool Lookup(string path, out string text) { return added.TryGetValue(path ?? "", out text); }
}

public static class Notify
{
    static readonly ConstructorInfo PenCtor = typeof(d22).GetConstructors().First(c => c.GetParameters().Length == 2);
    static readonly ConstructorInfo FloatingCtor = typeof(d53).GetConstructors().First(c => c.GetParameters().Length == 4);
    static readonly Color Default = new Color(230, 200, 120);

    // A line in the game's chat/log (where "New journal entry: ..." appears).
    public static void Log(string text, Color? colour = null)
    {
        var pen = (d22)PenCtor.Invoke(new object[] { b94.a(), colour ?? Default });
        var rich = new ecs(new[] { new dl6(new[] { new bph(pen, text) }) });
        a7v.a(new dx(rich));
    }

    // Floating text over the player (always shown, like the game's own notices); big = enlarged.
    public static void OverPlayer(string text, Color? colour = null, bool big = false) { Over(World.Player, text, colour, big); }

    // Floating text over any character.
    public static void Over(Character who, string text, Color? colour = null, bool big = false)
    {
        if (who == null) return;
        bmj.a(who.Entity, (c3r)FloatingCtor.Invoke(new object[] { text, colour ?? Default, null, big }));
    }
}

// The Harmony hooks behind this file, installed with the UI's.
static class ContentHooks
{
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var lookup = typeof(qv).GetMethod("b", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(string) }, null);
        harmony.Patch(lookup, prefix: new HarmonyLib.HarmonyMethod(typeof(ContentHooks).GetMethod("TextFirst", BindingFlags.Static | BindingFlags.NonPublic)));
        var start = typeof(dka).GetMethods(BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic).First(m => m.Name == "amu" && m.GetParameters().Length == 10);
        harmony.Patch(start, prefix: new HarmonyLib.HarmonyMethod(typeof(ContentHooks).GetMethod("DialogByName", BindingFlags.Static | BindingFlags.NonPublic)));
    }

    // dka.amu(name, initiator, target, actors, ignoreCombat, barkMode, face, temp, ?, ignoreHostility)
    static bool DialogByName(dka __instance, ref string __0, b0b __1, b0b __2, List<ble> __3, bool __4, bool __5, bool __6, PropertyCollection __7, bool __8, bool __9, ref bool __result)
    {
        try { return Dialogs.Prefix(__instance, ref __0, __1, __2, __3, __4, __5, __6, __7, __8, __9, ref __result); }
        catch (Exception ex) { Log.Write("dialogs: " + ex); return true; }
    }

    static bool TextFirst(string __0, ref string __result)
    {
        string text;
        if (!Text.Lookup(__0, out text)) return true;
        __result = text;
        return false;
    }
}
}
