// UnderRailWorks: the UI toolkit for mods (as tModLoader exposes Terraria's UIElement family). Mods build interface
// from the game's own controls, so it looks like the game and follows Works' interface scale and insets:
//   Ui.Label("text"), Ui.Button("text", onClick), Ui.TickBox(true), Ui.Panel(w, h) (the game's dark panel with a
//   border; holds other elements), Ui.Add(parent, element, x, y), Ui.Remove, Ui.Move, Ui.Resize, Ui.Show, Ui.SetText,
//   Ui.SetColor.
// Where to put it (the game builds these screens anew, e.g. the in-game screen after a load or a resolution change,
// so mods add their elements each time the event fires):
//   UiEvents.HudCreated(root)       the in-game screen (HUD and windows), root = its top control (ci3)
//   UiEvents.MainMenuCreated(root)  the main menu (a8k)
//   UiEvents.OptionsCreated(root)   the Options window (t1), after its tabs (and the Mods tab) are built
//   GameEvents.Update(seconds)      once per update, the time since the last one
//   Keys: Input.Pressed(Keys.F8)    true on the update the key went down (only while no text box has focus is up to
//                                   the mod; Input.Down for held keys)
//   Ui.Custom(w, h, canvas => ...)  an element the mod draws itself (textures, filled rectangles), clipped to nothing:
//                                   canvas.Bounds is where it is on screen
// Elements are Works' handles (UiElement and subclasses), never the game's controls: a mod compiles without the game.
// Positions and sizes are in interface units (Works scales them to pixels).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public static partial class Ui
{
    static readonly PropertyInfo ColorProperty = typeof(avo).GetProperty("Color");
    static readonly MethodInfo StyleButton = typeof(t1).GetMethod("a", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(dak), typeof(wj) }, null);
    static readonly FieldInfo DefaultFont = typeof(egg).GetField("aq", BindingFlags.Static | BindingFlags.Public);
    static readonly MethodInfo LabelFont = typeof(d7w).GetMethod("a", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
    static readonly MethodInfo NewTickBox = typeof(t1).GetMethod("b", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);
    internal static readonly MethodInfo AbsoluteMethod = typeof(euz).GetMethod("at", Type.EmptyTypes);
    internal static readonly MethodInfo DrawRectMethod = typeof(ob).GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .First(m => m.Name == "xa" && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType.Name == "Rectangle");

    // A line of text in the game's interface font (left-aligned).
    public static UiElement Label(string text, int width = 300)
    {
        var label = new avo();
        if (LabelFont != null && DefaultFont != null) LabelFont.Invoke(label, new[] { DefaultFont.GetValue(null) });
        label.a(cab.a);     // left
        label.Text = text;
        label.Size = new esj(width, 22);
        return UiElement.Of(label);
    }

    // A button styled like the options window's (Accept, Cancel), as wide as its text needs.
    public static UiElement Button(string text, Action onClick)
    {
        var button = new dak();
        button.Text = text;
        StyleButton.Invoke(null, new object[] { button, wj.a() });
        var resources = dr3.a();
        var font = resources == null || button.Font == null ? null : resources.GetResource<Microsoft.Xna.Framework.Graphics.SpriteFont>(button.Font);
        if (font != null) button.Size = new esj(Math.Max(UnderrailDropdownScroll.W(button.Size), (int)font.MeasureString(text).X + 40), UnderrailDropdownScroll.H(button.Size));
        e4z.j(button);     // its click sound
        button.c(new EventHandler<b25>((sender, e) => { try { onClick(); } catch (Exception ex) { Log.Write("mods: a button failed: " + ex); } }));
        return UiElement.Of(button);
    }

    // The game's tick box (as in its options).
    public static UiTickBox TickBox(bool value)
    {
        // t1.b(bool) builds it with the theme's art; it needs no t1 state.
        var options = (t1)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(t1));
        return new UiTickBox((cao)NewTickBox.Invoke(options, new object[] { value }));
    }

    // A text box (the game's own field, c68, as its split-stack dialog sets one up): click it to type; Text reads it.
    public static UiTextBox TextBox(int width, int maxLength = 40)
    {
        var theme = wj.a();
        var box = new c68();
        box.Size = new esj(width, theme.c() ? 25 : 28);
        var font = DefaultFont == null ? null : DefaultFont.GetValue(null) as string;
        if (font != null) BoxFont.Invoke(box, new object[] { font });
        if (!theme.c())
        {
            BoxTextureField.SetValue(box, theme.f);                            // the theme's field textures
            BoxOffsetField.SetValue(box, ThemeOffsetField.GetValue(theme));
            BoxSizeField.SetValue(box, ThemeSizeField.GetValue(theme));
        }
        TextOffset.Invoke(box, new object[] { Activator.CreateInstance(TextOffset.GetParameters()[0].ParameterType, 5, font == null ? 4 : egg.b(font)) });
        ((t3)box).b(maxLength);
        var handle = new UiTextBox(box);
        UiTextBox.Live.Add(handle);
        return handle;
    }
    // c68's fields and methods typed with XNA (Point, esj?): by reflection.
    static readonly MethodInfo BoxFont = typeof(c68).GetMethod("a", new[] { typeof(string) });
    static readonly FieldInfo BoxTextureField = typeof(c68).GetField("e"), BoxOffsetField = typeof(c68).GetField("f"), BoxSizeField = typeof(c68).GetField("g"),
        ThemeOffsetField = typeof(wj).GetField("fg"), ThemeSizeField = typeof(wj).GetField("fh");
    static readonly MethodInfo TextOffset = typeof(c68).GetMethods().First(m => m.Name == "a" && m.GetParameters().Length == 1 && m.GetParameters()[0].ParameterType.Name == "Point");

    // A panel in the game's style: dark, with a border in the interface colour. Holds other elements.
    public static UiPanel Panel(int width, int height) { return new UiPanel(new UiPanelControl { Size = new esj(width, height) }); }

    // An element the mod draws itself: draw is called every frame it is visible, with a canvas at its place on screen.
    public static UiElement Custom(int width, int height, Action<UiCanvas> draw)
    {
        return UiElement.Of(new UiCustomControl(draw) { Size = new esj(width, height) });
    }

    public static void Add(UiElement parent, UiElement element, int x, int y)
    {
        Move(element, x, y);
        UnderrailDropdownScroll.Add(parent.Control, element.Control);
        if (parent.Control is ci3) Hud.Track(element);     // on the HUD: hidden with the game's HUD
    }
    public static void Remove(UiElement element) { if (element != null && element.Control.Parent != null) UnderrailDropdownScroll.Remove(element.Control.Parent, element.Control); }
    public static void Move(UiElement element, int x, int y) { UnderrailDropdownScroll.Move(element.Control, x, y); }

    // --- Places that survive resolution and interface-scale changes ------------------------------------------------
    // A position saved as pixels is wrong (or off-screen) once the HUD changes size. Place(element) writes where it is
    // relative to the nearest edge or the centre of its parent, on each axis ("R24,B110": 24 from the right edge,
    // 110 from the bottom); Restore puts it back at the same distance from the same edge on any size, kept on screen.
    public static string Place(UiElement element)
    {
        var parent = element.Control.Parent;
        if (parent == null) return null;     // not on screen (removed): no place to keep
        int pw = UnderrailDropdownScroll.W(parent.Size), ph = UnderrailDropdownScroll.H(parent.Size);
        return Axis(element.X, element.Width, pw, 'L', 'C', 'R') + "," + Axis(element.Y, element.Height, ph, 'T', 'M', 'B');
    }

    // The place relative to a chosen anchor (instead of the nearest edge).
    public static string Place(UiElement element, HudAnchor anchor)
    {
        var parent = element.Control.Parent;
        if (parent == null) return null;
        int pw = parent == null ? 0 : UnderrailDropdownScroll.W(parent.Size), ph = parent == null ? 0 : UnderrailDropdownScroll.H(parent.Size);
        int col = (int)anchor % 3, row = (int)anchor / 3;
        string x = col == 0 ? "L" + element.X : col == 2 ? "R" + (pw - element.X - element.Width) : "C" + (element.X + element.Width / 2 - pw / 2);
        string y = row == 0 ? "T" + element.Y : row == 2 ? "B" + (ph - element.Y - element.Height) : "M" + (element.Y + element.Height / 2 - ph / 2);
        return x + "," + y;
    }
    // The anchor a place string uses.
    public static HudAnchor AnchorOfPlace(string place)
    {
        var p = (place ?? "").Split(',');
        int col = p.Length < 1 || p[0].Length == 0 ? 0 : p[0][0] == 'R' ? 2 : p[0][0] == 'C' ? 1 : 0;
        int row = p.Length < 2 || p[1].Length == 0 ? 0 : p[1][0] == 'B' ? 2 : p[1][0] == 'M' ? 1 : 0;
        return (HudAnchor)(row * 3 + col);
    }

    // The same for a rectangle in a parent of the given size (e.g. to convert positions saved as pixels).
    public static string Place(int x, int y, int width, int height, int parentWidth, int parentHeight)
    {
        return Axis(x, width, parentWidth, 'L', 'C', 'R') + "," + Axis(y, height, parentHeight, 'T', 'M', 'B');
    }
    // Keeps a position for element inside its parent.
    public static Point KeepInside(UiElement element, int x, int y)
    {
        var parent = element.Control.Parent;
        if (parent == null) return new Point(x, y);
        return new Point(Clamp(x, element.Width, UnderrailDropdownScroll.W(parent.Size)), Clamp(y, element.Height, UnderrailDropdownScroll.H(parent.Size)));
    }

    // Moves element to a place from Place (or an old "x,y"). False if the text isn't one.
    public static bool Restore(UiElement element, string place)
    {
        var parent = element.Control.Parent;
        if (parent == null || string.IsNullOrEmpty(place)) return false;
        var parts = place.Split(',');
        if (parts.Length != 2) return false;
        int pw = UnderrailDropdownScroll.W(parent.Size), ph = UnderrailDropdownScroll.H(parent.Size);
        int x, y;
        if (!Axis(parts[0], element.Width, pw, 'L', 'C', 'R', out x) || !Axis(parts[1], element.Height, ph, 'T', 'M', 'B', out y)) return false;
        Move(element, Clamp(x, element.Width, pw), Clamp(y, element.Height, ph));
        return true;
    }

    // Inside the parent when it fits, else its start edge on screen.
    static int Clamp(int at, int size, int room) { return size >= room ? 0 : Math.Max(0, Math.Min(room - size, at)); }

    static string Axis(int at, int size, int room, char start, char mid, char end)
    {
        int centre = at + size / 2;
        if (centre < room / 3) return start + at.ToString();
        if (centre > room * 2 / 3) return end + (room - at - size).ToString();
        return mid + (centre - room / 2).ToString();
    }

    static bool Axis(string text, int size, int room, char start, char mid, char end, out int at)
    {
        at = 0;
        text = text.Trim();
        int n;
        if (text.Length > 1 && !char.IsDigit(text[0]) && text[0] != '-' && int.TryParse(text.Substring(1), out n))
        {
            if (text[0] == start) at = n;
            else if (text[0] == end) at = room - size - n;
            else if (text[0] == mid) at = room / 2 + n - size / 2;
            else return false;
            return true;
        }
        return int.TryParse(text, out at);     // an old pixel position
    }
    public static void Resize(UiElement element, int width, int height) { element.Control.Size = new esj(width, height); }
    public static void Show(UiElement element, bool visible) { element.Visible = visible; }
    public static void SetText(UiElement element, string text) { element.Control.Text = text; }
    // A label's text colour.
    public static void SetColor(UiElement label, Color colour) { if (label.Control is avo) ColorProperty.SetValue(label.Control, colour, null); }

    // Splits text into lines that fit a width (interface units) in the game's interface font (Ui.Label's), at spaces.
    public static IList<string> Wrap(string text, int width)
    {
        var lines = new List<string>();
        var font = DefaultFontFace();
        foreach (var paragraph in (text ?? "").Replace("\r", "").Split('\n'))
        {
            var line = "";
            foreach (var word in paragraph.Split(' '))
            {
                var next = line.Length == 0 ? word : line + " " + word;
                if (line.Length > 0 && (font == null ? next.Length * 7 : font.MeasureString(next).X) > width) { lines.Add(line); line = word; }
                else line = next;
            }
            lines.Add(line);
        }
        return lines;
    }

    // Width of a text in the interface font.
    public static int TextWidth(string text) { var font = DefaultFontFace(); return font == null ? (text ?? "").Length * 7 : (int)Math.Ceiling(font.MeasureString(text ?? "").X); }

    internal static Microsoft.Xna.Framework.Graphics.SpriteFont DefaultFontFace()
    {
        var resources = dr3.a();
        var name = DefaultFont == null ? null : DefaultFont.GetValue(null) as string;
        try { return resources == null || name == null ? null : resources.GetResource<Microsoft.Xna.Framework.Graphics.SpriteFont>(name); } catch { return null; }
    }

    internal static Rectangle Bounds(euz control)
    {
        object at = AbsoluteMethod.Invoke(control, null);
        return new Rectangle(UnderrailDropdownScroll.X(at), UnderrailDropdownScroll.Y(at), UnderrailDropdownScroll.W(control.Size), UnderrailDropdownScroll.H(control.Size));
    }
}

