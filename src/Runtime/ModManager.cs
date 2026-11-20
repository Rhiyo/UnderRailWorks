// UnderRailWorks: the Mods screen, from the main menu's Mods button (as tModLoader's Mods menu): every installed mod
// in a list (the ones that will be on numbered in load order, then the others by name), the selected one's details,
// and buttons to switch it on or off, move it in the load order, use profiles and look in the Mods folder again.
// Changes are staged and shown, and only made on Accept (Cancel, Escape or the close button drop them): mods are then
// switched on and off at once (ModReloader.SwitchOn / SwitchOff), the order is written for the next start, and the main
// menu is built again if a mod that changes it was switched. Mod settings stay in Options -> Works. Only on the main
// menu: switching mods in a game would make its saves with other mods than it was played with.
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;

namespace UnderRailWorks.Internal
{
static class ModManager
{
    static readonly Color Good = new Color(130, 200, 100), Bad = new Color(225, 90, 70), Quiet = new Color(150, 140, 125), Plain = new Color(215, 215, 205),
        White = Color.White, Pending = new Color(235, 190, 80);

    static UiWindow window;
    static UiList list, details;
    static UiElement switchButton, upButton, downButton, topButton, note;
    static List<ModInfo> shown = new List<ModInfo>();
    static ModInfo selected;
    // The staged state: which mods will be on, and the load order of those (top loads first).
    static Dictionary<ModInfo, bool> want = new Dictionary<ModInfo, bool>();
    static List<ModInfo> order = new List<ModInfo>();
    static bool orderChanged, automatic;
    static string profile;     // a profile used: in use (mods.ini @profile) once accepted
    static readonly Dictionary<ModInfo, string> messages = new Dictionary<ModInfo, string>();     // per mod: why it changed, or what went wrong

    static bool IsOn(ModInfo m) { return m.Loaded || m.Enabled; }
    static bool Wants(ModInfo m) { bool w; return want.TryGetValue(m, out w) ? w : IsOn(m); }
    static bool Changed(ModInfo m) { return Wants(m) != IsOn(m); }
    // Switched on but it didn't load (an error): not in the load order; switching it off and on again (or Refresh after
    // fixing it) tries again.
    static bool Failed(ModInfo m) { return m.Enabled && !m.Loaded && !Changed(m); }

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
        window.Closed += () => { window = null; list = null; details = null; MainMenu.ShowButtons(true); };     // without Accept: nothing changes
        MainMenu.ShowButtons(false);     // the menu's buttons go while the screen is up, as for the game's own screens
        want = ModLoader.Mods.ToDictionary(m => m, IsOn);
        order = ModLoader.Mods.Where(m => m.Loaded).OrderBy(m => m.LoadIndex).ToList();
        orderChanged = false; automatic = false; profile = null;
        messages.Clear();

