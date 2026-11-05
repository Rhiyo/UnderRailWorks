// Runtime fixes called from the patched game (see Installer.cs). Every entry point checks UnderRailWorks.ini,
// so a switched-off fix behaves exactly like the stock code it replaced.
// UnderrailDisplayModes adds the monitor's display modes to the game's resolution table (efw). The built-in
// resolutions are kept; extra modes get synthetic DisplayResolution values (ModeKey: width << 16 | height).
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Collections;
using System.Reflection;
using TimelapseVertigo.Common.Settings;
using UnderRailWorks;

namespace UnderRailWorks.Internal
{

public static class UnderrailDisplayModes
{
    const int MinWidth = 1024, MinHeight = 720;     // smallest built-in resolutions are 1024x768 and 1280x720
    static readonly object Gate = new object();
    // A type reference, not a name: the build points it at the game's XNA Graphics assembly, and the 64-bit copy at FNA
    // (a name would load XNA, or under Proton Wine Mono's second FNA, in a 64-bit FNA process).
    internal static readonly Type AdapterType = typeof(Microsoft.Xna.Framework.Graphics.GraphicsAdapter);

    static IEnumerable<KeyValuePair<int, int>> ReadModes()
    {
        Type t = AdapterType;
        object adapter = t.GetProperty("DefaultAdapter", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
        var modes = (IEnumerable)t.GetProperty("SupportedDisplayModes").GetValue(adapter, null);
        foreach (object m in modes)
        {
            Type mt = m.GetType();
            yield return new KeyValuePair<int, int>((int)mt.GetProperty("Width").GetValue(m, null), (int)mt.GetProperty("Height").GetValue(m, null));
        }
    }
    static bool extended;

    // Synthetic DisplayResolution values for sizes the enum doesn't have: width << 16 | height, up to 32767x65535.
    // They only exist in memory (config.dat always gets a built-in value), so the encoding can change freely.
    // Built-in values are all below 1000; the smallest synthetic one is 1024 << 16.
    public const int MaxModeWidth = 32767, MaxModeHeight = 65535;
    public static DisplayResolution ModeKey(int w, int h) { return (DisplayResolution)((w << 16) | h); }

    public static bool TryModeSize(DisplayResolution key, out int w, out int h)
    {
        int v = (int)key;
        w = v >> 16; h = v & 0xFFFF;
        return w >= MinWidth && h >= MinHeight;
    }

    // Replaces egd.ToString. Stock: "W x H (X:Y)", leaving the ratio out when it reduces to more than 20:Y.
    public static string Label(egd e)
    {
        if (FixSettings.ResolutionFix || e.e.X > 20) return string.Format("{0} x {1}", e.a, e.b);
        return string.Format("{0} x {1} ({2}:{3})", e.a, e.b, e.e.X, e.e.Y);
    }

    public static bool TryDesktopSize(out int w, out int h)
    {
        w = h = 0;
        try
        {
            Type t = AdapterType;
            object adapter = t.GetProperty("DefaultAdapter", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
            object mode = t.GetProperty("CurrentDisplayMode").GetValue(adapter, null);
            w = (int)mode.GetType().GetProperty("Width").GetValue(mode, null);
            h = (int)mode.GetType().GetProperty("Height").GetValue(mode, null);
        }
        catch (Exception ex) { Log.Write("could not read the desktop resolution: " + ex.Message); }
        return w > 0 && h > 0;
    }

    // efw's tier rules, copied from its static constructor so new sizes snap to the same UI tiers.
    static void Tiers(int w, int h, out UiSize uiSize, out InterfaceElementSize2 elements)
    {
        uiSize = h > 1200 ? UiSize.Huge : ((h <= 800 || w < 1200) ? UiSize.Small : UiSize.Large);
        if (h > 1440) elements = InterfaceElementSize2.Huge;
        else if (h > 1080) elements = InterfaceElementSize2.ExtraLarge;
        else if (h == 1080) elements = InterfaceElementSize2.Large;
        else elements = (h < 800 || w < 1200) ? InterfaceElementSize2.Small : InterfaceElementSize2.Large;
    }

    static egd Make(DisplayResolution key, int w, int h)
    {
        UiSize uiSize;
        InterfaceElementSize2 elements;
        Tiers(w, h, out uiSize, out elements);
        float zoom = h > 1440 ? 2f : h >= 1080 ? 1.5f : 1f;     // the game's tiers; SetFixedZoom applies ZoomFix
        return new egd(key, w, h, uiSize, uiSize, elements, zoom);
    }

    // The game's own values for each entry, before ScaleForUi/SetFixedZoom change them, so both can be redone when
    // the settings change in the options (Reapply).
    sealed class Stock { public UiSize F, G; public InterfaceElementSize2 C; public float H; }
    static readonly Dictionary<egd, Stock> stock = new Dictionary<egd, Stock>();
    static Dictionary<DisplayResolution, egd> lastTable;

    static Stock Original(egd e)
    {
        Stock o;
        if (!stock.TryGetValue(e, out o)) stock[e] = o = new Stock { F = e.f, G = e.g, C = e.c, H = e.h };
        return o;
    }

    // Options changed: put every entry back to the game's values and adjust them again.
    public static void Reapply()
    {
        if (lastTable == null) return;
        foreach (egd e in lastTable.Values) { Stock o = Original(e); e.f = o.F; e.g = o.G; e.c = o.C; e.h = o.H; }
        scaled.Clear(); zoomed.Clear();
        ScaleForUi(lastTable);
        SetFixedZoom(lastTable);
    }

    // After Reapply: start the current resolution at its (new) fixed zoom, as the game does when it applies one.
    public static void ApplyFixedZoom()
    {
        cnk game = bpo.GetService<cnk>();
        if (game == null || game.aq1() == null) return;
        game.ar1(efw.a((DisplayResolution)ResolutionField.GetValue(game.aq1().g())).h, true);
    }

    // With a UI scale or insets the interface is laid out for its area / scale, so the Interface Size choices (f:
    // Auto's pick, g: the largest allowed) and element sizes (c) follow that virtual screen. Each entry once.
    static readonly HashSet<egd> scaled = new HashSet<egd>();

    static void ScaleForUi(Dictionary<DisplayResolution, egd> table)
    {
        lastTable = table;
        foreach (egd e in table.Values)
        {
            if (!scaled.Add(e)) continue;
            Original(e);
            var area = ScaleRules.Layout(e.a, e.b);     // the interface's share of the screen (insets), and its scale
            if (area.K == 1f && area.Width == e.a && area.Height == e.b) continue;
            UiSize uiSize;
            InterfaceElementSize2 elements;
            Tiers((int)Math.Round(area.Width / area.K), (int)Math.Round(area.Height / area.K), out uiSize, out elements);
            e.f = uiSize; e.g = uiSize; e.c = elements;
        }
    }

    // Zoom, with ZoomFix: the zoom a 1920x1080 screen would need for the same view, on every screen, in percent.
    // So the numbers are the game's own at 1080p (it starts at 150%, Allow Zoom goes 100% to 200%) and mean the
    // same view at any resolution. ZoomDefault is the starting zoom (and Allow Zoom off), ZoomMin/ZoomMax the
    // limits with Allow Zoom on. The shorter side is used, so ultrawide screens keep their extra width and portrait
    // ones their extra height. The world view draws as many tiles as fit at its zoom, so zooming out past the
    // game's limits just draws more of the map.
    // Without ZoomFix: the game's own zoom (1x to 2x, 3x when wider than 1920; fixed zoom by resolution tier).
    // Either way the stored zoom is divided by the UI scale: the world view is a UI control, so the two multiply.
    const double StandardHeight = 1080;

    // The zoom on this screen that matches 1x on a 1080p screen.
    static double Standard(int w, int h) { return Math.Min(w, h) / StandardHeight; }

    public static float MinZoom()
    {
        int w, h;
        Screen(out w, out h);
        double min = FixSettings.ZoomFix ? Standard(w, h) * FixSettings.ZoomMin / 100.0 : 1.0;
        return (float)(min / ScaleRules.Ui(w, h));
    }

    public static float MaxZoom()
    {
        int w, h;
        Screen(out w, out h);
        double max = FixSettings.ZoomFix ? Standard(w, h) * Math.Max(FixSettings.ZoomMin, FixSettings.ZoomMax) / 100.0 : (w > 1920 ? 3.0 : 2.0);
        return (float)(max / ScaleRules.Ui(w, h));
    }

    // One zoom step: SHIFT+wheel (mv, notches x the view's step, which ci3 sets to 0.25) and the zoom buttons (ci3,
    // 0.25 each): 25 points of the label on every screen, as at 1080p.
    public static float ZoomStep(float step)
    {
        int w, h;
        Screen(out w, out h);
        double screen = FixSettings.ZoomFix ? Math.Min(w, h) / StandardHeight : 1.0;
        return (float)(step * screen / ScaleRules.Ui(w, h));
    }

    // The zoom label (b5r shows this * 100 as a percentage): the 1080p-equivalent zoom.
    public static float ShownZoom(float stored)
    {
        int w, h;
        Screen(out w, out h);
        double zoom = stored * ScaleRules.Ui(w, h);
        return (float)(FixSettings.ZoomFix ? zoom / Standard(w, h) : zoom);
    }

    // The fixed zoom (egd.h, used with Allow Zoom off and as the starting zoom): the standard view with ZoomFix,
    // else the game's value for the resolution, divided by the UI scale. Applied once per entry.
    static readonly HashSet<egd> zoomed = new HashSet<egd>();

    static void SetFixedZoom(Dictionary<DisplayResolution, egd> table)
    {
        foreach (egd e in table.Values)
        {
            if (!zoomed.Add(e)) continue;
            Original(e);
            double zoom = FixSettings.ZoomFix ? Standard(e.a, e.b) * FixSettings.ZoomDefault / 100.0 : e.h;
            e.h = (float)(zoom / ScaleRules.Ui(e.a, e.b));
        }
    }

    // The current resolution. Runs every frame: the live settings come from the game's service (ls.a() would re-read config.dat).
    static void Screen(out int w, out int h)
    {
        w = 1920; h = 1080;
        try
        {
            cnk game = bpo.GetService<cnk>();
            if (game == null || game.aq1() == null) return;
            egd e = efw.a((DisplayResolution)ResolutionField.GetValue(game.aq1().g()));     // Get() only when ResolutionFix is on
            w = e.a; h = e.b;
        }
        catch { }
    }

    static readonly FieldInfo ResolutionField = typeof(cnt).GetField("a");     // cnt.a also names a method

    static void Extend(Dictionary<DisplayResolution, egd> table)
    {
        lock (Gate)
        {
            if (extended) return;
            var log = new List<string>();
            log.Add("");
            try
            {
                var have = new HashSet<long>(table.Values.Select(e => ((long)e.a << 32) | (uint)e.b));
                int found = 0;
                foreach (var m in ReadModes())
                {
                    found++;
                    int w = m.Key, h = m.Value;
                    if (w < MinWidth || h < MinHeight || w > MaxModeWidth || h > MaxModeHeight) { log.Add($"skip {w}x{h} (out of range)"); continue; }
                    if (!have.Add(((long)w << 32) | (uint)h)) continue;
                    var key = ModeKey(w, h);
                    table[key] = Make(key, w, h);
                    log.Add($"added {w}x{h}");
                }
                extended = found > 0;     // retry later if the adapter wasn't ready yet
                log[0] = $"{found} display modes reported";
            }
            catch (Exception ex)
            {
                log.Add("failed to read display modes: " + ex);
            }
            foreach (string line in log) Log.Write(line);
        }
    }

    // Replaces efw.a(): every resolution, smallest first (stock: the built-in enum order).
    public static List<egd> List(Dictionary<DisplayResolution, egd> table)
    {
        ScaleForUi(table);
        SetFixedZoom(table);
        if (!FixSettings.ResolutionFix)
            return Enum.GetValues(typeof(DisplayResolution)).Cast<DisplayResolution>().Where(table.ContainsKey).Select(r => table[r]).ToList();
        Extend(table);
        return table.Values.OrderBy(e => e.a).ThenBy(e => e.b).ToList();
    }

    // Replaces efw.a(DisplayResolution): exact match, else the nearest available size instead of a crash.
    public static egd Get(Dictionary<DisplayResolution, egd> table, DisplayResolution key)
    {
        egd e;
        if (scaled.Count == 0) { ScaleForUi(table); SetFixedZoom(table); }
        if (table.TryGetValue(key, out e)) return e;
        if (FixSettings.ResolutionFix)
        {
            Extend(table);
            ScaleForUi(table);
            SetFixedZoom(table);
            if (table.TryGetValue(key, out e)) return e;
        }
        int w, h;
        if (!TryModeSize(key, out w, out h)) { w = 1920; h = 1080; }
        return table.Values.OrderBy(x => Math.Abs(x.a - w) + Math.Abs(x.b - h)).First();
    }
}

// Makes long dropdowns (aw9) fit: after aw9 opens and builds one row (zt) per item, only the rows that
// fit above the first obstacle below the box stay attached (the end of its tab page, a button such as
// Accept/Cancel, or the screen edge), and the game's own scrollbar plus the mouse wheel slide that window.
public static class UnderrailDropdownScroll
{
    const int Margin = 4;
    sealed class State { public List<zt> Rows; public int Offset, Visible, Top, Step; public cs9 Bar; }
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<aw9, State> States = new System.Runtime.CompilerServices.ConditionalWeakTable<aw9, State>();
    static readonly System.Diagnostics.Stopwatch Clock = System.Diagnostics.Stopwatch.StartNew();
    static long lastWheel = -1000;
    // Point is FNA's type behind the XNA facade, so it is only touched through reflection.
    internal static readonly PropertyInfo LocationProp = typeof(euz).GetProperty("Location");
    static readonly MethodInfo AbsoluteMethod = typeof(euz).GetMethod("at", Type.EmptyTypes);
    static readonly FieldInfo ClosedSize = typeof(aw9).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(esj));
    static readonly FieldInfo TextOffset = typeof(aw9).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(int));
    static readonly FieldInfo BarSize = typeof(wj).GetField("bm");     // the themed scrollbar's width, as eis uses it

