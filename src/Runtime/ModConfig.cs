// UnderRailWorks: declarative mod settings, as tModLoader's ModConfig. A mod declares a class deriving from ModConfig
// with public fields; Works finds it, loads it from the mod's settings.ini (writing the file with comments and defaults
// the first time), builds its rows in the Mods tab, and writes changed values back on Accept. The mod writes no UI code.
//
//   public sealed class MyConfig : ModConfig
//   {
//       [Header("Visuals")]
//       [Label("Show the effect")] public bool ShowEffect = true;            // tick box
//       [Label("Strength"), Range(0, 100), Increment(5)] public int Strength = 75;   // slider
//       [Label("Style")] public Style Look = Style.Soft;                      // dropdown (enum; [Label] on members)
//       [Tooltip("Written above the key in settings.ini.")] [Hidden] public bool Debug;   // ini only
//   }
//   ... in the mod: var config = GetConfig<MyConfig>();  config.Strength;  override OnChanged() to react.
//
// Field initialisers are the defaults ("Restore defaults" puts them back). Values are stored by field name: bool 1/0,
// int as a number, enum as its name in lower case (read in any case). Supported types: bool, int, float (as a
// slider in steps), enums; other fields are ini-only.
using System;
using System.IO;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Reflection;
using System.Text;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

[AttributeUsage(AttributeTargets.Field)] public sealed class HeaderAttribute : Attribute { public readonly string Text; public HeaderAttribute(string text) { Text = text; } }
[AttributeUsage(AttributeTargets.Field | AttributeTargets.Class)] public sealed class LabelAttribute : Attribute { public readonly string Text; public LabelAttribute(string text) { Text = text; } }
[AttributeUsage(AttributeTargets.Field)] public sealed class TooltipAttribute : Attribute { public readonly string Text; public TooltipAttribute(string text) { Text = text; } }
[AttributeUsage(AttributeTargets.Field)] public sealed class RangeAttribute : Attribute { public readonly float Min, Max; public RangeAttribute(float min, float max) { Min = min; Max = max; } }
[AttributeUsage(AttributeTargets.Field)] public sealed class IncrementAttribute : Attribute { public readonly float Step; public IncrementAttribute(float step) { Step = step; } }
[AttributeUsage(AttributeTargets.Field)] public sealed class ReloadRequiredAttribute : Attribute { }
[AttributeUsage(AttributeTargets.Field)] public sealed class HiddenAttribute : Attribute { }


public abstract class ModConfig
{
    public ModInfo Mod { get; internal set; }
    // Bumped whenever values change (load, Accept), so code reading settings per frame can keep its own copy.
    public int Changes { get; private set; }

    // After Accept wrote new values.
    public virtual void OnChanged() { }


    ModSettings settings;
    ModConfig defaults;

    internal IEnumerable<FieldInfo> Fields()
    {
        return GetType().GetFields(BindingFlags.Public | BindingFlags.Instance).OrderBy(f => f.MetadataToken);
    }

    internal void Load(ModInfo mod)
    {
        Mod = mod;
        defaults = (ModConfig)Activator.CreateInstance(GetType());
        settings = ModSettings.For(mod.Id, IniText());
        foreach (var f in Fields())
        {
            string v = settings.Get(f.Name);
            if (v == null) continue;
            object parsed = Parse(f.FieldType, v);
            if (parsed != null) f.SetValue(this, parsed);
        }
        Changes++;
    }

    // The settings.ini written the first time: a header, then each field's tooltip and default.
    string IniText()
    {
        var b = new StringBuilder();
        b.Append("; ").Append(Mod.Name).Append(" settings (an UnderRailWorks mod). Options -> Mods in game changes them.\n");
        foreach (var f in Fields())
        {
            var header = f.GetCustomAttribute<HeaderAttribute>();
            if (header != null) b.Append("\n; ").Append(header.Text).Append('\n');
            var tip = f.GetCustomAttribute<TooltipAttribute>();
            var label = f.GetCustomAttribute<LabelAttribute>();
            if (tip != null) foreach (var line in tip.Text.Split('\n')) b.Append("; ").Append(line).Append('\n');
            else if (label != null) b.Append("; ").Append(label.Text).Append(f.FieldType.IsEnum ? " (" + string.Join(", ", Enum.GetNames(f.FieldType).Select(n => n.ToLowerInvariant()).ToArray()) + ")" : "").Append('\n');
            b.Append(f.Name).Append('=').Append(Format(f.GetValue(this))).Append('\n');
        }
        return b.ToString();
    }

    internal static string Format(object v)
    {
        if (v is bool) return (bool)v ? "1" : "0";
        if (v is Enum) return v.ToString().ToLowerInvariant();
        if (v is float) return ((float)v).ToString("R", CultureInfo.InvariantCulture);
        if (v is double) return ((double)v).ToString("R", CultureInfo.InvariantCulture);
        return Convert.ToString(v, CultureInfo.InvariantCulture);
    }

    static object Parse(Type t, string v)
    {
        v = v.Trim();
        try
        {
            if (t == typeof(bool)) { var l = v.ToLowerInvariant(); return l == "1" || l == "true" || l == "on" ? true : l == "0" || l == "false" || l == "off" ? (object)false : null; }
            if (t.IsEnum) { var n = Enum.GetNames(t).FirstOrDefault(x => x.Equals(v, StringComparison.OrdinalIgnoreCase)); return n == null ? null : Enum.Parse(t, n); }
            if (t == typeof(int)) return int.Parse(v.TrimEnd('%', ' '), CultureInfo.InvariantCulture);
            if (t == typeof(float)) return float.Parse(v, CultureInfo.InvariantCulture);
            if (t == typeof(double)) return double.Parse(v, CultureInfo.InvariantCulture);
            if (t == typeof(string)) return v;
        }
        catch { }
        return null;
    }

