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
        bindings.Add(new Tick { Key = key, Was = v ? "1" : "0", Box = UnderrailOptions.TickBox(Options, label, v) });
    }

    public void Choice(string key, string label, string[] texts, string[] values, string fallback)
    {
        var items = texts.Select((t, i) => new FixChoice(t, values[i])).ToList();
        string v = Settings.Get(key);
        if (v == null || !items.Any(i => i.Value.Equals(v, StringComparison.OrdinalIgnoreCase))) v = fallback;
        if (items.Count == 0) return;
        var current = items.FirstOrDefault(i => i.Value.Equals(v, StringComparison.OrdinalIgnoreCase)) ?? items[0];     // a default that isn't one of them (an enum field left at 0)
        var combo = UnderrailOptions.Add(Options, label, items, current, current);
        bindings.Add(new Pick { Key = key, Was = current.Value, Combo = combo, Items = items });
    }

    public void Slider(string key, string label, int min, int max, int step, int fallback)
    {
        int v = Settings.Int(key, fallback, min, max);
        var slider = UnderrailOptions.SliderRow(Options, Page, "works." + mod + "." + key, label, v, min, max, step, false);
        if (slider != null) bindings.Add(new Slide { Key = key, Was = v.ToString(System.Globalization.CultureInfo.InvariantCulture), Slider = slider });
    }

    // A decimal setting (float/double fields): the game's slider counts in steps of step.
    public void Slider(string key, string label, double min, double max, double step, double fallback)
    {
        if (step <= 0) step = 0.1;
        double v = fallback; double parsed;
        var raw = Settings.Get(key);
        if (raw != null && double.TryParse(raw, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out parsed)) v = Math.Max(min, Math.Min(max, parsed));
        var slider = UnderrailOptions.SliderRow(Options, Page, "works." + mod + "." + key, label, (int)Math.Round(v / step), (int)Math.Round(min / step), (int)Math.Round(max / step), 1,
            false, i => Math.Round(i * step, 4).ToString("0.####", System.Globalization.CultureInfo.InvariantCulture));
        if (slider != null) bindings.Add(new Slide { Key = key, Was = v.ToString(System.Globalization.CultureInfo.InvariantCulture), Slider = slider, Scale = step });
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
        UnderrailOptions.ControlRow(Options, label, note.Control);
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
    sealed class Entry { public Action<ModPage> Build; public Action Changed; }
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
    internal static void Register(string mod, Action<ModPage> build, Action changed = null)
    {
        Pages[mod] = new Entry { Build = build, Changed = changed };
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

    // View 0: Works' own part (engine fixes), then a Settings button per loaded mod that has settings, in load order.
    internal static void ListView(t1 options, cvh page, List<int> views, bool fixes)
    {
        var tab = new Tab { Page = page };
        Tabs.Remove(options);
        Tabs.Add(options, tab);
        views.Add(UnderrailOptions.RowY(options));
        Gap(options, TopMargin);
        int next = fixes ? 2 : 1;
        tab.WithSettings = ModLoader.Mods.Where(m => m.Loaded && Pages.ContainsKey(m.Id)).OrderBy(m => m.LoadIndex).ToList();

        UnderrailOptions.HeadingRow(options, "UnderRailWorks " + FixVersion.Value);
        Text(options, page, "Engine fixes: resolution, interface scale, zoom, frame rate, 64-bit mode.", Quiet);
        if (fixes) ButtonRow(options, page, "Engine fixes", 1);
        UnderrailOptions.SeparatorRow(options);
        UnderrailOptions.HeadingRow(options, "Mod settings");
        Text(options, page, "Mods are switched on and off, and put in order, from the main menu's Mods button.", Quiet);
        var troubled = ModLoader.Mods.Where(m => m.Loaded && m.Failures > 0).ToList();
        if (troubled.Count > 0) Text(options, page, "Errors this session: " + string.Join(", ", troubled.Select(m => m.Name + " (" + m.Failures + ")").ToArray()) + "; see UnderRailWorks.log", Bad);
        if (tab.WithSettings.Count == 0) Text(options, page, ModLoader.Mods.Any(m => m.Loaded) ? "None of the loaded mods has settings." : "No mods are loaded.", Quiet);
        for (int i = 0; i < tab.WithSettings.Count; i++) ButtonRow(options, page, tab.WithSettings[i].Name, next + i);
    }

    // A button at the left column that shows a view.
    static void ButtonRow(t1 options, cvh page, string text, int view)
    {
        var button = UnderrailOptions.NewButton(options, text, () => TabScroll.SetView(page, view));
        Gap(options, 4);
        UnderrailOptions.PlaceRow(options, button);
    }

    // The top of a settings view: back to the list, and the view's title.
    internal static void BackRow(t1 options, cvh page, string title)
    {
        Gap(options, TopMargin);
        var button = UnderrailOptions.NewButton(options, "Back to mods", () => TabScroll.SetView(page, 0));
        UnderrailOptions.PlaceRow(options, button);
        UnderrailOptions.HeadingRow(options, title);
        UnderrailOptions.SeparatorRow(options);
    }

    // One view per mod with settings (the Settings buttons' targets, in the same order).
    internal static void ModViews(t1 options, cvh page, List<int> views)
    {
        Tab tab;
        if (!Tabs.TryGetValue(options, out tab)) return;
        foreach (var mod in tab.WithSettings)
        {
            views.Add(UnderrailOptions.RowY(options));
            try
            {
                BackRow(options, page, mod.Name + " settings");
                var modPage = new ModPage(options, page, ModSettings.For(mod.Id), mod.Id);
                Pages[mod.Id].Build(modPage);
                modPage.Finish();
                tab.Pages.Add(new KeyValuePair<string, ModPage>(mod.Id, modPage));
            }
            catch (Exception ex) { Log.Write("mods: " + mod.Id + ": settings rows failed: " + ex); }
            Gap(options, 40);
        }
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
