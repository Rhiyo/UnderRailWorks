// A "Fixes" tab in the game's Options, with a group (heading, rows) for each fix that is switched on. It is built at the end
// of t1.h (where t1 builds its tabs), with t1's own row builder and dropdown factory through reflection, so it looks
// like the game's tabs. Accept (end of t1.d) writes the values to UnderrailFix.ini, and they apply at once: zoom
// limits are read every frame; scale, insets and the default zoom need the interface laid out again, which is done
// the way the game does it when Interface Size changes (bl9.b + ps.anc), after the game has applied its own options.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;

// A dropdown entry: what it shows and what it writes to the ini.
public sealed class FixChoice
{
    public readonly string Text, Value;
    public FixChoice(string text, string value) { Text = text; Value = value; }
    public override string ToString() { return Text; }
    public override bool Equals(object o) { return o is FixChoice c && c.Value == Value; }
    public override int GetHashCode() { return Value.GetHashCode(); }
}

enum StyleOnly { A }

public static class UnderrailOptions
{
    const string TabName = "Mods";
    internal const string TabTitle = TabName;
    sealed class Rows
    {
        public oz InsetX, InsetY, ZoomDefault, ZoomMin, ZoomMax;
        public int InsetXWas, InsetYWas;     // what the inset sliders started at (a pixel inset shows as the nearest %)
        public cao Screenshots, Background;
        public aw9 Scale, MaxFps;
    }
    static readonly ConditionalWeakTable<t1, Rows> Added = new ConditionalWeakTable<t1, Rows>();
    static bool relayout;
    static Dictionary<string, string> confirm;     // interface settings to put back unless the player keeps them
    const int ConfirmSeconds = 15;

    const BindingFlags Private = BindingFlags.Instance | BindingFlags.NonPublic;
    static readonly MethodInfo Combo = typeof(t1).GetMethod("CreateComboBoxFromEnum", Private).MakeGenericMethod(typeof(StyleOnly));
    static readonly MethodInfo Row = typeof(t1).GetMethods(Private).Single(m => m.Name == "a" && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(
        new[] { typeof(string), typeof(euz), typeof(euz), typeof(bool), typeof(bool), typeof(int), typeof(bool) }));
    static readonly FieldInfo Page = typeof(t1).GetField("s", Private), Tabs = typeof(t1).GetField("u", Private);
    // t1's group heading (a full-width text line) and separator (a line plus spacing), as its own tabs use them.
    static readonly MethodInfo Heading = typeof(t1).GetMethod("a", Private, null, new[] { typeof(string) }, null),
        Separator = typeof(t1).GetMethod("a", Private, null, new[] { typeof(bool) }, null);
    // t1's slider row a(key, label, value 0-1, min, max, small step, large step, percent, changed, text, wide); the slider
    // goes into t1.a9[key], where Accept reads it back.
    static readonly MethodInfo Slider = typeof(t1).GetMethod("a", Private, null, new[] { typeof(string), typeof(string), typeof(float), typeof(int), typeof(int),
        typeof(int), typeof(int), typeof(bool), typeof(EventHandler<c1>), typeof(Func<int, string>), typeof(bool) }, null);
    static readonly FieldInfo Sliders = typeof(t1).GetField("a9", Private);
    static readonly MethodInfo EndTab = typeof(t1).GetMethod("f", Private, null, Type.EmptyTypes, null), StartTab = typeof(t1).GetMethod("g", Private, null, Type.EmptyTypes, null);

    static string Pct(double v) { return v.ToString("0.##", CultureInfo.InvariantCulture) + "%"; }
    static FixChoice Percent(double v) { return v == 0 ? new FixChoice("0%", "0") : new FixChoice(Pct(v), Pct(v)); }

    // The ini's current values as choices (null if not valid).
    static FixChoice CurrentScale()
    {
        int? p = FixSettings.UiScale;
        return p.HasValue ? new FixChoice(p.Value + "%", p.Value.ToString()) : new FixChoice("Auto", "auto");
    }

