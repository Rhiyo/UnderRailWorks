// UnderRailWorks: dialogs for mods. A mod describes a conversation with Works' builder; Works builds the game's own
// dialog model from it (azm, DialogModel: Question = an NPC line, Answer = a player option, junctions for branches)
// each time it is opened, and the game runs it in its own dialog window. What the game does, measured from its code:
// UnderRailWorks.md, "Dialogs". Dialog models are never written to saves, so Works' conditions and actions (plain C#)
// are safe; anything they change (variables, quests, items) goes through the stock systems.
//   Dialogs.Define("mymod/stranger", d =>
//   {
//       d.Line("start", "Got a minute?")
//        .Option("Who are you?", "who")
//        .Option("Need any help?", "job", Condition.Not(Condition.Flag("mymod_jobGiven")))
//        .Option("Bye.", Dialog.End);
//       d.Line("who", "Nobody important.").Next("start");
//       d.Line("job", "Find my crate.", Outcome.SetFlag("mymod_jobGiven"), Outcome.StartQuest("mymod_crate")).Next("start");
//   });
//   Dialogs.Start(character, "mymod/stranger");      or: Dialogs.Override(character, "mymod/stranger") (Talk opens it)
// Text may use the game's $(context.<variable>) tokens.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Ouroboros.Common.Data;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

// A condition on game state, for options, branches and lines.
public sealed class Condition
{
    internal readonly Func<bool> Test;
    Condition(Func<bool> test) { Test = test; }

    public static Condition Code(Func<bool> test) { return new Condition(test); }
    // A global variable: a flag is set (true), an int compares, a string equals.
    public static Condition Flag(string variable, bool value = true) { return new Condition(() => Variables.Global.Get(variable, false) == value); }
    public static Condition AtLeast(string variable, int value) { return new Condition(() => Variables.Global.Get(variable, 0) >= value); }
    public static Condition Equals(string variable, string value) { return new Condition(() => Variables.Global.Get<string>(variable, null) == value); }
    public static Condition Quest(string id, QuestState state) { return new Condition(() => Quests.State(id) == state); }
    public static Condition Not(Condition c) { return new Condition(() => !c.Test()); }
    public static Condition All(params Condition[] c) { return new Condition(() => c.All(x => x.Test())); }
    public static Condition Any(params Condition[] c) { return new Condition(() => c.Any(x => x.Test())); }
}

// Something a line or option does when reached.
public sealed class Outcome
{
    internal readonly Action Run;
    Outcome(Action run) { Run = run; }

    public static Outcome Code(Action run) { return new Outcome(run); }
    public static Outcome SetFlag(string variable, bool value = true) { return new Outcome(() => Variables.Global.Set(variable, value)); }
    public static Outcome SetInt(string variable, int value) { return new Outcome(() => Variables.Global.Set(variable, value)); }
    public static Outcome AddInt(string variable, int amount) { return new Outcome(() => Variables.Global.Set(variable, Variables.Global.Get(variable, 0) + amount)); }
    public static Outcome SetString(string variable, string value) { return new Outcome(() => Variables.Global.Set(variable, value)); }
    public static Outcome StartQuest(string id) { return new Outcome(() => Quests.Start(id)); }
    public static Outcome CompleteQuest(string id) { return new Outcome(() => Quests.Complete(id)); }
    public static Outcome FailQuest(string id) { return new Outcome(() => Quests.Fail(id)); }
    public static Outcome Log(string text) { return new Outcome(() => Notify.Log(text)); }
}

public static class Dialog
{
    // goTo target that ends the conversation.
    public const string End = null;
}

public sealed class DialogLine
{
    internal readonly string Id, Text;
    internal readonly Outcome[] Effects;
    internal readonly List<DialogOption> Options = new List<DialogOption>();
    internal string NextId;
    internal bool HasNext;
    internal DialogLine(string id, string text, Outcome[] effects) { Id = id; Text = text; Effects = effects; }

    // A player option. goTo: the line or branch it leads to, Dialog.End to close. when: shown only while it holds.
    public DialogLine Option(string text, string goTo, params Outcome[] then) { return Option(text, goTo, null, then); }
    public DialogLine Option(string text, string goTo, Condition when, params Outcome[] then)
    {
        Options.Add(new DialogOption { Text = text, GoTo = goTo, When = when, Effects = then }); return this;
    }
    // The same, but shown greyed out (not hidden) while the condition fails, as the game shows failed checks.
    public DialogLine OptionGreyed(string text, string goTo, Condition enabledWhen, params Outcome[] then)
    {
        Options.Add(new DialogOption { Text = text, GoTo = goTo, EnabledWhen = enabledWhen, Effects = then }); return this;
    }
    // No options: the game shows Continue and goes on to this line (Dialog.End: closes).
    public DialogLine Next(string goTo) { NextId = goTo; HasNext = true; return this; }
}