        // Bottom: profiles, automatic order and refresh on the left; Accept and Cancel on the right; a note above.
        var profiles = Ui.Button("Profiles...", Profiles);
        var auto = Ui.Button("Automatic order", AutomaticOrder);
        var rescan = Ui.Button("Refresh", Rescan);
        Ui.Tooltip(rescan, "Looks in the Mods folder again: new mods are listed (on when you accept), removed ones go, changed DLLs are loaded again.");
        var accept = Ui.Button("Accept", Accept);
        var cancel = Ui.Button("Cancel", () => { if (window != null) window.Close(); });
        int bh = new[] { profiles, auto, rescan, accept, cancel }.Max(b => b.Height), gap6 = Ui.S(6);
        int buttonsY = h - pad - bh;
        int acceptX = w - pad - cancel.Width - gap6 - accept.Width;
        Ui.Add(window, cancel, w - pad - cancel.Width, buttonsY);
        Ui.Add(window, accept, acceptX, buttonsY);
        // Profiles / Automatic order / Refresh left of Accept, or in a row of their own above when they don't fit.
        var left = new[] { profiles, auto, rescan };
        int leftW = left.Sum(b => b.Width) + gap6 * (left.Length - 1);
        int leftY = pad + leftW + gap6 * 2 <= acceptX ? buttonsY : buttonsY - bh - gap6;
        int x = pad;
        foreach (var b in left) { Ui.Add(window, b, x, leftY); x += b.Width + gap6; }
        note = Ui.Label("", w - 2 * pad);
        int noteY = Math.Min(buttonsY, leftY) - Ui.LineHeight - Ui.S(6);
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
        topButton = Ui.Button("To top", ToTop);
        int rightX = pad * 2 + listW, rightW = w - rightX - pad;
        // The buttons in rows as wide as the details (the game's buttons have a minimum width that grows with the
        // interface size: at some sizes four don't fit in one row).
        var buttons = new[] { switchButton, upButton, downButton, topButton };
        int gap = Ui.S(6), rowH = buttons.Max(b => b.Height);
        var rowsOf = new List<List<UiElement>> { new List<UiElement>() };
        int used = 0;
        foreach (var b in buttons)
        {
            if (rowsOf[rowsOf.Count - 1].Count > 0 && used + gap + b.Width > rightW) { rowsOf.Add(new List<UiElement>()); used = 0; }
            used += (rowsOf[rowsOf.Count - 1].Count > 0 ? gap : 0) + b.Width;
            rowsOf[rowsOf.Count - 1].Add(b);
        }
        int actionsY = bottom - rowsOf.Count * rowH - (rowsOf.Count - 1) * gap;
        for (int r = 0; r < rowsOf.Count; r++)
        {
            int bx = rightX;
            foreach (var b in rowsOf[r]) { Ui.Add(window, b, bx, actionsY + r * (rowH + gap)); bx += b.Width + gap; }
        }
        details = Ui.List(rightW, actionsY - Ui.S(6) - top);
        Ui.Add(window, details.View, rightX, top);
        details.Selected += i => details.Selection = -1;     // text, not choices

        selected = order.FirstOrDefault() ?? ModLoader.Mods.FirstOrDefault();
        Refresh();
    }

