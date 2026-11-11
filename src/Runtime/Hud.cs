// UnderRailWorks: the game's HUD, windows, trade, prices and status effects for mods, in Works' own types. What the
// game does, measured from its code (UnderRailWorks.md, "HUD, windows, trade, effects"):
// - The in-game screen (ci3) builds its HUD parts once, in its constructor (ci3.c()); a resolution or interface size
//   change builds a new ci3. So a mod moves or hides parts on UiEvents.HudCreated, every time. The HUD toggle resets
//   every part's visibility: Hud.VisibilityChanged is where hidden parts get hidden again.
// - Windows are opened and closed through the screen service (ps: alv inventory, alx notes, ...), as the HUD's
//   buttons and hotkeys do; the game skips them while something modal runs (ps.alg()).
// - Trade: ps.amw(player, merchant) opens barter with a character that has the merchant aspect (dvp). The buy markup
//   is dvp.aqd(buyer), the sell factor b6o.d(): both computed when the barter window opens.
// - Status effects (els) live in the character's br5; durations in ms, a turn is 5000 ms, 0 = permanent. Their types
//   are obfuscated and there is no registry by code: Works builds one from the types it can construct.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Point = Microsoft.Xna.Framework.Point;
using Rectangle = Microsoft.Xna.Framework.Rectangle;
using Color = Microsoft.Xna.Framework.Color;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public enum HudPart
{
    PlayerPortrait, TargetPortrait, SystemBar, ZoneSecurity, ActionBar, ExperienceBar, AmmoBar, Feed, Alert,
    CombatPanel, SpeedIndicator, ZoomIndicator, SlowDownButton, SpeedUpButton, ZoomOutButton, ZoomInButton,
}

// Which edge or corner (or the centre) of the HUD a part keeps its distance from when the HUD changes size.
public enum HudAnchor { TopLeft, Top, TopRight, Left, Center, Right, BottomLeft, Bottom, BottomRight }

public static class Hud
{
    // Where the game itself anchors each of its parts (ci3's layout: the portrait at 10,10, the system bar 10 from the
    // top right, the action bar centred 10 above the bottom, the feed at the left above the XP bar...).
    public static HudAnchor AnchorOf(HudPart part)
    {
        switch (part)
        {
            case HudPart.PlayerPortrait: case HudPart.TargetPortrait: return HudAnchor.TopLeft;
            case HudPart.SystemBar: case HudPart.ZoneSecurity: case HudPart.Alert: return HudAnchor.TopRight;
            case HudPart.ActionBar: case HudPart.ExperienceBar: case HudPart.AmmoBar: case HudPart.SpeedIndicator: return HudAnchor.Bottom;
            case HudPart.CombatPanel: return HudAnchor.BottomRight;
            case HudPart.ZoomIndicator: return HudAnchor.Center;
            default: return HudAnchor.BottomLeft;     // Feed, the speed and zoom buttons
        }
    }

    // Where element would sit at offset (dx, dy) from anchor in the HUD: dx/dy count inwards from the anchored
    // edges (from the centre, rightwards/downwards, for Top/Bottom/Left/Right/Center's free axis).
    public static Microsoft.Xna.Framework.Point At(HudAnchor anchor, UiElement element, int dx, int dy)
    {
        var root = Root;
        int w = root == null ? 0 : root.Width, h = root == null ? 0 : root.Height;
        int col = (int)anchor % 3, row = (int)anchor / 3;
        int x = col == 0 ? dx : col == 2 ? w - element.Width - dx : (w - element.Width) / 2 + dx;
        int y = row == 0 ? dy : row == 2 ? h - element.Height - dy : (h - element.Height) / 2 + dy;
        return new Microsoft.Xna.Framework.Point(x, y);
    }

