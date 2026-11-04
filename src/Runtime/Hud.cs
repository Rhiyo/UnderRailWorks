// UnderRailWorks: the game's HUD, windows, trade, prices and status effects for mods, in Works' own types. What the
// game does, measured from its code (UnderRailWorks.md, "HUD, windows, trade, effects"):
// - The in-game screen (ci3) builds its HUD parts once, in its constructor (ci3.c()); a resolution or interface size
//   change builds a new ci3. So a mod moves or hides parts on UiEvents.HudCreated, every time. The HUD toggle resets
//   every part's visibility: Hud.VisibilityChanged is where hidden parts get hidden again.
// - Windows are opened and closed through the screen service (ps: alv inventory, alx notes, ...), as the HUD's
//   buttons and hotkeys do; the game skips them while something modal runs (ps.alg()).
// - Trade: ps.amw(player, merchant) opens barter with a character that has the merchant aspect (dvp). The buy markup
//   is dvp.aqd(buyer), the sell factor b6o.d(): both computed when the barter window opens.
// - Status effects (els) live in the character's br5; durations in ms, a turn is 5000 ms, 0 = permanent. Their types
//   are obfuscated and there is no registry by code: Works builds one from the types it can construct.
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;

public enum HudPart
{
    PlayerPortrait, TargetPortrait, SystemBar, ZoneSecurity, ActionBar, ExperienceBar, AmmoBar, Feed, Alert,
    CombatPanel, SpeedIndicator, ZoomIndicator, SlowDownButton, SpeedUpButton, ZoomOutButton, ZoomInButton,
}

public static class Hud
{
    // ci3's fields per part (ci3.c(), in this order).
    static readonly string[] Fields = { "m_g", "m_h", "m_i", "m_p", "m_j", "m_k", "m_l", "m_m", "m_n", "m_o", "m_q", "m_r", "m_v", "m_u", "w", "x" };
    // (The decompiler shows a field as m_x when a method is also named x; the real field is x.)
    static readonly FieldInfo[] PartFields = Fields.Select(f => typeof(ci3).GetField(f, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)
        ?? typeof(ci3).GetField(f.StartsWith("m_") ? f.Substring(2) : f, BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic)).ToArray();
    internal static ci3 Current;

    // There is an in-game screen (a game is loaded).
    public static bool Exists { get { return Current != null; } }
    // The in-game screen: mods add their own elements to it (they draw above the HUD).
    public static UiElement Root { get { return UiElement.Of(Current); } }

    // A part of the game's HUD (null when the game didn't make it: zoom buttons are optional). Move it or hide it
    // with Ui.Move / Ui.Show, from UiEvents.HudCreated (the game builds the HUD anew on loads and resolution changes).
    // Resizing isn't supported: the parts are drawn at fixed sizes.
    public static UiElement Get(HudPart part)
    {
        var f = PartFields[(int)part];
        return Current == null || f == null ? null : UiElement.Of(f.GetValue(Current) as euz);
    }

    // The whole HUD shown (the game's HUD toggle).
    public static bool Visible
    {
        get { return Current != null && Current.r(); }
        set { var screen = bpo.GetService<ps>(); if (screen != null && Current != null && Current.r() != value) screen.amj(); }
    }

    // After the game showed or hid the HUD (its toggle sets every part's visibility): re-hide parts a mod hides.
    public static readonly Hook<Action<bool>> VisibilityChanged = new Hook<Action<bool>>("HudVisibilityChanged");

    // --- Parts mods add: listed with the game's, so layout tools can move them too ---------------------------------
    static readonly List<HudModPart> modParts = new List<HudModPart>();

    // Adds a mod's element to the HUD as a named part (e.g. "Minimap") at its default place, and announces it
    // (PartAdded), so a layout mod can move it where the player put it. Call it again when the element is rebuilt.
    public static void AddPart(WorksMod mod, string name, UiElement element, int x, int y)
    {
        if (Current == null || element == null) return;
        modParts.RemoveAll(p => p.Name == name || p.Element.Control.Parent == null);
        var part = new HudModPart { Name = name, Mod = mod == null ? "" : mod.Id, Element = element, DefaultX = x, DefaultY = y };
        Ui.Add(Root, element, x, y);
        modParts.Add(part);
        if (PartAdded.Any) PartAdded.Run(h => h(part));
    }

    // The mod parts on the HUD now.
    public static IList<HudModPart> ModParts { get { modParts.RemoveAll(p => p.Element.Control.Parent == null); return modParts.ToList(); } }