// A handle on an interface element (one of the game's controls inside).
public class UiElement
{
    internal readonly euz Control;
    internal UiElement(euz control) { Control = control; }
    static readonly ConditionalWeakTable<euz, UiElement> Handles = new ConditionalWeakTable<euz, UiElement>();
    internal static UiElement Of(euz control) { return control == null ? null : Handles.GetValue(control, c => new UiElement(c)); }

    public bool Visible { get { return Control.Visible; } set { if (!Hud.Defer(this, value)) Control.Visible = value; } }
    // Its position in its parent (interface units), as Ui.Move sets it.
    public int X { get { return UnderrailDropdownScroll.X(UnderrailDropdownScroll.LocationOf(Control)); } }
    public int Y { get { return UnderrailDropdownScroll.Y(UnderrailDropdownScroll.LocationOf(Control)); } }
    public int Width { get { return UnderrailDropdownScroll.W(Control.Size); } }
    public int Height { get { return UnderrailDropdownScroll.H(Control.Size); } }
    // Where it is on screen (interface units).
    public Rectangle Bounds { get { return Ui.Bounds(Control); } }
}

public sealed class UiTextBox : UiElement
{
    internal static readonly List<UiTextBox> Live = new List<UiTextBox>();
    string last = "";
    internal UiTextBox(c68 box) : base(box) { }
    public string Text { get { return Control.Text ?? ""; } set { Control.Text = value ?? ""; last = Text; } }
    // After the text changed (checked once per update).
    public event Action<string> Changed;
    internal static void Poll()
    {
        Live.RemoveAll(b => b.Control.Parent == null && b.last == null);
        foreach (var b in Live.ToList())
        {
            var now = b.Text;
            if (now == b.last) continue;
            b.last = now;
            if (b.Changed != null) { try { b.Changed(now); } catch (Exception ex) { Log.Write("ui: a text box handler failed: " + ex.Message); } }
        }
    }
}

