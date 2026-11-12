// UnderRailWorks: asking the player something, in the game's own popups. Each one is the game's window, so its frame,
// fonts, button sizes, keys and screen dim are right for every resolution and interface size (UI.md). Use these
// rather than building a window with a text box or buttons:
//   Ui.Ask        a line of text (a name)          the save-name popup (e4u)
//   Ui.AskNumber  a number in a range (how many)   the split-stack popup (tg)
//   Ui.Confirm    yes / no                         the message box (bnr), as "Load it anyway?"
//   Ui.Choose     one of a few buttons             the message box (bnr)
// All return false when there's no interface to show them on; the answer comes later, through the callback (not at
// all when the player cancels).
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
public static partial class Ui
{
    // A line of text, in the save-name popup with the question above the box. OK (or Enter) passes the trimmed text
    // (OK stays disabled while it's empty); Cancel or Escape don't. Letters, digits and spaces, up to 30, plus the
    // characters in alsoAllow (".:" for an address, "-'" for names...).
    //   Ui.Ask(this, "What will you call your dog?", "Rex", name => { ... });
    public static bool Ask(WorksMod mod, string question, string initial, Action<string> onAnswer, string okText = "OK", string alsoAllow = null)
    {
        if (PlayerScreens.Route(new RemotePopup { Kind = PopupKind.Ask, Text = question, Initial = initial, Buttons = new[] { okText }, AlsoAllow = alsoAllow }, t => { if (onAnswer != null && !string.IsNullOrEmpty(t)) onAnswer(t); })) return true;
        var screen = UiPopups.Screen();
        if (screen == null) return false;
        var popup = new e4u(string.IsNullOrEmpty(initial) ? "<New>" : initial);
        var ok = UiPopups.Field<dak>(popup, "c");
        if (ok != null && okText != null) ok.Text = okText;
        UiPopups.AddQuestion(popup, question);
        if (!string.IsNullOrEmpty(alsoAllow)) { var box = UiPopups.Field<c68>(popup, "b"); if (box != null) foreach (var ch in alsoAllow) if (!box.m().Contains(ch)) box.m().Add(ch); }     // e4u.a(c68): its typed-character list
        popup.a((EventHandler)((sender, e) => UiPopups.Answer(mod, () => { var text = popup.e(); if (text.Length > 0 && onAnswer != null) onAnswer(text); })));
        UiPopups.Show(popup, screen);
        popup.d();      // the box has the keyboard (again over the next frames: a conversation closing meanwhile takes it back)
        UiPopups.KeepFocus(popup);
        return true;
    }

    // A number from min to max, in the split-stack popup (slider and a typed number) with the question above. OK or
    // Enter passes it; Cancel or Escape don't.
    //   Ui.AskNumber(this, "How many rations?", 1, 20, 5, n => { ... });
    public static bool AskNumber(WorksMod mod, string question, int min, int max, int initial, Action<int> onAnswer, string okText = "OK")
    {
        if (PlayerScreens.Route(new RemotePopup { Kind = PopupKind.AskNumber, Text = question, Min = min, Max = max, Initial = initial.ToString(), Buttons = new[] { okText } }, t => { int n; if (onAnswer != null && int.TryParse(t, out n)) onAnswer(n); })) return true;
        var screen = UiPopups.Screen();
        if (screen == null) return false;
        if (max < min) { var t = min; min = max; max = t; }
        if (max == min) max = min + 1;     // the slider needs a range
        var popup = new tg(max + 1);
        var slider = UiPopups.Field<oz>(popup, "d");
        var remainder = UiPopups.Field<avo>(popup, "b");     // "what's left" of a split: not ours
        var ok = UiPopups.Field<zt>(popup, "e");
        if (slider == null) return false;
        slider.b(min); slider.a(max); slider.e(Math.Max(min, Math.Min(max, initial)));
        if (remainder != null)
        {
            remainder.Visible = false;
            int left = Math.Max(remainder.Location.X, 10), gain = slider.Location.X - left;
            UnderrailDropdownScroll.Move(slider, left, slider.Location.Y);
            slider.Size = new esj(UnderrailDropdownScroll.W(slider.Size) + gain, UnderrailDropdownScroll.H(slider.Size));
        }
        if (ok != null && okText != null) ok.Text = okText;
        UiPopups.AddQuestion(popup, question, remainder == null ? null : remainder.g());     // in the popup's own font
        popup.c((EventHandler<EventArgs>)((sender, e) => UiPopups.Answer(mod, () => { if (onAnswer != null) onAnswer(slider.l()); })));
        UiPopups.Show(popup, screen);
        popup.a9();
        return true;
    }

