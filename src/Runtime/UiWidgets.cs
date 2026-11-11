// UnderRailWorks: more UI pieces built on Works' own drawing (Ui.Custom): pictures, tooltips on any element, scrolling
// lists, sliders and choice buttons, in the game's orange-on-black look. They live in any panel or window like the
// game's controls.
//   Ui.Add(win, Ui.Image(Assets.Texture(this, "art/map.png"), 200, 120), 10, win.Top);
//   Ui.Tooltip(button, "Saves what you wear.");
//   var list = Ui.List(300, 200, 20); list.Items = names; list.Selected += i => ...;
//   var s = Ui.Slider(200, 0, 100, 50); s.Changed += v => ...;
//   var c = Ui.Choice(160, new[] { "Easy", "Normal", "Hard" }, 1); c.Changed += i => ...;
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    // Rows of text with buttons at their right end, in an area of a panel or window (a list of sets, merchants...):
    // Clear and Add again whenever the content changes; the panel itself stays (rebuilding a window mid-click loses
    // the click).
    //   rows = UiRows.In(win, 10, win.Top + 4, 440, 28);
    //   rows.Clear(); foreach (var m in merchants) rows.Add(m.Name, null, UiRows.Button("Trade", () => Trade.Open(m)));
    public sealed class UiRows
    {
        readonly UiElement parent; readonly int x, y, width, height;
        readonly List<UiElement> made = new List<UiElement>();
        public int Count { get; private set; }
        UiRows(UiElement parent, int x, int y, int width, int height) { this.parent = parent; this.x = x; this.y = y; this.width = width; this.height = height; }
        public static UiRows In(UiElement parent, int x, int y, int width, int rowHeight) { return new UiRows(parent, x, y, width, rowHeight); }

        public sealed class RowButton { internal string Text; internal Action Click; }
        public static RowButton Button(string text, Action click) { return new RowButton { Text = text, Click = click }; }

        public void Clear() { foreach (var e in made) Ui.Remove(e); made.Clear(); Count = 0; }

        // A row: its text (cut to what's left of the buttons) in colour (null: the body colour), buttons right-aligned.
        public void Add(string text, Color? colour = null, params RowButton[] buttons)
        {
            int top = y + Count * height, right = x + width;
            foreach (var b in (buttons ?? new RowButton[0]).Reverse())
            {
                if (b == null) continue;
                var button = Ui.Button(b.Text, b.Click);
                right -= button.Width;
                Ui.Add(parent, button, right, top);
                made.Add(button);
                right -= 8;
            }
            var label = Ui.Label(text ?? "", Math.Max(20, right - x));
            Ui.SetColor(label, colour ?? Look.Text);
            Ui.Add(parent, label, x, top + 4);
            made.Add(label);
            Count++;
        }
    }

    static class Look
    {
        internal static readonly Color Orange = new Color(181, 92, 0), Light = new Color(230, 160, 60), Text = new Color(215, 215, 205), Dark = new Color(12, 12, 12, 225), Hot = new Color(90, 50, 10);
    }

    public sealed class UiList
    {
        internal UiElement Element;
        public UiElement View { get { return Element; } }
        public IList<string> Items { get; set; } = new List<string>();
        // Optional: each row's text colour (same order as Items; missing ones in the usual colour).
        public IList<Color> ItemColors { get; set; }
        public int Selection { get; set; } = -1;
        public int First { get; set; }
        internal int RowHeight;
        public event Action<int> Selected;
        internal void Select(int i) { Selection = i; Widgets.Raise(Selected, i); }
    }

    public sealed class UiSlider
    {
        internal UiElement Element;
        public UiElement View { get { return Element; } }
        public double Min { get; internal set; }
        public double Max { get; internal set; }
        public double Step { get; set; } = 1;
        public double Value { get; set; }
        public event Action<double> Changed;
        internal bool Dragging;
        internal void Set(double v)
        {
            v = Math.Max(Min, Math.Min(Max, Step > 0 ? Math.Round((v - Min) / Step) * Step + Min : v));
            if (v == Value) return;
            Value = v; Widgets.Raise(Changed, v);
        }
    }

    public sealed class UiChoice
    {
        internal UiElement Element;
        public UiElement View { get { return Element; } }
        public IList<string> Options { get; internal set; }
        public int Index { get; set; }
        public event Action<int> Changed;
        internal void Next(int step) { if (Options.Count == 0) return; Index = ((Index + step) % Options.Count + Options.Count) % Options.Count; Widgets.Raise(Changed, Index); }
    }

    public static partial class Ui
    {
        // A picture scaled into width x height (null texture: nothing drawn).
        public static UiElement Image(Texture2D texture, int width, int height)
        {
            return Custom(width, height, c => { if (texture != null && !texture.IsDisposed) c.DrawTexture(texture, c.Bounds, Color.White); });
        }

        // Text shown next to the mouse while it rests on element (half a second).
        public static void Tooltip(UiElement element, string text) { Widgets.Tooltips[element] = text; }

        // A scrolling list of text rows: the mouse wheel scrolls, a click selects.
        public static UiList List(int width, int height, int rowHeight = 20)
        {
            var l = new UiList { RowHeight = rowHeight };
            l.Element = Custom(width, height, c =>
            {
                var b = c.Bounds;
                c.Fill(b, Look.Dark); c.Frame(b, Look.Orange);
                int rows = Math.Max(1, (b.Height - 4) / l.RowHeight);
                l.First = Math.Max(0, Math.Min(l.First, Math.Max(0, l.Items.Count - rows)));
                var m = Input.Mouse;
                for (int i = 0; i < rows && l.First + i < l.Items.Count; i++)
                {
                    var r = new Rectangle(b.X + 2, b.Y + 2 + i * l.RowHeight, b.Width - 10, l.RowHeight);
                    int index = l.First + i;
                    if (index == l.Selection) c.Fill(r, Look.Orange * 0.6f);
                    else if (Ui.IsHot(l.Element) && r.Contains(m)) c.Fill(r, Look.Hot);
                    c.Text(l.Items[index], r.X + 4, r.Y + Math.Max(0, (l.RowHeight - 16) / 2), l.ItemColors != null && index < l.ItemColors.Count ? l.ItemColors[index] : Look.Text);
                }
                if (l.Items.Count > rows)
                {
                    int track = b.Height - 4, thumb = Math.Max(10, track * rows / l.Items.Count);
                    int y = b.Y + 2 + (track - thumb) * l.First / Math.Max(1, l.Items.Count - rows);
                    c.Fill(new Rectangle(b.Right - 6, y, 4, thumb), Look.Light);
                }
            });
            Widgets.Lists.Add(new WeakReference(l));
            return l;
        }

        // A slider from min to max (drag or click; the value is drawn on it).
        public static UiSlider Slider(int width, double min, double max, double value)
        {
            var s = new UiSlider { Min = min, Max = max, Value = value };
            s.Element = Custom(width, 22, c =>
            {
                var b = c.Bounds;
                c.Fill(new Rectangle(b.X, b.Center.Y - 2, b.Width, 4), Look.Hot);
                double f = s.Max > s.Min ? (s.Value - s.Min) / (s.Max - s.Min) : 0;
                int x = b.X + (int)Math.Round(f * (b.Width - 10));
                c.Fill(new Rectangle(b.X, b.Center.Y - 2, x - b.X + 5, 4), Look.Orange);
                c.Fill(new Rectangle(x, b.Y + 2, 10, b.Height - 4), Ui.IsHot(s.Element) || s.Dragging ? Look.Light : Look.Orange);
                var t = s.Value.ToString(s.Step >= 1 ? "0" : "0.##");
                c.Text(t, b.Right - Ui.TextWidth(t) - 2, b.Y - 16, Look.Text);
            });
            Widgets.Sliders.Add(new WeakReference(s));
            return s;
        }

        // A small button of any size drawn in the game's colours (Ui.Button is the game's own, at its own sizes).
        public static UiElement SmallButton(string text, int width, int height, Action click)
        {
            UiElement e = null;
            e = Custom(width, height, c =>
            {
                var b = c.Bounds; bool hot = Ui.IsHot(e);
                c.Fill(b, hot ? Look.Hot : Look.Dark); c.Frame(b, hot ? Look.Light : Look.Orange);
                c.Text(text, b.X + (b.Width - Ui.TextWidth(text)) / 2, b.Y + (b.Height - 16) / 2, Look.Light);
            });
            Widgets.Buttons.Add(new KeyValuePair<WeakReference, Action>(new WeakReference(e), click));
            return e;
        }

        // A button that steps through options on click (right-click: back).
        public static UiChoice Choice(int width, IList<string> options, int index = 0)
        {
            var ch = new UiChoice { Options = options ?? new List<string>(), Index = index };
            ch.Element = Custom(width, 24, c =>
            {
                var b = c.Bounds; bool hot = Ui.IsHot(ch.Element);
                c.Fill(b, hot ? Look.Hot : Look.Dark); c.Frame(b, Look.Orange);
                var t = ch.Options.Count == 0 ? "" : "< " + ch.Options[Math.Max(0, Math.Min(ch.Index, ch.Options.Count - 1))] + " >";
                c.Text(t, b.X + (b.Width - Ui.TextWidth(t)) / 2, b.Y + 4, Look.Light);
            });
            Widgets.Choices.Add(new WeakReference(ch));
            return ch;
        }
    }
}

