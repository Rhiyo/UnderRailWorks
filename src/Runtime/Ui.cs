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

public static class Ui
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

    static Microsoft.Xna.Framework.Graphics.SpriteFont DefaultFontFace()
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

public sealed class UiTickBox : UiElement
{
    internal UiTickBox(cao box) : base(box) { }
    public bool Checked { get { return ((cao)Control).Checked; } set { ((cao)Control).Checked = value; } }
}

public sealed class UiPanel : UiElement
{
    internal UiPanel(UiPanelControl panel) : base(panel) { }
    public Color Background { get { return ((UiPanelControl)Control).Background; } set { ((UiPanelControl)Control).Background = value; } }
    public Color Border { get { return ((UiPanelControl)Control).Border; } set { ((UiPanelControl)Control).Border = value; } }
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
    static MouseState mouseNow, mouseBefore;
    internal static void Tick() { before = now; now = Keyboard.GetState(); mouseBefore = mouseNow; mouseNow = UiScaleFix.GetMouse(); }
    // The mouse in interface units (the same coordinates as Ui elements: interface scale and insets applied).
    public static Point Mouse { get { return new Point(mouseNow.X, mouseNow.Y); } }
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
    internal static void Install()
    {
        var harmony = new HarmonyLib.Harmony("UnderRailWorks.Ui");
        Postfix(harmony, typeof(ci3).GetConstructors(), "Hud");
        Postfix(harmony, typeof(a8k).GetConstructors(), "MainMenu");
        var update = typeof(ci3).Assembly.GetType("tb").GetMethod("Update", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(GameTime) }, null);     // tb by name: it derives from XNA's Game (CS0012 here)
        if (update != null) harmony.Patch(update, postfix: new HarmonyLib.HarmonyMethod(typeof(UiHooks).GetMethod("Updated", BindingFlags.Static | BindingFlags.NonPublic)));
        ContentHooks.Install(harmony);
        Saves.Install(harmony);
        HudHooks.Install(harmony);
        CombatHooks.Install(harmony);
        Log.Write("ui: hooks in place");
    }

    static void Postfix(HarmonyLib.Harmony harmony, ConstructorInfo[] ctors, string what)
    {
        var method = new HarmonyLib.HarmonyMethod(typeof(UiHooks).GetMethod(what, BindingFlags.Static | BindingFlags.NonPublic));
        foreach (var c in ctors) harmony.Patch(c, postfix: method);
    }

    static void Hud(object __instance) { global::Hud.Created((ci3)__instance); var root = UiElement.Of((euz)__instance); if (UiEvents.HudCreated.Any) UiEvents.HudCreated.Run(h => h(root)); }
    static void MainMenu(object __instance) { var root = UiElement.Of((euz)__instance); if (UiEvents.MainMenuCreated.Any) UiEvents.MainMenuCreated.Run(h => h(root)); }
    internal static t1 CurrentOptions;
    internal static void Options(t1 options) { CurrentOptions = options; if (UiEvents.OptionsCreated.Any) UiEvents.OptionsCreated.Run(h => h(UiElement.Of(options))); }

    static void Updated(GameTime __0)
    {
        Input.Tick();
        try { Saves.Tick(); } catch (Exception ex) { Log.Write("saves: " + ex); }
        double dt = __0 == null ? 0 : __0.ElapsedGameTime.TotalSeconds;
        if (GameEvents.Update.Any) GameEvents.Update.Run(h => h(dt));
    }
}