    public static readonly Hook<Action<HudModPart>> PartAdded = new Hook<Action<HudModPart>>("HudPartAdded");

    internal static void Created(ci3 screen) { Current = screen; modParts.Clear(); }
    internal static void Toggled(bool shown) { if (VisibilityChanged.Any) VisibilityChanged.Run(h => h(shown)); }
}

public sealed class HudModPart
{
    public string Name { get; internal set; }       // e.g. "Minimap"
    public string Mod { get; internal set; }        // the mod's id
    public UiElement Element { get; internal set; }
    public int DefaultX { get; internal set; }      // where the mod put it
    public int DefaultY { get; internal set; }
}

public enum Window { Inventory, CharacterSheet, CombatStats, Crafting, Notes, Oddities, Psi, Map, QuickInvoker, GameMenu }

public static class Windows
{
    // ps open / close methods per window (the HUD's buttons and hotkeys call these).
    static readonly string[,] Methods = { { "alv", "alw" }, { "al3", null }, { "al4", "al5" }, { "al6", "al7" }, { "alx", "aly" }, { "alz", "al0" }, { "al1", "al2" }, { "al8", "al9" }, { "als", "alt" }, { "alq", "alr" } };
    static readonly MethodInfo CharacterSheetClose = typeof(dka).GetMethod("z", BindingFlags.Instance | BindingFlags.Public | BindingFlags.NonPublic, null, Type.EmptyTypes, null);

    // Opens a window as its HUD button would; false while the game is busy with something modal, or without a game.
    public static bool Open(Window w)
    {
        var screen = bpo.GetService<ps>();
        if (screen == null || !Hud.Exists || screen.alg()) return false;
        Call(screen, Methods[(int)w, 0]);
        return true;
    }

    public static void Close(Window w)
    {
        var screen = bpo.GetService<ps>();
        if (screen == null || !Hud.Exists) return;
        if (w == Window.CharacterSheet) { if (CharacterSheetClose != null) CharacterSheetClose.Invoke(screen, null); return; }
        Call(screen, Methods[(int)w, 1]);
    }

    // Closes the open Options window as its Accept (save: true) or Cancel button would. False if none is open.
    public static bool CloseOptions(bool save = true)
    {
        var options = UiHooks.CurrentOptions;
        if (options == null || options.Parent == null) return false;
        (save ? OptionsAccept : OptionsCancel).Invoke(options, new object[] { null, null });
        return true;
    }
    static readonly MethodInfo OptionsAccept = typeof(t1).GetMethod("a", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(object), typeof(b25) }, null),
        OptionsCancel = typeof(t1).GetMethod("b", BindingFlags.Instance | BindingFlags.NonPublic, null, new[] { typeof(object), typeof(b25) }, null);

    static void Call(ps screen, string name) { typeof(ps).GetMethod(name, Type.EmptyTypes).Invoke(screen, null); }
}

public static class Trade
{
    // Opens barter between the player and a merchant (a character with a store); false otherwise.
    public static bool Open(Character merchant)
    {
        var screen = bpo.GetService<ps>();
        var player = World.Player;
        if (screen == null || player == null || merchant == null || !merchant.IsMerchant) return false;
        return screen.amw(player.Entity, merchant.Entity);
    }
}

public static class Prices
{
    // Change what merchants charge: each handler gets the merchant and the markup so far (the game's: about 1.15-3.5,
    // lower with Mercantile) and returns the new one. Read when the barter window opens.
    public static readonly Hook<Func<Character, float, float>> BuyMarkup = new Hook<Func<Character, float, float>>("BuyMarkup");
    // Change what the player gets for selling: the factor so far (the difficulty's sold item value) -> the new one.
    public static readonly Hook<Func<float, float>> SellFactor = new Hook<Func<float, float>>("SellFactor");

    internal static float Buy(dvp merchant, float markup)
    {
        if (!BuyMarkup.Any) return markup;
        var who = Character.Of(merchant.a8());
        float v = markup;
        BuyMarkup.Run(h => v = h(who, v));
        return Math.Max(0.01f, v);
    }

    internal static float Sell(float factor)
    {
        if (!SellFactor.Any) return factor;
        float v = factor;
        SellFactor.Run(h => v = h(v));
        return Math.Max(0f, v);
    }
}

public enum ApplyResult { Applied, WeakerThanExisting, AppliedAtMaxStacks, Refused, UnknownCode }