    // esj's fields share names with its methods, and euz.g has an overload taking Point, so go through reflection.
    static readonly FieldInfo SizeW = typeof(esj).GetField("c"), SizeH = typeof(esj).GetField("d");
    static readonly MethodInfo AddMethod = typeof(euz).GetMethod("aox", new[] { typeof(euz) }), RemoveMethod = typeof(euz).GetMethod("g", new[] { typeof(euz) });
    internal static int W(esj s) { return (int)SizeW.GetValue(s); }
    internal static int H(esj s) { return (int)SizeH.GetValue(s); }
    internal static void Add(euz parent, euz child) { AddMethod.Invoke(parent, new object[] { child }); }
    internal static void Remove(euz parent, euz child) { RemoveMethod.Invoke(parent, new object[] { child }); }
    internal static int X(object point) { return (int)point.GetType().GetField("X").GetValue(point); }
    internal static int Y(object point) { return (int)point.GetType().GetField("Y").GetValue(point); }
    static object At(euz c) { return AbsoluteMethod.Invoke(c, null); }
    internal static object LocationOf(euz c) { return LocationProp.GetValue(c, null); }
    internal static void Move(euz c, int x, int y) { LocationProp.SetValue(c, Activator.CreateInstance(LocationProp.PropertyType, x, y), null); }