    static FixChoice CurrentInset(string text)
    {
        text = (text ?? "").Trim().ToLowerInvariant();
        double v; int px;
        var inv = CultureInfo.InvariantCulture;
        if (text.Length == 0 || text == "0" || text == "0px" || text == "0%") return new FixChoice("0%", "0");
        if (text.EndsWith("%") && double.TryParse(text.TrimEnd('%').Trim(), NumberStyles.Float, inv, out v)) return Percent(v);
        if (text.EndsWith("px") && int.TryParse(text.Substring(0, text.Length - 2).Trim(), out px)) return new FixChoice(px + " px", px + "px");
        if ((text.Contains(".") || text.Contains(",")) && double.TryParse(text.Replace(',', '.'), NumberStyles.Float, inv, out v)) return Percent(v * 100);
        if (int.TryParse(text, out px)) return new FixChoice(px + " px", px + "px");
        return null;
    }

    static FixChoice CurrentFps() { int f = FixSettings.MaxFps; return f > 0 ? new FixChoice(f + " FPS", f.ToString()) : new FixChoice("Unlimited", "0"); }

    static FixChoice Zoom(int p) { return new FixChoice(p + "%", p.ToString()); }

    internal static aw9 Add(t1 options, string label, IEnumerable<FixChoice> standard, FixChoice current, FixChoice fallback)
    {
        var items = standard.ToList();
        if (current != null && !items.Contains(current)) items.Add(current);     // a valid ini value the list doesn't have
        var combo = (aw9)Combo.Invoke(options, new object[] { StyleOnly.A, null, null });     // styled like the game's dropdowns
        combo.a(items, null);
        combo.b(items.FirstOrDefault(i => i.Equals(current)) ?? fallback);
        Row.Invoke(options, new object[] { label, combo, null, false, true, 0, false });
        return combo;
    }

    // End of t1.h: one tab after the game's own (the tab row has no room for more): a group per fix that is switched
    // on, then one per mod (ModOptions). Nothing to show: no tab.
    public static void AddTab(t1 options)
    {
        bool fixes = FixSettings.ResolutionFix || FixSettings.UiScaleFix || FixSettings.ZoomFix || FixSettings.FrameRateFix;
        if (!fixes && ModLoader.Mods.Count == 0) return;
        {
            var rows = new Rows();
            BuildTab(options, TabName, page =>
            {
                // View 0: the list of mods (ModOptions); view 1: Works' own fixes; then one per mod with settings.
                var views = new List<int>();
                ModOptions.ListView(options, page, views, fixes);
                if (!fixes) { ModOptions.ModViews(options, page, views); return views; }
                views.Add(RowY(options));
                ModOptions.BackRow(options, page, "Engine fixes");
                bool first = true;
                Action<string> group = title =>
                {
                    if (!first) SeparatorRow(options);
                    HeadingRow(options, title);
                    first = false;
                };
                if (FixSettings.ResolutionFix)
                try
                {
                    group("Resolution");
                    rows.Screenshots = TickBox(options, "1080p save screenshots", FixSettings.SaveScreenshots);
                }
                catch (Exception ex) { Failed("Resolution", ex); }
                if (FixSettings.UiScaleFix)
                try
                {
                    group("Interface");
                    rows.Scale = Add(options, "Interface scale", new[] { new FixChoice("Auto", "auto") }.Concat(new[] { 100, 125, 150, 175, 200, 250, 300 }.Select(p => new FixChoice(p + "%", p.ToString()))),
                        CurrentScale(), new FixChoice("Auto", "auto"));
                    rows.InsetXWas = InsetPercent(FixSettings.UiInsetX, true);
                    rows.InsetYWas = InsetPercent(FixSettings.UiInsetY, false);
                    rows.InsetX = SliderRow(options, page, "underrailfix.insetX", "Interface inset, sides", rows.InsetXWas, 0, 50, 5);
                    rows.InsetY = SliderRow(options, page, "underrailfix.insetY", "Interface inset, top and bottom", rows.InsetYWas, 0, 50, 5);
                    rows.Background = TickBox(options, "Show whole menu backgrounds", FixSettings.BackgroundFit);
                }
                catch (Exception ex) { Failed("Interface", ex); }
                if (FixSettings.ZoomFix)
                try
                {
                    group("Zoom");
                    rows.ZoomDefault = SliderRow(options, page, "underrailfix.zoomDefault", "Default zoom", FixSettings.ZoomDefault, 25, 400, 25);
                    rows.ZoomMin = SliderRow(options, page, "underrailfix.zoomMin", "Zoom out limit", FixSettings.ZoomMin, 25, 400, 25);
                    rows.ZoomMax = SliderRow(options, page, "underrailfix.zoomMax", "Zoom in limit", FixSettings.ZoomMax, 25, 400, 25);
                }
                catch (Exception ex) { Failed("Zoom", ex); }
                if (FixSettings.FrameRateFix)
                try
                {
                    group("Frame rate");
                    var caps = new[] { new FixChoice("Unlimited", "0") }.Concat(new[] { 30, 60, 120, 144, 165, 240 }.Select(f => new FixChoice(f + " FPS", f.ToString()))).ToList();
                    rows.MaxFps = Add(options, "Frame rate cap", caps, CurrentFps(), caps[0]);
                }
                catch (Exception ex) { Failed("Frame rate", ex); }
                ModOptions.ModViews(options, page, views);
                return views;
            });
            Added.Remove(options);
            Added.Add(options, rows);
        }
    }

