// UnderRailWorks: combat for mods, in Works' own types. What the game does, measured from its code
// (UnderRailWorks.md, "Combat"):
// - The combat manager (crp, the d02 service) keeps one entry per fighter (d4n: amc, AP c, MP d), refilled at each
//   turn start (d4n.a()); turns start in crp.f(), end in crp.ahq(entry, real end, ...), rounds end in crp.c().
// - Hit chance: three builders (euf.a: melee, ranged, thrown) return the chance (bg3.a, 0.10-0.95), used by the UI
//   preview and the attack alike.
// - Every damage source ends in the target's combat component: nw.a(cwt attack) -> cru outcome (miss, shield, block,
//   threshold and resistance, HP change, death). cwt.b = hit, cwt.e = critical, cwt.f = damage per type, cwt.i = who
//   attacked (name, uid); cru.a = HP damage per type, cru.c = killed.
//   Combat.ModifyHitChance.Add(this, h => h.Chance *= 1.1);
//   Combat.ModifyIncomingDamage.Add(this, d => { if (d.Target.IsPlayer) d.Scale(0.8f); });
//   Combat.AttackResolved.Add(this, a => Log.Info(a.AttackerName + " -> " + a.Target.Name + ": " + a.HealthLost));
using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using TimelapseVertigo.Rules.Combat;
using Microsoft.Xna.Framework;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

public sealed class DamagePart
{
    public string Type { get; set; }      // the game's damage type name: Mechanical, Heat, Cold, Electricity, Acid, Bio, Energy, Psionic...
    public int Amount { get; set; }
    public override string ToString() { return Amount + " " + Type; }
}

public sealed class IncomingDamage
{
    public Character Target { get; internal set; }
    public string AttackerName { get; internal set; }     // as the game records it (no attacker for traps and the like: null)
    public bool ByPlayer { get; internal set; }           // the player attacked
    public bool Hit { get; internal set; }
    public bool Critical { get; internal set; }
    public IList<DamagePart> Parts { get; internal set; }
    // Turns the attack into a miss.
    public bool Cancel { get; set; }
    public void Scale(float factor) { foreach (var p in Parts) p.Amount = Math.Max(0, (int)Math.Round(p.Amount * factor)); }
}

public sealed class AttackOutcome
{
    public Character Target { get; internal set; }
    public string AttackerName { get; internal set; }
    public bool ByPlayer { get; internal set; }
    public bool Hit { get; internal set; }
    public bool Critical { get; internal set; }
    public bool Killed { get; internal set; }
    public int HealthLost { get; internal set; }
    public IList<DamagePart> Parts { get; internal set; }   // HP damage per type, after resistances
}

public enum AttackKind { Melee, Ranged, Thrown }

public sealed class HitChance
{
    public Character Attacker { get; internal set; }
    public Character Target { get; internal set; }
    public AttackKind Kind { get; internal set; }
    // 0..1 (the game clamps its own to 0.10-0.95; a mod's value is used as given, within 0..1).
    public double Chance { get; set; }
}

public sealed class TurnPoints
{
    public Character Character { get; internal set; }
    public int ActionPoints { get; set; }
    public int MovementPoints { get; set; }
}

public static partial class Combat
{
    public static readonly Hook<Action<Character>> TurnStarted = new Hook<Action<Character>>("TurnStarted");
    public static readonly Hook<Action<Character>> TurnEnding = new Hook<Action<Character>>("TurnEnding");
    public static readonly Hook<Action<int>> RoundEnded = new Hook<Action<int>>("RoundEnded");
    // Change a fighter's AP and MP for the turn starting (after the game refilled them).
    public static readonly Hook<Action<TurnPoints>> ModifyTurnPoints = new Hook<Action<TurnPoints>>("ModifyTurnPoints");
    // Change a hit chance (also what the game's targeting preview shows).
    public static readonly Hook<Action<HitChance>> ModifyHitChance = new Hook<Action<HitChance>>("ModifyHitChance");
    // Change damage before the target's shields, threshold and resistances (every damage source).
    public static readonly Hook<Action<IncomingDamage>> ModifyIncomingDamage = new Hook<Action<IncomingDamage>>("ModifyIncomingDamage");
    // After an attack or any damage was applied.
    public static readonly Hook<Action<AttackOutcome>> AttackResolved = new Hook<Action<AttackOutcome>>("AttackResolved");