public sealed class UiTickBox : UiElement
{
    internal UiTickBox(cao box) : base(box) { }
    public bool Checked { get { return ((cao)Control).Checked; } set { ((cao)Control).Checked = value; } }
}

public class UiPanel : UiElement
{
    internal UiPanel(UiPanelControl panel) : base(panel) { }
    public Color Background { get { return ((UiPanelControl)Control).Background; } set { ((UiPanelControl)Control).Background = value; } }
    public Color Border { get { return ((UiPanelControl)Control).Border; } set { ((UiPanelControl)Control).Border = value; } }
    // Removes everything added to the panel (to rebuild its contents).
    public void Clear() { foreach (var child in Control.Controls.ToList()) UnderrailDropdownScroll.Remove(Control, child); }
}

// What a custom element draws with, during its draw: everything in interface units, through the game's interface batch
// (so it follows Works' interface scale and insets).
public sealed class UiCanvas
{
    internal cp9 Context;
    internal UiCanvas() { }
    static Microsoft.Xna.Framework.Graphics.Texture2D white;

    // Where the element is on screen.
    public Rectangle Bounds { get; internal set; }
    // For creating textures (the game's graphics device).
    public Microsoft.Xna.Framework.Graphics.GraphicsDevice Device { get { return DrawContexts.Device(Context); } }