    // A tab after the game's own: rows are added by build with t1's own row builders, then the tab scrolls. build
    // returns where each view starts (t1's row y), or null for one view; TabScroll shows one view at a time.
    internal static void BuildTab(t1 options, string name, Func<cvh, List<int>> build)
    {
        try
        {
            var page = new cvh();
            page.Text = name;
            ((dpg)Tabs.GetValue(options)).n().Add(page);
            Page.SetValue(options, page);
            // t1's row builder starts a new column when a row would pass the page's bottom; ours is one column and
            // scrolls instead (TabScroll), so the page is made tall while the rows are added.
            var pageSize = page.Size;
            page.Size = new esj(UnderrailDropdownScroll.W(pageSize), 100000);
            StartTab.Invoke(options, null);
            List<int> views = null;
            try { views = build(page); }
            catch (Exception ex) { Failed(name + " tab", ex); }
            // Always: the page back to its size, and the scrolling.
            try { EndTab.Invoke(options, null); StartTab.Invoke(options, null); } catch (Exception ex) { Failed("tab end", ex); }
            page.Size = pageSize;
            TabScroll.Attach(options, page, views);
        }
        catch (Exception ex) { Log.Write("options: the " + name + " tab failed: " + ex); }
    }

    internal static void HeadingRow(t1 options, string title) { Heading.Invoke(options, new object[] { title }); }
    // t1's row position (r: the next row's y), its left column (e) and its text label factory (a(int width)).
    static readonly FieldInfo RowField = typeof(t1).GetField("r", Private), LeftField = typeof(t1).GetField("e", Private);
    static readonly MethodInfo NewLabel = typeof(t1).GetMethod("a", Private, null, new[] { typeof(int) }, null),
        Place = typeof(t1).GetMethod("b", Private, null, new[] { typeof(euz) }, null);
    internal static int RowY(t1 options) { return (int)RowField.GetValue(options); }
    internal static void SetRowY(t1 options, int y) { RowField.SetValue(options, y); }
    internal static int Left(t1 options) { return (int)LeftField.GetValue(options); }
    internal static avo Label(t1 options, int width) { return (avo)NewLabel.Invoke(options, new object[] { width }); }
    // Any control as a row of its own at the left column (t1.b).
    internal static void PlaceRow(t1 options, euz control) { Place.Invoke(options, new object[] { control }); }
    internal static void SeparatorRow(t1 options) { Separator.Invoke(options, new object[] { false }); }
    internal static void ControlRow(t1 options, string label, euz control) { Row.Invoke(options, new object[] { label, control, null, false, true, 0, false }); }