    // d4n's fields b (amc), c (AP), d (MP) share names with methods: by reflection.
    static readonly FieldInfo FighterField = typeof(d4n).GetField("b"), ApField = typeof(d4n).GetField("c"), MpField = typeof(d4n).GetField("d");
    internal static amc Fighter(d4n e) { return e == null ? null : FighterField.GetValue(e) as amc; }
    internal static double Ap(d4n e) { return (double)ApField.GetValue(e); }
    internal static void SetAp(d4n e, double v) { ApField.SetValue(e, v); }
    internal static double Mp(d4n e) { return (double)MpField.GetValue(e); }
    internal static void SetMp(d4n e, double v) { MpField.SetValue(e, v); }

    internal static crp Manager { get { return bpo.GetService<d02>() as crp; } }
    public static bool InCombat { get { var m = Manager; return m != null && m.ahg(); } }
    // Whose turn it is (null outside combat).
    public static Character Current { get { var m = Manager; var e = m == null || !m.ahg() ? null : m.ahe(); var f = Fighter(e); return f == null ? null : Character.Of(f.a8()); } }
    public static int Round { get; internal set; }

    // A fighter's AP and MP (as the combat panel shows them); 0 outside combat.
    public static int ActionPoints(Character c) { var e = Entry(c); return e == null ? 0 : (int)Ap(e); }
    public static void SetActionPoints(Character c, int ap) { var e = Entry(c); if (e != null) SetAp(e, ap); }
    public static int MovementPoints(Character c) { var e = Entry(c); return e == null ? 0 : (int)Mp(e); }
    // Any fighter's entry (crp.ahu(character data)), not only the current one's.
    static d4n Entry(Character c)
    {
        var m = Manager;
        if (m == null || c == null || !m.ahg()) return null;
        var data = c.Entity is c5x ? ((c5x)c.Entity).GetAspect<amc>() : null;
        return data == null || data.aa() == null ? null : m.ahu(data.aa());
    }

    // A line in the chat/combat log, and floating text over a character (the game's own).
    // A line in the combat feed (the log during fights; Notify.Log writes to the chat feed, which a fight hides), as the
    // game's own combat lines (vr, shown through bey.a when the player's side is involved). {0} in the text is the
    // actor's name (default: the player).
    public static void Log(string text, Color? colour = null, Character actor = null)
    {
        actor = actor ?? World.Player;
        var data = actor == null ? null : CharacterData(actor);
        if (data == null) { Notify.Log(text, colour); return; }
        var line = (vr)CombatLineCtor.Invoke(new object[] { new ap9(data), null, text.Replace("{", "{{").Replace("}", "}}").Replace("{{0}}", "{0}"), colour, null });
        bey.a(line, data, ((c5x)actor.Entity).GetAspect<amc>());
        a7v.a(line);
    }
    static readonly ConstructorInfo CombatLineCtor = typeof(vr).GetConstructors().First(c => c.GetParameters().Length == 5);
    static efk CharacterData(Character c) { var e = c.Entity as c5x; var a = e == null ? null : e.GetAspect<amc>(); return a == null ? null : a.aa(); }
    // Damage the game's way (as its traps and scripts do: Defense.a with a hit): resistances and thresholds when
    // resistances is true, shields, the combat log, death, and Works' damage events. type: Mechanical, Heat, Cold,
    // Electricity, Acid, Energy, Bio. Returns the health it took (0 when dead already or immune).
    public static int Damage(Character target, int amount, string type = "Mechanical", Character source = null, bool resistances = true)
    {
        var data = target == null ? null : target.GameData;
        if (data == null || amount <= 0) return 0;
        DamageType dt;
        if (!Enum.TryParse(type ?? "Mechanical", true, out dt)) throw new ArgumentException("unknown damage type " + type);
        var src = source == null ? null : source.GameData;
        var hit = new cwt { b = true, q = resistances, f = new List<csk> { new csk(dt, amount) }, i = src == null ? null : new ap9(src) };
        var result = data.Defense.a(hit);
        return result == null ? 0 : result.a.Sum(p => p.Amount);
    }