    public void DrawTexture(Microsoft.Xna.Framework.Graphics.Texture2D texture, Rectangle area, Color colour)
    {
        Ui.DrawRectMethod.Invoke(Context.b(), new object[] { texture, area, colour });
    }

    public void Fill(Rectangle area, Color colour)
    {
        var g = Device;
        if (white == null || white.IsDisposed || white.GraphicsDevice != g) { white = new Microsoft.Xna.Framework.Graphics.Texture2D(g, 1, 1); white.SetData(new[] { Color.White }); }
        DrawTexture(white, area, colour);
    }

    // Text in the interface's default font, its top left at (x, y).
    public void Text(string text, int x, int y, Color colour)
    {
        var font = Ui.DefaultFontFace();
        if (font == null || string.IsNullOrEmpty(text)) return;
        DrawStringMethod.Invoke(Context.b(), new object[] { font, text, new Vector2(x, y), colour });     // ob.xb: XNA types in its signature
    }
    static readonly System.Reflection.MethodInfo DrawStringMethod = typeof(ob).GetMethod("xb");

    // A one-unit frame around an area.
    public void Frame(Rectangle r, Color colour)
    {
        Fill(new Rectangle(r.X, r.Y, r.Width, 1), colour); Fill(new Rectangle(r.X, r.Bottom - 1, r.Width, 1), colour);
        Fill(new Rectangle(r.X, r.Y, 1, r.Height), colour); Fill(new Rectangle(r.Right - 1, r.Y, 1, r.Height), colour);
    }
}