    static int ScreenHeight(euz c)
    {
        while (c.Parent != null) c = c.Parent;
        int h = H(c.Size);
        try
        {
            Type t = UnderrailDisplayModes.AdapterType;
            object adapter = t.GetProperty("DefaultAdapter", BindingFlags.Public | BindingFlags.Static).GetValue(null, null);
            object mode = t.GetProperty("CurrentDisplayMode").GetValue(adapter, null);
            int screen = (int)mode.GetType().GetProperty("Height").GetValue(mode, null);
            if (h <= 0 || screen < h) h = screen;
        }
        catch { }
        return h > 0 ? h : 720;
    }

    // Lowest y the open list may reach. The dropdown closes once the mouse leaves its tab page, and
    // buttons laid over the window (Accept/Cancel) would otherwise sit on top of the last rows.
    static int Bottom(aw9 box)
    {
        object at = At(box);
        int left = X(at), right = left + W(box.Size), top = Y(at);
        int bottom = ScreenHeight(box);
        var path = new HashSet<euz>();
        for (euz c = box; c != null; c = c.Parent) path.Add(c);
        for (euz a = box.Parent; a != null; a = a.Parent)
        {
            if (a is cvh || a is dpg) bottom = Math.Min(bottom, Y(At(a)) + H(a.Size));
            foreach (euz c in a.Controls)
            {
                if (path.Contains(c) || !(c is dak) || !c.Visible) continue;
                object cat = At(c);
                int cl = X(cat), ct = Y(cat);
                if (ct > top && cl < right && cl + W(c.Size) > left) bottom = Math.Min(bottom, ct);
            }
        }
        return bottom;
    }

