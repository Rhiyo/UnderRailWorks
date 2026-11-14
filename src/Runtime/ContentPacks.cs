// UnderRailWorks: content packs, mods written as data instead of code (the way Cataclysm DDA, RimWorld and Caves of Qud
// take most of their mods). Any mod folder may hold content/*.json; a mod needs no DLL for it. Each file can have:
//   "text":       { "quests/my_quest": "Journal text", "ui/my_key": "..." }
//   "quests":     [ { "id": "my_quest", "group": "South Gate Station", "text": "...", "xp": 50, "oddityXp": 1 } ]
//   "dialogs":    { "my/dialog": { "start": "hello", "nodes": {
//                     "hello":  { "text": "Hi.", "do": [ effects ], "options": [
//                                   { "text": "Bye.", "goto": null },
//                                   { "text": "Pay (10)", "goto": "paid", "if": { "money": 10 }, "do": [ { "takeMoney": 10 } ] } ] },
//                     "paid":   { "text": "Thanks.", "next": null },
//                     "choose": { "branch": [ { "if": { "flag": "met" }, "goto": "again" }, { "goto": "hello" } ] } } } }
//   "placements": [ { "id": "my_npc", "zone": "LU-SouthGateStation-N", "area": "LU-SouthGateStation-N", "x": 14, "y": 21,
//                     "blueprint": "creatures\\generichumans\\foundryworker", "name": "Stranger", "dialog": "my/dialog",
//                     "store": "bigbret", "when": { condition } } ]
//   "items":      [ { "id", "base": stock path, "name", "description", "value", "weight", "icon": png, "stats": {...} } ]
//   "itemEdits":  [ { "path", "name", "value", "critChance", "critDamage", "actionPoints", "minStrength", "armorPenalty",
//                     "damage": [ { "type", "min", "max" } ], "resistances": { "Heat": [threshold, percent] } } ]
//   "stores":     [ { "store", "item", "count", "once" } ]     "loot": [ { "item", "chance", "count", "character", "container" } ]
//   "music":      [ { "replace" | "add": id, "file": ogg } ]   "sounds": [ { "id", "file": wav } ]
//   "recipes":    [ { "id", "name", "category", "result", "count", "known", "slots": [ { "label", "code"|"codes", "skill", "count", "optional" } ] } ]
// Conditions (one key each, combinable): flag (+ "is"), atLeast [var, n], equals [var, text], quest + "is"
// (notstarted/active/completed/failed), money n, has item code + "count", skill / ability name + "min", level n,
// not {..}, all [..], any [..].
// Effects: setFlag (+ "to"), setInt + "to", addInt + "by", setString + "to", startQuest, completeQuest, failQuest,
// give item path + "count", take item code + "count", giveMoney n, takeMoney n, xp n, log text, say text (over the
// player), effect status code + "turns".
//   "dialogPatches": { "characters\\bigbret1": { "add": [ { "at": "passed all the tests", "text": "Heard anything odd?",
//                     "goto": "odd", "if": {..}, "do": [..] } ], "nodes": { "odd": { "text": "...", "options": [
//                     { "text": "Anyway...", "goto": "@" } ] } } } }   ("@": back to the stock line; "@text": a stock line)
//   "triggers":   [ { "id": "welcome", "on": "zoneEntered", "zone": "LU-SouthGateStation-N", "firstVisit": true,
//                     "if": { condition }, "do": [ effects ], "once": true } ]
//   Events and their filters: zoneEntered (zone, firstVisit), questChanged (quest, state), levelUp (level: at least),
//   itemAdded (item code), equipped (item code), statusApplied (status code; the player's), characterDied (character:
//   id or name), dialogEnded (dialog), combatStarted, combatEnded, gameLoaded, newGame. "once": only the first time in
//   each game (kept as a variable in the save).
// More conditions: equipped item code, status code (the player has it), zone id, inCombat true/false.
// Code mods add their own for packs: ContentPack.AddCondition(this, "myCheck", args => () => ...) and
// ContentPack.AddEffect(this, "myEffect", args => () => ...), used as { "myCheck": ..., other args }.
// Packs load after every mod's code. Everything goes through Works' API, so packs get the same save rules: placed
// characters are never saved, quests and variables stay stock-loadable.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{