    public static void Float(Character over, string text, Color? colour = null, bool big = false) { Notify.Over(over, text, colour, big); }

    // --- From character data (efk) to Works' Character: a scan of the zone, cached --------------------------------
    static readonly ConditionalWeakTable<efk, b0b> owners = new ConditionalWeakTable<efk, b0b>();
    internal static Character OfData(efk data)
    {
        if (data == null) return null;
        b0b e;
        if (owners.TryGetValue(data, out e))
        {
            // Still that character's entity, in the world? A zone change can give the same data a new entity (the
            // player's, measured: the old one no longer counted as the player and every hook saw a stranger).
            var c = e as c5x; var a = c == null ? null : c.GetAspect<amc>();
            if (a != null && a.aa() == data && c.Area != null) return Character.Of(e);
            owners.Remove(data);
        }
        var zone = Areas.CurrentZone();
        if (zone == null) return null;
        foreach (var area in zone.Areas)
            foreach (var x in area.Entities)
            {
                var c = x as c5x;
                var a = c == null ? null : c.GetAspect<amc>();
                if (a != null && a.aa() == data) { owners.Add(data, x); return Character.Of(x); }
            }
        return null;
    }
}

static class CombatHooks
{
    internal static void Install(HarmonyLib.Harmony harmony)
    {
        const BindingFlags any = BindingFlags.Instance | BindingFlags.Static | BindingFlags.Public | BindingFlags.NonPublic;
        Func<string, HarmonyLib.HarmonyMethod> m = n => new HarmonyLib.HarmonyMethod(typeof(CombatHooks).GetMethod(n, any));
        harmony.Patch(typeof(crp).GetMethod("f", any, null, Type.EmptyTypes, null), postfix: m("TurnStart"));
        harmony.Patch(typeof(crp).GetMethod("ahq", any), prefix: m("TurnEnd"));
        harmony.Patch(typeof(crp).GetMethod("c", any, null, Type.EmptyTypes, null), postfix: m("RoundEnd"));
        harmony.Patch(typeof(crp).GetMethod("ahn", any), postfix: m("CombatStart"));
        harmony.Patch(typeof(d4n).GetMethod("a", any, null, Type.EmptyTypes, null), postfix: m("Refill"));
        // The three hit-chance builders (melee, ranged, thrown), told apart by their parameters.
        var builders = typeof(euf).GetMethods(any).Where(x => x.Name == "a" && x.ReturnType == typeof(bg3)).ToList();
        foreach (var b in builders) harmony.Patch(b, postfix: m("Chance"));
        harmony.Patch(typeof(nw).GetMethod("a", any, null, new[] { typeof(cwt) }, null), prefix: m("Incoming"), postfix: m("Resolved"));
        Log.Write("combat: hooks in place (" + builders.Count + " hit chance builders)");
    }

    static void Run(string what, Action a) { try { a(); } catch (Exception ex) { Log.Write("combat: " + what + ": " + ex.Message); } }

    static void CombatStart() { Combat.Round = 1; }
    static void TurnStart(crp __instance) { Run("turn start", () => { if (Combat.TurnStarted.Any) { var c = Combat.Current; if (c != null) Combat.TurnStarted.Run(h => h(c)); } }); }
    static void TurnEnd(d4n __0, bool __1) { if (__1 && Combat.Fighter(__0) != null && Combat.TurnEnding.Any) Run("turn end", () => { var c = Character.Of(Combat.Fighter(__0).a8()); Combat.TurnEnding.Run(h => h(c)); }); }
    static void RoundEnd() { Run("round end", () => { var r = Combat.Round++; if (Combat.RoundEnded.Any) Combat.RoundEnded.Run(h => h(r)); }); }