    // Called at the end of aw9's "open" method.
    public static void Opened(aw9 box)
    {
        if (!FixSettings.ResolutionFix && !InFixesTab(box)) return;     // the Fixes tab's own dropdowns always
        try
        {
            var rows = box.Controls.OfType<zt>().ToList();
            if (rows.Count == 0) return;
            ToFront(box);
            esj closed = (esj)ClosedSize.GetValue(box);
            int textOffset = (int)TextOffset.GetValue(box);
            int step = H(closed) - 2, top = H(closed) + 2;
            int bottom = Bottom(box);
            int room = bottom - Y(At(box)) - top - textOffset - Margin;
            int visible = Math.Max(3, room / Math.Max(1, step));
            // Not enough room below: open upwards (the whole open box moves up so it ends where the closed one did; the
            // game always draws the chosen value at the top), if there's more room there. Moved back on close.
            int roomUp = Y(At(box)) + H(closed) - TopLimit(box) - top - textOffset - Margin;
            int visibleUp = Math.Max(3, roomUp / Math.Max(1, step));
            // Decided on the real room (the 3-row minimum is for how many rows a scrolling list shows).
            int fitBelow = Math.Max(0, room / Math.Max(1, step)), fitUp = Math.Max(0, roomUp / Math.Max(1, step));
            bool up = rows.Count > fitBelow && fitUp > fitBelow;
            if (up) visible = visibleUp;
            Log.Write($"dropdown opened: {rows.Count} rows, box y {Y(At(box))}, limit y {bottom}, fits {visible}{(up ? " (upwards)" : "")}");
            if (up)
            {
                // The box moves up by the list's height; its header (the chosen value, a child button at the top) moves
                // to the bottom, where the closed box was, and the rows go above it.
                object location = LocationProp.GetValue(box, null);
                Raised.Remove(box);
                Raised.Add(box, location);
                int open = H(closed) + textOffset + Math.Min(rows.Count, visible) * step;
                Move(box, X(location), Y(location) - (open - H(closed)));
                var header = Header.GetValue(box) as euz;
                if (header != null) Move(header, 0, open - H(closed));
                foreach (var r in rows) { object at = LocationProp.GetValue(r, null); Move(r, X(at), Y(at) - H(closed)); }
                top = 2;
            }
            if (rows.Count <= visible) return;

            wj theme = wj.a();
            int barWidth = theme.c() ? 26 : W((esj)BarSize.GetValue(theme));
            var s = new State { Rows = rows, Visible = visible, Top = top, Step = step };
            int selected = rows.FindIndex(r => Equals(r.Tag, box.l()));
            s.Offset = Clamp(s, selected - visible / 2);
            State old;
            if (States.TryGetValue(box, out old)) { RemoveBar(box, old); States.Remove(box); }
            else box.t(new EventHandler<b25>((sender, e) => Wheel(box, e)));     // once per box; rows are new on every open
            States.Add(box, s);
            foreach (var r in rows)
            {
                Remove(box, r);
                r.Size = new esj(W(r.Size) - barWidth, H(r.Size));
                r.t(new EventHandler<b25>((sender, e) => Wheel(box, e)));
            }
            box.Size = new esj(W(closed), H(closed) + textOffset + visible * step);

            bdl bar = theme.c() ? new bdl() : new dg8();
            bar.IsPrivateComponent = true;
            bar.b(0);
            bar.a(rows.Count - visible);
            bar.c(1);
            bar.d(Math.Max(1, visible - 1));
            bar.e(s.Offset);
            Move(bar, W(closed) - 4 - barWidth, top);
            bar.Size = new esj(barWidth, visible * step);
            e4z.c(bar.d());
            e4z.c(bar.e());
            e4z.d(bar.h());
            bar.f(new EventHandler<c1>((sender, e) => Scrolled(box)));
            bar.t(new EventHandler<b25>((sender, e) => Wheel(box, e)));
            s.Bar = bar;
            Add(box, bar);
            Show(box, s);
        }
        catch (Exception ex)
        {
            Log.Write("dropdown scroll failed: " + ex);
        }
    }