sealed class UiCustomControl : euz
{
    readonly Action<UiCanvas> draw;
    readonly UiCanvas canvas = new UiCanvas();
    bool failed;
    internal UiCustomControl(Action<UiCanvas> draw) { this.draw = draw; }

    public override void iv(e6d time, cp9 context)
    {
        if (!failed)
            try { canvas.Context = context; canvas.Bounds = Ui.Bounds(this); draw(canvas); }
            catch (Exception ex) { failed = true; Log.Write("ui: a custom element failed to draw (switched off): " + ex); }
        base.iv(time, context);
    }
}

// A panel: a flat background and a one-unit border, then its children. (The game's own fill, d0p, tints a dark
// texture: fine for dims, but a border tinted orange came out black.)
sealed class UiPanelControl : euz
{
    readonly UiCanvas canvas = new UiCanvas();
    public Microsoft.Xna.Framework.Color Background = new Microsoft.Xna.Framework.Color(12, 12, 12, 225);
    public Microsoft.Xna.Framework.Color Border = new Microsoft.Xna.Framework.Color(181, 92, 0);

    public override void iv(e6d time, cp9 context)
    {
        try
        {
            canvas.Context = context;
            var r = Ui.Bounds(this);
            canvas.Fill(r, Background);
            canvas.Frame(r, Border);
        }
        catch (Exception ex) { Log.Write("ui: panel draw failed: " + ex.Message); }
        base.iv(time, context);
    }
}

public static class UiEvents
{
    public static readonly Hook<Action<UiElement>> HudCreated = new Hook<Action<UiElement>>("HudCreated");
    public static readonly Hook<Action<UiElement>> MainMenuCreated = new Hook<Action<UiElement>>("MainMenuCreated");
    // The in-game HUD is gone (back to the main menu); HudCreated follows when a game starts again. Drop references
    // to HUD elements here.
    public static readonly Hook<Action> HudDestroyed = new Hook<Action>("HudDestroyed");
    public static readonly Hook<Action<UiElement>> OptionsCreated = new Hook<Action<UiElement>>("OptionsCreated");
}