    // The Mods tab rows (ModOptions builds the page; ModPage writes settings.ini on Accept, then Reload).
    internal void Build(ModPage page)
    {
        bool any = false;
        foreach (var f in Fields())
        {
            if (f.GetCustomAttribute<HiddenAttribute>() != null) continue;
            var header = f.GetCustomAttribute<HeaderAttribute>();
            if (header != null) page.Subsection(header.Text);
            string label = (f.GetCustomAttribute<LabelAttribute>()?.Text ?? Spaced(f.Name)) + (f.GetCustomAttribute<ReloadRequiredAttribute>() != null ? " (after a restart)" : "");
            var range = f.GetCustomAttribute<RangeAttribute>();
            float step = f.GetCustomAttribute<IncrementAttribute>()?.Step ?? 1;
            object v = f.GetValue(this), d = f.GetValue(defaults);
            if (f.FieldType == typeof(bool)) page.Toggle(f.Name, label, (bool)d);
            else if (f.FieldType.IsEnum)
            {
                var names = Enum.GetNames(f.FieldType);
                var texts = names.Select(n => f.FieldType.GetField(n).GetCustomAttribute<LabelAttribute>()?.Text ?? Spaced(n)).ToArray();
                page.Choice(f.Name, label, texts, names.Select(n => n.ToLowerInvariant()).ToArray(), Format(d));
            }
            else if (f.FieldType == typeof(int))
                page.Slider(f.Name, label, (int)(range?.Min ?? 0), (int)(range?.Max ?? 100), Math.Max(1, (int)step), (int)d);
            else if (f.FieldType == typeof(float) || f.FieldType == typeof(double))
                page.Slider(f.Name, label, (double)(range?.Min ?? 0), (double)(range?.Max ?? 1), f.GetCustomAttribute<IncrementAttribute>() == null ? 0.1 : (double)step, Convert.ToDouble(d));
            else if (f.FieldType == typeof(string))
                page.Note(label, (Format(v) ?? "") + "   (edit Mods/" + Path.GetFileName(Mod.Folder) + "/settings.ini)");
            else continue;     // ini only
            any = true;
        }
        // The mod's keys: shown here, changed in the Controls tab (one place for every key).
        var keys = Keybinds.Of(Mod.Id);
        if (keys.Count > 0)
        {
            page.Subsection("Keys (change them in Options -> Controls)");
            foreach (var k in keys) page.Note(k.Label, Keybinds.Name(k.Key));
            any = true;
        }
        if (any)
            page.Button("Default settings", "Reset settings to default", p =>
            {
                p.MarkChanged();     // the mod hears of it on Accept even if every value was a default already
                foreach (var f in Fields())
                    if (f.GetCustomAttribute<HiddenAttribute>() == null) p.Set(f.Name, Format(f.GetValue(defaults)));
            });
    }

    // Reads settings.ini again (after Accept wrote it, or after the mod changed values with Settings.Set).
    public void Reload()
    {
        foreach (var f in Fields())
        {
            string v = settings.Get(f.Name);
            object parsed = v == null ? null : Parse(f.FieldType, v);
            if (parsed != null) f.SetValue(this, parsed);
        }
        Changes++;
        try { OnChanged(); } catch (Exception ex) { Log.Write("mods: " + Mod.Id + ": OnChanged failed: " + ex); }

    }

    static string Spaced(string name)
    {
        var b = new StringBuilder();
        for (int i = 0; i < name.Length; i++)
        {
            if (i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1])) b.Append(' ');
            b.Append(i > 0 && char.IsUpper(name[i]) && !char.IsUpper(name[i - 1]) ? char.ToLowerInvariant(name[i]) : name[i]);
        }
        return b.ToString();
    }

    // --- The loader's side ---------------------------------------------------------------------------------------

    static readonly Dictionary<Type, ModConfig> Loaded = new Dictionary<Type, ModConfig>();
    internal static IEnumerable<ModConfig> All { get { return Loaded.Values.ToList(); } }

    internal static T Get<T>(ModInfo mod) where T : ModConfig
    {
        ModConfig c;
        if (!Loaded.TryGetValue(typeof(T), out c))
        {
            c = (ModConfig)Activator.CreateInstance(typeof(T));
            c.Load(mod);
            Loaded[typeof(T)] = c;
        }
        return (T)c;
    }

    // After a mod's Load: its config classes (loaded if the mod didn't ask for them yet), and their Mods tab rows.
    internal static void Register(ModInfo mod, Assembly assembly)
    {
        var configs = new List<ModConfig>();
        foreach (var t in assembly.GetExportedTypes().Where(t => typeof(ModConfig).IsAssignableFrom(t) && !t.IsAbstract))
        {
            ModConfig c;
            if (!Loaded.TryGetValue(t, out c)) { c = (ModConfig)Activator.CreateInstance(t); c.Load(mod); Loaded[t] = c; }
            configs.Add(c);
        }
        if (configs.Count == 0) return;
        ModOptions.Register(mod.Id, page => { foreach (var c in configs) c.Build(page); }, () => { foreach (var c in configs) c.Reload(); });
        Log.Write("mods: " + mod.Id + ": settings from " + string.Join(", ", configs.Select(c => c.GetType().Name).ToArray()));
    }
}
}