    // ci3's fields per part (ci3.c(), in this order).
    static readonly string[] Fields = { "m_g", "m_h", "m_i", "m_p", "m_j", "m_k", "m_l", "m_m", "m_n", "m_o", "m_q", "m_r", "m_v", "m_u", "w", "x" };
    // (The decompiler shows a field as m_x when a method is also named x; the real field is x.)
    static readonly FieldInfo[] PartFields = Fields.Select(f => typeof(ci3).GetField(f, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? typeof(ci3).GetField(f.StartsWith("m_") ? f.Substring(2) : f, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).ToArray();
    internal static ci3 Current;

    // There is an in-game screen (a game is loaded).
    public static bool Exists { get { return Current != null; } }
    // Runs build now if the in-game screen exists, and every time the game builds it anew (loads, resolution changes):
    // UiEvents.HudCreated alone misses the HUD that is already there when a mod subscribes late (after Load).
    public static void WhenReady(WorksMod mod, Action<UiElement> build)
    {
        UiEvents.HudCreated.Add(mod, build);
        var root = Root;
        // (A reload runs HudCreated for the mod afterwards.)
        if (root != null && ModScope.Reloading != mod.Id) try { build(root); } catch (Exception ex) { ModErrors.Report(mod.Id, "Hud.WhenReady", ex); }
    }

    // The in-game screen: mods add their own elements to it (they draw above the HUD).
    public static UiElement Root { get { return UiElement.Of(Current); } }

    // A part of the game's HUD (null when the game didn't make it: zoom buttons are optional). Move it or hide it
    // with Ui.Move / Ui.Show, from UiEvents.HudCreated (the game builds the HUD anew on loads and resolution changes).
    // Resizing isn't supported: the parts are drawn at fixed sizes.
    public static UiElement Get(HudPart part)
    {
        var f = PartFields[(int)part];
        return Current == null || f == null ? null : UiElement.Of(f.GetValue(Current) as euz);
    }

    // The whole HUD shown (the game's HUD toggle).
    public static bool Visible
    {
        get { return Current != null && Current.r(); }
        set { var screen = bpo.GetService<ps>(); if (screen != null && Current != null && Current.r() != value) screen.amj(); }
    }

    // After the game showed or hid the HUD (its toggle sets every part's visibility): re-hide parts a mod hides.
    public static readonly Hook<Action<bool>> VisibilityChanged = new Hook<Action<bool>>("HudVisibilityChanged");

    // --- Parts mods add: listed with the game's, so layout tools can move them too ---------------------------------
    static readonly List<HudModPart> modParts = new List<HudModPart>();
    // Reloading a mod: its parts come off the HUD (the new code adds them again).
    internal static void Forget(string mod)
    {
        foreach (var p in modParts.Where(p => p.Mod == mod).ToList()) { Ui.Remove(p.Element); modParts.Remove(p); }
    }

    // Adds a mod's element to the HUD as a named part (e.g. "Minimap") at its default place, and announces it
    // (PartAdded), so a layout mod can move it where the player put it. Call it again when the element is rebuilt.
    public static void AddPart(WorksMod mod, string name, UiElement element, int x, int y) { AddPart(mod, name, element, null, x, y); }
    // Anchored: (dx, dy) from anchor (see At), so it sits right on any HUD size and layout mods know its anchor.
    public static void AddPart(WorksMod mod, string name, UiElement element, HudAnchor anchor, int dx, int dy)
    {
        var at = At(anchor, element, dx, dy);
        AddPart(mod, name, element, (HudAnchor?)anchor, at.X, at.Y);
    }
    static void AddPart(WorksMod mod, string name, UiElement element, HudAnchor? anchor, int x, int y)
    {
        if (Current == null || element == null) return;
        var part = new HudModPart { Name = name, Mod = mod == null ? "" : mod.Id, Element = element, DefaultX = x, DefaultY = y, Anchor = anchor };
        modParts.RemoveAll(p => p.Key == part.Key || p.Element.Control.Parent == null);
        if (anchor != null)
        {
            // Keeps its place in the stack when rebuilt (first-add order), with the position it asks for now.
            int at = asked.FindIndex(a => a.Key == part.Key);
            var entry = new KeyValuePair<string, Point>(part.Key, new Point(x, y));
            if (at >= 0) asked[at] = entry; else asked.Add(entry);
        }
        Ui.Add(Root, element, x, y);
        part.AutoX = x; part.AutoY = y;
        modParts.Add(part);
        if (anchor != null) Flow(anchor.Value);
        if (PartAdded.Any) PartAdded.Run(h => h(part));
    }

    // --- Stacking: anchored mod parts sharing an anchor don't cover each other ------------------------------------
    // In the order they were first added (kept across HUD rebuilds, so a part rebuilt with new text keeps its place in
    // the stack), each visible part goes where it asked, or just past the earlier ones it would overlap (downwards for
    // top and middle anchors, upwards for bottom ones). A part the player moved (HUD Layout) isn't moved, but is still
    // in the way where it is.
    static readonly List<KeyValuePair<string, Point>> asked = new List<KeyValuePair<string, Point>>();     // first-add order, the position each asked for
    static string flowState;
    const int StackGap = 6;

    // The game's own parts are in the way too: their sizes change with the interface scale (the game has a layout per
    // size tier), so an offset that clears them at one resolution can cover them at another. Hidden ones count (the
    // combat panel, the target portrait come and go: a part shouldn't jump when they do).
    static List<Rectangle> GameObstacles()
    {
        var list = new List<Rectangle>();
        var sb = new System.Text.StringBuilder();
        foreach (HudPart part in Enum.GetValues(typeof(HudPart)))
        {
            var e = Get(part);
            if (e == null || !e.Visible) continue;
            var r = Content(e.Control, e.X, e.Y);
            if (r.Width <= 0 || r.Height <= 0) continue;
            list.Add(r);
            sb.Append(part + "=" + r.X + "," + r.Y + " " + r.Width + "x" + r.Height + "; ");
        }
        var text = sb.ToString();
        if (text != lastObstacles && FixSettings.Raw("HudDebug") == "1") { lastObstacles = text; Log.Write("hud: drawn parts " + (Root == null ? "" : Root.Width + "x" + Root.Height + ": ") + text); }
        return list;
    }

    // What a control actually shows: the union of its visible children (recursively), or itself if it has none. The
    // game's part controls are much larger than what they draw (the portrait's is 737x474 at 3584x1440, the combat
    // panel's a strip down the whole right edge).
    static Rectangle Content(euz c, int x, int y)
    {
        var kids = c.Controls;
        if (kids == null || kids.Count == 0) return new Rectangle(x, y, UnderrailDropdownScroll.W(c.Size), UnderrailDropdownScroll.H(c.Size));
        Rectangle? all = null;
        foreach (euz k in kids)
        {
            if (!k.Visible) continue;
            var at = UnderrailDropdownScroll.LocationOf(k);
            var r = Content(k, x + UnderrailDropdownScroll.X(at), y + UnderrailDropdownScroll.Y(at));
            if (r.Width <= 0 || r.Height <= 0) continue;
            all = all == null ? r : Rectangle.Union(all.Value, r);
        }
        return all ?? new Rectangle(x, y, UnderrailDropdownScroll.W(c.Size), UnderrailDropdownScroll.H(c.Size));     // draws itself (its children are hidden)
    }
    static string lastObstacles;

    static void Flow(HudAnchor anchor)
    {
        var parts = modParts.Where(p => p.Anchor == anchor && p.Element.Control.Parent != null)
            .OrderBy(p => asked.FindIndex(a => a.Key == p.Key)).ToList();
        var root = Root;
        int rw = root == null ? 0 : root.Width, rh = root == null ? 0 : root.Height;
        int col = (int)anchor % 3;
        bool up = (int)anchor / 3 == 2;
        var placed = GameObstacles();
        // Other anchors' mod parts where they are now.
        foreach (var other in modParts.Where(p => p.Anchor != anchor && p.Element.Control.Parent != null && p.Element.Visible))
            placed.Add(new Rectangle(other.Element.X, other.Element.Y, other.Element.Width, other.Element.Height));
        foreach (var p in parts)
        {
            var e = p.Element;
            bool moved = e.X != p.AutoX || e.Y != p.AutoY;     // the player put it somewhere
            if (!e.Visible) continue;
            if (moved) { placed.Add(new Rectangle(e.X, e.Y, e.Width, e.Height)); continue; }
            var a = asked.FirstOrDefault(k => k.Key == p.Key).Value;
            var r = Clear(new Rectangle(a.X, a.Y, e.Width, e.Height), placed, up, col, rw, rh);
            if (r.X != e.X || r.Y != e.Y) Ui.Move(e, r.X, r.Y);
            p.AutoX = r.X; p.AutoY = r.Y; p.DefaultX = r.X; p.DefaultY = r.Y;
            placed.Add(r);
        }
    }

    // Out of whatever it overlaps, one obstacle at a time: past it along the anchored axis (down for top/middle
    // anchors, up for bottom ones) or sideways (away from the anchored side; either way for centred ones), whichever
    // moves it least and stays on screen. E.g. a top-right part goes under the system bar, then left of the combat
    // panel's strip down the right edge.
    static Rectangle Clear(Rectangle want, List<Rectangle> taken, bool up, int col, int rw, int rh)
    {
        var r = want;
        for (int guard = 0; guard < 40; guard++)
        {
            var hits = taken.Where(q => q.Intersects(r)).ToList();
            if (hits.Count == 0) return r;
            var h = hits.OrderByDescending(q => Rectangle.Intersect(q, r).Width * Rectangle.Intersect(q, r).Height).First();
            var options = new List<Rectangle>();
            var v = r; v.Y = up ? h.Top - StackGap - r.Height : h.Bottom + StackGap; options.Add(v);
            if (col != 0) { var left = r; left.X = h.Left - StackGap - r.Width; options.Add(left); }
            if (col != 2) { var right = r; right.X = h.Right + StackGap; options.Add(right); }
            var fit = options.Where(o => rw <= 0 || (o.X >= 0 && o.Right <= rw && o.Y >= 0 && o.Bottom <= rh))
                .OrderBy(o => Math.Abs(o.X - r.X) + Math.Abs(o.Y - r.Y)).ToList();
            if (fit.Count == 0) return want;     // nowhere clear: where it asked
            r = fit[0];
        }
        return taken.Any(q => q.Intersects(r)) ? want : r;
    }

    // Each frame: re-stack when a part was shown, hidden or resized.
    internal static void FlowTick()
    {
        if (Current == null || modParts.Count == 0) return;
        var state = string.Join("|", modParts.Where(p => p.Anchor != null).Select(p => p.Key + (p.Element.Visible ? "+" : "-") + p.Element.Width + "x" + p.Element.Height))
            + "#" + string.Join("|", ((HudPart[])Enum.GetValues(typeof(HudPart))).Select(h => { var e = Get(h); return e == null ? "" : (e.Visible ? "+" : "-") + e.X + "," + e.Y; }));     // the game's parts shown, hidden or laid out
        if (state == flowState) return;
        flowState = state;
        foreach (var a in modParts.Where(p => p.Anchor != null).Select(p => p.Anchor.Value).Distinct().ToList()) Flow(a);
    }

    // The mod parts on the HUD now.
    public static IList<HudModPart> ModParts { get { modParts.RemoveAll(p => p.Element.Control.Parent == null); return modParts.ToList(); } }

    public static readonly Hook<Action<HudModPart>> PartAdded = new Hook<Action<HudModPart>>("HudPartAdded");

    internal static void Created(ci3 screen) { UiWindows.HudGone(); Current = screen; modParts.Clear(); tracked.Clear(); }
    internal static void Gone() { UiWindows.HudGone(); Current = null; modParts.Clear(); tracked.Clear(); }

    // --- Mod elements on the HUD hide with the game's HUD --------------------------------------------------------
    // The game hides its HUD (ci3.a(false): dka.j() while the game menu, Options or the character sheet is open, or the
    // HUD toggle is off) part by part; every element a mod puts directly on the HUD (Ui.Add to the root, AddPart) is
    // tracked and hidden with it, and shown again as the mod last wanted it (a mod's own show/hide while hidden is
    // remembered, not applied).
    static readonly Dictionary<UiElement, bool> tracked = new Dictionary<UiElement, bool>();     // element -> visible as the mod wants
    internal static bool Hidden { get { return Current != null && !Current.r(); } }

    internal static void Track(UiElement element)
    {
        if (tracked.ContainsKey(element)) return;
        tracked[element] = element.Control.Visible;
        if (Hidden) element.Control.Visible = false;
    }

    // UiElement.Visible's setter: a tracked element while the HUD is hidden keeps the wish for later.
    internal static bool Defer(UiElement element, bool visible)
    {
        if (!tracked.ContainsKey(element)) return false;
        tracked[element] = visible;
        return Hidden;
    }

    internal static void Toggled(bool shown)
    {
        foreach (var e in tracked.Keys.ToList())
        {
            if (e.Control.Parent == null) { tracked.Remove(e); continue; }     // removed by its mod
            e.Control.Visible = shown && tracked[e];
        }
        if (VisibilityChanged.Any) VisibilityChanged.Run(h => h(shown));
    }
}

public sealed class HudModPart
{
    public string Name { get; internal set; }       // e.g. "Minimap"
    public string Mod { get; internal set; }        // the mod's id
    // Unique across mods ("Minimap/Minimap", "BountyHunter/Abilities"): two mods may use the same part name.
    public string Key { get { return Mod + "/" + Name; } }
    public UiElement Element { get; internal set; }
    public int DefaultX { get; internal set; }      // where the mod put it
    public int DefaultY { get; internal set; }
    // The anchor the mod gave (null: added at a pixel position).
    public HudAnchor? Anchor { get; internal set; }
    internal int AutoX, AutoY;     // where Works last put it (stacking): elsewhere means the player moved it
}

// Lines of text for a HudText (each with its colour).
public sealed class HudLines
{
    internal readonly List<KeyValuePair<string, Color>> Items = new List<KeyValuePair<string, Color>>();
    public static readonly Color Body = new Color(215, 215, 205);
    public HudLines Add(string text) { return Add(text, Body); }
    public HudLines Add(string text, Color colour) { if (text != null) Items.Add(new KeyValuePair<string, Color>(text, colour)); return this; }
    public int Count { get { return Items.Count; } }
}

// A HUD part showing lines of text (a status readout, a tracker): create it once in Load, then Show the current lines
// whenever they may have changed. It is rebuilt only when the text or colours change, removed while there are no
// lines, and comes back by itself after the game rebuilds its HUD (loads, resolution changes). Players can move it
// like any HUD part.
//   board = HudText.Add(this, "Status Board", HudAnchor.Top, 0, 10);
//   board.Show(new HudLines().Add("Health " + hp, Color.Red).Add("AP " + ap));
public sealed class HudText
{
    readonly WorksMod mod; readonly string name; readonly HudAnchor anchor; readonly int dx, dy;
    UiPanel panel; string shown; HudLines last;
    public int MinWidth { get; set; } = 160;
    public Color Background { get; set; } = new Color(8, 8, 8) * 0.8f;
    public Color? Border { get; set; }
    // A line's height: the mod text font's (Ui.LineHeight), so it follows the interface size.
    public static int LineHeight { get { return Ui.LineHeight; } }

    HudText(WorksMod mod, string name, HudAnchor anchor, int dx, int dy) { this.mod = mod; this.name = name; this.anchor = anchor; this.dx = dx; this.dy = dy; }

    public static HudText Add(WorksMod mod, string name, HudAnchor anchor, int dx, int dy)
    {
        var t = new HudText(mod, name, anchor, dx, dy);
        UiEvents.HudCreated.Add(mod, root => { t.panel = null; t.shown = null; if (t.last != null) t.Show(t.last); });
        UiEvents.HudDestroyed.Add(mod, () => { t.panel = null; t.shown = null; });
        return t;
    }

    public bool Visible { get { return panel != null; } }

    public void Show(HudLines lines)
    {
        last = lines;
        if (!Hud.Exists) return;
        var items = lines == null ? new List<KeyValuePair<string, Color>>() : lines.Items;
        var key = string.Join("\n", items.Select(l => l.Key + "\u0001" + l.Value.PackedValue));
        if (key == shown) return;
        shown = key;
        if (panel != null) Ui.Remove(panel);
        panel = null;
        if (items.Count == 0) return;
        int w = Math.Max(Ui.S(MinWidth), items.Max(l => Ui.TextWidth(l.Key)) + 24), h = 10 + items.Count * LineHeight;
        panel = Ui.Panel(w, h);
        panel.Background = Background;
        if (Border.HasValue) panel.Border = Border.Value;
        for (int i = 0; i < items.Count; i++)
        {
            var label = Ui.Label(items[i].Key, w - 16);
            Ui.SetColor(label, items[i].Value);
            Ui.Add(panel, label, 8, 4 + i * LineHeight);
        }
        Hud.AddPart(mod, name, panel, anchor, dx, dy);
    }

    public void Hide() { Show(null); }
}

public enum Window { Inventory, CharacterSheet, CombatStats, Crafting, Notes, Oddities, Psi, Map, QuickInvoker, GameMenu, Barter, Container }

// An open window of the game with a title bar (a9t: the title, the close button at the right): inventory, notes...
public sealed class OpenWindow
{
    public Window Kind { get; internal set; }
    public UiElement Element { get; internal set; }
    // The title bar's height (interface units): the strip at the top, the close button at its right end.
    public int TitleHeight { get; internal set; }
    // Opening a window closes the others on its side of the screen (inventory, map, psi on the left; crafting, notes,
    // oddities, combat stats on the right). While true, this one stays open then; it still closes with its own button
    // or key. Until the window closes (each opening is a new window).
    public bool StaysOpen
    {
        get { object x; return WindowSideHooks.Kept.TryGetValue(Element.Control, out x); }
        set { WindowSideHooks.Kept.Remove(Element.Control); if (value) WindowSideHooks.Kept.Add(Element.Control, null); }
    }
}

public static class Windows
{
    // ps open / close methods per window (the HUD's buttons and hotkeys call these).
    static readonly string[,] Methods = { { "alv", "alw" }, { "al3", null }, { "al4", "al5" }, { "al6", "al7" }, { "alx", "aly" }, { "alz", "al0" }, { "al1", "al2" }, { "al8", "al9" }, { "als", "alt" }, { "alq", "alr" } };
    static readonly MethodInfo CharacterSheetClose = typeof(dka).GetMethod("z", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

    // The game's titled windows open now, in drawing order (the last one is on top). Each opening builds a new window
    // (a child of the in-game screen) at the game's place for it.
    public static IList<OpenWindow> OpenWindows
    {
        get
        {
            var list = new List<OpenWindow>();
            if (Hud.Current == null) return list;
            foreach (var c in Hud.Current.Controls.Cast<euz>())
            {
                var w = c as a9t;
                Window kind;
                if (w == null || !c.Visible || !Kinds.TryGetValue(c.GetType(), out kind)) continue;
                list.Add(new OpenWindow { Kind = kind, Element = UiElement.Of(c), TitleHeight = (int)TitleHeightField.GetValue(w) });
            }
            return list;
        }
    }
    // The window classes (the factories in ci3) by kind.
    static readonly Dictionary<Type, Window> Kinds = new Dictionary<Type, Window>
    {
        { typeof(evh), Window.Inventory }, { typeof(dhn), Window.Notes }, { typeof(eed), Window.Crafting }, { typeof(ckj), Window.Oddities },
        { typeof(ax3), Window.Psi }, { typeof(a5r), Window.Map }, { typeof(dm1), Window.CombatStats }, { typeof(b6o), Window.Barter },
        { typeof(c15), Window.Container },
    };
    static readonly FieldInfo TitleHeightField = typeof(a9t).GetField("i", BindingFlags.Instance | BindingFlags.Public);     // a9t.i: also a method name

    // Opens a window as its HUD button would; false while the game is busy with something modal, or without a game.
    public static bool Open(Window w)
    {
        var screen = bpo.GetService<ps>();
        if (screen == null || !Hud.Exists || screen.alg() || (int)w >= Methods.GetLength(0)) return false;     // barter, containers: opened by their own actions
        Call(screen, Methods[(int)w, 0]);
        return true;
    }

    public static void Close(Window w)
    {
        var screen = bpo.GetService<ps>();
        if (screen == null || !Hud.Exists) return;
        if (w == Window.CharacterSheet) { if (CharacterSheetClose != null) CharacterSheetClose.Invoke(screen, null); return; }
        if ((int)w >= Methods.GetLength(0)) return;
        Call(screen, Methods[(int)w, 1]);
    }

    // Closes the open Options window as its Accept (save: true) or Cancel button would. False if none is open.
    public static bool CloseOptions(bool save = true)
    {
        var options = UiHooks.CurrentOptions;
        if (options == null || options.Parent == null) return false;
        (save ? OptionsAccept : OptionsCancel).Invoke(options, new object[] { null, null });
        return true;
    }
    static readonly MethodInfo OptionsAccept = typeof(t1).GetMethod("a", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(object), typeof(b25) }, null),
        OptionsCancel = typeof(t1).GetMethod("b", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(object), typeof(b25) }, null);

    static void Call(ps screen, string name) { typeof(ps).GetMethod(name, Type.EmptyTypes).Invoke(screen, null); }
}

// dka.c() / dka.d(): the game closing the windows on the left / right before opening one there (the per-window close
// methods they call are one-liners the JIT inlines, so they can't be hooked). While they run, the controller of a
// window that StaysOpen (LocaleSubInterfaceController: m_b its state, y5.c = open; c its window) reads as closed, so
// its close does nothing; the state is put back afterwards.
static class WindowSideHooks
{
    internal static readonly System.Runtime.CompilerServices.ConditionalWeakTable<euz, object> Kept = new System.Runtime.CompilerServices.ConditionalWeakTable<euz, object>();
    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly Type ControllerType = typeof(TimelapseVertigo.Game.Controllers.LocaleSubInterfaceController<,>);
    // dka's window controllers, and their state / window fields (declared on the generic base, per instantiation).
    static readonly List<FieldInfo> Controllers = typeof(dka).GetFields(Inst).Where(f => Controller(f.FieldType) != null).ToList();
    static readonly Dictionary<Type, FieldInfo[]> StateAndWindow = new Dictionary<Type, FieldInfo[]>();

