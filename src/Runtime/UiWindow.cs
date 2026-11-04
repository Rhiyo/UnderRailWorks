// UnderRailWorks: a standard window for mods: a title bar with the title and a close button, dragged by its title
// bar, closed by the button or Escape (the topmost one first; the game's menu doesn't open meanwhile), its place
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
    internal UiWindow(UiPanelControl panel) : base(panel) { }
    // Where content starts (below the title bar).
    public int Top { get { return UiWindows.TitleHeight + 4; } }
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
    // A window on the HUD (null if there is no HUD). id: unique within the mod, for remembering its place. Opening a
    // window with an id already open returns that one.
    public static UiWindow Window(WorksMod mod, string id, string title, int width, int height)
    {
        var hud = Hud.Root;
        if (hud == null) return null;
        var open = UiWindows.Open.FirstOrDefault(o => o.Mod == mod && o.Id == id && o.IsOpen);
        if (open != null) return open;
        var w = new UiWindow(new UiPanelControl { Size = new esj(width, height) }) { Mod = mod, Id = id };
        w.Background = new Color(12, 12, 12, 235);
        var bar = Custom(width, UiWindows.TitleHeight, c => { c.Fill(c.Bounds, new Color(181, 92, 0, 200)); c.Text(title, c.Bounds.X + 6, c.Bounds.Y + 2, new Color(20, 12, 4)); });
        Add(w, bar, 0, 0);
        // A small close box (the game's buttons are taller than the title bar); clicks on it are taken in UiWindows.Tick.
        var close = Custom(UiWindows.CloseSize, UiWindows.CloseSize, c =>
        {
            var r = c.Bounds; bool hot = r.Contains(Input.Mouse);
            c.Fill(r, hot ? new Color(255, 210, 120) : new Color(30, 18, 6));
            var ink = hot ? new Color(30, 18, 6) : new Color(230, 160, 60);
            for (int i = 3; i < r.Width - 3; i++) { c.Fill(new Rectangle(r.X + i, r.Y + i, 1, 1), ink); c.Fill(new Rectangle(r.Right - 1 - i, r.Y + i, 1, 1), ink); }
        });
        Add(w, close, width - UiWindows.CloseSize - 2, (UiWindows.TitleHeight - UiWindows.CloseSize) / 2);
        Add(hud, w, (hud.Width - width) / 2, Math.Max(10, hud.Height / 6));
        var place = mod.Settings.Get("window." + id);
        if (!string.IsNullOrEmpty(place)) Restore(w, place);
        UiWindows.Open.Add(w);
        return w;
    }
}

static class UiWindows
{
    internal const int TitleHeight = 22, CloseSize = 16;
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
        Open.RemoveAll(w => !w.IsOpen);
        if (Open.Count == 0) { dragging = null; return; }
        var m = Input.Mouse;
        if (dragging == null && Input.MousePressed(MouseButton.Left))
        {
            for (int i = Open.Count - 1; i >= 0; i--)
            {
                var b = Open[i].Bounds;
                if (!b.Contains(m)) continue;
                if (m.Y < b.Y + TitleHeight && m.X >= b.Right - CloseSize - 4) { Open[i].Close(); return; }
                if (m.Y < b.Y + TitleHeight) { dragging = Open[i]; dragStart = m; windowStart = new Point(dragging.X, dragging.Y); }
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
