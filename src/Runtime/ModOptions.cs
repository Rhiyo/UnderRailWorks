// UnderRailWorks: the Mods tab in the game's Options: settings only. View 0 lists Works' engine fixes and a button per
// loaded mod with settings; each mod's settings, and Works' own engine fixes, are views of their own with a Back button
// (TabScroll shows one view at a time). Switching mods on and off, their order and profiles are on the main menu's Mods
// screen (ModManager.cs). A mod describes its settings with ModPage (toggles, choices, sliders, buttons, subsections, or
// its own controls through Custom); Accept writes changed values to its settings.ini and calls its changed callback.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class ModPage
{
    internal readonly t1 Options;
    internal readonly cvh Page;
    internal readonly ModSettings Settings;
    readonly string mod;
    int subsectionFrom = -1;

    abstract class Binding
    {
        public string Key;
        public abstract string Value { get; }
        public string Was;
        public abstract void Show(string value);
    }
    sealed class Tick : Binding
    {
        public cao Box;
        public override string Value { get { return Box.Checked ? "1" : "0"; } }
        public override void Show(string v) { Box.Checked = v == "1"; }
    }
    sealed class Pick : Binding
    {
        public aw9 Combo; public List<FixChoice> Items;
        public override string Value { get { var c = Combo.l() as FixChoice; return c == null ? Was : c.Value; } }
        public override void Show(string v) { var c = Items.FirstOrDefault(i => i.Value == v); if (c != null) Combo.b(c); }
    }
    sealed class Slide : Binding
    {
        public oz Slider;
        public double Scale = 1;     // a decimal setting: the game's slider holds value / Scale as whole numbers
        public override string Value { get { return UnderrailOptions.Moved(Slider) ? (Scale == 1 ? Slider.l().ToString(System.Globalization.CultureInfo.InvariantCulture) : Math.Round(Slider.l() * Scale, 10).ToString("0.##########", System.Globalization.CultureInfo.InvariantCulture)) : Was; } }
        public override void Show(string v)
        {
            double n;
            if (double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out n)) Slider.e((int)Math.Round(n / Scale));
        }
    }
    readonly List<Binding> bindings = new List<Binding>();

    // Each row as built (its controls and its y range), for EnabledIf / ShowIf.
    sealed class Row
    {
        public string Key; public List<euz> Controls; public int Start, End;
        public Func<bool> Enabled, Shown; public TabScroll.Fold Fold; public bool? WasEnabled;
        public Dictionary<avo, Microsoft.Xna.Framework.Color> Colours = new Dictionary<avo, Microsoft.Xna.Framework.Color>();
    }
    readonly List<Row> rows = new List<Row>();
    int rowFrom, rowY;
    void BeginRow() { rowFrom = Page.Controls.Count; rowY = UnderrailOptions.RowY(Options); }
    void EndRow(string key, euz alsoMine)
    {
        var mine = Page.Controls.Cast<euz>().Skip(rowFrom).ToList();
        if (alsoMine != null && !mine.Contains(alsoMine)) mine.Add(alsoMine);     // a dropdown joins the page at the end
        rows.Add(new Row { Key = key, Controls = mine, Start = rowY, End = UnderrailOptions.RowY(Options) });
    }
    // Conditions for the row just built for key (null: none of that kind).
    internal void Rule(string key, Func<bool> enabled, Func<bool> shown)
    {
        var r = rows.LastOrDefault(x => x.Key == key);
        if (r == null) return;
        r.Enabled = enabled; r.Shown = shown;
        if (shown != null) r.Fold = TabScroll.AddFold(Page, r.Start, r.End, false);
    }

    // The value a row shows now (settings.ini's form), and setting it (shown at once, saved on Accept).
    internal string Value(string key) { var b = bindings.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase)); return b == null ? null : b.Value; }
    readonly Dictionary<string, string> seen = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
    internal bool SetValue(string key, string value)
    {
        var b = bindings.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (b == null || value == null) return false;
        b.Show(value);
        seen[b.Key] = b.Value;     // not an edit of the player's: OnEdit isn't called for it
        return true;
    }
    // The keys the player changed since the last call.
    internal List<string> Edited()
    {
        var list = new List<string>();
        foreach (var b in bindings)
        {
            string v = b.Value, last;
            if (!seen.TryGetValue(b.Key, out last)) last = b.Was;
            if (v == last) continue;
            seen[b.Key] = v;
            list.Add(b.Key);
        }
        return list;
    }
    static readonly System.Reflection.PropertyInfo LabelColour = typeof(avo).GetProperty("Color");
    static readonly Microsoft.Xna.Framework.Color Greyed = new Microsoft.Xna.Framework.Color(110, 105, 95);
    // Applies the rows' conditions; true when a row was hidden or shown (the page must be laid out again).
    internal bool ApplyRules()
    {
        bool moved = false;
        foreach (var r in rows)
        {
            if (r.Enabled != null)
            {
                bool on = true;
                try { on = r.Enabled(); } catch (Exception ex) { ModErrors.Report(mod, "a setting's EnabledIf", ex); r.Enabled = null; }
                if (r.WasEnabled != on)
                {
                    r.WasEnabled = on;
                    foreach (var c in r.Controls)
                    {
                        var label = c as avo;
                        if (label != null && LabelColour != null)
                        {
                            if (!r.Colours.ContainsKey(label)) r.Colours[label] = (Microsoft.Xna.Framework.Color)LabelColour.GetValue(label, null);
                            LabelColour.SetValue(label, on ? r.Colours[label] : Greyed, null);
                        }
                        else c.Enabled = on;
                    }
                }
            }
            if (r.Shown != null && r.Fold != null)
            {
                bool show = true;
                try { show = r.Shown(); } catch (Exception ex) { ModErrors.Report(mod, "a setting's ShowIf", ex); r.Shown = null; }
                if (r.Fold.Closed == show) { r.Fold.Closed = !show; moved = true; }
            }
        }
        return moved;
    }

    internal ModPage(t1 options, cvh page, ModSettings settings, string mod)
    {
        Options = options; Page = page; Settings = settings; this.mod = mod;
    }

    void EndSubsection()
    {
        if (subsectionFrom >= 0) UnderrailOptions.Indent(Page, subsectionFrom);
        subsectionFrom = -1;
    }
    internal void Finish() { EndSubsection(); }

    // A heading inside the mod's group.
    public void Heading(string text) { EndSubsection(); UnderrailOptions.HeadingRow(Options, text); }

    // An indented subsection: its heading and the rows after it, up to the next heading or subsection.
    public void Subsection(string text)
    {
        EndSubsection();
        subsectionFrom = Page.Controls.Count;
        UnderrailOptions.HeadingRow(Options, text);
    }

    public void Toggle(string key, string label, bool fallback)
    {
        bool v = Settings.Bool(key, fallback);
        BeginRow();
        var box = UnderrailOptions.TickBox(Options, label, v);
        bindings.Add(new Tick { Key = key, Was = v ? "1" : "0", Box = box });
        EndRow(key, box);
    }

    public void Choice(string key, string label, string[] texts, string[] values, string fallback)
    {
        var items = texts.Select((t, i) => new FixChoice(t, values[i])).ToList();
        string v = Settings.Get(key);
        if (v == null || !items.Any(i => i.Value.Equals(v, StringComparison.OrdinalIgnoreCase))) v = fallback;
        if (items.Count == 0) return;
        var current = items.FirstOrDefault(i => i.Value.Equals(v, StringComparison.OrdinalIgnoreCase)) ?? items[0];     // a default that isn't one of them (an enum field left at 0)
        BeginRow();
        var combo = UnderrailOptions.Add(Options, label, items, current, current);
        bindings.Add(new Pick { Key = key, Was = current.Value, Combo = combo, Items = items });
        EndRow(key, combo);
    }

    public void Slider(string key, string label, int min, int max, int step, int fallback)
    {
        int v = Settings.Int(key, fallback, min, max);
        BeginRow();
        var slider = UnderrailOptions.SliderRow(Options, Page, "works." + mod + "." + key, label, v, min, max, step, false);
        if (slider != null) bindings.Add(new Slide { Key = key, Was = v.ToString(System.Globalization.CultureInfo.InvariantCulture), Slider = slider });
        EndRow(key, slider);
    }

    // A decimal setting (float/double fields): the game's slider counts in steps of step.
    public void Slider(string key, string label, double min, double max, double step, double fallback)
    {
        if (step <= 0) step = 0.1;
        double v = fallback; double parsed;
        var raw = Settings.Get(key);
        if (raw != null && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)) v = Math.Max(min, Math.Min(max, parsed));
        BeginRow();
        var slider = UnderrailOptions.SliderRow(Options, Page, "works." + mod + "." + key, label, (int)Math.Round(v / step), (int)Math.Round(min / step), (int)Math.Round(max / step), 1,
            false, i => Math.Round(i * step, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
        if (slider != null) bindings.Add(new Slide { Key = key, Was = v.ToString(System.Globalization.CultureInfo.InvariantCulture), Slider = slider, Scale = step });
        EndRow(key, slider);
    }

    // A button on its own row; click runs on the options screen (e.g. to put rows back with Set).
    public void Button(string label, string text, Action<ModPage> click)
    {
        var button = UnderrailOptions.NewButton(Options, text, () => { try { ModScope.As(mod, click, () => click(this)); } catch (Exception ex) { ModErrors.Report(mod, "a settings button", ex); } });
        UnderrailOptions.ControlRow(Options, label, button);
    }

    // A row with text instead of a control.
    internal void Note(string label, string text)
    {
        var note = Ui.Label(text, 360);
        Ui.SetColor(note, new Microsoft.Xna.Framework.Color(160, 160, 150));
        BeginRow();
        UnderrailOptions.ControlRow(Options, label, note.Control);
        EndRow(label, note.Control);
    }

    // Shows a value in a row (not saved until Accept), e.g. for a reset button.
    public void Set(string key, string value)
    {
        var b = bindings.FirstOrDefault(x => x.Key.Equals(key, StringComparison.OrdinalIgnoreCase));
        if (b != null) b.Show(value);
    }

    // The mod's own controls: build gets the options window and the page (add rows with the game's builders, or
    // controls of its own). Saving them is up to the mod.
    internal void Custom(Action<t1, cvh> build) { build(Options, Page); }

    // Counts as changed on Accept even if no row's value differs (Restore defaults on values that were defaults already:
    // the mod still hears of it).
    internal bool Touched;
    public void MarkChanged() { Touched = true; }

    // Accept: the changed values into the mod's settings.ini.
    internal bool Save()
    {
        bool changed = false;
        foreach (var b in bindings)
        {
            string v = b.Value;
            if (v == null || v == b.Was) continue;
            Settings.Set(b.Key, v);
            Log.Write("mods: " + mod + ": " + b.Key + "=" + v);
            changed = true;
        }
        return changed;
    }
}