    static Type Controller(Type t)
    {
        for (; t != null; t = t.BaseType) if (t.IsGenericType && t.GetGenericTypeDefinition() == ControllerType) return t;
        return null;
    }
    static FieldInfo[] Fields(Type t)
    {
        FieldInfo[] f;
        if (!StateAndWindow.TryGetValue(t, out f))
        {
            var b = Controller(t);
            const BindingFlags d = Inst | BindingFlags.DeclaredOnly;
            f = StateAndWindow[t] = new[] { b.GetField("m_b", d) ?? b.GetField("b", d), b.GetField("c", d) };
        }
        return f;
    }

    // Other callers of c() / d() close everything as the game does: the HUD rebuild (anc) and the five others (dialogs
    // and full-screen interactions; their signatures below).
    static int closingAll;
    static void ClosingAll() { closingAll++; }
    static Exception ClosedAll(Exception __exception) { closingAll--; return __exception; }

    static void Clearing(dka __instance, ref List<object> __state)
    {
        __state = null;
        if (closingAll > 0) return;
        try
        {
            foreach (var cf in Controllers)
            {
                var controller = cf.GetValue(__instance);
                if (controller == null) continue;
                var f = Fields(controller.GetType());
                var window = f[1].GetValue(controller) as euz;
                object x;
                if (window == null || !Kept.TryGetValue(window, out x) || (y5)f[0].GetValue(controller) != y5.c) continue;
                f[0].SetValue(controller, y5.a);
                (__state ?? (__state = new List<object>())).Add(controller);
            }
        }
        catch (Exception ex) { Log.Write("windows: " + ex.Message); }
    }