public static partial class GameEvents
{
    // Once per game update; the argument is the time since the last one, in seconds.
    public static readonly Hook<Action<double>> Update = new Hook<Action<double>>("Update");
}

public enum MouseButton { Left, Right, Middle }

public static class Input
{
    static KeyboardState now, before;
    static WeakReference lastFocus;     // the last focused control that wasn't a mod's text box
    static MouseState mouseNow, mouseBefore;
    internal static void Tick()
    {
        before = now; now = Keyboard.GetState(); mouseBefore = mouseNow; mouseNow = UiScaleFix.GetMouse();
        // A click outside a mod's focused text box takes its focus away (the game's text fields are in modal dialogs; a box
        // on the HUD otherwise kept the keyboard after a click on the world, and no key reached the game).
        // Focus goes back to what had it before the box (the game's hotkeys arrive through the focused control: with none,
        // no key reached the game).
        var focusService = bpo.GetService<d1a>();
        var focusedNow = focusService == null ? null : focusService.aa7();
        var focusedBox = focusedNow == null ? null : UiTextBox.Live.FirstOrDefault(b => b.Control == focusedNow);
        if (focusedBox == null && focusedNow != null) lastFocus = new WeakReference(focusedNow);
        if (focusedBox != null && mouseNow.LeftButton == ButtonState.Pressed && mouseBefore.LeftButton != ButtonState.Pressed && !focusedBox.Bounds.Contains(Mouse))
        {
            var back = lastFocus == null ? null : lastFocus.Target as euz;
            focusService.aa4(back != null && back.Parent != null ? back : null);
            Typing = false;
        }
        if (!now.Equals(before)) Typing = FocusedText(Root());     // only when a key changed: a walk over the interface
    }

    // A text field has the keyboard (a mod's text box, the game's own fields, its console): keys are text, not
    // shortcuts. Keybind.Pressed/Down are false meanwhile, as the game's own hotkeys are.
    public static bool Typing { get; private set; }

    static euz Root() { var ui = bpo.GetService<bcm>(); return ui == null ? null : ui.aau(); }
    static bool FocusedText(euz c)
    {
        if (c == null || !c.Visible) return false;
        if (c is t3 && c.IsFocus) return true;
        foreach (euz child in c.Controls) if (FocusedText(child)) return true;
        return false;
    }
    // The mouse in interface units (the same coordinates as Ui elements: interface scale and insets applied).
    public static Point Mouse { get { return new Point(mouseNow.X, mouseNow.Y); } }
    // Mouse wheel notches turned since the last update (up is positive).
    public static int Wheel { get { return (mouseNow.ScrollWheelValue - mouseBefore.ScrollWheelValue) / 120; } }
    public static bool MouseDown(MouseButton b) { return Pressed(mouseNow, b); }
    public static bool MousePressed(MouseButton b) { return Pressed(mouseNow, b) && !Pressed(mouseBefore, b); }
    public static bool MouseReleased(MouseButton b) { return !Pressed(mouseNow, b) && Pressed(mouseBefore, b); }
    static bool Pressed(MouseState m, MouseButton b) { return (b == MouseButton.Left ? m.LeftButton : b == MouseButton.Right ? m.RightButton : m.MiddleButton) == ButtonState.Pressed; }
    public static bool Down(Keys key) { return now.IsKeyDown(key); }
    public static bool Pressed(Keys key) { return now.IsKeyDown(key) && !before.IsKeyDown(key); }
}

// Where the events come from: Harmony patches Works makes on the game (new hooks are Harmony, not the patcher).
static class UiHooks
{
    static void Group(string what, Action install)
    {
        try { install(); } catch (Exception ex) { Log.Write("UnderRailWorks: " + what + " hooks failed (mods using them won't see those events): " + ex); }
    }