    // An open dropdown drawn over everything around it: last in its parent's list of controls (euz.p, drawn in order,
    // changed under the parent's own lock as euz.aox does).
    static readonly FieldInfo Children = typeof(euz).GetField("p", BindingFlags.Instance | BindingFlags.NonPublic),
        ChildrenLock = typeof(euz).GetField("q", BindingFlags.Instance | BindingFlags.NonPublic);
    static void ToFront(euz c)
    {
        var parent = c.Parent;
        var list = parent == null ? null : Children.GetValue(parent) as List<euz>;
        var gate = parent == null ? null : ChildrenLock.GetValue(parent) as System.Threading.ReaderWriterLock;
        if (list == null || gate == null) return;
        gate.AcquireWriterLock(-1);
        try { if (list.Remove(c)) list.Add(c); }
        finally { gate.ReleaseWriterLock(); }
    }

    // Called at the end of aw9's "close" method, which only removes the rows itself.
    public static void Closed(aw9 box)
    {
        State s;
        if (States.TryGetValue(box, out s)) RemoveBar(box, s);
        object location;
        if (Raised.TryGetValue(box, out location))
        {
            Raised.Remove(box);
            LocationProp.SetValue(box, location, null);
            var header = Header.GetValue(box) as euz;
            if (header != null) Move(header, 0, 0);
        }
    }