sealed class DialogOption
{
    public string Text, GoTo;
    public Condition When, EnabledWhen;
    public Outcome[] Effects;
}

public sealed class DialogBranch
{
    internal readonly string Id;
    internal readonly List<KeyValuePair<Condition, string>> Cases = new List<KeyValuePair<Condition, string>>();
    internal DialogBranch(string id) { Id = id; }
    // The first case that holds is taken.
    public DialogBranch If(Condition when, string goTo) { Cases.Add(new KeyValuePair<Condition, string>(when, goTo)); return this; }
    public DialogBranch Else(string goTo) { Cases.Add(new KeyValuePair<Condition, string>(null, goTo)); return this; }
}

public sealed class DialogBuilder
{
    internal readonly Dictionary<string, object> Nodes = new Dictionary<string, object>();
    internal string StartId;
    internal bool CanCancel = true;

    // An NPC line (the first one added is where the conversation starts, unless Start says otherwise).
    public DialogLine Line(string id, string text, params Outcome[] effects)
    {
        var line = new DialogLine(id, text, effects);
        Add(id, line);
        return line;
    }

    // A silent choice between lines, by condition (e.g. greet differently the second time).
    public DialogBranch Branch(string id)
    {
        var branch = new DialogBranch(id);
        Add(id, branch);
        return branch;
    }

    public DialogBuilder Start(string id) { StartId = id; return this; }
    // Escape closes the conversation (default true).
    public DialogBuilder Cancellable(bool value) { CanCancel = value; return this; }

    void Add(string id, object node)
    {
        if (Nodes.ContainsKey(id)) throw new ArgumentException("two dialog nodes named " + id);
        Nodes[id] = node;
        if (StartId == null) StartId = id;
    }
}

public static partial class Dialogs
{
    static readonly Dictionary<string, Action<DialogBuilder>> defined = new Dictionary<string, Action<DialogBuilder>>(StringComparer.OrdinalIgnoreCase);
    static readonly ConditionalWeakTable<b0b, string> overrides = new ConditionalWeakTable<b0b, string>();

    // A conversation, by name (prefix it with the mod's id). The builder runs each time it is opened, so it may read
    // game state (but conditions are the cleaner way).
    public static void Define(string name, Action<DialogBuilder> build) { defined[name] = build; }

    public static bool IsDefined(string name) { return name != null && defined.ContainsKey(name); }

    // Talking to this character opens the named conversation (Works' or the game's). Kept in memory only: the
    // character's own dialog stays as saved.
    public static void Override(Character npc, string name)
    {
        overrides.Remove(npc.Entity);
        if (name != null) overrides.Add(npc.Entity, name);
    }

    // Opens a conversation between the player and npc now (false if the game refuses: combat, hostile, one is open).
    public static bool Start(Character npc, string name)
    {
        var player = World.Player;
        var screen = bpo.GetService<ps>();
        if (player == null || npc == null || screen == null) return false;
        var actors = Actors(player.Entity, npc.Entity);
        return screen.amu(name, player.Entity, npc.Entity, actors, false, false, true, null, false, false);
    }

    // As c9m (the Talk action) gathers them: both sides, then everyone in the area.
    static List<ble> Actors(b0b initiator, b0b target)
    {
        var list = new List<ble>();
        foreach (var e in new[] { initiator, target })
        {
            var c = e as c5x;
            var actor = c == null ? null : c.GetAspect<j7>();
            if (actor != null && !list.Contains(actor)) list.Add(actor);
        }
        if (target.Area != null) foreach (var a in a37.a(target.Area)) if (!list.Contains(a)) list.Add(a);
        return list;
    }

    // --- The hook: start of the game's "start dialog by name" (dka.amu) ---------------------------------------------

    internal static bool Prefix(dka screen, ref string name, b0b initiator, b0b target, List<ble> actors, bool ignoreCombat, bool barkMode, bool face,
        PropertyCollection temp, bool a8, bool ignoreHostility, ref bool result)
    {
        string over;
        if (target != null && overrides.TryGetValue(target, out over)) name = over;
        if (GameEvents.DialogStarting.Any && name != null && !barkMode)
        {
            var e = new DialogStart { Name = name, With = target == null ? null : Character.Of(target) };
            GameEvents.DialogStarting.Run(h => h(e));
            if (e.Cancel) { result = false; return false; }
            name = e.Name;     // a handler may send it to another dialog
        }
        Action<DialogBuilder> build;
        if (name == null || !defined.TryGetValue(name, out build)) return true;     // the game's own dialog
        azm model;
        try { model = Build(name, build, ActorId(target), ActorId(initiator)); }
        catch (Exception ex) { Log.Write("dialogs: " + name + " failed to build: " + ex); result = false; return false; }
        result = screen.amv(model, initiator, target, actors, ignoreCombat, barkMode, face, temp, a8, ignoreHostility);
        return false;
    }