namespace UnderRailWorks.Internal
{
    static class Widgets
    {
        internal static readonly Dictionary<UiElement, string> Tooltips = new Dictionary<UiElement, string>();
        internal static readonly List<WeakReference> Lists = new List<WeakReference>(), Sliders = new List<WeakReference>(), Choices = new List<WeakReference>();
        internal static readonly List<KeyValuePair<WeakReference, Action>> Buttons = new List<KeyValuePair<WeakReference, Action>>();
        static UiElement tipFor, tipPanel;
        static double hovered;

        static bool Live(UiElement e) { return e != null && Ui.IsShown(e); }

        // A mod's handler failing doesn't stop the other widgets (or repeat every frame unnoticed).
        internal static void Raise<T>(Action<T> handler, T value)
        {
            if (handler == null) return;
            foreach (Action<T> h in handler.GetInvocationList())
                try { h(value); } catch (Exception ex) { var mod = ModErrors.ModOf(h.Method.DeclaringType); if (mod != null) ModErrors.Report(mod, "a widget's handler", ex); else Log.Write("ui: widget handler: " + ex.Message); }
        }

        internal static void Tick(double seconds)
        {
            var m = Input.Mouse;
            foreach (var w in Lists.ToList()) { var l = w.Target as UiList; if (l == null) { Lists.Remove(w); continue; } if (!Ui.IsHot(l.Element)) continue; var b = l.Element.Bounds;
                if (Input.Wheel != 0) l.First = Math.Max(0, l.First - Input.Wheel * 3);
                if (Input.MousePressed(MouseButton.Left)) { int i = l.First + (m.Y - b.Y - 2) / l.RowHeight; if (i >= 0 && i < l.Items.Count) l.Select(i); } }
            foreach (var w in Sliders.ToList()) { var s = w.Target as UiSlider; if (s == null) { Sliders.Remove(w); continue; } if (!Live(s.Element)) { s.Dragging = false; continue; } var b = s.Element.Bounds;
                if (Input.MousePressed(MouseButton.Left) && Ui.IsHot(s.Element)) s.Dragging = true;
                if (s.Dragging) { s.Set(s.Min + (s.Max - s.Min) * Math.Max(0, Math.Min(1, (m.X - b.X - 5) / (double)Math.Max(1, b.Width - 10)))); if (!Input.MouseDown(MouseButton.Left)) s.Dragging = false; } }
            foreach (var w in Choices.ToList()) { var c = w.Target as UiChoice; if (c == null) { Choices.Remove(w); continue; } if (!Ui.IsHot(c.Element)) continue;
                if (Input.MousePressed(MouseButton.Left)) c.Next(1); else if (Input.MousePressed(MouseButton.Right)) c.Next(-1); }
            if (Input.MousePressed(MouseButton.Left))
                foreach (var kv in Buttons.ToList())
                {
                    var b = kv.Key.Target as UiElement;
                    if (b == null) { Buttons.Remove(kv); continue; }
                    if (Ui.IsHot(b)) { try { kv.Value(); } catch (Exception ex) { var mod = ModErrors.ModOf(kv.Value.Method.DeclaringType); if (mod != null) ModErrors.Report(mod, "a button", ex); else Log.Write("ui: button: " + ex.Message); } }
                }
            Tooltip(seconds, m);
        }

