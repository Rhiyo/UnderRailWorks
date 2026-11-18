// UnderRailWorks: the Mods screen, from the main menu's Mods button (as tModLoader's Mods menu): every installed mod
// in a list (loaded ones numbered in load order, then the others by name), the selected one's details, and buttons to
// switch it on or off (now: ModReloader.SwitchOn / SwitchOff, with what it requires or what requires it), move it up or
// down in the load order (from the next start), and profiles (saved sets of mods and their order). Mod settings stay in
// Options -> Mods. Only on the main menu: switching mods in a game would make its saves with other mods than it was
// played with.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace UnderRailWorks.Internal
{
static class ModManager
{
    static readonly Color Good = new Color(130, 200, 100), Bad = new Color(225, 90, 70), Quiet = new Color(150, 140, 125), Plain = new Color(215, 215, 205), White = Color.White;

    static UiWindow window;
    static UiList list, details;
    static UiElement switchButton, upButton, downButton, note;
    static List<ModInfo> shown = new List<ModInfo>();
    static List<ModInfo> order = new List<ModInfo>();     // the loaded mods, top loads first (Up / Down change it for the next start)
    static bool orderChanged;
    static ModInfo selected;
    static readonly Dictionary<ModInfo, string> messages = new Dictionary<ModInfo, string>();     // the last switch's result, per mod

    internal static void Open()
    {
        if (window != null && window.IsOpen) return;
        if (Hud.Exists) return;
        var screen = UiPopups.Screen();
        if (!(screen is a8k)) return;
        var root = UiElement.Of(screen);
        int pad = Ui.S(10);
        int w = Math.Min(Ui.S(760), root.Width - Ui.S(40)), h = Math.Min(Ui.S(480), root.Height - Ui.S(40));
        window = Ui.OpenWindow(null, "mods", "Mods", w, h);
        if (window == null) return;
        w = window.Width; h = window.Height;
        window.Closed += () => { window = null; list = null; details = null; };
        order = ModLoader.Mods.Where(m => m.Loaded).OrderBy(m => m.LoadIndex).ToList();
        orderChanged = false;
        messages.Clear();

        // Bottom: profiles and automatic order on the left, a note above them.
        var profiles = Ui.Button("Profiles...", Profiles);
        var auto = Ui.Button("Automatic order", AutomaticOrder);
        int buttonsY = h - pad - Math.Max(profiles.Height, auto.Height);
        Ui.Add(window, profiles, pad, buttonsY);
        Ui.Add(window, auto, pad + profiles.Width + Ui.S(6), buttonsY);
        note = Ui.Label("", w - 2 * pad);
        int noteY = buttonsY - Ui.LineHeight - Ui.S(6);
        Ui.Add(window, note, pad, noteY);
        Ui.SetColor(note, Quiet);

        // Left: the list. Right: the details, the selected mod's buttons under them.
        int top = window.Top + Ui.S(4), listW = (w - 3 * pad) * 2 / 5, bottom = noteY - Ui.S(6);
        list = Ui.List(listW, bottom - top);
        Ui.Add(window, list.View, pad, top);
        list.Selected += i => { if (i >= 0 && i < shown.Count) { selected = shown[i]; ShowDetails(); } };
        switchButton = Ui.Button("Switch off", Switch);
        upButton = Ui.Button("Up", () => Move(-1));
        downButton = Ui.Button("Down", () => Move(1));
        int rightX = pad * 2 + listW, rightW = w - rightX - pad;
        int actionsY = bottom - switchButton.Height;
        Ui.Add(window, switchButton, rightX, actionsY);
        Ui.Add(window, upButton, rightX + switchButton.Width + Ui.S(6), actionsY);
        Ui.Add(window, downButton, rightX + switchButton.Width + upButton.Width + Ui.S(12), actionsY);
        details = Ui.List(rightW, actionsY - Ui.S(6) - top);
        Ui.Add(window, details.View, rightX, top);
        details.Selected += i => details.Selection = -1;     // text, not choices

        selected = order.FirstOrDefault() ?? ModLoader.Mods.FirstOrDefault();
        Refresh();
    }

    // The list as it is now, the selection kept.
    static void Refresh()
    {
        if (list == null) return;
        order.RemoveAll(m => !m.Loaded);
        foreach (var m in ModLoader.Mods.Where(m => m.Loaded && !order.Contains(m)).OrderBy(m => m.LoadIndex)) order.Add(m);
        shown = order.Concat(ModLoader.Mods.Where(m => !m.Loaded).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        var items = new List<string>(); var colours = new List<Color>();
        for (int i = 0; i < shown.Count; i++)
        {
            var m = shown[i];
            if (m.Loaded) { items.Add((i + 1) + ". " + m.Name); colours.Add(m.Failures > 0 ? Bad : White); }
            else if (!m.Enabled) { items.Add("    " + m.Name + "  (off)"); colours.Add(Quiet); }
            else { items.Add("    " + m.Name + "  (not loaded)"); colours.Add(Bad); }
        }
        list.Items = items; list.ItemColors = colours;
        list.Selection = selected == null ? -1 : shown.IndexOf(selected);
        if (shown.Count == 0) Ui.SetText(note, "No mods installed: each mod is a folder in the game's Mods folder.");
        else Ui.SetText(note, orderChanged ? "The new load order applies when you restart the game. Mod settings: Options -> Mods."
            : "Switching a mod on or off applies now. Mod settings: Options -> Mods.");
        ShowDetails();
    }

    static void ShowDetails()
    {
        if (details == null) return;
        var lines = new List<KeyValuePair<string, Color>>();
        var m = selected;
        int width = details.View.Width - Ui.S(16);
        Action<string, Color> add = (text, colour) => { foreach (var part in Ui.Wrap(text, width)) lines.Add(new KeyValuePair<string, Color>(part, colour)); };
        if (m == null) add("No mod selected.", Quiet);
        else
        {
            add(m.Name + (m.Version != null ? "  " + m.VersionText : ""), White);
            if (m.Author != null) add("by " + m.Author, Quiet);
            lines.Add(new KeyValuePair<string, Color>("", Plain));
            string status = m.Loaded ? "Loaded" + (order.Contains(m) ? ", " + Ordinal(order.IndexOf(m) + 1) + " in the load order" : "")
                : !m.Enabled ? "Switched off" : "Not loaded" + (m.Error != null ? ": " + m.Error : "");
            add(status, m.Loaded ? Good : !m.Enabled ? Quiet : Bad);
            string message;
            if (messages.TryGetValue(m, out message)) add(message, message.StartsWith("Couldn't") ? Bad : Quiet);
            if (m.Loaded)
            {
                var late = m.Requires.Select(d => ModLoader.Find(d.Id)).Where(d => d != null && order.IndexOf(d) > order.IndexOf(m)).ToList();
                if (late.Count > 0) add("Above " + string.Join(", ", late.Select(d => d.Name).ToArray()) + ", which it needs: it will still load after it.", Bad);
                if (m.Warning != null) add("Note: " + m.Warning, Quiet);
                if (m.Failures > 0) add("Errors this session: " + m.Failures + " (last: " + m.LastFailure + "); see UnderRailWorks.log", Bad);
            }
            if (!string.IsNullOrEmpty(m.Description))
            {
                lines.Add(new KeyValuePair<string, Color>("", Plain));
                foreach (var line in m.Description.Split('\n')) add(line.Trim(), Plain);
            }
            lines.Add(new KeyValuePair<string, Color>("", Plain));
            if (m.Requires.Count > 0) add("Requires: " + string.Join(", ", m.Requires.Select(Describe).ToArray()), m.Requires.Any(d => Problem(d) != null) ? Bad : Quiet);
            if (m.Optional.Count > 0) add("Works with: " + string.Join(", ", m.Optional.Select(Describe).ToArray()), Quiet);
            if (m.Incompatible.Count > 0) add("Not with: " + string.Join(", ", m.Incompatible.Select(d => d.Id).ToArray()), Quiet);
            var needed = ModReloader.Dependents(m);
            if (needed.Count > 0) add("Needed by: " + string.Join(", ", needed.Select(d => d.Name).ToArray()), Quiet);
            if (m.Loaded && ModOptions.HasPage(m.Id)) add("Settings: Options -> Mods -> " + m.Name, Quiet);
            var links = new[] { m.Homepage, m.License == null ? null : "Licence: " + m.License, m.Game == null ? null : "Made for UnderRail " + m.Game }.Where(l => l != null).ToList();
            if (links.Count > 0) add(string.Join("   ", links), Quiet);
        }
        details.Items = lines.Select(l => l.Key).ToList();
        details.ItemColors = lines.Select(l => l.Value).ToList();
        details.First = 0; details.Selection = -1;
        bool any = m != null;
        Ui.Show(switchButton, any);
        if (any) Ui.SetText(switchButton, m.Loaded || m.Enabled ? "Switch off" : "Switch on");
        Ui.Show(upButton, any && m.Loaded && order.Count > 1);
        Ui.Show(downButton, any && m.Loaded && order.Count > 1);
    }

    static string Ordinal(int n) { int t = n % 100; string s = t >= 11 && t <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th"; return n + s; }

    static string Problem(ModDependency d)
    {
        var m = ModLoader.Find(d.Id);
        return m == null ? "not installed" : d.Min != null && (m.Version == null || m.Version < d.Min) ? "version " + (m.Version == null ? "?" : m.VersionText) + " installed"
            : !m.Enabled ? "off" : m.Loaded ? null : "not loaded";
    }
    static string Describe(ModDependency d)
    {
        var m = ModLoader.Find(d.Id); var p = Problem(d);
        return (m != null ? m.Name : d.Id) + (d.Min != null ? " " + d.Min + "+" : "") + (p == null ? "" : " (" + p + ")");
    }

    static void Switch()
    {
        var m = selected;
        if (m == null) return;
        if (m.Loaded || m.Enabled)
        {
            var also = AllDependents(m);
            Action off = () =>
            {
                var gone = new List<ModInfo>();
                var r = ModReloader.SwitchOff(m, gone);
                if (r != null) messages[m] = "Couldn't switch it off: " + r;
                else
                {
                    var left = ModScope.LeftBy(m.Id);
                    if (left.Length > 0) messages[m] = "Its " + string.Join(", ", left) + " stay in the game until you restart it."; else messages.Remove(m);
                    foreach (var g in gone) messages[g] = "Switched off with " + m.Name + ", which it needs.";
                }
                Refresh();
            };
            if (also.Count > 0) Ui.Confirm(null, "Switch off " + m.Name, string.Join(", ", also.Select(d => d.Name).ToArray()) + (also.Count == 1 ? " needs" : " need") + " it and will be switched off too.", off, "Switch off", "Cancel");
            else off();
        }
        else
        {
            var also = MissingRequirements(m);
            Action on = () =>
            {
                var added = new List<ModInfo>();
                var r = ModReloader.SwitchOn(m, added);
                if (r != null) messages[m] = "Couldn't switch it on: " + r; else messages.Remove(m);
                foreach (var a in added) messages[a] = "Switched on for " + m.Name + ", which needs it.";
                Refresh();
            };
            if (also.Count > 0) Ui.Confirm(null, "Switch on " + m.Name, "It needs " + string.Join(", ", also.Select(d => d.Name).ToArray()) + ", which will be switched on too.", on, "Switch on", "Cancel");
            else on();
        }
    }

    static List<ModInfo> AllDependents(ModInfo m)
    {
        var all = new List<ModInfo>();
        var queue = new Queue<ModInfo>(new[] { m });
        while (queue.Count > 0) foreach (var d in ModReloader.Dependents(queue.Dequeue())) if (!all.Contains(d)) { all.Add(d); queue.Enqueue(d); }
        return all;
    }

    static List<ModInfo> MissingRequirements(ModInfo m)
    {
        var all = new List<ModInfo>();
        var queue = new Queue<ModInfo>(new[] { m });
        while (queue.Count > 0)
            foreach (var d in queue.Dequeue().Requires)
            {
                var dep = ModLoader.Find(d.Id);
                if (dep != null && !dep.Loaded && dep != m && !all.Contains(dep)) { all.Add(dep); queue.Enqueue(dep); }
            }
        return all;
    }

    static void Move(int by)
    {
        int i = order.IndexOf(selected), j = i + by;
        if (i < 0 || j < 0 || j >= order.Count) return;
        order[i] = order[j]; order[j] = selected;
        ModLoader.SetPlayerOrder(order.Select(m => m.Id).ToList());
        orderChanged = true;
        Refresh();
    }

    static void AutomaticOrder()
    {
        ModLoader.SetPlayerOrder(null);
        order = ModLoader.Order(order.ToList());
        orderChanged = true;
        Refresh();
    }

    // Save the mods switched on now and their order; use one (switched now, the order from the next start); delete one.
    static void Profiles()
    {
        var names = ModLoader.Profiles().Take(6).ToList();     // the message box holds a few buttons
        var buttons = new List<string> { "Save as profile..." };
        buttons.AddRange(names.Select(n => "Use \"" + n + "\""));
        if (names.Count > 0) buttons.Add("Delete a profile...");
        buttons.Add("Cancel");
        string current = ModLoader.CurrentProfile;
        Ui.Choose(null, "Profiles", "A profile is a saved set of mods and their order." + (current != null ? " In use: " + current + "." : ""), i =>
        {
            if (i == 0)
                Ui.Ask(null, "Profile name:", current ?? "My mods", name =>
                {
                    bool ok = ModLoader.SaveProfile(name, ModLoader.Mods.Select(m => new KeyValuePair<string, bool>(m.Id, m.Loaded || m.Enabled)), order.Select(m => m.Id).ToList());
                    if (ok) ModLoader.UseProfile(name);
                    Notify.Log(ok ? "Mods: profile \"" + ModLoader.ProfileName(name) + "\" saved." : "Mods: couldn't save that profile.");
                    Refresh();
                }, "Save", " -_'()");
            else if (i >= 1 && i <= names.Count) UseProfile(names[i - 1]);
            else if (names.Count > 0 && i == names.Count + 1)
                Ui.Choose(null, "Delete a profile", "Delete which profile? (The mods themselves stay.)",
                    k => { if (k < names.Count && ModLoader.DeleteProfile(names[k])) Notify.Log("Mods: profile \"" + names[k] + "\" deleted."); }, names.Concat(new[] { "Cancel" }).ToArray());
        }, buttons.ToArray());
    }

    static void UseProfile(string name)
    {
        var p = ModLoader.ReadProfile(name);
        if (p == null || !ModLoader.UseProfile(name)) return;
        // Off first (what the profile leaves out may be in the way of what it switches on), then on.
        foreach (var kv in p.Value.Key.Where(kv => !kv.Value))
        {
            var m = ModLoader.Find(kv.Key);
            if (m != null && (m.Loaded || m.Enabled)) { var r = ModReloader.SwitchOff(m); if (r != null) messages[m] = "Couldn't switch it off: " + r; }
        }
        foreach (var kv in p.Value.Key.Where(kv => kv.Value))
        {
            var m = ModLoader.Find(kv.Key);
            if (m != null && !m.Loaded) { var r = ModReloader.SwitchOn(m); messages[m] = r != null ? "Couldn't switch it on: " + r : "Switched on by the profile."; }
        }
        var want = p.Value.Value ?? new List<string>();
        Func<ModInfo, int> rank = m => { int r = want.FindIndex(id => id.Equals(m.Id, StringComparison.OrdinalIgnoreCase)); return r < 0 ? int.MaxValue : r; };
        order = ModLoader.Mods.Where(m => m.Loaded).OrderBy(rank).ThenBy(m => m.LoadIndex).ToList();
        orderChanged = want.Count > 0;
        Notify.Log("Mods: profile \"" + name + "\" in use.");
        Refresh();
    }
}
}