    // A button styled like the options window's own (Accept, Cancel), as wide as its text needs.
    internal static dak NewButton(t1 options, string text, Action click)
    {
        var button = new dak();
        button.Text = text;
        StyleButton.Invoke(options, new object[] { button, wj.a() });
        var resources = dr3.a();
        var font = resources == null || button.Font == null ? null : resources.GetResource<Microsoft.Xna.Framework.Graphics.SpriteFont>(button.Font);
        if (font != null) button.Size = new esj(Math.Max(UnderrailDropdownScroll.W(button.Size), (int)font.MeasureString(button.Text).X + 40), UnderrailDropdownScroll.H(button.Size));
        e4z.j(button);
        button.c(new EventHandler<b25>((sender, e) => click()));
        return button;
    }

    // The slider row is built with t1's narrow label (the dropdown rows use the wide one), so its slider starts further
    // left: move it and its value text (added right after it) in line with the dropdown above, and widen its label
    // (added right before it) to the dropdown's label width.
    // t1.a(dak, wj): how the options window styles its own buttons (Accept, Cancel).
    static readonly MethodInfo StyleButton = typeof(t1).GetMethod("a", BindingFlags.Static | BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(dak), typeof(wj) }, null);

    // The game's slider row (as its Unobscuration alpha option): a percentage, min-max, moving by step.
    internal static oz SliderRow(t1 options, cvh page, string key, string label, int value, int min, int max, int step)
    {
        Slider.Invoke(options, new object[] { key, label, value / 100f, min, max, step, Math.Max(step, 25), true, null, null, false });
        var slider = ((System.Collections.IDictionary)Sliders.GetValue(options))[key] as oz;
        AlignSlider(page, slider);
        // Dragging snaps to whole steps (a 25-400 range moved by single points is too fiddly).
        if (slider != null) { SliderStart.Remove(slider); SliderStart.Add(slider, slider.l()); }
        if (slider != null) slider.f(new EventHandler<c1>((sender, e) =>
        {
            int v = slider.l(), snapped = Math.Max(min, Math.Min(max, min + (int)Math.Round((v - min) / (double)step) * step));
            if (snapped != v) slider.e(snapped);
        }));
        return slider;
    }

    // What each slider showed when the tab was built: only a moved slider is written (a value the slider can't show,
    // e.g. a 75 % inset, stays as it is otherwise).
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<oz, object> SliderStart = new System.Runtime.CompilerServices.ConditionalWeakTable<oz, object>();
    internal static bool Moved(oz slider) { object start; return slider != null && (!SliderStart.TryGetValue(slider, out start) || (int)start != slider.l()); }

    static bool WriteSlider(oz slider, string key, int current)
    {
        if (!Moved(slider)) return false;
        FixSettings.SetValue(key, slider.l().ToString());
        Log.Write("options: " + key + "=" + slider.l());
        return true;
    }

    // The game's tick box row (t1.b(bool), as its VSync option).
    static readonly MethodInfo NewTickBox = typeof(t1).GetMethod("b", Private, null, new[] { typeof(bool) }, null);
    internal static cao NewTick(t1 options, bool value) { return (cao)NewTickBox.Invoke(options, new object[] { value }); }
    internal static cao TickBox(t1 options, string label, bool value)
    {
        var box = (cao)NewTickBox.Invoke(options, new object[] { value });
        Row.Invoke(options, new object[] { label, box, null, true, true, 0, false });
        return box;
    }

    static bool WriteTick(cao box, string key, bool current)
    {
        if (box == null || box.Checked == current) return false;
        FixSettings.SetValue(key, box.Checked ? "1" : "0");
        Log.Write("options: " + key + "=" + (box.Checked ? "1" : "0"));
        return true;
    }

    // An inset as a whole percentage of the screen (a pixel value as the nearest one).
    static int InsetPercent(string text, bool sides)
    {
        var choice = CurrentInset(text);
        if (choice == null || choice.Value == "0") return 0;
        double v;
        if (choice.Value.EndsWith("%") && double.TryParse(choice.Value.TrimEnd('%'), NumberStyles.Float, CultureInfo.InvariantCulture, out v)) return (int)Math.Round(v);
        int px, screen = sides ? UiScaleFix.ScreenWidth : UiScaleFix.ScreenHeight;
        if (choice.Value.EndsWith("px") && int.TryParse(choice.Value.Substring(0, choice.Value.Length - 2), out px) && screen > 0) return (int)Math.Round(px * 100.0 / screen);
        return 0;
    }

    // Only when the slider was moved: a pixel inset in the ini stays as it is otherwise.
    static bool WriteInset(oz slider, int was, string key)
    {
        if (!Moved(slider)) return false;
        FixSettings.SetValue(key, slider.l() == 0 ? "0" : slider.l() + "%");
        Log.Write("options: " + key + "=" + slider.l() + "%");
        return true;
    }

    static void AlignSlider(cvh page, oz slider)
    {
        if (slider == null) return;
        var controls = page.Controls.Cast<euz>().ToList();
        int i = controls.IndexOf(slider);
        if (i < 1) return;
        var location = typeof(euz).GetProperty("Location");
        Func<euz, object> at = c => location.GetValue(c, null);
        // t1 puts a narrow row's control one label width (m_h) from its label, a wide row's 2.5: move by 1.5 of it.
        int dx = (int)Math.Round(1.5 * (UnderrailDropdownScroll.X(at(slider)) - UnderrailDropdownScroll.X(at(controls[i - 1]))));
        if (dx == 0) return;
        foreach (var c in new[] { slider, i + 1 < controls.Count ? controls[i + 1] : null })
            if (c != null) UnderrailDropdownScroll.Move(c, UnderrailDropdownScroll.X(at(c)) + dx, UnderrailDropdownScroll.Y(at(c)));
        var label = controls[i - 1];
        label.Size = new esj(UnderrailDropdownScroll.W(label.Size) + dx, UnderrailDropdownScroll.H(label.Size));
    }

    // A subsection: its heading and row labels (the texts at the page's label column) moved right; the controls stay
    // in the shared column.
    const int SubsectionIndent = 24;
    internal static void Indent(cvh page, int from)
    {
        var location = typeof(euz).GetProperty("Location");
        var added = page.Controls.Cast<euz>().Skip(from).ToList();
        if (added.Count == 0) return;
        int column = added.OfType<avo>().Select(c => UnderrailDropdownScroll.X(location.GetValue(c, null))).DefaultIfEmpty(0).Min();
        foreach (var c in added.OfType<avo>())
        {
            object at = location.GetValue(c, null);
            if (UnderrailDropdownScroll.X(at) != column) continue;     // value texts beside sliders stay
            UnderrailDropdownScroll.Move(c, column + SubsectionIndent, UnderrailDropdownScroll.Y(at));
            c.Size = new esj(Math.Max(1, UnderrailDropdownScroll.W(c.Size) - SubsectionIndent), UnderrailDropdownScroll.H(c.Size));
        }
    }

    internal static void TabFailed(string what, Exception ex) { Failed(what, ex); }
    static void Failed(string what, Exception ex)
    {
        Log.Write("options: the " + what + " rows failed: " + ex);
    }

    // End of t1.d (Accept saves the options): write what changed.
    public static void Save(t1 options)
    {
        ModOptions.Save(options);
        Rows rows;
        if (!Added.TryGetValue(options, out rows)) return;
        try
        {
            // Interface scale and insets can leave the menus hard to use, so they are confirmed (see Applied).
            var before = new[] { "UiScale", "UiInsetX", "UiInsetY" }.ToDictionary(k => k, k => FixSettings.Raw(k) ?? "");
            bool ui = Write(rows.Scale, "UiScale", CurrentScale());
            ui |= WriteInset(rows.InsetX, rows.InsetXWas, "UiInsetX");
            ui |= WriteInset(rows.InsetY, rows.InsetYWas, "UiInsetY");
            if (ui) { confirm = before; relayout = true; }
            WriteTick(rows.Screenshots, "SaveScreenshots", FixSettings.SaveScreenshots);     // used at the next save
            relayout |= WriteTick(rows.Background, "BackgroundFit", FixSettings.BackgroundFit);     // only rebuilds the backgrounds; no confirmation needed
            relayout |= WriteSlider(rows.ZoomDefault, "ZoomDefault", FixSettings.ZoomDefault);
            WriteSlider(rows.ZoomMin, "ZoomMin", FixSettings.ZoomMin);
            WriteSlider(rows.ZoomMax, "ZoomMax", FixSettings.ZoomMax);
            if (Write(rows.MaxFps, "MaxFps", CurrentFps())) FrameRate.Reapply();     // applies at once
        }
        catch (Exception ex) { Log.Write("saving UnderRailWorks options failed: " + ex); }
    }

    static bool Write(aw9 combo, string key, FixChoice current)
    {
        var chosen = combo == null ? null : combo.l() as FixChoice;
        if (chosen == null || chosen.Equals(current)) return false;
        FixSettings.SetValue(key, chosen.Value);
        Log.Write("options: " + key + "=" + chosen.Value);
        return true;
    }

    // End of d6f.a(ls) (the game applying its options after Accept): lay the interface out again if needed.
    public static void Applied()
    {
        if (!relayout) return;
        relayout = false;
        try
        {
            UnderrailDisplayModes.Reapply();
            bl9.b();
            ps screen = bpo.GetService<ps>();
            if (screen != null) screen.anc();
            UnderrailDisplayModes.ApplyFixedZoom();
            Log.Write("options: interface laid out again");
            if (confirm != null) { var previous = confirm; confirm = null; Ask(previous); }
        }
        catch (Exception ex) { Log.Write("applying UnderRailWorks options failed: " + ex); }
    }

    // The game's own "keep the new video settings?" countdown dialog (bnr.a); running out of time picks Revert.
    // Its signature has XNA Color? parameters typed with the game's XNA reference, so it is called through reflection.
    static readonly MethodInfo Dialog = typeof(bnr).GetMethods(BindingFlags.Public | BindingFlags.Static)
        .Single(m => m.Name == "a" && m.GetParameters().Length == 12 && m.GetParameters()[0].ParameterType == typeof(esj));

    static void Ask(Dictionary<string, string> previous)
    {
        euz owner = null;
        bcm screens = bpo.GetService<bcm>();
        if (screens != null && screens.aau() != null) owner = screens.aau().Controls.OfType<cef>().FirstOrDefault();     // as the game does: over the Escape menu
        Dialog.Invoke(null, new object[] {
            new esj(500, 250), new Confirm(previous), "Interface settings",
            "Do you want to keep the new interface settings? They will be put back in {0} second(s).",
            null, "R", null, null, ConfirmSeconds * 1000, new[] { new cpk("K", "Keep"), new cpk("R", "Revert") }, owner, owner });
    }

    sealed class Confirm : eyp
    {
        readonly Dictionary<string, string> previous;
        public Confirm(Dictionary<string, string> previous) { this.previous = previous; }

        public void g0(string button)
        {
            if (button == "K") { Log.Write("options: interface settings kept"); return; }
            foreach (var kv in previous) FixSettings.SetValue(kv.Key, kv.Value);
            Log.Write("options: interface settings reverted");
            relayout = true;
            Applied();
        }
    }
}

