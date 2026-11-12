// UnderRailWorks: a standard window for mods: the game's own titled window (a9t, as its notes and inventory windows:
// the interface size's frame, title strip, font and close button), dragged by its title bar, closed by the button or
// Escape (the topmost one first; the game's menu doesn't open meanwhile), its place
// remembered per mod and kept relative to the screen edges (Ui.Place), so it survives resolution changes.
//   var w = Ui.Window(this, "ledger", "Merchant ledger", 560, 360);
//   Ui.Add(w, Ui.Label("..."), 10, w.Top + 8);      // content below the title bar
//   w.Closed += () => { ... };
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class UiWindow : UiPanel
{
    internal WorksMod Mod;
    internal string Id;
    internal UiWindow(a9t window) : base(window) { }
    // The title bar's height at this interface size.
    public int TitleHeight { get { var w = Control as a9t; return w == null ? 22 : (int)UiWindows.TitleField.GetValue(w); } }
    // Where content starts (below the title bar).
    public int Top { get { return TitleHeight + 4; } }
    public bool IsOpen { get { return Control.Parent != null; } }
    // Runs once when the window closes (button, Escape, Close, or the HUD going away).
    public event Action Closed;
    public void Close()
    {
        if (!UiWindows.Open.Remove(this)) return;
        Ui.Remove(this);
        var c = Closed; Closed = null;
        if (c != null) try { c(); } catch (Exception ex) { Log.Write("mods: " + Mod.Id + ": a window's Closed failed: " + ex.Message); }
    }
}

public static partial class Ui
{
    // A window on the HUD, or on the main menu when it is showing (null otherwise). id: unique within the mod, for remembering its place. Opening a
    // window with an id already open returns that one. For a panel that stays open (a ledger, a list). To ask the player
    // something, use the game's own popups instead: Ui.Ask (text), Ui.AskNumber, Ui.Confirm (yes/no), Ui.Choose
    // (buttons). height counts a 22-high title bar; the real one follows the interface size (place content from Top).
    public static UiWindow Window(WorksMod mod, string id, string title, int width, int height)
    {
        if (PlayerScreens.ActingIsRemote) { Log.Write("mods: " + mod.Id + ": a window (" + title + ") for a player on another machine: not opened here"); return null; }
        var hud = Hud.Root;
        if (hud == null) { var screen = UiPopups.Screen(); if (screen is a8k) hud = UiElement.Of(screen); }     // on the main menu: over it (closed when a game starts)
        if (hud == null) return null;
        var open = UiWindows.Open.FirstOrDefault(o => o.Mod == mod && o.Id == id && o.IsOpen);
        if (open != null) return open;
        var game = new a9t();
        game.Text = title;
        var w = new UiWindow(game) { Mod = mod, Id = id };
        // height: as if the title bar were 22 high (the content keeps its room at every interface size)
        // at least as wide as its title in the title font (a9t's title label), plus the close button
        var titleLabel = game.Controls.Cast<euz>().OfType<avo>().FirstOrDefault();
        var titleFont = titleLabel == null || dr3.a() == null ? null : dr3.a().GetResource<Microsoft.Xna.Framework.Graphics.SpriteFont>(titleLabel.g());
        if (titleFont != null) width = Math.Max(width, (int)Math.Ceiling(titleFont.MeasureString(title ?? "").X) + titleLabel.Location.X + 2 * w.TitleHeight + 8);
        // never shorter than the frame's picture (wj.b7, 96 high): below that its middle gets a negative height and draws
        // above the window
        var frame = UiWindows.FrameField == null ? (object)null : UiWindows.FrameField.GetValue(game);
        int minHeight = frame is esj ? UnderrailDropdownScroll.H((esj)frame) + 4 : 100;
        game.Size = new esj(width, Math.Max(minHeight, height - 22 + w.TitleHeight));
        game.a((EventHandler<el5>)((sender, e) => w.Close()));     // its own close button
        Add(hud, w, (hud.Width - UnderrailDropdownScroll.W(game.Size)) / 2, Math.Max(10, hud.Height / 6));
        var place = mod.Settings.Get("window." + id);
        if (!string.IsNullOrEmpty(place)) Restore(w, place);
        UiWindows.Open.Add(w);
        return w;
    }
}

static class UiWindows
{
    internal static readonly System.Reflection.FieldInfo FrameField = typeof(a9t).GetField("f", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);     // a9t.f: the frame picture's size
    internal static readonly System.Reflection.FieldInfo TitleField = typeof(a9t).GetField("i", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.Public);     // a9t.i: also a method name
    internal static readonly List<UiWindow> Open = new List<UiWindow>();
    static UiWindow dragging;
    static Point dragStart, windowStart;

    internal static bool HoldEscape { get { return Open.Any(w => w.IsOpen); } }

    // dka.a(sender, key event): the in-game key handler; Escape (ane.a == 27) closes the topmost game screen or opens
    // the game menu (dka.e()). With a mod window open, Escape closes that window instead and the game never sees it
    // (not while typing in a text box: the game's own fields keep Escape).
    static readonly System.Reflection.FieldInfo KeyField = typeof(ane).GetField("a");
    internal static bool KeyPressed(ane __1)
    {
        try
        {
            if (Open.Count == 0 || __1 == null || Convert.ToInt32(KeyField.GetValue(__1)) != 27) return true;
            Open.RemoveAll(w => !w.IsOpen);
            if (Open.Count == 0) return true;
            Open[Open.Count - 1].Close();
            return false;
        }
        catch (Exception ex) { Log.Write("windows: " + ex.Message); return true; }
    }

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        var any = System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic | System.Reflection.BindingFlags.Public;
        var handler = typeof(dka).GetMethods(any).First(m => m.Name == "a" && m.GetParameters().Length == 2 && m.GetParameters()[0].ParameterType == typeof(object) && m.GetParameters()[1].ParameterType == typeof(ane));
        harmony.Patch(handler, prefix: new HarmonyLib.HarmonyMethod(typeof(UiWindows).GetMethod("KeyPressed", System.Reflection.BindingFlags.Static | System.Reflection.BindingFlags.NonPublic)));
    }

    internal static void Tick()
    {
        UiPopups.Tick();
        Open.RemoveAll(w => !w.IsOpen);
        if (Open.Count == 0) { dragging = null; return; }
        var m = Input.Mouse;
        if (dragging == null && Input.MousePressed(MouseButton.Left))
        {
            for (int i = Open.Count - 1; i >= 0; i--)
            {
                var b = Open[i].Bounds;
                if (!b.Contains(m)) continue;
                int title = Open[i].TitleHeight;
                if (m.Y < b.Y + title && m.X < b.Right - title) { dragging = Open[i]; dragStart = m; windowStart = new Point(dragging.X, dragging.Y); }
                break;
            }
        }
        if (dragging == null) return;
        if (!dragging.IsOpen) { dragging = null; return; }
        var to = Ui.KeepInside(dragging, windowStart.X + m.X - dragStart.X, windowStart.Y + m.Y - dragStart.Y);
        Ui.Move(dragging, to.X, to.Y);
        if (!Input.MouseDown(MouseButton.Left))
        {
            var place = Ui.Place(dragging);
            if (place != null) dragging.Mod.Settings.Set("window." + dragging.Id, place);
            dragging = null;
        }
    }

    // The HUD went away: its windows with it.
    internal static void HudGone() { foreach (var w in Open.ToList()) w.Close(); Open.Clear(); dragging = null; }
}
}