    static Exception Cleared(List<object> __state, Exception __exception)
    {
        if (__state != null) foreach (var c in __state) try { Fields(c.GetType())[0].SetValue(c, y5.c); } catch (Exception ex) { Log.Write("windows: " + ex.Message); }
        return __exception;
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(WindowSideHooks).GetMethod(n, BindingFlags.Static | BindingFlags.NonPublic));
        foreach (var side in new[] { "c", "d" })
            harmony.Patch(typeof(dka).GetMethod(side, Inst, null, Type.EmptyTypes, null), prefix: m("Clearing"), finalizer: m("Cleared"));
        var all = new[] {
            typeof(dka).GetMethod("anc", Inst, null, Type.EmptyTypes, null),
            typeof(dka).GetMethod("am3", Inst, null, new[] { typeof(string) }, null),
            typeof(dka).GetMethod("am8", Inst, null, new[] { typeof(b0b), typeof(object), typeof(csy) }, null),
            typeof(dka).GetMethod("a", Inst, null, new[] { typeof(c5x), typeof(c7j) }, null),
            typeof(dka).GetMethod("a", Inst, null, new[] { typeof(c5x), typeof(wv) }, null),
            typeof(dka).GetMethod("a", Inst, null, new[] { typeof(c5x), typeof(object), typeof(b14), typeof(int), typeof(Microsoft.Xna.Framework.Point?), typeof(string) }, null) };
        foreach (var x in all)
            if (x == null) Log.Write("windows: a close-everything method is missing"); else harmony.Patch(x, prefix: m("ClosingAll"), finalizer: m("ClosedAll"));
        Log.Write("windows: " + Controllers.Count + " window controllers");
    }
}