// A condition's or effect's arguments, for ContentPack.AddCondition / AddEffect.
public sealed class PackArgs
{
    internal readonly Dictionary<string, object> Values;
    internal PackArgs(Dictionary<string, object> values) { Values = values; }
    public bool Has(string key) { return Values.ContainsKey(key); }
    public string Text(string key, string fallback = null) { object v; return Values.TryGetValue(key, out v) && v != null ? Convert.ToString(v, CultureInfo.InvariantCulture) : fallback; }
    public double Number(string key, double fallback = 0) { object v; return Values.TryGetValue(key, out v) && v != null ? (v is double ? (double)v : double.Parse(Convert.ToString(v, CultureInfo.InvariantCulture), CultureInfo.InvariantCulture)) : fallback; }
    public int Int(string key, int fallback = 0) { return (int)Number(key, fallback); }
    public bool Bool(string key, bool fallback = false) { object v; if (!Values.TryGetValue(key, out v) || v == null) return fallback; return v is bool ? (bool)v : Text(key) == "1" || string.Equals(Text(key), "true", StringComparison.OrdinalIgnoreCase); }
}

public static class ContentPack
{
    internal static readonly Dictionary<string, Func<PackArgs, Func<bool>>> Conditions = new Dictionary<string, Func<PackArgs, Func<bool>>>(StringComparer.OrdinalIgnoreCase);
    internal static readonly Dictionary<string, Func<PackArgs, Action>> Effects = new Dictionary<string, Func<PackArgs, Action>>(StringComparer.OrdinalIgnoreCase);
    // A condition packs can use as { "key": ... }: make reads the arguments once (at load) and returns the check.
    // Not a built-in key (Works' own come first, so it would never be reached); another mod's key: replaced, logged.
    public static void AddCondition(WorksMod mod, string key, Func<PackArgs, Func<bool>> make)
    {
        if (Array.Exists(ContentPacks.CondMains, k => k.Equals(key, StringComparison.OrdinalIgnoreCase))) { Log.Write("content: " + mod.Id + ": \"" + key + "\" is a built-in condition: not added (name it after the mod)"); return; }
        if (Conditions.ContainsKey(key)) Log.Write("content: " + mod.Id + " replaces the condition " + key + " another mod added");
        Conditions[key] = make; Log.Write("content: " + mod.Id + " adds the condition " + key);
    }
    // An effect packs can use in "do" lists: make reads the arguments and returns what to do.
    public static void AddEffect(WorksMod mod, string key, Func<PackArgs, Action> make)
    {
        if (Array.Exists(ContentPacks.EffectMains, k => k.Equals(key, StringComparison.OrdinalIgnoreCase))) { Log.Write("content: " + mod.Id + ": \"" + key + "\" is a built-in effect: not added (name it after the mod)"); return; }
        if (Effects.ContainsKey(key)) Log.Write("content: " + mod.Id + " replaces the effect " + key + " another mod added");
        Effects[key] = make; Log.Write("content: " + mod.Id + " adds the effect " + key);
    }
}