// The Mods tab scrolls when its rows don't fit above the Accept/Cancel buttons, which t1 lays over the bottom of the
// tab area: the game's themed scrollbar on the right, the mouse wheel over the tab, and only the rows that fit fully
// above Accept are on the page. The rows can be split into views (the mod list, a mod's settings...): one is shown
// at a time (SetView), each scrolled on its own.
static class TabScroll
{
    const int Gap = 8, Step = 20;
    // Items: each row's control, its y and its height as built (a dropdown's closed height).
    sealed class State
    {
        public List<Tuple<euz, int, int>> Items; public int Offset, Room, Content; public cs9 Bar; public cvh Page;
        public List<int> Starts; public int View;
        public int Start { get { return Starts[View]; } }
        public int End { get { return View + 1 < Starts.Count ? Starts[View + 1] : int.MaxValue; } }
        public bool InView(Tuple<euz, int, int> i) { return i.Item2 >= Start && i.Item2 < End; }
    }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<cvh, State> States = new System.Runtime.CompilerServices.ConditionalWeakTable<cvh, State>();
    static readonly FieldInfo Accept = typeof(t1).GetField("ac", BindingFlags.Instance | BindingFlags.NonPublic), Tabs = typeof(t1).GetField("u", BindingFlags.Instance | BindingFlags.NonPublic);
    static readonly FieldInfo BarSize = typeof(wj).GetField("bm");
    static readonly PropertyInfo Location = typeof(euz).GetProperty("Location");
    static readonly MethodInfo Close = typeof(aw9).GetMethod("b", BindingFlags.Instance | BindingFlags.NonPublic, null, Type.EmptyTypes, null);     // aw9.b() (private) closes the list
    static int Top(euz c) { return UnderrailDropdownScroll.Y(Location.GetValue(c, null)); }

