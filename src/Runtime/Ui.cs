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
// Elements are the game's own controls (euz and its subclasses): anything here returns them, so a mod can go further
// with the game's API or Harmony. Positions and sizes are in interface units (Works scales them to pixels).
using System;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;

public static class Ui
{
    const BindingFlags Any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
    static readonly PropertyInfo ColorProperty = typeof(avo).GetProperty("Color");
    static readonly MethodInfo StyleButton = typeof(t1).GetMethod("a", BindingFlags.Static | BindingFlags.NonPublic, null, new[] { typeof(dak), typeof(wj) }, null);
    static readonly FieldInfo DefaultFont = typeof(egg).GetField("aq", BindingFlags.Static | BindingFlags.Public);
    static readonly MethodInfo LabelFont = typeof(d7w).GetMethod("a", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);
    static readonly MethodInfo NewTickBox = typeof(t1).GetMethod("b", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(bool) }, null);

    // A line of text in the game's interface font (left-aligned).
    public static avo Label(string text, int width = 300)
    {
        var label = new avo();
        if (LabelFont != null && DefaultFont != null) LabelFont.Invoke(label, new[] { DefaultFont.GetValue(null) });
        label.a(cab.a);     // left
        label.Text = text;
        label.Size = new esj(width, 22);
        return label;
    }

    // A button styled like the options window's (Accept, Cancel), as wide as its text needs.
    public static dak Button(string text, Action onClick)
    {
        var button = new dak();
        button.Text = text;
        StyleButton.Invoke(null, new object[] { button, wj.a() });
        var resources = dr3.a();
        var font = resources == null || button.Font == null ? null : resources.GetResource<Microsoft.Xna.Framework.Graphics.SpriteFont>(button.Font);
        if (font != null) button.Size = new esj(Math.Max(UnderrailDropdownScroll.W(button.Size), (int)font.MeasureString(text).X + 40), UnderrailDropdownScroll.H(button.Size));
        e4z.j(button);     // its click sound
        button.c(new EventHandler<b25>((sender, e) => { try { onClick(); } catch (Exception ex) { Log.Write("mods: a button failed: " + ex); } }));
        return button;
    }

    // The game's tick box (as in its options). Checked: cao.Checked.
    public static cao TickBox(bool value)
    {
        // t1.b(bool) builds it with the theme's art; it needs no t1 state.
        var options = (t1)System.Runtime.Serialization.FormatterServices.GetUninitializedObject(typeof(t1));
        return (cao)NewTickBox.Invoke(options, new object[] { value });
    }

    // A panel in the game's style: dark, with a border in the interface colour. Holds other elements.
    public static UiPanel Panel(int width, int height) { return new UiPanel { Size = new esj(width, height) }; }

    public static void Add(euz parent, euz element, int x, int y) { Move(element, x, y); UnderrailDropdownScroll.Add(parent, element); }
    public static void Remove(euz element) { if (element != null && element.Parent != null) UnderrailDropdownScroll.Remove(element.Parent, element); }
    public static void Move(euz element, int x, int y) { UnderrailDropdownScroll.Move(element, x, y); }
    public static void Resize(euz element, int width, int height) { element.Size = new esj(width, height); }
    public static void Show(euz element, bool visible) { element.Visible = visible; }
    public static void SetText(euz element, string text) { element.Text = text; }
    public static void SetColor(avo label, Color colour) { ColorProperty.SetValue(label, colour, null); }
    // Where an element is on screen (interface units), e.g. for drawing in a custom control's iv.
    public static Rectangle Bounds(euz element)
    {
        object at = AbsoluteMethod.Invoke(element, null);
        return new Rectangle(UnderrailDropdownScroll.X(at), UnderrailDropdownScroll.Y(at), Width(element), Height(element));
    }
    static readonly MethodInfo AbsoluteMethod = typeof(euz).GetMethod("at", Type.EmptyTypes);
    static readonly MethodInfo DrawRectMethod = typeof(ob).GetMethod("xa");
    // Draws a texture over a rectangle (interface units) with the game's interface batch, from a custom control's iv.
    public static void DrawTexture(cp9 context, Microsoft.Xna.Framework.Graphics.Texture2D texture, Rectangle area, Color colour)
    {
        DrawRectMethod.Invoke(context.b(), new object[] { texture, area, colour });
    }
    public static int Width(euz element) { return UnderrailDropdownScroll.W(element.Size); }
    public static int Height(euz element) { return UnderrailDropdownScroll.H(element.Size); }
}