static class ContentPacks
{
    internal static void Load(ModInfo mod)
    {
        var dir = Path.Combine(mod.Folder, "content");
        if (!Directory.Exists(dir)) return;
        foreach (var file in Directory.GetFiles(dir, "*.json").OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
        {
            var where = mod.Id + "/content/" + Path.GetFileName(file);
            Dictionary<string, object> root;
            try { root = Json.Parse(File.ReadAllText(file)) as Dictionary<string, object>; }
            catch (Exception ex) { Log.Write("content: " + where + ": not valid JSON: " + ex.Message); continue; }
            if (root == null) { Log.Write("content: " + where + ": the file must be a JSON object"); continue; }
            int count = 0;
            foreach (var kv in root)
            {
                try
                {
                    switch (kv.Key.ToLowerInvariant())
                    {
                        case "text": count += LoadText(kv.Value as Dictionary<string, object>); break;
                        case "quests": count += Each(kv.Value, where, "quest", LoadQuest); break;
                        case "dialogs": count += EachNamed(kv.Value, where, "dialog", (name, d) => LoadDialog(name, d, where)); break;
                        case "placements": count += Each(kv.Value, where, "placement", LoadPlacement); break;
                        case "dialogpatches": count += EachNamed(kv.Value, where, "dialog patch", (name, d) => LoadDialogPatch(mod.Id, name, d)); break;
                        case "triggers": count += Each(kv.Value, where, "trigger", t => LoadTrigger(mod.Id, t)); break;
                        case "items": count += Each(kv.Value, where, "item", i => LoadItem(mod, i)); break;
                        case "itemedits": count += Each(kv.Value, where, "item edit", i => LoadItemEdit(mod, i)); break;
                        case "stores": count += Each(kv.Value, where, "store entry", i => LoadStore(mod, i)); break;
                        case "loot": count += Each(kv.Value, where, "loot rule", i => LoadLoot(mod, i)); break;
                        case "music": count += Each(kv.Value, where, "song", i => LoadMusic(mod, i)); break;
                        case "sounds": count += Each(kv.Value, where, "sound", i => LoadSound(mod, i)); break;
                        case "recipes": count += Each(kv.Value, where, "recipe", i => LoadRecipe(mod, i)); break;
                        default: Log.Write("content: " + where + ": unknown section \"" + kv.Key + "\" (text, quests, dialogs, dialogPatches, placements, triggers, items, itemEdits, stores, loot, music, sounds, recipes)"); break;
                    }
                }
                catch (Exception ex) { Log.Write("content: " + where + ": " + kv.Key + ": " + ex.Message); }
            }
            Log.Write("content: " + where + ": " + count + " entries");
        }
    }

    // Each entry on its own: a bad one is reported (file, which entry) and the rest still load.
    static int Each(object list, string where, string what, Action<Dictionary<string, object>> load)
    {
        int n = 0, i = 0;
        if (list != null && !(list is List<object>)) { Log.Write("content: " + where + ": the " + what + " section must be a list [ ... ]"); return 0; }
        foreach (var o in (list as List<object>) ?? new List<object>())
        {
            i++;
            var d = o as Dictionary<string, object>;
            object id = null; if (d != null) d.TryGetValue("id", out id);
            try { if (d == null) throw new FormatException("must be an object { ... }"); load(d); n++; }
            catch (Exception ex) { Log.Write("content: " + where + ": " + what + " " + i + (id != null ? " (\"" + id + "\")" : "") + ": " + ex.Message); }
        }
        return n;
    }
    static int EachNamed(object map, string where, string what, Action<string, Dictionary<string, object>> load)
    {
        int n = 0;
        var d = map as Dictionary<string, object>;
        if (map != null && d == null) { Log.Write("content: " + where + ": the " + what + " section must be an object { \"name\": { ... } }"); return 0; }
        foreach (var kv in d ?? new Dictionary<string, object>())
        {
            try { var v = kv.Value as Dictionary<string, object>; if (v == null) throw new FormatException("must be an object { ... }"); load(kv.Key, v); n++; }
            catch (Exception ex) { Log.Write("content: " + where + ": " + what + " \"" + kv.Key + "\": " + ex.Message); }
        }
        return n;
    }

    // --- Sections --------------------------------------------------------------------------------------------------

    static int LoadText(Dictionary<string, object> text)
    {
        int n = 0;
        foreach (var kv in text ?? new Dictionary<string, object>())
        {
            int slash = kv.Key.IndexOf('/');
            if (slash <= 0) throw new FormatException("text keys are \"category/key\": " + kv.Key);
            Text.Set(kv.Key.Substring(0, slash), kv.Key.Substring(slash + 1), Str(kv.Value)); n++;
        }
        return n;
    }

    static void LoadQuest(Dictionary<string, object> q)
    {
        Quests.Define(Req(q, "id"), Opt(q, "group") ?? "", Req(q, "text"), Int(q, "xp"), Int(q, "oddityXp"));
    }

    static void LoadPlacement(Dictionary<string, object> p)
    {
        World.Place(new Placement
        {
            Id = Req(p, "id"), Zone = Req(p, "zone"), Area = Opt(p, "area") ?? Req(p, "zone"), X = Int(p, "x"), Y = Int(p, "y"),
            Blueprint = Req(p, "blueprint"), Name = Opt(p, "name"), Dialog = Opt(p, "dialog"), Store = Opt(p, "store"),
            Friendly = !p.ContainsKey("friendly") || Bool(p["friendly"]),
            When = p.ContainsKey("when") ? Cond(p["when"]) : null,
        });
    }

    static void LoadDialog(string name, Dictionary<string, object> d, string where)
    {
        if (d == null) throw new FormatException("dialog " + name + " must be an object");
        var nodes = d.ContainsKey("nodes") ? d["nodes"] as Dictionary<string, object> : null;
        if (nodes == null || nodes.Count == 0) throw new FormatException("dialog " + name + " has no nodes");
        // Parsed once into a plan; the builder runs it each time the dialog opens (conditions are checked then).
        var start = Opt(d, "start");
        bool cancellable = !d.ContainsKey("cancellable") || Bool(d["cancellable"]);
        Dialogs.Define(name, b =>
        {
            b.Cancellable(cancellable);
            AddNodes(b, nodes);
            if (start != null) b.Start(start);
        });
    }

    static void AddNodes(DialogBuilder b, Dictionary<string, object> nodes)
    {
        foreach (var kv in nodes)
        {
            var n = kv.Value as Dictionary<string, object>;
            if (n == null) continue;
            if (n.ContainsKey("branch"))
            {
                var br = b.Branch(kv.Key);
                foreach (var c in (n["branch"] as List<object>) ?? new List<object>())
                {
                    var cd = (Dictionary<string, object>)c;
                    if (cd.ContainsKey("if")) br.If(Cond(cd["if"]), Opt(cd, "goto")); else br.Else(Opt(cd, "goto"));
                }
                continue;
            }
            var line = b.Line(kv.Key, Str(n.ContainsKey("text") ? n["text"] : ""), Effects(n));
            foreach (var o in (n.ContainsKey("options") ? n["options"] as List<object> : null) ?? new List<object>())
            {
                var od = (Dictionary<string, object>)o;
                var go = od.ContainsKey("goto") ? Opt(od, "goto") : Dialog.End;
                if (od.ContainsKey("enabledIf")) line.OptionGreyed(Req(od, "text"), go, Cond(od["enabledIf"]), Effects(od));
                else line.Option(Req(od, "text"), go, od.ContainsKey("if") ? Cond(od["if"]) : null, Effects(od));
            }
            if (n.ContainsKey("next")) line.Next(Opt(n, "next"));
        }
    }

    // "dialogPatches": { "characters\\abram": { "add": [ { "at": "line text", "text": "...", "goto": "node" or "@",
    //   "if" / "enabledIf": condition, "do": [effects] } ], "nodes": { lines and branches, as in dialogs } } }
    static void LoadDialogPatch(string mod, string dialog, Dictionary<string, object> d)
    {
        if (d == null || !d.ContainsKey("add")) throw new FormatException("a dialog patch needs \"add\"");
        var adds = ((List<object>)d["add"]).Cast<Dictionary<string, object>>().ToList();
        foreach (var a in adds) { Req(a, "at"); Req(a, "text"); }
        var nodes = d.ContainsKey("nodes") ? d["nodes"] as Dictionary<string, object> : null;
        Dialogs.Patch(mod, dialog, p =>
        {
            if (nodes != null) AddNodes(p.Builder, nodes);
            foreach (var a in adds)
            {
                var go = a.ContainsKey("goto") ? Opt(a, "goto") : Dialog.End;
                if (a.ContainsKey("enabledIf")) p.AddOptionGreyed(Opt(a, "at"), Opt(a, "text"), go, Cond(a["enabledIf"]), Effects(a));
                else p.AddOption(Opt(a, "at"), Opt(a, "text"), go, a.ContainsKey("if") ? Cond(a["if"]) : null, Effects(a));
            }
        });
    }

    // A WorksMod stand-in for packs without code (the APIs take a mod for its id and folder).
    sealed class PackMod : WorksMod { }
    static readonly Dictionary<string, WorksMod> packMods = new Dictionary<string, WorksMod>(StringComparer.OrdinalIgnoreCase);
    static WorksMod Mod(ModInfo info)
    {
        if (info.Instance != null) return info.Instance;
        WorksMod m;
        if (!packMods.TryGetValue(info.Id, out m)) packMods[info.Id] = m = new PackMod { Info = info, Log = new ModLog(info.Id) };
        return m;
    }

    // "items": [ { "id": "stim", "base": "consumables\\medicine\\healthhypo", "name": "...", "description": "...",
    //   "value": 300, "weight": 0.2, "icon": "art/stim.png", "stats": { ... as itemEdits } } ]
    static void LoadItem(ModInfo info, Dictionary<string, object> i)
    {
        var mod = Mod(info);
        var icon = Opt(i, "icon");
        var path = Items.Add(mod, Req(i, "id"), Req(i, "base"), d => { Edit(d, i); if (i.ContainsKey("stats")) Edit(d, (Dictionary<string, object>)i["stats"]); });
        if (icon != null) Icons.SetItemIcon(mod, "works." + info.Id + "." + Req(i, "id"), icon);
        Log.Write("content: " + info.Id + " item " + path);
    }

    // "itemEdits": [ { "path": "weapons\\...", "name": "...", "value": 500, "critChance": 0.1, "damage": [ { "type": "Mechanical", "min": 10, "max": 20 } ],
    //   "resistances": { "Heat": [5, 20] } } ]
    static void LoadItemEdit(ModInfo info, Dictionary<string, object> i) { Items.EditDefinition(Mod(info), Req(i, "path"), d => Edit(d, i)); }

    static void Edit(ItemDefinition d, Dictionary<string, object> v)
    {
        if (v.ContainsKey("name")) d.Name = Str(v["name"]);
        if (v.ContainsKey("description")) d.Description = Str(v["description"]);
        if (v.ContainsKey("value")) d.Value = Num(v["value"]);
        if (v.ContainsKey("weight")) d.Weight = Num(v["weight"]);
        if (v.ContainsKey("maxStack")) d.MaxStack = WholeNum(v["maxStack"]);
        if (v.ContainsKey("actionPoints")) d.ActionPoints = WholeNum(v["actionPoints"]);
        if (v.ContainsKey("critChance")) d.CritChance = Num(v["critChance"]);
        if (v.ContainsKey("critDamage")) d.CritDamage = Num(v["critDamage"]);
        if (v.ContainsKey("minStrength")) d.MinStrength = WholeNum(v["minStrength"]);
        if (v.ContainsKey("armorPenalty")) d.ArmorPenalty = Num(v["armorPenalty"]);
        if (v.ContainsKey("damage"))
            d.Damage = ((List<object>)v["damage"]).Cast<Dictionary<string, object>>().Select(x => new DamageRange { Type = Req(x, "type"), Min = Int(x, "min"), Max = Int(x, "max") }).ToList();
        if (v.ContainsKey("resistances"))
            foreach (var kv in (Dictionary<string, object>)v["resistances"]) { var a = (List<object>)kv.Value; d.SetResistance(kv.Key, WholeNum(a[0]), WholeNum(a[1])); }
    }

    // "stores": [ { "store": "bigbret", "item": "path", "count": 3, "once": false } ]
    static void LoadStore(ModInfo info, Dictionary<string, object> s)
    {
        var mod = Mod(info); var path = Req(s, "item"); int n = Math.Max(1, Int(s, "count"));
        if (s.ContainsKey("once") && Bool(s["once"])) Stores.AddOnce(mod, Req(s, "store"), path, n); else Stores.AddRestock(mod, Req(s, "store"), path, n);
    }

    // "loot": [ { "item": "path", "chance": 0.5, "count": 2, "character": "Rathound" (name contains), "container": true } ]
    static void LoadLoot(ModInfo info, Dictionary<string, object> l)
    {
        var who = Opt(l, "character"); bool boxes = l.ContainsKey("container") && Bool(l["container"]);
        Loot.Add(Mod(info), (c, box) => box != null ? boxes : c != null && (who == null || c.Name.IndexOf(who, StringComparison.OrdinalIgnoreCase) >= 0),
            Req(l, "item"), l.ContainsKey("chance") ? Num(l["chance"]) : 1.0, Math.Max(1, Int(l, "count")));
    }

    // "music": [ { "replace": "Junkyard", "file": "music/junk.ogg" } or { "add": "myTrack", "file": "..." } ]
    static void LoadMusic(ModInfo info, Dictionary<string, object> m)
    {
        if (m.ContainsKey("replace")) Music.Replace(Mod(info), Req(m, "replace"), Req(m, "file")); else Music.Add(Mod(info), Req(m, "add"), Req(m, "file"));
    }

    // "sounds": [ { "id": "Interface.ButtonClick", "file": "sounds/click.wav" } ]
    static void LoadSound(ModInfo info, Dictionary<string, object> m) { Sound.Replace(Mod(info), Req(m, "id"), Req(m, "file")); }

    // "recipes": [ { "id", "name", "category": "Medicine", "result": item path, "count": 1, "known": true,
    //   "slots": [ { "label", "code" | "codes": [...], "skill": 0, "count": 1, "optional": false } ] } ]
    static void LoadRecipe(ModInfo info, Dictionary<string, object> r)
    {
        var slots = r.ContainsKey("slots") ? ((List<object>)r["slots"]).Cast<Dictionary<string, object>>().Select(s =>
        {
            var codes = s.ContainsKey("codes") ? ((List<object>)s["codes"]).Select(Str).ToArray() : new[] { Req(s, "code") };
            var slot = RecipeSlot.AnyOf(Req(s, "label"), codes, Int(s, "skill"), Math.Max(1, Int(s, "count")));
            return s.ContainsKey("optional") && Bool(s["optional"]) ? slot.AsOptional() : slot;
        }).ToArray() : new RecipeSlot[0];
        var recipe = Crafting.AddRecipe(Mod(info), Req(r, "id"), Req(r, "name"), Opt(r, "category") ?? "Devices", Req(r, "result"), slots);
        if (r.ContainsKey("count")) recipe.Count = Math.Max(1, Int(r, "count"));
        if (r.ContainsKey("known")) recipe.KnownByDefault = Bool(r["known"]);
    }

    static void LoadTrigger(string mod, Dictionary<string, object> t)
    {
        var on = Req(t, "on");
        var id = Opt(t, "id");
        bool once = t.ContainsKey("once") && Bool(t["once"]);
        if (once && id == null) throw new FormatException("a trigger with \"once\" needs an \"id\"");
        var when = t.ContainsKey("if") ? Cond(t["if"]) : null;
        var effects = Effects(t);
        var onceVar = "cp_" + mod + "_" + id;
        Action fire = () =>
        {
            if (once && Variables.Global.Get(onceVar, false)) return;
            if (when != null && !when.Test()) return;
            if (once) Variables.Global.Set(onceVar, true);
            foreach (var e in effects)
            {
                try { e.Run(); }     // one failing effect doesn't stop the others (nor, after ten, the trigger)
                catch (Exception ex) { Log.Write("content: " + mod + ": trigger " + (id ?? on) + ": an effect failed: " + ex.Message); }
            }
        };
        Func<string, string> f = k => Opt(t, k);
        Func<string, string, bool> match = (want, got) => want == null || string.Equals(want, got, StringComparison.OrdinalIgnoreCase);
        switch (on.ToLowerInvariant())
        {
            case "zoneentered":
                bool? first = t.ContainsKey("firstVisit") ? Bool(t["firstVisit"]) : (bool?)null;
                GameEvents.ZoneLoaded.Add(mod, (zone, firstVisit) => { if (match(f("zone"), zone) && (first == null || first == firstVisit)) fire(); }); break;
            case "questchanged":
                GameEvents.QuestChanged.Add(mod, (quest, state) => { if (match(f("quest"), quest) && match(f("state"), state.ToString())) fire(); }); break;
            case "levelup":
                int min = Int(t, "level");
                GameEvents.LeveledUp.Add(mod, level => { if (level >= min) fire(); }); break;
            case "itemadded":
                GameEvents.ItemAdded.Add(mod, item => { if (match(f("item"), item.Code)) fire(); }); break;
            case "equipped":
                GameEvents.Equipped.Add(mod, e => { if (e.Character.IsPlayer && match(f("item"), e.Item.Code)) fire(); }); break;
            case "statusapplied":
                GameEvents.StatusApplied.Add(mod, s => { if (s.Character != null && s.Character.IsPlayer && match(f("status"), s.Code)) fire(); }); break;
            case "characterdied":
                GameEvents.CharacterDied.Add(mod, c => { var want = f("character"); if (want == null || string.Equals(want, c.Key, StringComparison.OrdinalIgnoreCase) || string.Equals(want, c.Name, StringComparison.OrdinalIgnoreCase)) fire(); }); break;
            case "dialogended":
                GameEvents.DialogEnded.Add(mod, d => { if (match(f("dialog"), d)) fire(); }); break;
            case "combatstarted": GameEvents.CombatStarted.Add(mod, fire); break;
            case "combatended": GameEvents.CombatEnded.Add(mod, fire); break;
            case "gameloaded": GameEvents.GameLoaded.Add(mod, fire); break;
            case "newgame": GameEvents.NewGame.Add(mod, fire); break;
            default: throw new FormatException("unknown trigger event \"" + on + "\"");
        }
    }

    // --- Conditions and effects ------------------------------------------------------------------------------------

    // One condition or effect per object: { "flag": "a", "money": 5 } is an error (it used to check only the first),
    // and keys that belong to none are reported (a typo was ignored).
    internal static readonly string[] CondMains = { "not", "all", "any", "flag", "atLeast", "equals", "quest", "money", "has", "skill", "ability", "level", "equipped", "status", "zone", "inCombat" };
    static readonly string[] CondParams = { "is", "count", "min" };
    internal static readonly string[] EffectMains = { "setFlag", "setInt", "addInt", "setString", "startQuest", "completeQuest", "failQuest", "give", "take", "giveMoney", "takeMoney", "xp", "log", "say", "effect" };
    static readonly string[] EffectParams = { "to", "by", "count", "turns" };
    static void OneKey(Dictionary<string, object> d, string what, string[] mains, string[] parms, ICollection<string> modKeys)
    {
        Func<string, string[], bool> isIn = (k, list) => Array.Exists(list, x => x.Equals(k, StringComparison.OrdinalIgnoreCase));
        var found = d.Keys.Where(k => isIn(k, mains) || modKeys.Contains(k)).ToList();
        if (found.Count > 1) throw new FormatException("one " + what + " per object (" + string.Join(", ", found.ToArray()) + "): put them in a list" + (what == "condition" ? " under \"all\"" : ""));
        if (found.Count == 1 && isIn(found[0], mains))
        {
            var unknown = d.Keys.Where(k => !k.Equals(found[0], StringComparison.OrdinalIgnoreCase) && !isIn(k, parms)).ToList();
            if (unknown.Count > 0) throw new FormatException("\"" + found[0] + "\" doesn't take " + string.Join(", ", unknown.ToArray()));
        }
    }

    static Condition Cond(object o)
    {
        var d = o as Dictionary<string, object>;
        if (d == null || d.Count == 0) throw new FormatException("a condition must be an object like { \"flag\": \"name\" }");
        OneKey(d, "condition", CondMains, CondParams, ContentPack.Conditions.Keys);
        if (d.ContainsKey("not")) return Condition.Not(Cond(d["not"]));
        if (d.ContainsKey("all")) return Condition.All(((List<object>)d["all"]).Select(Cond).ToArray());
        if (d.ContainsKey("any")) return Condition.Any(((List<object>)d["any"]).Select(Cond).ToArray());
        if (d.ContainsKey("flag")) { var name = Str(d["flag"]); bool want = !d.ContainsKey("is") || Bool(d["is"]); return Condition.Flag(name, want); }
        if (d.ContainsKey("atLeast")) { var a = (List<object>)d["atLeast"]; return Condition.AtLeast(Str(a[0]), WholeNum(a[1])); }
        if (d.ContainsKey("equals")) { var a = (List<object>)d["equals"]; return Condition.Equals(Str(a[0]), Str(a[1])); }
        if (d.ContainsKey("quest")) { var id = Str(d["quest"]); QuestState s; if (!Enum.TryParse(Opt(d, "is") ?? "active", true, out s)) throw new FormatException("quest state: notstarted, active, completed, failed"); return Condition.Quest(id, s); }
        if (d.ContainsKey("money")) { int n = WholeNum(d["money"]); return Condition.Code(() => World.Player != null && World.Player.Inventory.Money >= n); }
        if (d.ContainsKey("has")) { var code = Str(d["has"]); int n = d.ContainsKey("count") ? WholeNum(d["count"]) : 1; return Condition.Code(() => World.Player != null && World.Player.Inventory.Has(code, n, true)); }
        if (d.ContainsKey("skill")) { var s = Str(d["skill"]); int min = WholeNum(d["min"]); return Condition.Code(() => World.Player != null && World.Player.Skill(s) >= min); }
        if (d.ContainsKey("ability")) { var s = Str(d["ability"]); int min = WholeNum(d["min"]); return Condition.Code(() => World.Player != null && World.Player.Ability(s) >= min); }
        if (d.ContainsKey("level")) { int n = WholeNum(d["level"]); return Condition.Code(() => World.Player != null && World.Player.Level >= n); }
        if (d.ContainsKey("equipped")) { var code = Str(d["equipped"]); return Condition.Code(() => World.Player != null && World.Player.Equipment.All.Values.Any(i => i.Code == code)); }
        if (d.ContainsKey("status")) { var code = Str(d["status"]); return Condition.Code(() => World.Player != null && World.Player.HasStatusEffect(code)); }
        if (d.ContainsKey("zone")) { var zone = Str(d["zone"]); return Condition.Code(() => string.Equals(World.ZoneId, zone, StringComparison.OrdinalIgnoreCase)); }
        if (d.ContainsKey("inCombat")) { bool want = Bool(d["inCombat"]); return Condition.Code(() => Combat.InCombat == want); }
        foreach (var kv in ContentPack.Conditions) if (d.ContainsKey(kv.Key)) { var check = kv.Value(new PackArgs(d)); return Condition.Code(check); }
        throw new FormatException("unknown condition: " + string.Join(", ", d.Keys));
    }

    static Outcome[] Effects(Dictionary<string, object> node)
    {
        if (!node.ContainsKey("do")) return new Outcome[0];
        return ((node["do"] as List<object>) ?? new List<object>()).Select(o => Effect((Dictionary<string, object>)o)).ToArray();
    }

    static Outcome Effect(Dictionary<string, object> d)
    {
        OneKey(d, "effect", EffectMains, EffectParams, ContentPack.Effects.Keys);
        // No player yet (a newGame trigger runs before the character exists): effects on the player do nothing.
        Func<Character> p = () => World.Player;
        Func<Action<Character>, Outcome> onPlayer = run => Outcome.Code(() => { var pl = p(); if (pl != null) run(pl); });
        if (d.ContainsKey("setFlag")) { var n = Str(d["setFlag"]); bool v = !d.ContainsKey("to") || Bool(d["to"]); return Outcome.SetFlag(n, v); }
        if (d.ContainsKey("setInt")) return Outcome.SetInt(Str(d["setInt"]), WholeNum(d["to"]));
        if (d.ContainsKey("addInt")) return Outcome.AddInt(Str(d["addInt"]), d.ContainsKey("by") ? WholeNum(d["by"]) : 1);
        if (d.ContainsKey("setString")) return Outcome.SetString(Str(d["setString"]), Str(d["to"]));
        if (d.ContainsKey("startQuest")) return Outcome.StartQuest(Str(d["startQuest"]));
        if (d.ContainsKey("completeQuest")) return Outcome.CompleteQuest(Str(d["completeQuest"]));
        if (d.ContainsKey("failQuest")) return Outcome.FailQuest(Str(d["failQuest"]));
        if (d.ContainsKey("give")) { var path = Str(d["give"]); int n = d.ContainsKey("count") ? WholeNum(d["count"]) : 1; return onPlayer(pl => pl.Inventory.Add(path, n)); }
        if (d.ContainsKey("take")) { var code = Str(d["take"]); int n = d.ContainsKey("count") ? WholeNum(d["count"]) : 1; return onPlayer(pl => pl.Inventory.Take(code, n)); }
        if (d.ContainsKey("giveMoney")) { int n = WholeNum(d["giveMoney"]); return onPlayer(pl => pl.Inventory.AddMoney(n)); }
        if (d.ContainsKey("takeMoney")) { int n = WholeNum(d["takeMoney"]); return onPlayer(pl => pl.Inventory.TakeMoney(n)); }
        if (d.ContainsKey("xp")) { int n = WholeNum(d["xp"]); return onPlayer(pl => pl.GiveExperience(n)); }
        if (d.ContainsKey("log")) return Outcome.Log(Str(d["log"]));
        if (d.ContainsKey("say")) { var t = Str(d["say"]); return Outcome.Code(() => Notify.OverPlayer(t)); }
        if (d.ContainsKey("effect")) { var code = Str(d["effect"]); double turns = d.ContainsKey("turns") ? Num(d["turns"]) : 0; return onPlayer(pl => StatusEffects.Apply(pl, code, turns)); }
        foreach (var kv in ContentPack.Effects) if (d.ContainsKey(kv.Key)) { var run = kv.Value(new PackArgs(d)); return Outcome.Code(run); }
        throw new FormatException("unknown effect: " + string.Join(", ", d.Keys));
    }

    // --- Values ----------------------------------------------------------------------------------------------------
    static string Str(object o) { return o == null ? null : Convert.ToString(o, CultureInfo.InvariantCulture); }
    // A number (JSON's, or text "1.5"; never a thousands separator), a whole one where one is needed.
    static double Num(object o)
    {
        if (o is double) return (double)o;
        double d;
        if (!double.TryParse(Str(o), NumberStyles.Float, CultureInfo.InvariantCulture, out d) || double.IsNaN(d) || double.IsInfinity(d)) throw new FormatException("\"" + Str(o) + "\" isn't a number");
        return d;
    }
    static int WholeNum(object o) { var d = Num(o); if (d != Math.Floor(d)) throw new FormatException(d.ToString(CultureInfo.InvariantCulture) + " isn't a whole number"); return (int)d; }
    static bool Bool(object o)
    {
        if (o is bool) return (bool)o;
        var t = (Str(o) ?? "").Trim().ToLowerInvariant();
        if (t == "1" || t == "true" || t == "yes" || t == "on") return true;
        if (t == "0" || t == "false" || t == "no" || t == "off") return false;
        throw new FormatException("\"" + Str(o) + "\" isn't true or false");
    }
    static string Opt(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? Str(v) : null; }
    static string Req(Dictionary<string, object> d, string k) { var v = Opt(d, k); if (string.IsNullOrEmpty(v)) throw new FormatException("missing \"" + k + "\""); return v; }
    static int Int(Dictionary<string, object> d, string k) { object v; return d.TryGetValue(k, out v) ? WholeNum(v) : 0; }
}
}