    public static void Attach(t1 options, cvh page, List<int> starts)
    {
        try
        {
            var tabs = Tabs.GetValue(options) as euz;
            var accept = Accept.GetValue(options) as euz;
            int room = UnderrailDropdownScroll.H(page.Size);
            if (tabs != null && accept != null) room = Math.Min(room, Top(accept) - Top(tabs) - Top(page) - Gap);
            var items = page.Controls.Cast<euz>().Select(c => Tuple.Create(c, Top(c), UnderrailDropdownScroll.H(c.Size))).ToList();
            if (items.Count == 0) return;
            if (starts == null || starts.Count == 0) starts = new List<int> { items.Min(i => i.Item2) };

            wj theme = wj.a();
            int barWidth = theme.c() ? 26 : UnderrailDropdownScroll.W((esj)BarSize.GetValue(theme));
            var s = new State { Items = items, Room = room, Page = page, Starts = starts };
            bdl bar = theme.c() ? new bdl() : new dg8();
            bar.IsPrivateComponent = true;
            bar.b(0);
            bar.c(Step);
            bar.d(Math.Max(Step, room / 2));
            UnderrailDropdownScroll.Move(bar, UnderrailDropdownScroll.W(page.Size) - barWidth - 4, 0);
            bar.Size = new esj(barWidth, room);
            e4z.c(bar.d());
            e4z.c(bar.e());
            e4z.d(bar.h());
            bar.f(new EventHandler<c1>((sender, e) => Scrolled(page)));
            s.Bar = bar;
            States.Remove(page);
            States.Add(page, s);
            page.t(new EventHandler<b25>((sender, e) => Wheel(page, e)));
            bar.t(new EventHandler<b25>((sender, e) => Wheel(page, e)));
            foreach (var i in items) { i.Item1.t(new EventHandler<b25>((sender, e) => Wheel(page, e))); NoSliderWheel(i.Item1, page); }
            SetView(page, 0);
        }
        catch (Exception ex) { UnderrailOptions.TabFailed("scrolling", ex); }
    }