    static string ActorId(b0b e)
    {
        var c = e as c5x;
        var actor = c == null ? null : c.GetAspect<j7>();
        return actor == null ? "player" : actor.e9();
    }

    // A fresh model each time (the game changes models after loading them).
    static azm Build(string name, Action<DialogBuilder> build, string npcId, string playerId)
    {
        var b = new DialogBuilder();
        build(b);
        if (b.StartId == null) throw new InvalidOperationException("the dialog has no lines");
        var model = new azm(name);
        var npc = new ax8(npcId); var player = new ax8(playerId);
        model.a().Add(player); model.a().Add(npc);
        model.b(b.CanCancel);
        var start = new a31("Start Element");
        start.PossibleNextSteps.Add(new NodeMaker(b, npc, player, null).Node(b.StartId));
        model.a(start);
        return model;
    }

    // Turns a builder's lines and branches into the game's nodes. external resolves ids the builder doesn't have
    // (a patch's "@..." references to the stock dialog's lines).
    internal sealed class NodeMaker
    {
        readonly DialogBuilder b;
        readonly ax8 npc, player;
        readonly Func<string, d55> external;
        readonly Dictionary<string, d55> made = new Dictionary<string, d55>();
        int index;
        internal NodeMaker(DialogBuilder b, ax8 npc, ax8 player, Func<string, d55> external) { this.b = b; this.npc = npc; this.player = player; this.external = external; }

        internal atr Option(DialogOption o)
        {
            var a = new atr("a" + (index++) + " (" + Guid.NewGuid() + ")");
            a.b(o.Text);
            if (o.When != null) a.CausingCondition = new WorksCondition(o.When);
            if (o.EnabledWhen != null) a.a(new WorksCondition(Condition.Not(o.EnabledWhen)));     // the game's "disabled when"
            if (o.Effects != null) foreach (var e in o.Effects) a.e().Add(new WorksAction(e));
            if (o.GoTo != null) a.PossibleNextSteps.Add(Node(o.GoTo));
            return a;
        }

        internal d55 Node(string id)
        {
            d55 n;
            if (made.TryGetValue(id, out n)) return n;
            object def;
            if (!b.Nodes.TryGetValue(id, out def))
            {
                n = external == null ? null : external(id);
                if (n == null) throw new ArgumentException("no dialog node named " + id);
                return n;
            }
            var line = def as DialogLine;
            if (line != null)
            {
                var q = new ad7(id + " (" + Guid.NewGuid() + ")") { Questioner = npc, Answerer = player };
                q.b(line.Text);
                made[id] = q;     // before its options: they may lead back to it
                foreach (var e in line.Effects) q.e().Add(new WorksAction(e));
                foreach (var o in line.Options) q.PossibleAnswers.Add(Option(o));
                if (line.HasNext && line.NextId != null) q.PossibleNextSteps.Add(Node(line.NextId));
                return q;
            }
            var branch = (DialogBranch)def;
            var j = new a31(id + " (" + Guid.NewGuid() + ")");
            made[id] = j;
            foreach (var c in branch.Cases)
            {
                // A junction per case carries the condition, so a line reached from several places isn't gated.
                var gate = new a31("case (" + Guid.NewGuid() + ")");
                if (c.Key != null) gate.CausingCondition = new WorksCondition(c.Key);
                if (c.Value != null) gate.PossibleNextSteps.Add(Node(c.Value));
                j.PossibleNextSteps.Add(gate);
            }
            return j;
        }
    }
}

// Works' condition and action nodes inside a dialog model (never saved: models aren't).
sealed class WorksCondition : dnh
{
    readonly Condition condition;
    public WorksCondition(Condition condition) { this.condition = condition; }
    public override bool cw(bq8 context, bq8 temp)
    {
        try { return condition.Test(); }
        catch (Exception ex) { Log.Write("dialogs: a condition failed: " + ex.Message); return false; }
    }
}

sealed class WorksAction : esc
{
    readonly Outcome effect;
    public WorksAction(Outcome effect) { this.effect = effect; }
    public override int d5(bq8 context, bq8 temp)
    {
        try { effect.Run(); }
        catch (Exception ex) { Log.Write("dialogs: an effect failed: " + ex.Message); }
        return 1;
    }
}
}
