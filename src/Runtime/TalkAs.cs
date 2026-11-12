// UnderRailWorks: a conversation held by a character other than the player (a co-op partner, a second protagonist)
// that someone else follows and answers (over the network, a mod's own window...).
//   TalkAs.Talk(buddy, npc, line => Show(line.Speaker, line.Text, line.Choices), () => Hide());
//   TalkAs.Choose(2);      // the answer the remote player picked (-1: Continue / End when there are none)
// The character walks to the NPC and starts the conversation as the player's click does (its own j7.a), the game runs
// it as usual (scripts, quests, items, XP all happen in this game), but:
// - the character's own sheet answers the dialog's checks (skills, items, stats: the game reads them from "the
//   player", cyt.v(), which is swapped to its sheet while the dialog is evaluated, synchronously, and back);
// - the dialog window stays hidden on this machine, and this game isn't paused for it (the counters zq.n1 raises,
//   dka.aml/amf/amh, are lowered again and raised before zq.n2 lowers them);
// - only one conversation at a time, as in the game (the player can't start one meanwhile).
// Needs a character with a whole player sheet (World.SpawnFromCharacterFile). The game's side: the dialog controller
// zq (dka.aky()), its runtime epf (zq.nw(); at7() the node: b() text, d() choices aof with Text and Answer.d() =
// disabled), zq.nx() the text shown, zq.nt() the speaker, the window d49 (zq.h()), d49.a(int, ecs) answers.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public sealed class TalkChoice { public int Index { get; internal set; } public string Text { get; internal set; } public bool Enabled { get; internal set; } }
    public sealed class TalkLine
    {
        // Who holds the conversation (the player, or TalkAs's speaker).
        public Character Talker { get; internal set; }
        public string Speaker { get; internal set; }
        public string Text { get; internal set; }
        // Empty: the line only goes on (Choose(-1): Continue, or End when it's the last).
        public IList<TalkChoice> Choices { get; internal set; }
    }

    public static partial class GameEvents
    {
        // Every line any conversation shows (the player's own, or one held through TalkAs): who holds it, the line, the
        // answers. For showing it elsewhere (co-op players listening, a log). GameEvents.DialogEnded when it closes.
        public static readonly Hook<Action<TalkLine>> ConversationLine = new Hook<Action<TalkLine>>("ConversationLine");
    }

    public static class TalkAs
    {
        // False: no such characters, the speaker has no player sheet, or a conversation is already going.
        public static bool Talk(Character speaker, Character npc, Action<TalkLine> onLine, Action onEnd) { return Talk(speaker, npc, onLine, onEnd, false); }
        // pause: the game pauses while it runs, as for the player's own conversations (else it goes on).
        public static bool Talk(Character speaker, Character npc, Action<TalkLine> onLine, Action onEnd, bool pause)
        {
            var me = speaker == null ? null : speaker.Entity as c5x;
            var them = npc == null ? null : npc.Entity as c5x;
            var sheet = me == null || me.GetAspect<amc>() == null ? null : me.GetAspect<amc>().aa() as au2;
            var mine = me == null ? null : me.GetAspect<j7>(); var theirs = them == null ? null : them.GetAspect<j7>();
            if (sheet == null || mine == null || theirs == null || IsOpen) return false;
            var run = new TalkHooks.Run { Speaker = me, Sheet = sheet, OnLine = onLine, OnEnd = onEnd, Npc = npc, Who = speaker, Mine = mine, Theirs = theirs, KeepPaused = pause };
            // Walks next to the NPC first (its ordinary walk order), then the conversation starts without the game's own
            // approach step (which didn't move a character a mod controls).
            var at = npc.Position;
            if (Math.Abs(speaker.Position.X - at.X) + Math.Abs(speaker.Position.Y - at.Y) > 2.5f)
            {
                var spot = World.FreeTilesNear((int)Math.Round(at.X), (int)Math.Round(at.Y), 2).OrderBy(t => Math.Abs(t.X - at.X) + Math.Abs(t.Y - at.Y)).ThenBy(t => Math.Abs(t.X - speaker.Position.X) + Math.Abs(t.Y - speaker.Position.Y)).FirstOrDefault();
                if (spot == Microsoft.Xna.Framework.Point.Zero || !speaker.WalkTo(spot.X, spot.Y)) return false;
                TalkHooks.Walking = run; run.Waited = 0;
                return true;
            }
            TalkHooks.Start(run);
            return true;
        }
        public static bool IsOpen { get { return TalkHooks.Active != null || TalkHooks.Walking != null || TalkHooks.Pending != null; } }
        // Answers with choice index (as listed in the last line), or -1 to go on / end when it has none.
        public static bool Choose(int index) { return TalkHooks.Choose(index); }
        // Ends the conversation now.
        public static void Close() { var a = TalkHooks.Active; if (a != null && a.Controller != null) a.Controller.ad(); }
    }
}

