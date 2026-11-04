// UnderRailWorks: the Mods tab in the game's Options, after tModLoader's Mods menu. View 0 lists a card per mod
// (switch, name, version, author, status, why it didn't load, description, requirements, a settings button); each
// mod's settings, and Works' own engine fixes, are views of their own with a Back button (TabScroll shows one view
// at a time). A mod describes its settings with ModPage (toggles, choices, sliders, buttons, subsections, or its own
// controls through Custom); Accept writes changed values to its settings.ini and calls its changed callback, and
// writes the switches to Mods\mods.ini (next start; switching a mod on also switches on what it requires).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;

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
        public override string Value { get { return UnderrailOptions.Moved(Slider) ? Slider.l().ToString() : Was; } }
        public override void Show(string v) { int n; if (int.TryParse(v, out n)) Slider.e(n); }
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
        var current = items.First(i => i.Value.Equals(v, StringComparison.OrdinalIgnoreCase));
        var combo = UnderrailOptions.Add(Options, label, items, current, current);
        bindings.Add(new Pick { Key = key, Was = current.Value, Combo = combo, Items = items });
    }

    public void Slider(string key, string label, int min, int max, int step, int fallback)
    {
        int v = Settings.Int(key, fallback, min, max);
        var slider = UnderrailOptions.SliderRow(Options, Page, "works." + mod + "." + key, label, v, min, max, step);
        if (slider != null) bindings.Add(new Slide { Key = key, Was = v.ToString(), Slider = slider });
    }

    // A button on its own row; click runs on the options screen (e.g. to put rows back with Set).
    public void Button(string label, string text, Action<ModPage> click)
    {
        var button = UnderrailOptions.NewButton(Options, text, () => { try { click(this); } catch (Exception ex) { Log.Write("mods: " + mod + ": button: " + ex); } });
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
        public List<KeyValuePair<ModInfo, cao>> Switches = new List<KeyValuePair<ModInfo, cao>>();
        public List<ModInfo> WithSettings = new List<ModInfo>();
        public cvh Page;
    }
    static readonly ConditionalWeakTable<t1, Tab> Tabs = new ConditionalWeakTable<t1, Tab>();

    // From a mod's OnLoad: its settings rows in the Mods tab. changed runs after Accept wrote new values.
    public static void Register(string mod, Action<ModPage> build, Action changed = null)
    {
        Pages[mod] = new Entry { Build = build, Changed = changed };
    }

    static readonly Color Good = new Color(130, 200, 100), Bad = new Color(225, 90, 70), Quiet = new Color(150, 140, 125);
    const int CardGap = 14, TopMargin = 10;

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

    // View 0: Works' own card (engine fixes), then a card per mod: switch, name, version, author, status, description,
    // requirements, and a Settings button that shows the mod's settings view.
    internal static void ListView(t1 options, cvh page, List<int> views, bool fixes)
    {
        var tab = new Tab { Page = page };
        Tabs.Remove(options);
        Tabs.Add(options, tab);
        views.Add(UnderrailOptions.RowY(options));
        Gap(options, TopMargin);
        int next = fixes ? 2 : 1;
        tab.WithSettings = ModLoader.Mods.Where(m => m.Loaded && Pages.ContainsKey(m.Id)).ToList();

        UnderrailOptions.HeadingRow(options, "UnderRailWorks " + FixVersion.Value);
        Text(options, page, "Engine fixes: resolution, interface scale, zoom, frame rate, 64-bit mode.", Quiet);
        if (fixes) ButtonRow(options, page, "Engine fixes", 1);
        UnderrailOptions.SeparatorRow(options);
        UnderrailOptions.HeadingRow(options, ModLoader.Mods.Count == 0 ? "No mods installed (Mods folder)" : "Mods (" + ModLoader.Mods.Count(m => m.Loaded) + " of " + ModLoader.Mods.Count + " loaded)");
        // Mods load when the game starts. Switching them is left to the main menu: a change made in a game would only
        // show after a restart, and that game's saves would then load with a different set of mods than they were played with.
        if (InGame) Text(options, page, "Mods are switched on and off from the main menu's Options; settings can be changed here.", Quiet);
        else if (ModLoader.Mods.Count > 0) Text(options, page, "Switching a mod on or off applies when you restart the game.", Quiet);

        foreach (var mod in ModLoader.Mods.OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase))
        {
            try
            {
                Gap(options, CardGap / 2);
                Card(options, page, tab, mod, tab.WithSettings.Contains(mod) ? next + tab.WithSettings.IndexOf(mod) : -1);
            }
            catch (Exception ex) { Log.Write("mods: " + mod.Id + ": card failed: " + ex); }
        }
    }

    static void Card(t1 options, cvh page, Tab tab, ModInfo mod, int settingsView)
    {
        // Line 1: [x] Name  version  by author ................ status
        int y = UnderrailOptions.RowY(options), left = UnderrailOptions.Left(options);
        var box = UnderrailOptions.NewTick(options, mod.Enabled);
        if (InGame) box.Enabled = false;     // see ListView
        UnderrailOptions.PlaceRow(options, box);
        tab.Switches.Add(new KeyValuePair<ModInfo, cao>(mod, box));
        int x = left + UnderrailDropdownScroll.W(box.Size) + 12, lineH = UnderrailDropdownScroll.H(box.Size);
        string title = mod.Name + (mod.Version != null ? "  " + mod.VersionText : "") + (mod.Author != null ? "   by " + mod.Author : "");
        var name = UnderrailOptions.Label(options, 520);
        name.a(cab.a);
        name.Text = title;
        Paint(name, Color.White);
        UnderrailDropdownScroll.Move(name, x, y + (lineH - UnderrailDropdownScroll.H(name.Size)) / 2);
        UnderrailDropdownScroll.Add(page, name);
        string status; Color colour;
        if (!mod.Enabled) { status = mod.Loaded ? "Off at restart" : "Off"; colour = Quiet; }
        else if (!mod.Loaded && mod.Error == null) { status = "On at restart"; colour = Good; }
        else if (mod.Loaded) { status = "Loaded"; colour = Good; }
        else { status = "Not loaded"; colour = Bad; }
        var state = UnderrailOptions.Label(options, 200);
        state.a(cab.c);     // right-aligned
        state.Text = status;
        Paint(state, colour);
        UnderrailDropdownScroll.Move(state, UnderrailDropdownScroll.W(page.Size) - left - 40 - 200, y + (lineH - UnderrailDropdownScroll.H(state.Size)) / 2);
        UnderrailDropdownScroll.Add(page, state);

        // Then: why it didn't load, the description, what it needs, its settings.
        // A missing requirement shows on the Requires line (in red); other reasons on a line of their own.
        bool unmet = mod.Requires.Any(d => Describe(d, true) != null);
        if (mod.Enabled && mod.Error != null && !(unmet && mod.Error.StartsWith("needs "))) Text(options, page, "   " + Capital(mod.Error), Bad);
        if (!string.IsNullOrEmpty(mod.Description))
        {
            // Wrapped to the page (a long description was cut at the page's edge).
            int width = UnderrailDropdownScroll.W(page.Size) - 2 * UnderrailOptions.Left(options) - 80;
            foreach (var line in mod.Description.Split('\n'))
                foreach (var part in Ui.Wrap(line.Trim(), width)) Text(options, page, "   " + part);
        }
        if (mod.Requires.Count > 0)
            Text(options, page, "   Requires: " + string.Join(", ", mod.Requires.Select(d => Describe(d, false)).ToArray()), unmet && mod.Enabled ? Bad : Quiet);
        if (mod.Optional.Count > 0) Text(options, page, "   Works with: " + string.Join(", ", mod.Optional.Select(d => Describe(d, false)).ToArray()), Quiet);
        if (settingsView > 0) ButtonRow(options, page, mod.Name + " settings", settingsView);
        else if (mod.Enabled && !mod.Loaded) { }
        Gap(options, CardGap / 2);
        UnderrailOptions.SeparatorRow(options);
    }

    static string Capital(string s) { return s.Length == 0 ? s : char.ToUpperInvariant(s[0]) + s.Substring(1); }

    // "Name 2.0+", with what's wrong in brackets; problemOnly: just the problem (null if none).
    static string Describe(ModDependency d, bool problemOnly)
    {
        var m = ModLoader.Find(d.Id);
        string state = m == null ? "not installed"
            : !m.Enabled ? "off"
            : d.Min != null && (m.Version == null || m.Version < d.Min) ? "version " + (m.Version == null ? "?" : m.VersionText) + " installed"
            : m.Loaded ? null : "not loaded";
        if (problemOnly) return state;
        string name = m != null ? m.Name : d.Id;
        return name + (d.Min != null ? " " + d.Min + "+" : "") + (state == null ? "" : " (" + state + ")");
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

    static bool InGame { get { return Hud.Exists; } }

    internal static void Save(t1 options)
    {
        Tab tab;
        if (!Tabs.TryGetValue(options, out tab)) return;
        // Switching a mod on switches on what it requires (as tModLoader offers to); switching one off leaves the mods
        // that need it to show why they didn't load at the next start.
        var on = tab.Switches.Where(s => s.Value.Checked).Select(s => s.Key).ToList();
        for (int i = 0; i < on.Count; i++)
            foreach (var d in on[i].Requires)
            {
                var dep = ModLoader.Find(d.Id);
                if (dep != null && !on.Contains(dep)) { on.Add(dep); Log.Write("mods: " + dep.Id + " switched on, " + on[i].Id + " needs it"); }
            }
        foreach (var s in tab.Switches.Where(x => !InGame))
        {
            bool want = on.Contains(s.Key);
            if (want != s.Key.Enabled) { ModLoader.SetEnabled(s.Key.Id, want); s.Key.Enabled = want; }
        }
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
