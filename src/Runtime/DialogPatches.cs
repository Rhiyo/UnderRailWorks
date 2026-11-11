// UnderRailWorks: adding to the game's own conversations (as WeiDU's EXTEND/APPEND do for the Infinity Engine games).
// A patch names a stock dialog (its file name under data/dialogs, e.g. "characters\\abram"; ContentIndex lists them)
// and adds options to its NPC lines, leading into the mod's own lines, which may lead back. The game loads a dialog
// fresh from disk each time it opens (bmz.y3), then fills in its texts (aji.a); Works patches the loaded model right
// after that, so nothing is ever written to the dialog files or the save (dialog models are never saved).
//   Dialogs.Patch("characters\\abram", p =>
//   {
//       p.AddOption("Anything else", "Heard any rumours about the Tchortists?", "rumour");
//       p.Line("rumour", "Only that they pay well and ask little.").Option("Back to business.", "@");
//   });
// Where an option goes: one of the patch's lines; "@" back to the stock line the option was added to; "@text" the
// stock NPC line whose text contains "text"; Dialog.End (null) ends the conversation.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class DialogPatch
{
    internal readonly DialogBuilder Builder = new DialogBuilder();
    internal readonly List<KeyValuePair<string, DialogOption>> Added = new List<KeyValuePair<string, DialogOption>>();
    internal readonly List<KeyValuePair<string, bool>> First = new List<KeyValuePair<string, bool>>();

    // A line of the mod's own, reached from an added option.
    public DialogLine Line(string id, string text, params Outcome[] effects) { return Builder.Line(id, text, effects); }
    public DialogBranch Branch(string id) { return Builder.Branch(id); }

    // An option on every stock NPC line whose English text contains atLine (or whose node name is atLine), after the
    // game's own options. when: shown only while it holds.
    public DialogPatch AddOption(string atLine, string text, string goTo, Condition when = null, params Outcome[] effects)
    {
        Added.Add(new KeyValuePair<string, DialogOption>(atLine, new DialogOption { Text = text, GoTo = goTo, When = when, Effects = effects }));
        return this;
    }
    // The same, greyed out unless enabledWhen holds (the game's skill-check look).
    public DialogPatch AddOptionGreyed(string atLine, string text, string goTo, Condition enabledWhen, params Outcome[] effects)
    {
        Added.Add(new KeyValuePair<string, DialogOption>(atLine, new DialogOption { Text = text, GoTo = goTo, EnabledWhen = enabledWhen, Effects = effects }));
        return this;
    }
}

public static partial class Dialogs
{
    static readonly Dictionary<string, List<KeyValuePair<string, Action<DialogPatch>>>> patches = new Dictionary<string, List<KeyValuePair<string, Action<DialogPatch>>>>(StringComparer.OrdinalIgnoreCase);

    // Adds to a stock dialog each time it opens. Several mods may patch the same dialog (in load order).
    public static void Patch(WorksMod mod, string dialog, Action<DialogPatch> patch) { Patch(mod.Id, dialog, patch); }
    internal static void Forget(string mod) { foreach (var l in patches.Values) l.RemoveAll(kv => kv.Key == mod); }
    internal static void Patch(string mod, string dialog, Action<DialogPatch> patch)
    {
        List<KeyValuePair<string, Action<DialogPatch>>> list;
        if (!patches.TryGetValue(Norm(dialog), out list)) patches[Norm(dialog)] = list = new List<KeyValuePair<string, Action<DialogPatch>>>();
        list.Add(new KeyValuePair<string, Action<DialogPatch>>(mod, patch));
    }

    // The NPC lines of a stock dialog (English text), for finding where to patch. Loads it from disk.
    public static IList<string> StockLines(string dialog)
    {
        var model = bpo.GetServiceOrThrow<eve>().y3(dialog);
        if (model == null) return new List<string>();
        aji.a(model, dialog);
        return Questions(model).Select(q => q.g() ?? "").ToList();
    }