public static partial class Trade
{
    // Opens barter between the player and a merchant (a character with a store); false otherwise.
    public static bool Open(Character merchant)
    {
        var screen = bpo.GetService<ps>();
        var player = World.Player;
        if (screen == null || player == null || merchant == null || !merchant.IsMerchant) return false;
        return screen.amw(player.Entity, merchant.Entity);
    }
}

public static class Prices
{
    // Change what merchants charge: each handler gets the merchant and the markup so far (the game's: about 1.15-3.5,
    // lower with Mercantile) and returns the new one. Read when the barter window opens.
    public static readonly Hook<Func<Character, float, float>> BuyMarkup = new Hook<Func<Character, float, float>>("BuyMarkup");
    // Change what the player gets for selling: the factor so far (the difficulty's sold item value) -> the new one.
    public static readonly Hook<Func<float, float>> SellFactor = new Hook<Func<float, float>>("SellFactor");

    internal static float Buy(dvp merchant, float markup)
    {
        if (!BuyMarkup.Any) return markup;
        var who = Character.Of(merchant.a8());
        float v = markup;
        BuyMarkup.Run(h => v = h(who, v));
        return Math.Max(0.01f, v);
    }

    internal static float Sell(float factor)
    {
        if (!SellFactor.Any) return factor;
        float v = factor;
        SellFactor.Run(h => v = h(v));
        return Math.Max(0f, v);
    }
}