    // Yes or no, in the game's message box (title, text, a button per answer). Enter: yes; Escape: no.
    //   Ui.Confirm(this, "Release Max", "Give Max to the Housing Corporation? This can't be undone.", () => { ... });
    public static bool Confirm(WorksMod mod, string title, string text, Action onYes, string yes = "Yes", string no = "No", Action onNo = null)
    {
        return Choose(mod, title, text, i => { if (i == 0) { if (onYes != null) onYes(); } else if (onNo != null) onNo(); }, yes, no);
    }

    // One of a few answers (buttons top to bottom) in the game's message box; onChoice gets the button's index.
    // Enter: the first; Escape: the last.
    public static bool Choose(WorksMod mod, string title, string text, Action<int> onChoice, params string[] buttons)
    {
        if (PlayerScreens.Route(new RemotePopup { Kind = PopupKind.Choose, Title = title, Text = text, Buttons = buttons }, t => { int n; if (onChoice != null && int.TryParse(t, out n)) onChoice(n); })) return true;
        var screen = UiPopups.Screen();
        if (screen == null || buttons == null || buttons.Length == 0) return false;
        var choices = buttons.Select((b, i) => new cpk(i.ToString(), b)).ToArray();
        var reply = new UiPopups.Reply(tag => UiPopups.Answer(mod, () => { int i; if (onChoice != null && int.TryParse(tag, out i)) onChoice(i); }));
        bnr.a(UiPopups.MessageSize(title, text, buttons.Length), reply, title ?? "", text ?? "", "0", (buttons.Length - 1).ToString(), null, null, 0, choices);
        return true;
    }
}
}

namespace UnderRailWorks.Internal
{
static class UiPopups
{
    // Testing: the game's split popup exactly as the game makes it.
    internal static void RawSplit(int total) { var screen = Screen(); if (screen == null) return; var p = new tg(total); Show(p, screen); p.a9(); }
    internal static euz Screen() { var ui = bjn.b(); return ui == null ? null : ui.aau(); }

    // A private field of a game popup by its (decompiled) name, m_ prefixed or not.
    internal static T Field<T>(object popup, string name) where T : class
    {
        var t = popup.GetType();
        var f = t.GetField(name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly) ?? t.GetField("m_" + name, BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly);
        if (f == null || !typeof(T).IsAssignableFrom(f.FieldType)) f = t.GetFields(BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.DeclaredOnly).FirstOrDefault(x => x.Name.TrimStart('m', '_') == name && typeof(T).IsAssignableFrom(x.FieldType));
        return f == null ? null : f.GetValue(popup) as T;
    }

    internal static void Answer(WorksMod mod, Action run)
    {
        try { run(); }
        catch (Exception ex) { if (mod != null) ModErrors.Report(mod.Id, "a popup's answer", ex); else Log.Write("ui: a popup's answer failed: " + ex.Message); }
    }

    // Modal on the interface's root, centred on the screen as it is now.
    internal static void Show(euz popup, euz screen)
    {
        popup.bj();
        UnderrailDropdownScroll.Move(popup, (UnderrailDropdownScroll.W(screen.Size) - UnderrailDropdownScroll.W(popup.Size)) / 2, (UnderrailDropdownScroll.H(screen.Size) - UnderrailDropdownScroll.H(popup.Size)) / 2);
    }