    static bool InFixesTab(euz c)
    {
        for (; c != null; c = c.Parent) if (c is cvh && c.Text == UnderrailOptions.TabTitle) return true;
        return false;
    }

    // aw9's header: the button showing the chosen value (its only dak field).
    static readonly FieldInfo Header = typeof(aw9).GetFields(BindingFlags.Instance | BindingFlags.NonPublic).Single(f => f.FieldType == typeof(dak));

    // Dropdowns opened upwards: where they were (a boxed Point).
    static readonly System.Runtime.CompilerServices.ConditionalWeakTable<aw9, object> Raised = new System.Runtime.CompilerServices.ConditionalWeakTable<aw9, object>();

    // Highest y an open list may reach: the top of its tab page (or the screen).
    static int TopLimit(aw9 box)
    {
        int top = 0;
        for (euz a = box.Parent; a != null; a = a.Parent)
            if (a is cvh || a is dpg) top = Math.Max(top, Y(At(a)));
        return top;
    }

    static void RemoveBar(aw9 box, State s)
    {
        if (s.Bar != null && s.Bar.Parent == box) Remove(box, s.Bar);
    }

    static int Clamp(State s, int offset) { return Math.Max(0, Math.Min(s.Rows.Count - s.Visible, offset)); }

    static void Show(aw9 box, State s)
    {
        foreach (var r in s.Rows) if (r.Parent == box) Remove(box, r);
        for (int i = 0; i < s.Visible; i++)
        {
            zt r = s.Rows[s.Offset + i];
            Move(r, 4, s.Top + s.Step * i);
            Add(box, r);
        }
    }

    static bool IsOpen(aw9 box, State s) { return s.Bar != null && s.Bar.Parent == box; }

    static void Scrolled(aw9 box)
    {
        State s;
        if (!States.TryGetValue(box, out s) || !IsOpen(box, s)) return;
        int offset = Clamp(s, s.Bar.l());
        if (offset == s.Offset) return;
        s.Offset = offset;
        Show(box, s);
    }