public static class ModOptions
{
    sealed class Entry { public Action<ModPage> Build; public Action Changed; public Action<string, SettingsEdit> Edited; }
    static readonly Dictionary<string, Entry> Pages = new Dictionary<string, Entry>(StringComparer.OrdinalIgnoreCase);

    sealed class Tab
    {
        public List<KeyValuePair<string, ModPage>> Pages = new List<KeyValuePair<string, ModPage>>();
        public List<ModInfo> WithSettings = new List<ModInfo>();
        public cvh Page;
    }
    static readonly ConditionalWeakTable<t1, Tab> Tabs = new ConditionalWeakTable<t1, Tab>();

    // From a mod's OnLoad: its settings rows in the Mods tab. changed runs after Accept wrote new values.
    internal static void Forget(string mod) { Pages.Remove(mod); }
    internal static bool HasPage(string mod) { return Pages.ContainsKey(mod); }
    internal static void Register(string mod, Action<ModPage> build, Action changed = null, Action<string, SettingsEdit> edited = null)
    {
        Pages[mod] = new Entry { Build = build, Changed = changed, Edited = edited };
    }

    // --- Settings that depend on others (ModConfig's EnabledIf / ShowIf / OnEdit), live on the open Options page ---

    static Tab Live { get { var o = UiHooks.CurrentOptions; Tab t; return o != null && Tabs.TryGetValue(o, out t) ? t : null; } }