// A panel: a flat background and a one-unit border, drawn with a white pixel through the game's interface batch (so
// they follow interface scale and insets), then its children. (The game's own fill, d0p, tints a dark texture: fine for
// dims, but a border tinted orange came out black.)
public sealed class UiPanel : euz
{
    static readonly MethodInfo Absolute = typeof(euz).GetMethod("at", Type.EmptyTypes);
    static readonly MethodInfo DrawRect = typeof(ob).GetMethods(BindingFlags.Instance | BindingFlags.Public)
        .First(m => m.Name == "xa" && m.GetParameters().Length == 3 && m.GetParameters()[1].ParameterType.Name == "Rectangle");
    static Microsoft.Xna.Framework.Graphics.Texture2D white;
    public Color Background = new Color(12, 12, 12, 225);
    public Color Border = new Color(181, 92, 0);

    public override void iv(e6d time, cp9 context)
    {
        try
        {
            var g = DrawContexts.Device(context);
            if (white == null || white.IsDisposed || white.GraphicsDevice != g) { white = new Microsoft.Xna.Framework.Graphics.Texture2D(g, 1, 1); white.SetData(new[] { Microsoft.Xna.Framework.Color.White }); }
            object at = Absolute.Invoke(this, null);
            int x = UnderrailDropdownScroll.X(at), y = UnderrailDropdownScroll.Y(at), w = Ui.Width(this), h = Ui.Height(this);
            ob batch = context.b();
            foreach (var r in new[] { Tuple.Create(new Rectangle(x, y, w, h), Background), Tuple.Create(new Rectangle(x, y, w, 1), Border),
                Tuple.Create(new Rectangle(x, y + h - 1, w, 1), Border), Tuple.Create(new Rectangle(x, y, 1, h), Border), Tuple.Create(new Rectangle(x + w - 1, y, 1, h), Border) })
                DrawRect.Invoke(batch, new object[] { white, r.Item1, r.Item2 });
        }
        catch (Exception ex) { Log.Write("ui: panel draw failed: " + ex.Message); }
        base.iv(time, context);
    }
}

public static class UiEvents
{
    public static readonly Hook<Action<euz>> HudCreated = new Hook<Action<euz>>("HudCreated");
    public static readonly Hook<Action<euz>> MainMenuCreated = new Hook<Action<euz>>("MainMenuCreated");
    public static readonly Hook<Action<euz>> OptionsCreated = new Hook<Action<euz>>("OptionsCreated");
}

public static class GameEvents
{
    // Once per game update; the argument is the time since the last one, in seconds.
    public static readonly Hook<Action<double>> Update = new Hook<Action<double>>("Update");
}

public static class Input
{
    static KeyboardState now, before;
    internal static void Tick() { before = now; now = Keyboard.GetState(); }
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
        Log.Write("ui: hooks in place");
    }

    static void Postfix(HarmonyLib.Harmony harmony, ConstructorInfo[] ctors, string what)
    {
        var method = new HarmonyLib.HarmonyMethod(typeof(UiHooks).GetMethod(what, BindingFlags.Static | BindingFlags.NonPublic));
        foreach (var c in ctors) harmony.Patch(c, postfix: method);
    }

    static void Hud(object __instance) { var root = (euz)__instance; if (UiEvents.HudCreated.Any) UiEvents.HudCreated.Run(h => h(root)); }
    static void MainMenu(object __instance) { var root = (euz)__instance; if (UiEvents.MainMenuCreated.Any) UiEvents.MainMenuCreated.Run(h => h(root)); }
    internal static void Options(t1 options) { if (UiEvents.OptionsCreated.Any) UiEvents.OptionsCreated.Run(h => h(options)); }

    static void Updated(GameTime __0)
    {
        Input.Tick();
        double dt = __0 == null ? 0 : __0.ElapsedGameTime.TotalSeconds;
        if (GameEvents.Update.Any) GameEvents.Update.Run(h => h(dt));
    }
}