    // Shows one view, from its top.
    public static void SetView(cvh page, int view)
    {
        State s;
        if (!States.TryGetValue(page, out s) || view < 0 || view >= s.Starts.Count) return;
        foreach (var i in s.Items) if (Open(i)) Close.Invoke(i.Item1, null);
        s.View = view;
        s.Offset = 0;
        var mine = s.Items.Where(s.InView).ToList();
        s.Content = mine.Count == 0 ? 0 : mine.Max(i => i.Item2 + i.Item3) - s.Start;
        s.Bar.a(Math.Max(0, s.Content - s.Room));
        s.Bar.e(0);
        Show(s);
    }

    // Sliders (c5h) change their value on the wheel themselves (c5h.it, the first MouseScroll handler, added by euz's
    // ctor), before the page's handler scrolls: scrolling the tab over a slider moved it. In this tab they lose that
    // handler, so the wheel only scrolls the tab (sliders still drag and click).
    static readonly FieldInfo WheelHandlers = typeof(euz).GetField("ab", BindingFlags.Instance | BindingFlags.NonPublic);
    static void NoSliderWheel(euz control, cvh page)
    {
        if (control == null || WheelHandlers == null || WheelHandlers.FieldType != typeof(EventHandler<b25>)) return;
        if (control is c5h)
        {
            var handlers = WheelHandlers.GetValue(control) as EventHandler<b25>;
            if (handlers != null)
                foreach (var d in handlers.GetInvocationList())
                    if (d.Target == control && d.Method.Name == "it") handlers = (EventHandler<b25>)Delegate.Remove(handlers, d);
            WheelHandlers.SetValue(control, handlers);
            control.t(new EventHandler<b25>((sender, e) => Wheel(page, e)));     // the wheel over it scrolls the tab (it no longer passes it up)
        }
        foreach (var child in control.Controls.Cast<euz>()) NoSliderWheel(child, page);
    }