public enum ApplyResult { Applied, WeakerThanExisting, AppliedAtMaxStacks, Refused, UnknownCode }

public sealed class StatusEffect
{
    internal readonly els Game;
    readonly br5 owner;
    internal StatusEffect(els e, br5 owner) { Game = e; this.owner = owner; }
    public string Code { get { return Game.Code; } }
    public string Name { get { return Game.LocalizedName ?? Game.Name; } }
    public int Stacks { get { return Game.Stacks; } }
    // Turns left (a turn is 5 s outside combat); 0 for permanent effects.
    public double TurnsLeft { get { return Game.Duration == 0 ? 0 : Game.TimeLeft / 5000.0; } }
    public bool Permanent { get { return Game.Duration == 0; } }
    public void Remove() { owner.d(Game); }
    public override string ToString() { return Name + (Stacks > 1 ? " x" + Stacks : "") + (Permanent ? "" : " (" + Math.Ceiling(TurnsLeft) + ")"); }
}

public static class StatusEffects
{
    static Dictionary<string, ConstructorInfo> registry;

    // Every status effect code Works can create (built on first use, from the game's effect types).
    public static IList<string> Codes { get { return Registry.Keys.OrderBy(c => c).ToList(); } }
    public static bool Exists(string code) { return code != null && Registry.ContainsKey(code); }