    internal static void Install()
    {
        var harmony = new HarmonyLib.Harmony("UnderRailWorks.Ui");
        Postfix(harmony, typeof(ci3).GetConstructors(), "Hud");
        Postfix(harmony, typeof(a8k).GetConstructors(), "MainMenu");
        var update = typeof(ci3).Assembly.GetType("tb").GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(GameTime) }, null);     // tb by name: it derives from XNA's Game (CS0012 here)
        if (update != null) harmony.Patch(update, postfix: new HarmonyLib.HarmonyMethod(typeof(UiHooks).GetMethod("Updated", BindingFlags.Static | BindingFlags.NonPublic)));
        // Each group on its own: one that fails (a game update moved its method) leaves the others working.
        Group("content", () => ContentHooks.Install(harmony));
        Group("saves", () => Saves.Install(harmony));
        Group("HUD", () => HudHooks.Install(harmony));
        Group("combat", () => CombatHooks.Install(harmony));
        Group("keybinds", () => Keybinds.Install(harmony));
        Group("status effects and levels", () => ProgressHooks.Install(harmony));
        Group("equipment", () => EquipmentHooks.Install(harmony));
        Group("progression", () => ProgressionHooks.Install(harmony));
        Group("dialog patches", () => Dialogs.InstallPatches(harmony));
        Group("containers, loot and locks", () => ContainerHooks.Install(harmony));
        Group("stealth", () => StealthHooks.Install(harmony));
        Group("mod windows", () => UiWindows.Install(harmony));
        Group("assets", () => AssetStore.Install(harmony));
        Group("clock, items, crafting, stealing", () => ActivityHooks.Install(harmony));
        Group("item definitions and tooltips", () => ItemHooks.Install(harmony));
        Group("music and sounds", () => AudioHooks.Install(harmony));
        Group("console commands", () => CommandHooks.Install(harmony));
        Group("stores", () => StoreHooks.Install(harmony));
        Group("loot rules", () => LootHooks.Install(harmony));
        Group("mod statuses", () => ModStatusStore.Install(harmony));
        Group("world hit test with insets", () => WorldHitTest.Install(harmony));
        Log.Write("ui: hooks in place");
    }

    static void Postfix(HarmonyLib.Harmony harmony, ConstructorInfo[] ctors, string what)
    {
        var method = new HarmonyLib.HarmonyMethod(typeof(UiHooks).GetMethod(what, BindingFlags.Static | BindingFlags.NonPublic));
        foreach (var c in ctors) harmony.Patch(c, postfix: method);
    }

    static void Hud(object __instance) { UnderRailWorks.Hud.Created((ci3)__instance); var root = UiElement.Of((euz)__instance); if (UiEvents.HudCreated.Any) UiEvents.HudCreated.Run(h => h(root)); }
    static void MainMenu(object __instance)
    {
        if (UnderRailWorks.Hud.Exists) { UnderRailWorks.Hud.Gone(); if (UiEvents.HudDestroyed.Any) UiEvents.HudDestroyed.Run(h => h()); }     // back to the main menu: no HUD
        var root = UiElement.Of((euz)__instance);
        if (UiEvents.MainMenuCreated.Any) UiEvents.MainMenuCreated.Run(h => h(root));
    }
    internal static t1 CurrentOptions;
    internal static void Options(t1 options) { CurrentOptions = options; if (UiEvents.OptionsCreated.Any) UiEvents.OptionsCreated.Run(h => h(UiElement.Of(options))); }

    static void Updated(GameTime __0)
    {
        Input.Tick();
        UiWindows.Tick();
        AssetStore.Apply();
        UnderRailWorks.Hud.FlowTick();
        try { UiTextBox.Poll(); } catch (Exception ex) { Log.Write("ui: " + ex.Message); }
        try { Saves.Tick(); } catch (Exception ex) { Log.Write("saves: " + ex); }
        double dt = __0 == null ? 0 : __0.ElapsedGameTime.TotalSeconds;
        if (GameEvents.Update.Any) GameEvents.Update.Run(h => h(dt));
    }
}
}