public sealed class StatusEffect
{
    internal readonly els Game;
    readonly br5 owner;
    internal StatusEffect(els e, br5 owner) { Game = e; this.owner = owner; }
    public string Code { get { return Game.Code; } }
    public string Name { get { return Game.LocalizedName ?? Game.Name; } }
    public int Stacks { get { return Game.Stacks; } }
    // Turns left (a turn is 5 s outside combat); 0 for permanent effects.
    public double TurnsLeft { get { return Game.Duration == 0 ? 0 : Game.TimeLeft / 5000.0; } }
    public bool Permanent { get { return Game.Duration == 0; } }
    public void Remove() { owner.d(Game); }
    public override string ToString() { return Name + (Stacks > 1 ? " x" + Stacks : "") + (Permanent ? "" : " (" + Math.Ceiling(TurnsLeft) + ")"); }
}

public static class StatusEffects
{
    static Dictionary<string, ConstructorInfo> registry;

    // Every status effect code Works can create (built on first use, from the game's effect types).
    public static IList<string> Codes { get { return Registry.Keys.OrderBy(c => c).ToList(); } }
    public static bool Exists(string code) { return code != null && Registry.ContainsKey(code); }

    // Applies a fresh instance of an effect (by code) as the game's own effect application does (br5.a). turns: its
    // duration (0: the effect's own). Only stock effect types: they are saved with the character.
    public static ApplyResult Apply(Character c, string code, double turns = 0, bool refresh = false)
    {
        ConstructorInfo ctor;
        if (c == null || !c.IsCharacter || code == null || !Registry.TryGetValue(code, out ctor)) return ApplyResult.UnknownCode;
        var effect = (els)ctor.Invoke(ctor.GetParameters().Length == 0 ? new object[0] : new object[] { (int)(turns * 5000) });
        if (turns > 0) SetDuration(effect, (int)(turns * 5000));
        var r = c.Effects.a(effect, refresh, false, false);
        return r == ea4.a ? ApplyResult.Applied : r == ea4.c ? ApplyResult.AppliedAtMaxStacks : r == ea4.b ? ApplyResult.WeakerThanExisting : ApplyResult.Refused;
    }

    static readonly PropertyInfo DurationProperty = typeof(els).GetProperty("Duration");
    static void SetDuration(els e, int ms) { var set = DurationProperty.GetSetMethod(true); if (set != null) set.Invoke(e, new object[] { ms }); }

    static Dictionary<string, ConstructorInfo> Registry
    {
        get
        {
            if (registry != null) return registry;
            var found = new Dictionary<string, ConstructorInfo>(StringComparer.OrdinalIgnoreCase);
            foreach (var t in typeof(els).Assembly.GetTypes().Where(t => typeof(els).IsAssignableFrom(t) && !t.IsAbstract))
            {
                var ctor = t.GetConstructor(Type.EmptyTypes) ?? t.GetConstructor(new[] { typeof(int) });
                if (ctor == null) continue;
                try
                {
                    var e = (els)ctor.Invoke(ctor.GetParameters().Length == 0 ? new object[0] : new object[] { 5000 });
                    if (!string.IsNullOrEmpty(e.Code) && !found.ContainsKey(e.Code)) found[e.Code] = ctor;
                }
                catch { }
            }
            Log.Write("effects: " + found.Count + " status effect codes");
            return registry = found;
        }
    }
}

// The Harmony hooks behind this file.
static class HudHooks
{
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(HudHooks).GetMethod(n, any));
        harmony.Patch(typeof(ci3).GetMethod("a", any, null, new[] { typeof(bool) }, null), postfix: m("Toggled"));
        harmony.Patch(typeof(dvp).GetMethod("aqd", any, null, new[] { typeof(efk) }, null), postfix: m("Markup"));
        harmony.Patch(typeof(b6o).GetMethod("d", any, null, Type.EmptyTypes, null), postfix: m("SellFactorHook"));
    }

    static void Toggled(bool __0) { try { Hud.Toggled(__0); } catch (Exception ex) { Log.Write("hud: " + ex.Message); } }
    static void Markup(dvp __instance, ref float __result) { try { __result = Prices.Buy(__instance, __result); } catch (Exception ex) { Log.Write("prices: " + ex.Message); } }
    static void SellFactorHook(ref float __result) { try { __result = Prices.Sell(__result); } catch (Exception ex) { Log.Write("prices: " + ex.Message); } }
}