    // A setting as the page shows it now (the open Options page), else as saved (settings.ini); null: no such mod or key.
    internal static string Value(string mod, string key)
    {
        var tab = Live;
        if (tab != null) foreach (var p in tab.Pages) if (p.Key.Equals(mod, StringComparison.OrdinalIgnoreCase)) { var v = p.Value.Value(key); if (v != null) return v; }
        var m = ModLoader.Find(mod);
        if (m == null || !m.Loaded || !Pages.ContainsKey(m.Id)) return null;
        return ModSettings.For(m.Id).Get(key);
    }
    internal static bool SetValue(string mod, string key, string value)
    {
        var tab = Live;
        if (tab == null) return false;
        foreach (var p in tab.Pages) if (p.Key.Equals(mod, StringComparison.OrdinalIgnoreCase)) return p.Value.SetValue(key, value);
        return false;
    }
    internal static bool Truthy(string v) { return v != null && (v == "1" || v.Equals("true", StringComparison.OrdinalIgnoreCase) || v.Equals("on", StringComparison.OrdinalIgnoreCase)); }
    // want: null = on; "x" = equals x (any case; numbers by value); "!x" = anything but x.
    internal static bool Matches(string actual, string want)
    {
        if (want == null) return Truthy(actual);
        bool not = want.StartsWith("!");
        if (not) want = want.Substring(1);
        bool same;
        double a, b;
        if (actual != null && double.TryParse(actual, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out a)
            && double.TryParse(want, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out b)) same = a == b;
        else if (want.Equals("true", StringComparison.OrdinalIgnoreCase) || want.Equals("false", StringComparison.OrdinalIgnoreCase)) same = Truthy(actual) == want.Equals("true", StringComparison.OrdinalIgnoreCase);
        else same = string.Equals(actual, want, StringComparison.OrdinalIgnoreCase);
        return not ? !same : same;
    }