    // A dropdown that is open is taller than it was built.
    static bool Open(Tuple<euz, int, int> i) { return i.Item1 is aw9 && i.Item1.Parent != null && UnderrailDropdownScroll.H(i.Item1.Size) > i.Item3; }

    static void Show(State s)
    {
        foreach (var i in s.Items) if (Open(i)) Close.Invoke(i.Item1, null);     // close it before it moves
        foreach (var i in s.Items) if (i.Item1.Parent == s.Page) UnderrailDropdownScroll.Remove(s.Page, i.Item1);
        if (s.Bar.Parent == s.Page) UnderrailDropdownScroll.Remove(s.Page, s.Bar);
        foreach (var i in s.Items)
        {
            if (!s.InView(i)) continue;
            int y = i.Item2 - s.Start - s.Offset;
            if (y < 0 || y + i.Item3 > s.Room) continue;
            UnderrailDropdownScroll.Move(i.Item1, UnderrailDropdownScroll.X(Location.GetValue(i.Item1, null)), y);
            UnderrailDropdownScroll.Add(s.Page, i.Item1);
        }
        if (s.Content > s.Room) UnderrailDropdownScroll.Add(s.Page, s.Bar);
    }

    static void Scrolled(cvh page)
    {
        State s;
        if (!States.TryGetValue(page, out s)) return;
        int offset = Math.Max(0, Math.Min(s.Content - s.Room, s.Bar.l()));
        if (offset == s.Offset) return;
        s.Offset = offset;
        Show(s);
    }

    static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    static long lastWheel = -1000;

    static void Wheel(cvh page, b25 e)
    {
        State s;
        if (e.c() == 0 || !States.TryGetValue(page, out s) || s.Content <= s.Room) return;
        if (s.Items.Any(Open)) return;     // the wheel scrolls the open dropdown's list, not the tab
        long now = Clock.ElapsedMilliseconds;
        if (now - lastWheel < 4) return;     // a row and the page both report the same notch
        lastWheel = now;
        s.Bar.e(Math.Max(0, Math.Min(s.Content - s.Room, s.Offset - Math.Sign(e.c()) * Step * 2)));     // Scrolled moves the rows
    }
}