    // Applies a fresh instance of an effect (by code) as the game's own effect application does (br5.a). turns: its
    // duration (0: the effect's own). Only stock effect types: they are saved with the character.
    public static ApplyResult Apply(Character c, string code, double turns = 0, bool refresh = false)
    {
        ConstructorInfo ctor;
        if (c == null || !c.IsCharacter || code == null || !Registry.TryGetValue(code, out ctor)) return ApplyResult.UnknownCode;
        var effect = (els)ctor.Invoke(ctor.GetParameters().Length == 0 ? new object[0] : new object[] { (int)(turns * 5000) });
        if (turns > 0) SetDuration(effect, (int)(turns * 5000));
        var r = c.Effects.a(effect, refresh, false, false);
        return r == ea4.a ? ApplyResult.Applied : r == ea4.c ? ApplyResult.AppliedAtMaxStacks : r == ea4.b ? ApplyResult.WeakerThanExisting : ApplyResult.Refused;
    }

    static readonly PropertyInfo DurationProperty = typeof(els).GetProperty("Duration");
    static void SetDuration(els e, int ms) { var set = DurationProperty.GetSetMethod(true); if (set != null) set.Invoke(e, new object[] { ms }); }

    static Dictionary<string, ConstructorInfo> Registry
    {
        get
        {
            if (registry != null) return registry;
            var found = new Dictionary<string, ConstructorInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in typeof(els).Assembly.GetTypes().Where(t => typeof(els).IsAssignableFrom(t) && !t.IsAbstract))
            {
                var ctor = t.GetConstructor(Type.EmptyTypes) ?? t.GetConstructor(new[] { typeof(int) });
                if (ctor == null) continue;
                try
                {
                    var e = (els)ctor.Invoke(ctor.GetParameters().Length == 0 ? new object[0] : new object[] { 5000 });
                    if (!string.IsNullOrEmpty(e.Code) && !found.ContainsKey(e.Code)) found[e.Code] = ctor;
                }
                catch { }
            }
            Log.Write("effects: " + found.Count + " status effect codes");
            return registry = found;
        }
    }
}

// The Harmony hooks behind this file.
static class HudHooks
{
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(HudHooks).GetMethod(n, any));
        harmony.Patch(typeof(ci3).GetMethod("a", any, null, new[] { typeof(bool) }, null), postfix: m("Toggled"));
        harmony.Patch(typeof(dvp).GetMethod("aqd", any, null, new[] { typeof(efk) }, null), postfix: m("Markup"));
        harmony.Patch(typeof(b6o).GetMethod("d", any, null, Type.EmptyTypes, null), postfix: m("SellFactorHook"));
    }

    static void Toggled(bool __0) { try { Hud.Toggled(__0); } catch (Exception ex) { Log.Write("hud: " + ex.Message); } }
    static void Markup(dvp __instance, ref float __result) { try { __result = Prices.Buy(__instance, __result); } catch (Exception ex) { Log.Write("prices: " + ex.Message); } }
    static void SellFactorHook(ref float __result) { try { __result = Prices.Sell(__result); } catch (Exception ex) { Log.Write("prices: " + ex.Message); } }
}
}
