// UnderRailWorks: the game's text feeds (the log at the bottom left: Combat and Chat), line by line.
//   GameEvents.FeedLineAdded.Add(this, line => { if (line.In("Combat")) send(line.Encode()); });
//   Notify.AddFeedLine(FeedLine.Decode(text));     // the same line in this game's feeds, as the game shows its own
// The game's side: every line reaches the feeds through a96.a(b2h) (the feed service, aha): the feeds it goes to
// (b2h.b(), "Combat" / "Chat"), its rich text (se(): rows of coloured runs) and whether it starts a new block (a()).
// Lines about things the player can't see were already left out (a96.cr).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Text;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    [Preview("multiplayer: built for Co-op so far")] public struct FeedRun
    {
        public string Text; public Color Color;
        public FeedRun(string text, Color color) { Text = text; Color = color; }
    }

    // One line of a feed: its rows (as the game wrapped them before its own wrapping), each a list of coloured runs.
    [Preview("multiplayer: built for Co-op so far")] public sealed class FeedLine
    {
        public string[] Feeds = new string[0];
        public bool NewBlock;
        public List<List<FeedRun>> Rows = new List<List<FeedRun>>();
        public bool In(string feed) { return Feeds.Any(f => string.Equals(f, feed, StringComparison.OrdinalIgnoreCase)); }
        public string Text { get { return string.Join(" ", Rows.Select(r => string.Concat(r.Select(x => x.Text).ToArray())).ToArray()); } }

        // One line of text without tabs or line breaks (for a network message), and back.
        public string Encode()
        {
            var sb = new StringBuilder();
            sb.Append(string.Join(",", Feeds)).Append('\u001c').Append(NewBlock ? '1' : '0');
            foreach (var row in Rows)
            {
                sb.Append('\u001d');
                bool first = true;
                foreach (var run in row)
                {
                    if (!first) sb.Append('\u001e');
                    first = false;
                    sb.Append(run.Color.PackedValue.ToString("x8")).Append(Clean(run.Text));
                }
            }
            return sb.ToString();
        }
        static string Clean(string s) { return (s ?? "").Replace('\t', ' ').Replace('\r', ' ').Replace('\n', ' ').Replace('\u001c', ' ').Replace('\u001d', ' ').Replace('\u001e', ' '); }
        public static FeedLine Decode(string text)
        {
            if (string.IsNullOrEmpty(text)) return null;
            var parts = text.Split('\u001d');
            var head = parts[0].Split('\u001c');
            var line = new FeedLine { Feeds = head[0].Split(new[] { ',' }, StringSplitOptions.RemoveEmptyEntries), NewBlock = head.Length > 1 && head[1] == "1" };
            for (int i = 1; i < parts.Length; i++)
            {
                var row = new List<FeedRun>();
                foreach (var r in parts[i].Split('\u001e'))
                {
                    if (r.Length < 8) continue;
                    uint packed; if (!uint.TryParse(r.Substring(0, 8), System.Globalization.NumberStyles.HexNumber, null, out packed)) continue;
                    var c = new Color(); c.PackedValue = packed;
                    row.Add(new FeedRun(r.Substring(8), c));
                }
                line.Rows.Add(row);
            }
            return line;
        }
    }

    public static partial class GameEvents
    {
        // A line went into the game's feeds (Combat, Chat). Not raised for lines added with Notify.AddFeedLine.
        [Preview("multiplayer: built for Co-op so far")] public static readonly Hook<Action<FeedLine>> FeedLineAdded = new Hook<Action<FeedLine>>("FeedLineAdded");
    }

    public static partial class Notify
    {
        // A line into this game's feeds, shown as the game shows its own (its font, wrapping, the feed tabs).
        [Preview("multiplayer: built for Co-op so far")] public static bool AddFeedLine(FeedLine line)
        {
            if (line == null) return false;
            var feeds = bpo.GetService<aha>() as a96;
            if (feeds == null || FeedHooks.Add == null) return false;
            var rows = new List<dl6>();
            foreach (var r in line.Rows)
                rows.Add(new dl6(r.Select(x => (bph)FeedHooks.RunCtor.Invoke(new object[] { FeedHooks.PenCtor.Invoke(new object[] { feeds.v8(), x.Color }), x.Text, null }))));
            FeedHooks.Adding = true;
            try { FeedHooks.Add.Invoke(feeds, new object[] { new FeedHooks.Given(line.Feeds.ToList(), line.NewBlock, new ecs(rows)) }); }
            finally { FeedHooks.Adding = false; }
            return true;
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class FeedHooks
    {
        const BindingFlags Any = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
        internal static readonly MethodInfo Add = typeof(a96).GetMethod("a", Any, null, new[] { typeof(b2h) }, null);
        internal static readonly ConstructorInfo PenCtor = typeof(d22).GetConstructors().First(c => c.GetParameters().Length == 2);
        internal static readonly ConstructorInfo RunCtor = typeof(bph).GetConstructors().First(c => c.GetParameters().Length == 3 && c.GetParameters()[0].ParameterType == typeof(d22));     // (pen, text, link = null)
        static readonly FieldInfo Rows = typeof(ecs).GetField("a", Any), Runs = typeof(dl6).GetField("a", Any), Pen = typeof(bph).GetField("a", Any), RunText = typeof(bph).GetField("b", Any), PenColor = typeof(d22).GetField("b", Any);
        [ThreadStatic] internal static bool Adding;

        // A line made by Notify.AddFeedLine.
        internal sealed class Given : b2h
        {
            readonly List<string> feeds; readonly bool block; readonly ecs text;
            internal Given(List<string> f, bool b, ecs t) { feeds = f; block = b; text = t; }
            public bool a() { return block; }
            public List<string> b() { return feeds; }
            public ecs se() { return text; }
        }

        static void Adding_(b2h __0)
        {
            if (Adding || !GameEvents.FeedLineAdded.Any || __0 == null) return;
            try
            {
                var feeds = __0.b();
                if (feeds == null || feeds.Count == 0) return;
                var rich = __0.se();     // a new one each call: the game's own call restyles its copy
                var rows = rich == null ? null : Rows.GetValue(rich) as List<dl6>;
                if (rows == null || rows.Count == 0) return;
                var line = new FeedLine { Feeds = feeds.Where(f => f != null).ToArray(), NewBlock = __0.a() };
                foreach (var row in rows)
                {
                    var runs = Runs.GetValue(row) as List<bph>;
                    var r = new List<FeedRun>();
                    if (runs != null) foreach (var run in runs) { var pen = Pen.GetValue(run); r.Add(new FeedRun((string)RunText.GetValue(run), pen == null ? Color.White : (Color)PenColor.GetValue(pen))); }
                    line.Rows.Add(r);
                }
                GameEvents.FeedLineAdded.Run(h => h(line));
            }
            catch (Exception ex) { Log.Write("feeds: " + ex.Message); }
        }

        internal static void Install(HarmonyLib.Harmony harmony)
        {
            if (Add == null) { Log.Write("feeds: the feed service's add wasn't found: no feed lines"); return; }
            harmony.Patch(Add, prefix: new HarmonyLib.HarmonyMethod(typeof(FeedHooks).GetMethod("Adding_", BindingFlags.Static | BindingFlags.NonPublic)));
        }
    }
}
