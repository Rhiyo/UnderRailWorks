// UnderRailWorks: the Load Game window says when a save's mods differ from the ones running now (as tModLoader marks
// a world in its list; RimWorld and Bethesda's games ask before loading).
// - Each save records the mods that were running (UnderRailWorks.json "running", SaveFiles.Save).
// - Selecting a save: a line under the game's own details: "Mods: 2 missing, 1 added" (orange), red when a missing
//   mod left data in the save. Saves from before Works kept the list only show missing mods with data.
// - Load: only when a missing mod left data, the game's message box asks first and names them. Their data stays in
//   the save either way (kept and written back by later saves), so switching the mod on again later finds it.
// - Order isn't compared: in Works it comes from the mods themselves, and only decides whose handlers run first.
// The game's side (UnderRail 1.3.1.2): arv is the save/load window (m_b SavedGameManipulationMode, q the selected
// a3r, i the "Version: ..." label); arv.a(a3r) fills the details when a save is selected; arv.c(object, b25) is the
// Load/Save button.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks.Internal
{
static class LoadWarning
{
    const BindingFlags Inst = BindingFlags.Instance | BindingFlags.NonPublic | BindingFlags.Public | BindingFlags.DeclaredOnly;
    static FieldInfo ModeField, SaveField, VersionLabel;
    static MethodInfo LoadButton;
    static readonly ConditionalWeakTable<arv, avo> labels = new ConditionalWeakTable<arv, avo>();
    static arv confirmed;     // the window whose Load was just confirmed: let the next press through

    internal static void Install(HarmonyLib.Harmony harmony)
    {
        ModeField = typeof(arv).GetFields(Inst).First(f => f.FieldType == typeof(TimelapseVertigo.UI.SavedGameManipulationMode));
        SaveField = typeof(arv).GetFields(Inst).First(f => f.FieldType == typeof(a3r));
        VersionLabel = typeof(arv).GetField("i", Inst) ?? typeof(arv).GetField("m_i", Inst);
        if (VersionLabel == null || VersionLabel.FieldType != typeof(avo)) throw new MissingFieldException("arv: the version label");
        var selected = typeof(arv).GetMethod("a", Inst, null, new[] { typeof(a3r) }, null);
        LoadButton = typeof(arv).GetMethods(Inst).First(m => !m.IsStatic && m.ReturnType == typeof(void) && m.GetParameters().Select(p => p.ParameterType).SequenceEqual(new[] { typeof(object), typeof(b25) })
            && m.Name == "c");
        harmony.Patch(selected, postfix: new HarmonyLib.HarmonyMethod(typeof(LoadWarning).GetMethod("Selected", BindingFlags.Static | BindingFlags.NonPublic)));
        harmony.Patch(LoadButton, prefix: new HarmonyLib.HarmonyMethod(typeof(LoadWarning).GetMethod("Pressed", BindingFlags.Static | BindingFlags.NonPublic)));
    }

    static bool Loading(arv w) { return ModeField.GetValue(w).ToString() == "Load"; }

    // What differs, for one save: null when nothing worth saying.
    internal sealed class Report { internal List<string> Missing = new List<string>(), Added = new List<string>(), Updated = new List<string>(), MissingWithData = new List<string>(); }
    internal static Report Compare(string folder)
    {
        var saved = SaveFiles.Read(folder);
        var now = Mods.Running().Select(Split).ToDictionary(p => p[0], p => p[1], StringComparer.OrdinalIgnoreCase);
        var r = new Report();
        Func<string, string> name = id => { var m = ModLoader.Find(id); return m == null ? id : m.Name; };
        var then = saved.Running == null ? null : saved.Running.Select(Split).GroupBy(p => p[0], StringComparer.OrdinalIgnoreCase).ToDictionary(g => g.Key, g => g.First()[1], StringComparer.OrdinalIgnoreCase);
        foreach (var kv in saved.WithData)
        {
            if (kv.Key.StartsWith("UnderRailWorks", StringComparison.OrdinalIgnoreCase) || now.ContainsKey(kv.Key)) continue;
            var text = name(kv.Key) + (kv.Value.Length > 0 ? " " + kv.Value : "");
            r.MissingWithData.Add(text);
            if (then == null || !then.ContainsKey(kv.Key)) r.Missing.Add(text);
        }
        if (then != null)
        {
            foreach (var kv in then) if (!now.ContainsKey(kv.Key)) r.Missing.Add(name(kv.Key) + " " + kv.Value);
            foreach (var kv in now) if (!then.ContainsKey(kv.Key)) r.Added.Add(name(kv.Key));
            foreach (var kv in then) if (now.ContainsKey(kv.Key) && now[kv.Key] != kv.Value) r.Updated.Add(name(kv.Key) + " " + kv.Value + " -> " + now[kv.Key]);
        }
        return r.Missing.Count + r.Added.Count + r.Updated.Count == 0 ? null : r;
    }
    static string[] Split(string s) { int at = s.LastIndexOf('@'); return at < 0 ? new[] { s, "" } : new[] { s.Substring(0, at), s.Substring(at + 1) }; }

    internal static string Summary(Report r)
    {
        var parts = new List<string>();
        if (r.Missing.Count > 0) parts.Add(r.Missing.Count + " missing");
        if (r.Added.Count > 0) parts.Add(r.Added.Count + " added");
        if (r.Updated.Count > 0) parts.Add(r.Updated.Count + " updated");
        return "Mods: " + string.Join(", ", parts.ToArray()) + " since this save";
    }

    // After the game filled in the selected save's details.
    static void Selected(arv __instance, a3r __0)
    { try {
        var label = LabelOf(__instance);
        if (label == null) return;
        label.Text = null;
        if (__0 == null || __0.New || !Loading(__instance)) return;
        var r = Compare(__0.FolderName);
        if (r == null) return;
        label.Text = Summary(r);
        label.Color = r.MissingWithData.Count > 0 ? Color.Red : Color.OrangeRed;
    } catch (Exception ex_) { PatchGuard.Failed("LoadWarning.Selected", ex_); }
    }

    // Our line under the game's last detail line ("Version: ..."), in its font.
    static avo LabelOf(arv w)
    {
        avo l;
        if (labels.TryGetValue(w, out l)) return l;
        var below = VersionLabel.GetValue(w) as avo;
        if (below == null) return null;
        l = new avo();
        l.a(below.g());
        int h = UnderrailDropdownScroll.H(below.Size), gap = Math.Max(2, h / 5);
        l.Location = new Point(below.Location.X, below.Location.Y + h + gap);
        l.Size = below.Size;
        l.IsPrivateComponent = true;
        w.aox(l);
        labels.Add(w, l);
        return l;
    }

    // Load pressed: asks first when a missing mod left data in the save.
    static bool Pressed(arv __instance, object __0, b25 __1)
    { try {
        if (!Loading(__instance)) return true;
        if (confirmed == __instance) { confirmed = null; return true; }
        var save = SaveField.GetValue(__instance) as a3r;
        if (save == null || save.New) return true;
        var r = Compare(save.FolderName);
        if (r == null || r.MissingWithData.Count == 0) return true;
        var text = "Mods that kept data in this save aren't running: " + string.Join(", ", r.MissingWithData.ToArray())
            + ". What they added won't be there while they're off (their data stays in the save). Load anyway?";
        Log.Write("load: " + save.FolderName + ": " + Summary(r) + "; with data: " + string.Join(", ", r.MissingWithData.ToArray()));
        var w = __instance;
        return !Ui.Choose(null, "Mods missing", text, i => { if (i != 0) return; confirmed = w; LoadButton.Invoke(w, new object[] { __0, __1 }); }, "Load anyway", "Cancel");
    } catch (Exception ex_) { PatchGuard.Failed("LoadWarning.Pressed", ex_); return true; }
    }
}
}