namespace UnderRailWorks.Internal
{
    static class TalkHooks
    {
        internal sealed class Run { public c5x Speaker; public au2 Sheet; public Action<TalkLine> OnLine; public Action OnEnd; public zq Controller; public bool Paused; public au2 Saved; public Character Npc, Who; public j7 Mine, Theirs; public double Waited; public bool KeepPaused; }
        internal static Run Pending, Active, Walking;

        internal static void Start(Run r)
        {
            Pending = r;
            r.Mine.a(r.Theirs, false, null, true);
            // Refused (the player is in combat, another conversation...): nothing opened.
            if (Pending == r) { Pending = null; End(r); }
        }

        // Each update: a speaker walking to its NPC starts talking once it's there and stopped (or gives up after 30 s).
        internal static void Tick(double seconds)
        {
            var w = Walking;
            if (w == null) return;
            w.Waited += seconds;
            var a = w.Who.Position; var b = w.Npc.Position;
            bool near = Math.Abs(a.X - b.X) + Math.Abs(a.Y - b.Y) <= 2.5f;
            if (near && !w.Who.IsWalking) { Walking = null; Start(w); }
            else if (w.Waited > 30 || w.Who.IsDead || w.Npc.IsDead || w.Who.Area == null || (!w.Who.IsWalking && !Character.Walks.ContainsKey(w.Who) && w.Waited > 2)) { Walking = null; End(w); }
        }
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic | BindingFlags.DeclaredOnly;
        static readonly MethodInfo Answer = typeof(d49).GetMethod("a", Any, null, new[] { typeof(int), typeof(ecs) }, null);
        static readonly MethodInfo Show = typeof(zq).BaseType.GetMethod("b", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(bool) }, null);
        static readonly FieldInfo Model = typeof(zq).GetField("m_a", Any) ?? typeof(zq).GetFields(Any).FirstOrDefault(f => f.FieldType == typeof(azm));

        static dka Game { get { return bpo.GetService<ps>() as dka; } }
        static void SwapIn(Run r) { var s = bpo.GetService<cnk>().aqu(); r.Saved = s.v(); s.a(r.Sheet); }
        static void SwapOut(Run r) { if (r.Saved != null) { bpo.GetService<cnk>().aqu().a(r.Saved); r.Saved = null; } }

        static void Opening(zq __instance, e89 __0)
        {
            var p = Pending;
            if (p == null || __0 == null || __0.h() != p.Speaker) return;
            Pending = null; Active = p; p.Controller = __instance;
            SwapIn(p);
        }

        static void Opened(zq __instance, bool __result)
        {
            var a = Active;
            if (a == null || a.Controller != __instance || a.Saved == null) return;
            SwapOut(a);
            if (!__result) { Active = null; End(a); return; }     // a one-line bark: over already
            try
            {
                if (Show != null) Show.Invoke(__instance, new object[] { false });     // the controller's own switch (b(bool)): its redraws respect it
                var window = __instance.h();
                if (window != null) window.Visible = false;
                var game = Game;
                var model = Model == null ? null : Model.GetValue(__instance) as azm;
                a.Paused = model != null && model.g();
                if (game != null && !a.KeepPaused) { game.amm(); game.amg(); if (a.Paused) game.ami(); }
            }
            catch (Exception ex) { Log.Write("talk: " + ex.Message); }
            Report(a);
        }