    // The list as staged, the selection kept.
    static void Refresh()
    {
        if (list == null) return;
        foreach (var m in ModLoader.Mods) if (!want.ContainsKey(m)) want[m] = IsOn(m);
        foreach (var m in want.Keys.Where(m => !ModLoader.Mods.Contains(m)).ToList()) want.Remove(m);
        // The order holds the mods that will run: on (or to be switched on), not ones that failed to load (listed after, in red).
        order.RemoveAll(m => !Wants(m) || Failed(m) || !ModLoader.Mods.Contains(m));
        foreach (var m in ModLoader.Mods.Where(m => Wants(m) && !Failed(m) && !order.Contains(m)).OrderBy(m => m.Loaded ? m.LoadIndex : int.MaxValue)) order.Add(m);
        shown = order.Concat(ModLoader.Mods.Where(m => !order.Contains(m)).OrderBy(m => m.Name, StringComparer.OrdinalIgnoreCase)).ToList();
        var items = new List<string>(); var colours = new List<Color>();
        for (int i = 0; i < shown.Count; i++)
        {
            var m = shown[i];
            bool on = Wants(m);
            if (Changed(m)) { items.Add((on ? (i + 1) + ". " : "    ") + m.Name + (on ? "  (on when accepted)" : "  (off when accepted)")); colours.Add(Pending); }
            else if (m.Loaded) { items.Add((i + 1) + ". " + m.Name); colours.Add(m.Failures > 0 ? Bad : White); }
            else if (!m.Enabled) { items.Add("    " + m.Name + "  (off)"); colours.Add(Quiet); }
            else { items.Add("    " + m.Name + "  (not loaded)"); colours.Add(Bad); }
        }
        int room = list.View.Width - Ui.S(16);
        for (int i = 0; i < items.Count; i++) items[i] = Fit(items[i], room);
        list.Items = items; list.ItemColors = colours;
        list.Selection = selected == null ? -1 : shown.IndexOf(selected);
        int visible = Math.Max(1, (list.View.Height - 4) / Math.Max(1, list.RowHeight));
        if (list.Selection >= 0 && (list.Selection < list.First || list.Selection >= list.First + visible)) list.First = Math.Max(0, list.Selection - visible / 2);     // keep it in view
        int pending = ModLoader.Mods.Count(Changed);
        if (shown.Count == 0) Ui.SetText(note, "No mods installed: each mod is a folder in the game's Mods folder.");
        else if (pending > 0 || orderChanged) Ui.SetText(note, (pending > 0 ? pending + (pending == 1 ? " mod" : " mods") + " to switch" : "Load order changed") + ": Accept makes the changes" + (orderChanged ? " (the order from the next start)" : "") + ".");
        else Ui.SetText(note, "Mods switch on and off when you accept, without a restart. Their settings: Options -> Works.");
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
            if (m.Works != null) add("Built for UnderRailWorks API " + m.Works.Major + "." + m.Works.Minor + (ModLoader.WorksVersionNote(m) != null ? " (this is " + WorksApi.Version.Major + "." + WorksApi.Version.Minor + "; everything it uses is checked when it loads)" : ""), Quiet);
            lines.Add(new KeyValuePair<string, Color>("", Plain));
            if (Changed(m)) add(Wants(m) ? "Switched on when you accept" : "Switched off when you accept", Pending);
            string status = m.Loaded ? "Loaded" + (order.Contains(m) && !orderChanged ? ", " + Ordinal(order.IndexOf(m) + 1) + " in the load order" : "")
                : !m.Enabled ? "Off" : "Not loaded" + (m.Error != null ? ": " + m.Error : "");
            add(status, m.Loaded ? Good : !m.Enabled ? Quiet : Bad);
            string message;
            if (messages.TryGetValue(m, out message)) add(message, message.StartsWith("Couldn't") ? Bad : Quiet);
            if (Wants(m))
            {
                var late = m.Requires.Select(d => ModLoader.Find(d.Id)).Where(d => d != null && order.IndexOf(d) > order.IndexOf(m)).ToList();
                if (late.Count > 0) add("Above " + string.Join(", ", late.Select(d => d.Name).ToArray()) + ", which it needs: it will still load after it.", Bad);
            }
            if (m.Loaded)
            {
                if (m.Warning != null) add("Note: " + m.Warning, Quiet);
                if (m.Failures > 0) add("Errors this session: " + m.Failures + " (last: " + m.LastFailure + "); see UnderRailWorks.log", Bad);
            }
            if (!string.IsNullOrEmpty(m.Description))
            {
                lines.Add(new KeyValuePair<string, Color>("", Plain));
                foreach (var line in m.Description.Split('\n')) add(line.Trim(), Plain);
            }
            lines.Add(new KeyValuePair<string, Color>("", Plain));
            if (m.Requires.Count > 0) add("Requires: " + string.Join(", ", m.Requires.Select(Describe).ToArray()), m.Requires.Any(d => Problem(d) != null) && Wants(m) ? Bad : Quiet);
            if (m.Optional.Count > 0) add("Works with: " + string.Join(", ", m.Optional.Select(Describe).ToArray()), Quiet);
            if (m.Incompatible.Count > 0) add("Not with: " + string.Join(", ", m.Incompatible.Select(d => d.Id).ToArray()), Quiet);
            var needed = Dependents(m).Where(Wants).ToList();
            if (needed.Count > 0) add("Needed by: " + string.Join(", ", needed.Select(d => d.Name).ToArray()), Quiet);
            if (m.Loaded && ModOptions.HasPage(m.Id)) add("Settings: Options -> Works -> " + m.Name, Quiet);
            var links = new[] { m.Homepage, m.License == null ? null : "Licence: " + m.License, m.Game == null ? null : "Made for UnderRail " + m.Game }.Where(l => l != null).ToList();
            if (links.Count > 0) add(string.Join("   ", links), Quiet);
        }
        details.Items = lines.Select(l => l.Key).ToList();
        details.ItemColors = lines.Select(l => l.Value).ToList();
        details.First = 0; details.Selection = -1;
        bool any = m != null;
        Ui.Show(switchButton, any);
        if (any) Ui.SetText(switchButton, Wants(m) ? "Switch off" : "Switch on");
        Ui.Show(upButton, any && order.Contains(m) && order.Count > 1);
        Ui.Show(downButton, any && order.Contains(m) && order.Count > 1);
        Ui.Show(topButton, any && order.Contains(m) && order.IndexOf(m) > 0);
    }

    // Cut to a width, with "..." (a long name ran past the list's edge).
    static string Fit(string text, int width)
    {
        if (Ui.TextWidth(text) <= width) return text;
        while (text.Length > 1 && Ui.TextWidth(text + "...") > width) text = text.Substring(0, text.Length - 1);
        return text.TrimEnd() + "...";
    }

    static string Ordinal(int n) { int t = n % 100; string s = t >= 11 && t <= 13 ? "th" : n % 10 == 1 ? "st" : n % 10 == 2 ? "nd" : n % 10 == 3 ? "rd" : "th"; return n + s; }

    // As staged: a requirement that won't be on is a problem.
    static string Problem(ModDependency d)
    {
        var m = ModLoader.Find(d.Id);
        return m == null ? "not installed" : d.Min != null && (m.Version == null || m.Version < d.Min) ? "version " + (m.Version == null ? "?" : m.VersionText) + " installed"
            : !Wants(m) ? "off" : null;
    }
    static string Describe(ModDependency d)
    {
        var m = ModLoader.Find(d.Id); var p = Problem(d);
        return (m != null ? m.Name : d.Id) + (d.Min != null ? " " + d.Min + "+" : "") + (p == null ? "" : " (" + p + ")");
    }
    static List<ModInfo> Dependents(ModInfo mod) { return ModLoader.Mods.Where(m => m != mod && m.Requires.Any(d => d.Id.Equals(mod.Id, StringComparison.OrdinalIgnoreCase))).ToList(); }

    // Staged: on brings what it requires (and theirs), off takes what requires it (and theirs), each noted.
    static void Switch()
    {
        var m = selected;
        if (m == null) return;
        bool on = !Wants(m);
        want[m] = on;
        messages.Remove(m);
        var queue = new Queue<ModInfo>(new[] { m });
        while (queue.Count > 0)
        {
            var cur = queue.Dequeue();
            var next = on ? cur.Requires.Select(d => ModLoader.Find(d.Id)).Where(d => d != null && !Wants(d)).ToList()
                          : Dependents(cur).Where(Wants).ToList();
            foreach (var d in next)
            {
                want[d] = on;
                messages[d] = on ? "Switched on with " + m.Name + ", which needs it." : "Switched off with " + m.Name + ", which it needs.";
                queue.Enqueue(d);
            }
        }
        Refresh();
    }

    static void Move(int by)
    {
        int i = order.IndexOf(selected), j = i + by;
        if (i < 0 || j < 0 || j >= order.Count) return;
        order[i] = order[j]; order[j] = selected;
        orderChanged = true; automatic = false;
        Refresh();
    }

    static void ToTop()
    {
        int i = order.IndexOf(selected);
        if (i <= 0) return;
        order.RemoveAt(i); order.Insert(0, selected);
        orderChanged = true; automatic = false;
        Refresh();
    }

    // The mods' own order (requirements, sortAfter / sortBefore, then names), without the player's.
    static void AutomaticOrder()
    {
        var keep = ModLoader.PlayerOrder;
        try { ModLoader.PlayerOrder = new List<string>(); order = ModLoader.Order(order.ToList()); }
        finally { ModLoader.PlayerOrder = keep; }
        orderChanged = true; automatic = true;
        Refresh();
    }

    // The Mods folder again: new mods listed (on when accepted, as mods.ini says), removed ones gone, changed DLLs
    // loaded again now (they're already on).
    static void Rescan()
    {
        List<ModInfo> gone;
        var added = ModLoader.Rescan(out gone);
        foreach (var m in added)
        {
            want[m] = m.Enabled && m.Error == null;
            messages[m] = m.Error != null ? "New: can't load: " + m.Error : m.Enabled ? "New: switched on when you accept." : "New (switched off).";
        }
        int reloads = ModReloader.RequestChanged();
        Notify.Log("Mods: " + (added.Count == 0 && gone.Count == 0 && reloads == 0 ? "nothing new in the Mods folder." :
            added.Count + " new, " + gone.Count + " removed" + (reloads > 0 ? ", " + reloads + " changed (loading again)" : "") + "."));
        if (added.Count > 0) selected = added[0];
        Refresh();
    }

    // The staged changes made: off first (what goes may be in the way of what comes), then on in the staged order, then
    // the order written. Closes the window unless something failed (then the reasons are shown).
    static void Accept()
    {
        bool menu = false, failed = false;
        if (profile != null) ModLoader.UseProfile(profile);
        foreach (var m in ModLoader.Mods.Where(m => !Wants(m) && IsOn(m)).ToList())
        {
            if (!IsOn(m)) continue;     // went with a mod switched off before it
            bool touched = TouchesMenu(m);
            var r = ModReloader.SwitchOff(m);
            if (r != null) { messages[m] = "Couldn't switch it off: " + r; failed = true; }
            else { menu |= touched; var left = ModScope.LeftBy(m.Id); if (left.Length > 0) messages[m] = "Its " + string.Join(", ", left) + " stay in the game until you restart it."; }
        }
        foreach (var m in order.Where(m => !m.Loaded && Wants(m)).ToList())
        {
            if (m.Loaded) continue;     // came with a mod switched on before it
            var r = ModReloader.SwitchOn(m);
            if (r != null) { messages[m] = "Couldn't switch it on: " + r; failed = true; }
            else { messages.Remove(m); menu |= TouchesMenu(m); }
        }
        if (orderChanged) ModLoader.SetPlayerOrder(automatic ? null : order.Where(IsOn).Select(m => m.Id).ToList());
        Log.Write("mods: Mods screen accepted" + (failed ? " (some failed)" : ""));
        if (menu) rebuildMenu = true;
        if (failed)
        {
            want = ModLoader.Mods.ToDictionary(m => m, IsOn);
            orderChanged = false;
            Refresh();
            Notify.Log("Mods: some mods couldn't be switched; see the Mods screen.");
            return;
        }
        if (window != null) window.Close();
    }

    // Does mod change the main menu (its buttons, a texture, a handler on its creation, music)?
    static bool TouchesMenu(ModInfo m)
    {
        return MainMenu.HasButtons(m.Id) || AssetStore.Replaces(m.Id) || UiEvents.MainMenuCreated.HasOwner(m.Id) || ModScope.LeftBy(m.Id).Contains("music");
    }
    static bool rebuildMenu;
    // Mods were switched from elsewhere (Mods.Match): the main menu is built again (their buttons).
    internal static void MenuChanged() { rebuildMenu = true; }
    // Every update: the main menu built again once the window closed (not from inside a click on it).
    internal static void Tick()
    {
        if (!rebuildMenu || (window != null && window.IsOpen)) return;
        rebuildMenu = false;
        try { MainMenu.Rebuild(); } catch (Exception ex) { Log.Write("main menu: building it again failed: " + ex.Message); }
    }

    // Profiles: save what's staged; use one (staged too, made on Accept); delete one.
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
                    bool ok = ModLoader.SaveProfile(name, ModLoader.Mods.Select(m => new KeyValuePair<string, bool>(m.Id, Wants(m))), order.Select(m => m.Id).ToList());
                    Notify.Log(ok ? "Mods: profile \"" + ModLoader.ProfileName(name) + "\" saved." : "Mods: couldn't save that profile.");
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
        if (p == null) return;
        foreach (var kv in p.Value.Key)
        {
            var m = ModLoader.Find(kv.Key);
            if (m != null && Wants(m) != kv.Value) { want[m] = kv.Value; messages[m] = "Switched " + (kv.Value ? "on" : "off") + " by the profile \"" + name + "\"."; }
        }
        var wanted = p.Value.Value ?? new List<string>();
        Func<ModInfo, int> rank = m => { int r = wanted.FindIndex(id => id.Equals(m.Id, StringComparison.OrdinalIgnoreCase)); return r < 0 ? int.MaxValue : r; };
        order = ModLoader.Mods.Where(Wants).OrderBy(rank).ThenBy(m => m.Loaded ? m.LoadIndex : int.MaxValue).ToList();
        orderChanged = wanted.Count > 0; automatic = false; profile = name;
        Refresh();
    }
}
}