    static void Wheel(aw9 box, b25 e)
    {
        State s;
        if (e.c() == 0 || !States.TryGetValue(box, out s) || !IsOpen(box, s)) return;
        long now = Clock.ElapsedMilliseconds;
        if (now - lastWheel < 4) return;     // a row and the box both report the same notch
        lastWheel = now;
        s.Bar.e(Clamp(s, s.Offset - Math.Sign(e.c())));     // moves the thumb; Scrolled updates the rows
    }
}

// The resolution field of the game's video settings (cnt.a) passes through here on its way in and out of config.dat.
// ResolutionFix=1: the game runs at UnderRailWorks.ini's Width/Height, menu changes go to the ini, and config.dat keeps
// whatever it already held. ResolutionFix=0: config.dat as usual. Either way config.dat only ever gets values the
// unmodified game knows, so turning the fix off or removing it never leaves an unreadable config behind.
public static class ResolutionStore
{
    static readonly Dictionary<DisplayResolution, KeyValuePair<int, int>> Stock = Enum.GetValues(typeof(DisplayResolution)).Cast<DisplayResolution>()
        .ToDictionary(r => r, r => { string[] p = r.ToString().Substring(1).Split('x'); return new KeyValuePair<int, int>(int.Parse(p[0]), int.Parse(p[1])); });
    static DisplayResolution configValue, running;
    static bool haveConfig, haveRunning;
    static int desktopW, desktopH;

    static bool Size(DisplayResolution key, out int w, out int h)
    {
        KeyValuePair<int, int> s;
        int v = (int)key;
        if (Stock.TryGetValue(key, out s)) { w = s.Key; h = s.Value; return true; }
        if (v == 205) { w = 3840; h = 1080; return true; }     // values an earlier exe-patching build of this fix stored
        if (v == 210) { w = 5120; h = 1440; return true; }
        return UnderrailDisplayModes.TryModeSize(key, out w, out h);
    }

    static DisplayResolution Key(int w, int h)
    {
        foreach (var s in Stock) if (s.Value.Key == w && s.Value.Value == h) return s.Key;
        return UnderrailDisplayModes.ModeKey(w, h);
    }

    static DisplayResolution NearestStock(DisplayResolution key)
    {
        int w, h;
        if (Stock.ContainsKey(key) || !Size(key, out w, out h)) return Stock.ContainsKey(key) ? key : DisplayResolution.r1920x1080;
        // The largest built-in size that fits inside it (5120x1440 -> 3440x1440), else simply the closest one.
        var fits = Stock.Where(s => s.Value.Key <= w && s.Value.Value <= h).OrderByDescending(s => (long)s.Value.Key * s.Value.Value).ToList();
        if (fits.Count > 0) return fits[0].Key;
        return Stock.OrderBy(s => Math.Abs(s.Value.Key - w) + Math.Abs(s.Value.Value - h)).First().Key;
    }

    static DisplayResolution Wanted()
    {
        if (haveRunning) return running;
        UnderrailDisplayModes.TryDesktopSize(out desktopW, out desktopH);
        int w = FixSettings.Width ?? desktopW, h = FixSettings.Height ?? desktopH;
        running = w > 0 && h > 0 ? Key(w, h) : DisplayResolution.r1920x1080;
        haveRunning = true;
        Log.Write($"resolution Width={(FixSettings.Width.HasValue ? w.ToString() : "native")} Height={(FixSettings.Height.HasValue ? h.ToString() : "native")} -> {w}x{h} (desktop {desktopW}x{desktopH})");
        return running;
    }

    // Every cnt constructor's default value (also used when settings are copied, so it must not touch configValue).
    public static DisplayResolution Default(DisplayResolution value)
    {
        return FixSettings.ResolutionFix ? Wanted() : value;
    }

    // cnt's deserializing constructor: VS:R as stored in config.dat.
    public static DisplayResolution Load(DisplayResolution stored)
    {
        configValue = NearestStock(stored);
        haveConfig = true;
        if (!FixSettings.ResolutionFix) return configValue;
        Log.Write($"config.dat has {stored}; it keeps {configValue}");
        return Wanted();
    }

    // cnt.GetObjectData: the value written to config.dat.
    public static DisplayResolution Save(DisplayResolution current)
    {
        if (!FixSettings.ResolutionFix) return NearestStock(current);
        Wanted();
        if (current != running)
        {
            running = current;
            int w, h;
            if (Size(current, out w, out h))
            {
                // The desktop's own width or height is stored as "native", so it keeps following the desktop.
                FixSettings.SetSize(w == desktopW ? (int?)null : w, h == desktopH ? (int?)null : h);
                Log.Write($"resolution changed in game -> {w}x{h}");
            }
        }
        return haveConfig ? configValue : NearestStock(current);     // no config.dat yet: the closest built-in size
    }
}
}