        static void Tooltip(double seconds, Point m)
        {
            UiElement over = null;
            // Elements no longer under a live screen are dropped (closed windows, old HUDs): nothing keeps them alive.
            foreach (var kv in Tooltips.ToList()) { if (!Attached(kv.Key)) { Tooltips.Remove(kv.Key); continue; } if (Ui.IsHot(kv.Key)) over = kv.Key; }
            if (over != tipFor) { tipFor = over; hovered = 0; Hide(); }
            if (over == null) return;
            hovered += seconds;
            if (hovered < 0.5 || tipPanel != null) return;
            var root = UnderRailWorks.Hud.Root ?? (UiHooks.CurrentOptions == null ? null : UiElement.Of(UiHooks.CurrentOptions));
            if (root == null) return;
            var lines = Ui.Wrap(Tooltips[over], 320);
            int w = Math.Min(340, lines.Max(l => Ui.TextWidth(l)) + 16), h = 8 + lines.Count * 18;
            tipPanel = Ui.Custom(w, h, c => { c.Fill(c.Bounds, new Color(8, 8, 8, 235)); c.Frame(c.Bounds, Look.Orange); for (int i = 0; i < lines.Count; i++) c.Text(lines[i], c.Bounds.X + 8, c.Bounds.Y + 4 + i * 18, Look.Text); });
            int x = Math.Min(root.Width - w - 4, m.X + 16), y = Math.Min(root.Height - h - 4, m.Y + 20);
            Ui.Add(root, tipPanel, Math.Max(0, x), Math.Max(0, y));
        }

        static bool Attached(UiElement e)
        {
            for (euz c = e.Control; c != null; c = c.Parent) { if (c == (euz)Hud.Current || c == (euz)UiHooks.CurrentOptions) return true; if (c.Parent == null) return Ui.IsScreen(c); }
            return false;
        }

        static void Hide() { if (tipPanel != null) Ui.Remove(tipPanel); tipPanel = null; }
    }
}