    // The theme's popup font (the classic theme: egg.b, a field that shares its name with a method).
    static readonly FieldInfo ClassicFont = typeof(egg).GetField("b", BindingFlags.Static | BindingFlags.Public);
    static string PopupFont() { var theme = wj.a(); return theme.c() ? ClassicFont.GetValue(null) as string : theme.gm; }
    static SpriteFont Font(string name) { var r = dr3.a(); return r == null || name == null ? null : r.GetResource<SpriteFont>(name); }
    static readonly MethodInfo LabelFont = typeof(d7w).GetMethod("a", BindingFlags.Instance | BindingFlags.Public, null, new[] { typeof(string) }, null);

    // A line of text above a popup's parts: the popup grows by a line of its font and the parts move down.
    internal static void AddQuestion(euz popup, string question, string fontName = null)
    {
        if (string.IsNullOrEmpty(question)) return;
        fontName = fontName ?? PopupFont();
        var font = Font(fontName);
        int margin = 10, lineHeight = font == null ? 22 : font.LineSpacing, width = UnderrailDropdownScroll.W(popup.Size);
        var label = new avo();
        if (LabelFont != null) LabelFont.Invoke(label, new object[] { fontName });
        label.a(cab.a);
        label.Text = question;
        label.Size = new esj(width - 2 * margin, lineHeight + 4);
        foreach (euz part in popup.Controls.Cast<euz>().ToList()) UnderrailDropdownScroll.Move(part, part.Location.X, part.Location.Y + lineHeight + margin / 2);
        popup.Size = new esj(width, UnderrailDropdownScroll.H(popup.Size) + lineHeight + margin / 2);
        popup.aox(label);
        UnderrailDropdownScroll.Move(label, margin, margin);
    }

    // The message box's size for this text, in the units bnr.a takes (it scales them by the interface size, wj.g4):
    // its layout (bnr.b) is the title, 10 px, the text word-wrapped to the box's width less margins, then a full-width
    // row per button (35 high) and a 10 px margin. Measured with the box's own fonts at the current interface size.
    internal static esj MessageSize(string title, string text, int buttons)
    {
        var theme = wj.a();
        var scale = theme.c() ? Vector2.One : theme.g4;
        const int baseWidth = 520;
        float width = baseWidth * scale.X;
        var body = Font(theme.c() ? ClassicFont.GetValue(null) as string : theme.g5);
        int lines = WrappedLines(body, text ?? "", width - 2 * 20 * scale.X - 8);
        float line = body == null ? 22 : body.LineSpacing;
        float height = (10 + 6) * scale.Y + (line + 5) + 10 + lines * line + 10 * scale.Y + buttons * 35 * scale.Y + 10 * scale.Y + 8;
        return new esj(baseWidth, (int)Math.Ceiling(height / scale.Y));
    }

    static int WrappedLines(SpriteFont font, string text, float width)
    {
        if (font == null) return Math.Max(1, (text.Length + 59) / 60);
        int lines = 0;
        foreach (var paragraph in text.Split('\n'))
        {
            lines++;
            float x = 0, space = font.MeasureString(" ").X;
            foreach (var word in paragraph.Split(' '))
            {
                float w = font.MeasureString(word).X;
                if (x > 0 && x + space + w > width) { lines++; x = w; }
                else x += (x > 0 ? space : 0) + w;
            }
        }
        return lines;
    }

    // bnr's callback: the chosen button's tag.
    internal sealed class Reply : eyp
    {
        readonly Action<string> chosen;
        internal Reply(Action<string> chosen) { this.chosen = chosen; }
        public void g0(string tag) { chosen(tag); }
    }

    // e4u's box gets the keyboard when it opens, and again over the next frames if something closing in the same
    // moment (a conversation) takes it back.
    static e4u asking;
    static int askFrames;
    internal static void KeepFocus(e4u popup) { asking = popup; askFrames = 30; }
    internal static void Tick()
    {
        if (asking == null) return;
        var box = Field<c68>(asking, "b");
        if (asking.Parent == null || box == null || --askFrames <= 0) { asking = null; return; }
        if (!box.IsFocus) asking.d();
    }
}
}