    static string Norm(string name) { return (name ?? "").Replace('/', '\\').Trim('\\'); }

    // Every NPC line reachable from the start.
    static List<ad7> Questions(azm model)
    {
        var seen = new HashSet<d55>();
        var result = new List<ad7>();
        var todo = new Stack<d55>();
        if (model.b() != null) todo.Push(model.b());
        while (todo.Count > 0)
        {
            var n = todo.Pop();
            if (n == null || !seen.Add(n)) continue;
            var q = n as ad7;
            if (q != null) { result.Add(q); foreach (var a in q.PossibleAnswers) todo.Push(a); }
            foreach (var next in n.PossibleNextSteps) todo.Push(next);
        }
        return result;
    }

    // The lines whose options are shown after q: q itself if it has options, else the first lines with options that
    // its option-less continuation reaches (through junctions and option-less lines; each branch).
    static List<ad7> OptionsHolders(ad7 q)
    {
        if (q.PossibleAnswers.Count > 0 || q.PossibleNextSteps.Count == 0) return new List<ad7> { q };
        var result = new List<ad7>();
        var seen = new HashSet<d55> { q };
        var todo = new Queue<d55>(q.PossibleNextSteps);
        while (todo.Count > 0 && seen.Count < 50)
        {
            var n = todo.Dequeue();
            if (n == null || !seen.Add(n)) continue;
            var next = n as ad7;
            if (next != null && next.PossibleAnswers.Count > 0) { result.Add(next); continue; }
            if (n is atr) continue;
            foreach (var step in n.PossibleNextSteps) todo.Enqueue(step);
        }
        return result.Count > 0 ? result : new List<ad7> { q };
    }

    static bool Matches(ad7 q, string at)
    {
        if (string.IsNullOrEmpty(at)) return false;
        var text = q.g();
        return (text != null && text.IndexOf(at, StringComparison.OrdinalIgnoreCase) >= 0) || string.Equals(q.Name, at, StringComparison.OrdinalIgnoreCase);
    }

    // aji.a(model, name): the loaded model's texts are final; apply the patches.
    internal static void Patched(azm __0, string __1)
    {
        List<KeyValuePair<string, Action<DialogPatch>>> list;
        if (__0 == null || __1 == null || !patches.TryGetValue(Norm(__1), out list)) return;
        var questions = Questions(__0);
        foreach (var entry in list)
        {
            try
            {
                var p = new DialogPatch();
                entry.Value(p);
                int added = 0;
                foreach (var add in p.Added)
                {
                    foreach (var q in questions.Where(x => Matches(x, add.Key)).ToList())
                    {
                        var here = q;
                        var maker = new NodeMaker(p.Builder, q.Questioner, q.Answerer, id =>
                        {
                            if (id == "@") return here;
                            if (id.StartsWith("@")) return questions.FirstOrDefault(x => Matches(x, id.Substring(1)));
                            return null;
                        });
                        // A line without options of its own that goes on (Continue, or straight on) to the line that
                        // shows them (Jyles' "What room...?" -> an empty line with the rooms): the option goes there. On
                        // the line itself it would make it a question and the game's own options would never show.
                        foreach (var holder in OptionsHolders(q))
                        {
                            holder.PossibleAnswers.Add(maker.Option(add.Value));
                            added++;
                        }
                    }
                }
                if (added == 0) Log.Write("dialogs: " + entry.Key + "'s patch of " + __1 + " found no line to add to");
            }
            catch (Exception ex) { Log.Write("dialogs: " + entry.Key + "'s patch of " + __1 + " failed: " + ex.Message); }
        }
    }

    internal static void InstallPatches(HarmonyLib.Harmony harmony)
    {
        harmony.Patch(typeof(aji).GetMethod("a", BindingFlags.Static | BindingFlags.Public, null, new[] { typeof(azm), typeof(string) }, null),
            postfix: new HarmonyLib.HarmonyMethod(typeof(Dialogs).GetMethod("Patched", BindingFlags.Static | BindingFlags.NonPublic)));
    }
}
}