    static void Refill(d4n __instance)
    {
        if (!Combat.ModifyTurnPoints.Any || Combat.Fighter(__instance) == null) return;
        Run("turn points", () =>
        {
            var p = new TurnPoints { Character = Character.Of(Combat.Fighter(__instance).a8()), ActionPoints = (int)Combat.Ap(__instance), MovementPoints = (int)Combat.Mp(__instance) };
            Combat.ModifyTurnPoints.Run(h => h(p));
            Combat.SetAp(__instance, Math.Max(0, p.ActionPoints)); Combat.SetMp(__instance, Math.Max(0, p.MovementPoints));
        });
    }

    static void Chance(MethodBase __originalMethod, efk __0, efk __1, bg3 __result)
    {
        if (!Combat.ModifyHitChance.Any || __result == null) return;
        Run("hit chance", () =>
        {
            var ps = __originalMethod.GetParameters();
            var kind = ps.Length == 5 ? AttackKind.Melee : ps.Length == 11 ? AttackKind.Ranged : AttackKind.Thrown;
            var h = new HitChance { Attacker = Combat.OfData(__0), Target = Combat.OfData(__1), Kind = kind, Chance = __result.a };
            Combat.ModifyHitChance.Run(x => x(h));
            __result.a = Math.Max(0, Math.Min(1, h.Chance));
        });
    }

    // cwt.i (ap9, who attacked): its field a, the name, shares its name with a method.
    static readonly FieldInfo AttackerNameField = typeof(ap9).GetField("a");
    static string AttackerName(cwt a) { return a.i == null ? null : AttackerNameField.GetValue(a.i) as string; }
    // cwt.i.d: the attacker's unique id (efk.Uid).
    static readonly FieldInfo AttackerUidField = typeof(ap9).GetField("d");
    static bool ByPlayer(cwt a)
    {
        var game = bpo.GetService<cnk>();
        var p = game == null || game.aqu() == null ? null : game.aqu().v();
        return a.i != null && p != null && (Guid)AttackerUidField.GetValue(a.i) == p.Uid;
    }

    static List<DamagePart> Parts(List<csk> list) { return list == null ? new List<DamagePart>() : list.Select(p => new DamagePart { Type = p.DamageType.ToString(), Amount = p.Amount }).ToList(); }

    static void Incoming(nw __instance, cwt __0)
    {
        if (!Combat.ModifyIncomingDamage.Any || __0 == null) return;
        Run("incoming damage", () =>
        {
            var d = new IncomingDamage { Target = Combat.OfData(__instance.a1()), AttackerName = AttackerName(__0), ByPlayer = ByPlayer(__0), Hit = __0.b, Critical = __0.e, Parts = Parts(__0.f) };
            Combat.ModifyIncomingDamage.Run(h => h(d));
            if (d.Cancel) { __0.b = false; return; }
            for (int i = 0; i < __0.f.Count && i < d.Parts.Count; i++) { var p = __0.f[i]; p.Amount = Math.Max(0, d.Parts[i].Amount); __0.f[i] = p; }
        });
    }

    static void Resolved(nw __instance, cwt __0, cru __result)
    {
        if (!Combat.AttackResolved.Any || __0 == null) return;
        Run("attack resolved", () =>
        {
            // A miss comes back with an outcome whose damage list is null (or none at all).
            var parts = __result == null ? new List<DamagePart>() : Parts(__result.a);
            var o = new AttackOutcome { Target = Combat.OfData(__instance.a1()), AttackerName = AttackerName(__0), ByPlayer = ByPlayer(__0), Hit = __0.b && __result != null, Critical = __0.e, Killed = __result != null && __result.c, Parts = parts, HealthLost = parts.Sum(p => p.Amount) };
            Combat.AttackResolved.Run(h => h(o));
        });
    }
}
}