        internal static bool Choose(int index)
        {
            var a = Active;
            if (a == null || a.Controller == null || Answer == null) return false;
            var node = a.Controller.nw() == null ? null : a.Controller.nw().at7();
            if (node == null) return false;
            var choices = node.d();
            if (index >= 0 && (choices == null || index >= choices.Count || choices[index].Answer.d())) return false;     // a disabled answer
            SwapIn(a);
            // As the window's answer (d49.a(int, ecs)) without its log line: the runtime answers, the controller moves on.
            try { var runtime = a.Controller.nw(); if (index >= 0) runtime.aud(index); else runtime.aue(); a.Controller.nz(); var w = a.Controller.h(); if (w != null) w.Visible = false; }
            catch (Exception ex) { Log.Write("talk: answer: " + (ex.InnerException ?? ex).Message); }
            finally { SwapOut(a); }
            if (Active == a) Report(a);
            return true;
        }

        static void Report(Run a)
        {
            var line = LineOf(a.Controller);
            if (line == null) return;
            try { if (a.OnLine != null) a.OnLine(line); } catch (Exception ex) { Log.Write("talk: a line handler failed: " + ex.Message); }
        }

        static readonly FieldInfo Initiator = typeof(zq).GetFields(Any).FirstOrDefault(f => f.Name == "g" && f.FieldType == typeof(b0b));
        internal static TalkLine LineOf(zq controller)
        {
            var node = controller.nw() == null ? null : controller.nw().at7();
            if (node == null) return null;
            var speaker = controller.nt();
            var talker = Initiator == null ? null : Initiator.GetValue(controller) as c5x;
            var line = new TalkLine { Talker = talker == null ? null : Character.Of(talker), Speaker = speaker == null ? "" : speaker.ActorName, Text = controller.nx() ?? node.b(), Choices = new List<TalkChoice>() };
            var choices = node.d();
            if (choices != null) for (int i = 0; i < choices.Count; i++) line.Choices.Add(new TalkChoice { Index = i, Text = choices[i].Text, Enabled = !choices[i].Answer.d() });
            return line;
        }

        // zq.nz: a conversation's line shown (any conversation: the player's, or one held through TalkAs).
        static void Shown(zq __instance)
        {
            if (!GameEvents.ConversationLine.Any) return;
            try { var line = LineOf(__instance); if (line != null) GameEvents.ConversationLine.Run(h => h(line)); }
            catch (Exception ex) { Log.Write("talk: conversation line: " + ex.Message); }
        }

        // zq.n2 lowers the counters n1 raised: ours were lowered already, so they go back up first.
        static void Closing(zq __instance)
        {
            var a = Active;
            if (a == null || a.Controller != __instance) return;
            var game = Game;
            if (game != null && !a.KeepPaused) { game.aml(); game.amf(); if (a.Paused) game.amh(); }
        }

        static void Closed(zq __instance)
        {
            var a = Active;
            if (a == null || a.Controller != __instance) return;
            Active = null;
            End(a);
        }

        static void End(Run a) { try { if (a.OnEnd != null) a.OnEnd(); } catch (Exception ex) { Log.Write("talk: an end handler failed: " + ex.Message); } }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(TalkHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
            var open = typeof(zq).GetMethod("n1", Any, null, new[] { typeof(e89) }, null);
            var close = typeof(zq).GetMethod("n2", Any, null, new[] { typeof(el5) }, null);
            harmony.Patch(open, prefix: m("Opening"), postfix: m("Opened"));
            harmony.Patch(close, prefix: m("Closing"), postfix: m("Closed"));
            harmony.Patch(typeof(zq).GetMethod("nz", Any, null, Type.EmptyTypes, null), postfix: m("Shown"));
            Log.Write("talk: hooks in place (answer " + (Answer != null) + ", model " + (Model != null) + ")");
        }
    }
}