    // Every update while an Options page with mod settings exists: the player's edits go to their mod's OnEdit, then
    // every row's conditions are applied (greyed / hidden), across mods.
    static Tab ticked;
    internal static void Tick()
    {
        var tab = Live;
        if (tab == null) return;
        bool first = ticked != tab; ticked = tab;
        bool edited = false;
        foreach (var p in tab.Pages)
        {
            var keys = p.Value.Edited();
            if (keys.Count == 0) continue;
            edited = true;
            Entry e;
            if (!Pages.TryGetValue(p.Key, out e) || e.Edited == null) continue;
            foreach (var k in keys)
            {
                var key = k; var edit = new SettingsEdit(p.Key);
                try { ModScope.As(p.Key, e.Edited, () => e.Edited(key, edit)); } catch (Exception ex) { ModErrors.Report(p.Key, "OnEdit", ex); }
            }
        }
        if (!first && !edited) return;
        bool moved = false;
        foreach (var p in tab.Pages) moved |= p.Value.ApplyRules();
        if (moved) TabScroll.Refold(tab.Page);
    }

    static readonly Color Bad = new Color(225, 90, 70), Quiet = new Color(150, 140, 125);
    const int TopMargin = 10;

    // A line of text at the left column, as wide as the page allows.
    static avo Text(t1 options, cvh page, string text, Color? color = null)
    {
        int width = UnderrailDropdownScroll.W(page.Size) - 2 * UnderrailOptions.Left(options) - 40;
        var label = UnderrailOptions.Label(options, width);
        label.a(cab.a);     // left-aligned
        label.Text = text;
        if (color.HasValue) Paint(label, color.Value);
        UnderrailOptions.PlaceRow(options, label);
        return label;
    }

    // avo.Color is typed with the game's XNA reference (CS0012 here), so it is set by reflection.
    static readonly System.Reflection.PropertyInfo ColorProperty = typeof(avo).GetProperty("Color");
    static void Paint(avo label, Color colour) { ColorProperty.SetValue(label, colour, null); }

    static void Gap(t1 options, int px) { UnderrailOptions.SetRowY(options, UnderrailOptions.RowY(options) + px); }

