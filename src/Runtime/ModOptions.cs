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
        public List<KeyValuePair<ModInfo, cao>> Switches = new List<KeyValuePair<ModInfo, cao>>();
        public List<ModInfo> WithSettings = new List<ModInfo>();
        // The load order as shown (loaded mods, top = first), and each card's controls, height and title line.
        public List<ModInfo> Order; public bool OrderChanged;
        public List<CardParts> Cards = new List<CardParts>();
        public cvh Page;
        public Dictionary<ModInfo, avo> States = new Dictionary<ModInfo, avo>();     // each card's status (Loaded, Off at restart...)
        public avo ProfileLabel;
    }
    sealed class CardParts { public ModInfo Mod; public List<euz> Controls; public int Height; public avo Title; public string TitleText;
    }
    static readonly ConditionalWeakTable<t1, Tab> Tabs = new ConditionalWeakTable<t1, Tab>();

    // From a mod's OnLoad: its settings rows in the Mods tab. changed runs after Accept wrote new values.
    internal static void Forget(string mod) { Pages.Remove(mod); }
    internal static void Register(string mod, Action<ModPage> build, Action changed = null)
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
        // One list, as RimWorld's and Mod Organizer's: loaded mods in load order (top loads first; when two change the
        // same thing, the lower one has the final say), Up / Down to change it (next start); then the others by name.
        tab.Order = ModLoader.Mods.Where(m => m.Loaded).OrderBy(m => m.LoadIndex).ToList();
        if (tab.Order.Count > 1)
        {
            Text(options, page, "Loaded mods are listed in load order: when two change the same thing, the lower one has the final say.", Quiet);
            if (!InGame)
            {
                var auto = UnderrailOptions.NewButton(options, "Automatic order", () =>
                {
                    ModLoader.SetPlayerOrder(null); tab.OrderChanged = false;
                    Notify.Log("Mods: automatic load order (from the mods' requirements) at the next start.");
                });
                Gap(options, 4);
                UnderrailOptions.PlaceRow(options, auto);
            }
        }

        if (!InGame && ModLoader.Mods.Count > 0) ProfileRow(options, page, tab);

        var shown = tab.Order.Concat(ModLoader.Mods.Where(m => !m.Loaded).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        foreach (var mod in shown)
        {
            try
            {
                var before = new HashSet<euz>(page.Controls.Cast<euz>());
                int top = UnderrailOptions.RowY(options);
                Gap(options, CardGap / 2);
                var title = Card(options, page, tab, mod, tab.WithSettings.Contains(mod) ? next + tab.WithSettings.IndexOf(mod) : -1);
                if (mod.Loaded)
                    tab.Cards.Add(new CardParts { Mod = mod, Controls = page.Controls.Cast<euz>().Where(c => !before.Contains(c)).ToList(), Height = UnderrailOptions.RowY(options) - top, Title = title.Key, TitleText = title.Value });
            }
            catch (Exception ex) { Log.Write("mods: " + mod.Id + ": card failed: " + ex); }
        }
        Number(tab);
    }

    static KeyValuePair<avo, string> Card(t1 options, cvh page, Tab tab, ModInfo mod, int settingsView)
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
        var state = UnderrailOptions.Label(options, 200);
        state.a(cab.c);     // right-aligned
        ShowStatus(state, mod, mod.Enabled);
        tab.States[mod] = state;
        int stateX = UnderrailDropdownScroll.W(page.Size) - left - 40 - 200;
        UnderrailDropdownScroll.Move(state, stateX, y + (lineH - UnderrailDropdownScroll.H(state.Size)) / 2);
        UnderrailDropdownScroll.Add(page, state);
        if (mod.Loaded && !InGame && tab.Order.Count > 1)
        {
            // Up / Down, left of the status.
            int bx = stateX + 200 - UnderrailDropdownScroll.W(state.Size) - 8;
            var mine = mod;
            foreach (var b in new[] { UnderrailOptions.NewButton(options, "Down", () => Move(tab, mine, 1), true), UnderrailOptions.NewButton(options, "Up", () => Move(tab, mine, -1), true) })
            {
                bx -= UnderrailDropdownScroll.W(b.Size) + 6;
                UnderrailDropdownScroll.Move(b, bx, y + (lineH - UnderrailDropdownScroll.H(b.Size)) / 2);
                UnderrailDropdownScroll.Add(page, b);
            }
        }

        // Then: why it didn't load, the description, what it needs, its settings.
        // A missing requirement shows on the Requires line (in red); other reasons on a line of their own.
        bool unmet = mod.Requires.Any(d => Describe(d, true) != null);
        if (mod.Enabled && mod.Error != null && !(unmet && mod.Error.StartsWith("needs "))) Text(options, page, "   " + Capital(mod.Error), Bad);
        if (mod.Loaded && mod.Warning != null) Text(options, page, "   Note: " + Capital(mod.Warning), Quiet);
        if (mod.Failures > 0) Text(options, page, "   Errors this session: " + mod.Failures + " (last: " + mod.LastFailure + "); see UnderRailWorks.log", Bad);
        var links = new[] { mod.Homepage, mod.License == null ? null : "Licence: " + mod.License, mod.Game == null ? null : "Made for UnderRail " + mod.Game }.Where(l => l != null).ToList();
        if (links.Count > 0) Text(options, page, "   " + string.Join("   ", links), Quiet);
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
        return new KeyValuePair<avo, string>(name, title);
    }

    static string ProfileText(string name) { return "Profile: " + (name ?? "none") + "   (a saved set of mods and their order)"; }

    // Profiles: saved sets of mods and their order (main menu only, as switching mods).
    static void ProfileRow(t1 options, cvh page, Tab tab)
    {
        var current = ModLoader.CurrentProfile;
        tab.ProfileLabel = Text(options, page, ProfileText(current), Quiet);
        int y = UnderrailOptions.RowY(options) + 4, x = UnderrailOptions.Left(options), h = 0;
        Action<string, Action> button = (text, click) =>
        {
            var b = UnderrailOptions.NewButton(options, text, click, true);
            UnderrailDropdownScroll.Move(b, x, y);
            UnderrailDropdownScroll.Add(page, b);
            x += UnderrailDropdownScroll.W(b.Size) + 8; h = Math.Max(h, UnderrailDropdownScroll.H(b.Size));
        };
        button("Save as profile...", () => Ui.Ask(null, "Profile name:", current ?? "My mods", name =>
        {
            var mods = tab.Switches.Select(sw => new KeyValuePair<string, bool>(sw.Key.Id, sw.Value.Checked));
            Notify.Log(ModLoader.SaveProfile(name, mods, tab.Order.Select(m => m.Id).ToList()) ? "Mods: profile \"" + ModLoader.ProfileName(name) + "\" saved." : "Mods: couldn't save that profile.");
        }, "Save", " -_'()"));
        var names = ModLoader.Profiles().Take(8).ToList();     // the message box holds a few buttons
        if (names.Count > 0)
        {
            button("Use profile...", () => Ui.Choose(null, "Use a profile", "Switches mods on and off and sets their order as the profile has them. Applies when you restart the game.",
                i =>
                {
                    if (i >= names.Count || !ModLoader.UseProfile(names[i])) return;
                    var p = ModLoader.ReadProfile(names[i]);
                    if (p != null)
                    {
                        // Show it as it will be: switches, statuses, and the loaded mods' cards in the profile's order.
                        foreach (var sw in tab.Switches)
                        {
                            bool on; if (p.Value.Key.TryGetValue(sw.Key.Id, out on)) sw.Value.Checked = on;
                            avo st; if (tab.States.TryGetValue(sw.Key, out st)) ShowStatus(st, sw.Key, sw.Value.Checked);
                        }
                        var want = p.Value.Value ?? new List<string>();
                        Func<ModInfo, int> rank = m => { int r = want.FindIndex(id => id.Equals(m.Id, StringComparison.OrdinalIgnoreCase)); return r < 0 ? int.MaxValue : r; };
                        for (int target = 0; target < tab.Cards.Count; target++)
                        {
                            int best = target;
                            for (int k = target + 1; k < tab.Cards.Count; k++) if (rank(tab.Cards[k].Mod) < rank(tab.Cards[best].Mod)) best = k;
                            for (int k = best; k > target; k--) Move(tab, tab.Cards[k].Mod, -1);
                        }
                    }
                    if (tab.ProfileLabel != null) tab.ProfileLabel.Text = ProfileText(names[i]);
                    tab.OrderChanged = false;     // the profile's order is written; Accept keeps it
                    Notify.Log("Mods: profile \"" + names[i] + "\" applies when you restart the game.");
                }, names.Concat(new[] { "Cancel" }).ToArray()));
            button("Delete profile...", () => Ui.Choose(null, "Delete a profile", "Delete which profile? (The mods themselves stay.)",
                i => { if (i < names.Count && ModLoader.DeleteProfile(names[i])) Notify.Log("Mods: profile \"" + names[i] + "\" deleted."); }, names.Concat(new[] { "Cancel" }).ToArray()));
        }
        UnderrailOptions.SetRowY(options, y + h + 6);
    }

    // A card's status for the switch as it is now (on: ticked).
    static void ShowStatus(avo state, ModInfo mod, bool on)
    {
        string status; Color colour;
        if (!on) { status = mod.Loaded ? "Off at restart" : "Off"; colour = Quiet; }
        else if (!mod.Loaded && (mod.Error == null || !mod.Enabled)) { status = "On at restart"; colour = Good; }
        else if (mod.Loaded) { status = "Loaded"; colour = Good; }
        else { status = "Not loaded"; colour = Bad; }
        state.Text = status;
        Paint(state, colour);
    }

    // "3. Name ..." on loaded mods' cards; red when a mod is above one it requires (it will load after it anyway).
    static void Number(Tab tab)
    {
        for (int i = 0; i < tab.Cards.Count; i++)
        {
            var c = tab.Cards[i];
            var late = c.Mod.Requires.Select(d => ModLoader.Find(d.Id)).Where(d => d != null && tab.Order.IndexOf(d) > i).ToList();
            c.Title.Text = (i + 1) + ". " + c.TitleText + (late.Count > 0 ? "   (loads after " + string.Join(", ", late.Select(d => d.Name).ToArray()) + ": it needs it)" : "");
            Paint(c.Title, late.Count > 0 ? Bad : Color.White);
        }
    }

    static void Move(Tab tab, ModInfo mod, int by)
    {
        int i = tab.Cards.FindIndex(c => c.Mod == mod), j = i + by;
        if (i < 0 || j < 0 || j >= tab.Cards.Count) return;
        int a = Math.Min(i, j);
        CardParts upper = tab.Cards[a], lower = tab.Cards[a + 1];
        TabScroll.Shift(tab.Page, upper.Controls, lower.Height);
        TabScroll.Shift(tab.Page, lower.Controls, -upper.Height);
        tab.Cards[a] = lower; tab.Cards[a + 1] = upper;
        tab.Order = tab.Cards.Select(c => c.Mod).ToList();
        tab.OrderChanged = true;
        Number(tab);
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
        if (tab.OrderChanged && !InGame) { ModLoader.SetPlayerOrder(tab.Order.Select(m => m.Id).ToList()); tab.OrderChanged = false; }
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