    // The whole tab: Works' own settings under its heading (fixes: their rows, from Options.AddTab), then a Mods heading
    // and a section per loaded mod with settings, in load order, closed until its title is clicked (sections stay as the
    // player left them while the game runs).
    internal static void ListView(t1 options, cvh page, Action fixes)
    {
        var tab = new Tab { Page = page };
        Tabs.Remove(options);
        Tabs.Add(options, tab);
        Gap(options, TopMargin);
        tab.WithSettings = ModLoader.Mods.Where(m => m.Loaded && Pages.ContainsKey(m.Id)).OrderBy(m => m.LoadIndex).ToList();

        UnderrailOptions.HeadingRow(options, "UnderRailWorks " + FixVersion.Value);
        if (fixes != null) fixes();
        Gap(options, 6);
        UnderrailOptions.SeparatorRow(options);
        UnderrailOptions.HeadingRow(options, "Mods");
        Text(options, page, "Switch mods on and off, and change their order, with the main menu's Mods button.", Quiet);
        var troubled = ModLoader.Mods.Where(m => m.Loaded && m.Failures > 0).ToList();
        if (troubled.Count > 0) Text(options, page, "Errors this session: " + string.Join(", ", troubled.Select(m => m.Name + " (" + m.Failures + ")").ToArray()) + "; see UnderRailWorks.log", Bad);
        if (tab.WithSettings.Count == 0) Text(options, page, ModLoader.Mods.Any(m => m.Loaded) ? "None of the loaded mods has settings." : "No mods are loaded.", Quiet);
        Gap(options, 4);
        foreach (var mod in tab.WithSettings)
        {
            var m = mod;
            Section(options, page, m.Name, m.Version != null ? m.VersionText : null, m.Id, () =>
            {
                try
                {
                    var modPage = new ModPage(options, page, ModSettings.For(m.Id), m.Id);
                    Pages[m.Id].Build(modPage);
                    modPage.Finish();
                    tab.Pages.Add(new KeyValuePair<string, ModPage>(m.Id, modPage));
                }
                catch (Exception ex) { Log.Write("mods: " + m.Id + ": settings rows failed: " + ex); }
            });
        }
    }

    // Open sections, by key (a mod's id), for this run of the game.
    static readonly HashSet<string> Open = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

    // An accordion row (as web frameworks' disclosure headers): the whole width is the title, a chevron at its left
    // (pointing right when closed, down when open), the version at its right in grey, a thin rule under it in the
    // interface's orange, a faint highlight under the mouse; a click anywhere on it opens or closes the rows build adds,
    // which are folded away (TabScroll) while closed.
    static readonly Color Title = new Color(222, 132, 40), TitleHot = new Color(250, 180, 90), Rule = new Color(120, 66, 12), HotFill = new Color(70, 40, 10, 110), Meta = new Color(150, 140, 125);
    static void Section(t1 options, cvh page, string title, string meta, string key, Action build)
    {
        bool open = Open.Contains(key);
        TabScroll.Fold fold = null;
        int width = UnderrailDropdownScroll.W(page.Size) - 2 * UnderrailOptions.Left(options) - 40;
        UiElement header = null;
        header = Ui.Custom(width, Ui.LineHeight + Ui.S(10), c =>
        {
            var b = c.Bounds;
            bool hot = Ui.IsHot(header);
            bool closed = fold == null || fold.Closed;
            if (hot) c.Fill(b, HotFill);
            var colour = hot ? TitleHot : Title;
            int s = Math.Max(4, Ui.S(5)), cx = b.X + Ui.S(8) + s / 2, cy = b.Y + (b.Height - 1) / 2;
            if (closed) for (int i = 0; i <= s; i++) c.Fill(new Rectangle(cx - s / 2 + i, cy - (s - i), 1, 2 * (s - i) + 1), colour);     // pointing right
            else for (int j = 0; j <= s; j++) c.Fill(new Rectangle(cx - (s - j), cy - s / 2 + j, 2 * (s - j) + 1, 1), colour);           // pointing down
            int textY = b.Y + (b.Height - Ui.LineHeight) / 2;
            c.Text(title, b.X + Ui.S(8) + s + Ui.S(12), textY, colour);
            if (meta != null) c.Text(meta, b.Right - Ui.S(8) - Ui.TextWidth(meta), textY, Meta);
            c.Fill(new Rectangle(b.X, b.Bottom - 1, b.Width, 1), Rule);
            if (hot && fold != null && Input.MousePressed(MouseButton.Left))
            {
                fold.Closed = !fold.Closed;
                if (fold.Closed) Open.Remove(key); else Open.Add(key);
                TabScroll.Refold(page);
            }
        });
        UnderrailOptions.PlaceRow(options, header.Control);
        int start = UnderrailOptions.RowY(options);
        Gap(options, 4);
        build();
        Gap(options, 10);
        fold = TabScroll.AddFold(page, start, UnderrailOptions.RowY(options), !open);
    }

    internal static void Save(t1 options)
    {
        Tab tab;
        if (!Tabs.TryGetValue(options, out tab)) return;
        foreach (var p in tab.Pages)
        {
            try
            {
                Entry entry;
                if ((p.Value.Save() | p.Value.Touched) && Pages.TryGetValue(p.Key, out entry) && entry.Changed != null) entry.Changed();
            }
            catch (Exception ex) { Log.Write("mods: " + p.Key + ": saving options failed: " + ex); }
        }
    }
}
}
