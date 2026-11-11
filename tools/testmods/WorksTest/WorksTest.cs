using System;
// Test-only mod (not shipped): keys that set up game state for testing Works in game.
//   F10: give the player 20 charons (Inventory.AddMoney). F11: 5000 XP. U: trade with the test merchant. J: talk to the prospector (content pack test). F12: the stranger turns hostile. F4: hide the portrait,
//   open the notes. F6: list status effect codes and apply one. Logs every Works event.
using System.Linq;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class WorksTest : WorksMod
{
    UiTextBox box;
    int detections, instants, suspicions;
    bool failNow;
    string stimPath;
    bool musicOn;
    ModStatus glow;
    AreaTrigger trigger;
    ModFeat grit;
    bool aiHold, block, aiFlee;
    static int updates;
    Character pet; double petLog, autoWait, autoPoll;
    int startX, startY;
    bool statsOn;
    ModBaseAbility luck; ModSkill survival;
    readonly System.Collections.Generic.Queue<string> autorun = new System.Collections.Generic.Queue<string>();
    int aiLogs;
    TimeSpan gameTime;
    bool blockUse;
    public override void PostLoad() { Log.Info("cross-mod call: " + Mods.Call("WorksTest", "Echo", "hi") + ", typed: " + Mods.Api<TestApi>("WorksTest").Echo("there")); }
    bool logOutlines, hoverWatch;
    string lastHover;
    public override void Load()
    {
        GameEvents.StatusApplying.Add(this, c => { Log.Info("status applying " + c.Code + " on " + (c.Character == null ? "?" : c.Character.Name)); if (c.Code == "Hidden" && Data.GetBool("noHide")) c.Cancel = true; });
        GameEvents.StatusApplied.Add(this, c => Log.Info("status applied " + c.Code + " (" + c.Name + ")"));
        GameEvents.StatusRemoved.Add(this, c => Log.Info("status removed " + c.Code));
        GameEvents.LevelledUp.Add(this, l => Log.Info("levelled up to " + l));
        Items.EditDefinition(this, "consumables\\medicine\\bandage", d => { d.Name = "Field Bandage"; d.Value = 99; });
        stimPath = Items.Define(this, "stim", "consumables\\medicine\\bandage", d => { d.Name = "Works Stim"; d.Description = "A test item defined by a mod."; d.Value = 1234; d.Weight = 0.1; });
        Stores.AddRestock(this, "bigbret", stimPath, 3);
        Stores.AddOnce(this, "bigbret", "armor\\jkktacticalvest", 1);
        Loot.Add(this, (who, box) => who != null && who.Name.Contains("Rathound"), "components\\scraps\\fabricscraps", 1.0, 7);
        glow = ModStatuses.Define(this, "glow", "Works Glow", "A test status from a mod: +1 to a counter each turn.", "Status.PsiBoost", StatBonus.Dodge(15), StatBonus.Ability("Agility", 1));
        // A neutral NPC lays a bear trap next to the player (someone else's trap: the player can spot and disarm it).
        Commands.Add(this, "wt.npctrap", "wt.npctrap: a neutral NPC lays a bear trap near the player", args =>
        {
            var p = World.Player; if (p == null) return "no player";
            var tiles = World.FreeTilesNear((int)p.Position.X, (int)p.Position.Y, 3);
            if (tiles.Count < 2) return "no room";
            var npc = World.Spawn("creatures\\generichumans\\foundryworker", tiles[1].X, tiles[1].Y, SpawnSide.Neutral);
            if (npc == null) return "no npc";
            npc.Inventory.Add("traps\\beartrap", 1, false);
            var trap = npc.Inventory.Items.FirstOrDefault(i => i.Code != null && i.Code.StartsWith("trap."));
            bool used = trap != null && Items.Use(npc, trap);
            Log.Info("npctrap: npc " + npc.Name + " at " + tiles[1] + ", trap " + (trap != null) + ", used " + used);
            return "used: " + used;
        });
        Commands.Add(this, "wt.npcinv", "wt.npcinv: traps carried by characters near the player", args =>
        {
            var p = World.Player; if (p == null) return "no player";
            var lines = World.CharactersNear(p.Position, 6).Where(c => !c.IsPlayer).Select(c => c.Name + " at " + c.Position + ": " + c.Inventory.Items.Count(i => i.Code != null && i.Code.StartsWith("trap.")) + " traps").ToList();
            Log.Info("npcinv: " + string.Join("; ", lines));
            return string.Join("; ", lines);
        });
        Commands.Add(this, "wt.laytrap", "wt.laytrap: the player lays a bear trap (gives one if needed)", args =>
        {
            var p = World.Player; if (p == null) return "no player";
            if (!p.Inventory.Items.Any(i => i.Code != null && i.Code.StartsWith("trap."))) p.Inventory.Add("traps\\beartrap", 1, false);
            var trap = p.Inventory.Items.FirstOrDefault(i => i.Code != null && i.Code.StartsWith("trap."));
            bool used = Items.Use(p, trap);
            Log.Info("laytrap: used " + used);
            return "used: " + used;
        });
        Commands.Add(this, "wt.gun", "wt.gun: a Beretta 99 in hand and 9 mm rounds in the bag", args =>
        {
            var p = World.Player; if (p == null) return "no player";
            p.Inventory.Add("weapons\\beretta99", 1, false); p.Inventory.Add("ammo\\caliber_5_std", 40, false);
            var gun = p.Inventory.Items.FirstOrDefault(i => i.Code == "wep.beretta99");     // the game spells it "Bieretta 99"
            bool ok = gun != null && p.Equipment.Equip(gun);
            Log.Info("gun: equipped " + ok);
            return "equipped: " + ok;
        });
        Commands.Add(this, "wt.reload", "wt.reload: the player uses the bag's first ammo (the game's reload)", args =>
        {
            var p = World.Player; if (p == null) return "no player";
            var ammo = p.Inventory.Items.FirstOrDefault(i => i.Code != null && i.Code.StartsWith("Ammo.Caliber_5"));
            var gun = p.Equipment[EquipSlot.PrimaryWeapon];
            bool ok = ammo != null && Items.Use(p, ammo, gun);     // the ammo's use, on the weapon
            Log.Info("reload command: " + ok + (gun == null ? " (no gun found)" : ""));
            return "used: " + ok;
        });
        // A zone with map traps (lu-b22u: 4 in its first level, found by scanning the map files for trap aspects).
        Commands.Add(this, "wt.b22", "wt.b22: travel to lu-b22u (map traps)", args => "travel: " + World.Travel("lu-b22u", "FromNorth"));
        Commands.Add(this, "wt.traps", "wt.traps: list the zone's traps; go next to the nearest one the player doesn't know", args =>
        {
            var p = World.Player; if (p == null) return "no player";
            var traps = Traps.InZone;
            foreach (var t in traps) Log.Info("trap at " + t.Position + ": detect " + t.DetectDifficulty + ", disarm " + t.DisarmDifficulty + ", known " + t.KnownTo(p) + ", distance " + (int)Microsoft.Xna.Framework.Vector2.Distance(new Microsoft.Xna.Framework.Vector2(t.Position.X, t.Position.Y), new Microsoft.Xna.Framework.Vector2(p.Position.X, p.Position.Y)));
            var near = traps.Where(t => !t.KnownTo(p) && (int)t.Position.Z == (int)p.Position.Z).OrderBy(t => Microsoft.Xna.Framework.Vector2.Distance(new Microsoft.Xna.Framework.Vector2(t.Position.X, t.Position.Y), new Microsoft.Xna.Framework.Vector2(p.Position.X, p.Position.Y))).FirstOrDefault();
            if (near == null) return traps.Count + " traps, none unknown here";
            var tile = World.FreeTilesNear((int)near.Position.X, (int)near.Position.Y, 3).Skip(1).FirstOrDefault();
            bool moved = p.TeleportTo(tile.X, tile.Y);
            Log.Info("traps: " + traps.Count + "; next to the one at " + near.Position + ": " + moved);
            return traps.Count + " traps; teleported " + moved;
        });
        int paceStep = 0;
        Commands.Add(this, "wt.pace", "wt.pace: walk to another free tile near the nearest unknown trap (scans run while moving)", args =>
        {
            var p = World.Player; if (p == null) return "no player";
            var t = Traps.InZone.Where(x => !x.KnownTo(p)).OrderBy(x => Microsoft.Xna.Framework.Vector2.Distance(new Microsoft.Xna.Framework.Vector2(x.Position.X, x.Position.Y), new Microsoft.Xna.Framework.Vector2(p.Position.X, p.Position.Y))).FirstOrDefault();
            if (t == null) return "no unknown trap";
            var tiles = World.FreeTilesNear((int)t.Position.X, (int)t.Position.Y, 3).Skip(1).ToList();
            if (tiles.Count == 0) return "no room";
            var to = tiles[(paceStep++ * 3) % tiles.Count];
            return "walking to " + to + ": " + p.MoveTo(to.X, to.Y);
        });
        Commands.Add(this, "wt.disarm", "wt.disarm: the player disarms the nearest trap", args =>
        {
            var p = World.Player; if (p == null) return "no player";
            var t = Traps.InZone.OrderBy(x => Microsoft.Xna.Framework.Vector2.Distance(new Microsoft.Xna.Framework.Vector2(x.Position.X, x.Position.Y), new Microsoft.Xna.Framework.Vector2(p.Position.X, p.Position.Y))).FirstOrDefault();
            bool ok = t != null && t.Disarm(p);
            Log.Info("disarm command: " + ok);
            return "disarm: " + ok;
        });
        // A hostile gunner next to the player, who takes no damage while wt_god is set: end turns until it reloads.
        Combat.ModifyIncomingDamage.Add(this, d => { if (d.Target.IsPlayer && Variables.Global.Get("wt_god", false)) d.Scale(0); });
        Commands.Add(this, "wt.gunner", "wt.gunner: a hostile Ironhead gunner next to the player (player takes no damage)", args =>
        {
            var p = World.Player; if (p == null) return "no player";
            Variables.Global.Set("wt_god", true);
            var t = World.FreeTilesNear((int)p.Position.X, (int)p.Position.Y, 4).Skip(2).FirstOrDefault();
            var g = World.Spawn("creatures\\generichumans\\ironheadgunner", t.X, t.Y, SpawnSide.Hostile);
            Log.Info("gunner: " + (g == null ? "none" : g.Name + " at " + t));
            return g == null ? "no gunner" : "spawned " + g.Name;
        });
        Commands.Add(this, "wt.house", "wt.house: travel to the Core City player house", args => "travel: " + World.Travel("cc_ph", "FromOutside"));
        Commands.Add(this, "wt.leave", "wt.leave: travel out of the house to the residential district", args => "travel: " + World.Travel("cc-res", "FromPlayerHouse"));
        Commands.Add(this, "wt.house.set", "wt.house.set <element> <value>: set a house element (applies on the next load of the house)", args => "set: " + PlayerHouse.SetElement(args[0], args.Length > 1 ? args[1] : null));
        Commands.Add(this, "wt.house.info", "wt.house.info: the house's state", args =>
        {
            Log.Info("house: owned " + PlayerHouse.Owned + ", main " + PlayerHouse.Renovated(HouseFloor.Main) + ", upper " + PlayerHouse.Renovated(HouseFloor.Upper) + ", security " + PlayerHouse.SecurityLevel + ", elements " + string.Join(", ", PlayerHouse.Elements.Select(kv => kv.Key + "=" + kv.Value)));
            return "info";
        });
        Commands.Add(this, "wt.money", "wt.money <n>: charons", args => { World.Player.Inventory.AddMoney(int.Parse(args[0])); return "money " + World.Player.Inventory.Money; });
        Commands.Add(this, "wt.jyles", "wt.jyles: Jyles' conversation with the nearest character", args =>
        {
            Commands.CloseConsole();
            var c = World.Characters.Where(x => !x.IsPlayer).OrderBy(x => Microsoft.Xna.Framework.Vector3.Distance(x.Position, World.Player.Position)).FirstOrDefault();
            return "jyles: " + (c != null && Dialogs.Start(c, "characters\\cc_jyles"));
        });
        Commands.Add(this, "wt.talk", "wt.talk <name> <dialog>: open a dialog with the character of that name", args =>
        {
            Commands.CloseConsole();
            var c = World.Characters.FirstOrDefault(x => x.Name == args[0]);
            return "talk: " + (c == null ? "none" : c.Name + " " + Dialogs.Start(c, args[1]));
        });
        Commands.Add(this, "wt.containers", "wt.containers: the zone's containers, name and place", args =>
        {
            Log.Info("containers: " + string.Join("; ", World.Containers.Select(c => c.Name + " @" + c.Position.X + "," + c.Position.Y + " " + (c.Area == null ? "?" : c.Area.Name))));
            return "containers";
        });
        Commands.Add(this, "wt.goto", "wt.goto x y: teleport the player to a tile", args => "goto: " + World.Player.TeleportTo(int.Parse(args[0]), int.Parse(args[1])));
        Commands.Add(this, "wt.tiles", "wt.tiles x y r: a map of free floor tiles around x,y (log)", args =>
        {
            int x = int.Parse(args[0]), y = int.Parse(args[1]), r = int.Parse(args[2]);
            var free = new System.Collections.Generic.HashSet<Microsoft.Xna.Framework.Point>(World.FreeTilesNear(x, y, r));
            var sb = new System.Text.StringBuilder("tiles around " + x + "," + y + " (rows y, columns x from " + (x - r) + "):\n");
            for (int ty = y - r; ty <= y + r; ty++)
            {
                sb.Append(ty.ToString().PadLeft(3)).Append(' ');
                for (int tx = x - r; tx <= x + r; tx++) sb.Append(tx == x && ty == y ? 'X' : free.Contains(new Microsoft.Xna.Framework.Point(tx, ty)) ? '.' : '#');
                sb.Append('\n');
            }
            Log.Info(sb.ToString());
            return "tiles";
        });
        Commands.Add(this, "wt.hover", "wt.hover: the character under the mouse", args => { var h = World.Hovered; Log.Info("hovered: " + (h == null ? "none" : h.Name) + " mouse " + Input.Mouse); return "hover"; });
        Commands.Add(this, "wt.outlines", "wt.outlines: next frame, log the outlines (hidden or not) with their character and bounds", args => { logOutlines = true; return "outlines"; });
        WorldDraw.ShaderPassStarting.Add(this, view =>
        {
            if (!logOutlines) return;
            logOutlines = false;
            Log.Info("outlines: " + string.Join("; ", Outlines.All(view).Select(kv => { var c = kv.Key.Owner.Entity.Character; return (c == null ? "?" : c.Name) + (kv.Value ? " hidden" : "") + " " + kv.Key.Bounds; })));
        });
        Commands.Add(this, "wt.hoverwatch", "wt.hoverwatch: log the hovered character whenever it changes (every frame, outside the console)", args =>
        {
            if (!hoverWatch) { hoverWatch = true; GameEvents.Update.Add(this, sec => { var h = World.Hovered; var n = h == null ? "none" : h.Name; if (n != lastHover) { lastHover = n; Log.Info("hover now: " + n + " mouse " + Input.Mouse); } }); }
            Commands.CloseConsole();
            return "watching";
        });
        Commands.Add(this, "wt.ask", "wt.ask: Ui.Ask", args => { Commands.CloseConsole(); return "ask: " + Ui.Ask(this, "What will you call your dog?", "Rex", t => Log.Info("answered " + t)); });
        Commands.Add(this, "wt.asknum", "wt.asknum: Ui.AskNumber 1-20", args => { Commands.CloseConsole(); return "asknum: " + Ui.AskNumber(this, "How many rations?", 1, 20, 5, n => Log.Info("number " + n)); });
        Commands.Add(this, "wt.confirm", "wt.confirm: Ui.Confirm", args => { Commands.CloseConsole(); return "confirm: " + Ui.Confirm(this, "Release Max", "Give Max back to the Core City Housing Corporation? You won't get your charons back, and this can't be undone.", () => Log.Info("confirmed yes"), onNo: () => Log.Info("confirmed no")); });
        Commands.Add(this, "wt.choose", "wt.choose: Ui.Choose with three buttons", args => { Commands.CloseConsole(); return "choose: " + Ui.Choose(this, "Feeding time", "What do you give Biscuit?", i => Log.Info("chose " + i), "Hopper steak", "Raw meat", "Nothing"); });
        Commands.Add(this, "wt.cook", "wt.cook: 3 Cave Hopper Meat and a Siphoner Tongue, then the crafting window", args =>
        {
            Commands.CloseConsole();
            World.Player.Inventory.Add("consumables\\food\\cavehoppermeat", 3); World.Player.Inventory.Add("components\\bio\\siphonertongue", 1);
            Log.Info("cook: known " + string.Join(", ", Crafting.Known.Where(k => k.Name.Contains("Steak") || k.Name.Contains("Tongue")).Select(k => k.Name)) + "; window " + Windows.Open(Window.Crafting));
            return "cook";
        });
        Commands.Add(this, "wt.hurt", "wt.hurt <name> <amount>: damage the first living character whose name contains <name> (Combat.Damage)", args =>
        {
            var c = World.Characters.FirstOrDefault(x => x.Name == args[0] && !x.IsDead) ?? World.Characters.FirstOrDefault(x => x.Name != null && x.Name.Contains(args[0]) && !x.IsDead);
            if (c == null) return "none";
            var before = c.Health; var name = c.Name; var dealt = Combat.Damage(c, int.Parse(args[1]));
            Log.Info("hurt " + name + ": " + before + " -> " + (c.Area == null ? "gone" : c.Health.ToString()) + " (dealt " + dealt + ")");
            return "hurt";
        });
        // A new zone: Mods\WorksTest\data\Maps\wt_testzone.uz + _L1.uzl, copied into the test copy from a game zone
        // (never committed: game files).
        Zones.Add(this, "wt_testzone", "Works Test Zone", "wa-shop");
        Zones.AddExit(this, "toTest", "cc-res", "cc-res", 11, 9, "wt_testzone", "FromElevator", "Strange hole");
        Zones.AddExit(this, "back", "wt_testzone", "wa-shop", 2, 3, "cc-res", "FromPlayerHouse", "Way back up");
        Zones.AddEdgeExit(this, "edge", "cc-res", "cc-res", new Microsoft.Xna.Framework.Rectangle(11, 11, 2, 1), "wt_testzone", "FromElevator");
        Commands.Add(this, "wt.useexit", "wt.useexit <id>: the player uses a WorksTest exit (toTest, back)", args => { Commands.CloseConsole(); return "use: " + World.Use(World.Player, "WorksTest.exit." + args[0]); });
        Commands.Add(this, "wt.zone", "wt.zone: travel to the mod zone wt_testzone", args => "travel: " + World.Travel("wt_testzone", "FromElevator"));
        Commands.Add(this, "wt.skill", "wt.skill <name> <n>: set a game skill's base points", args => { World.Player.SetBaseSkill(args[0], int.Parse(args[1])); return args[0] + " " + World.Player.Skill(args[0]); });
        Commands.Add(this, "wt.sneak", "wt.sneak: the player starts sneaking (the game's stealthMode)", args => { Commands.CloseConsole(); return "sneak: " + StatusEffects.Apply(World.Player, "stealthMode"); });
        Commands.Add(this, "wt.gunnerat", "wt.gunnerat x y: a hostile Ironhead gunner at a tile (player takes no damage)", args =>
        {
            Variables.Global.Set("wt_god", true);
            var g = World.Spawn("creatures\\generichumans\\ironheadgunner", int.Parse(args[0]), int.Parse(args[1]), SpawnSide.Hostile);
            return "gunner: " + (g == null ? "none" : g.Name);
        });
        Commands.Add(this, "wt.cam", "wt.cam [x y]: log the camera (and move it to x y)", args =>
        {
            if (args.Length >= 2) Camera.Centre = new Microsoft.Xna.Framework.Vector2(float.Parse(args[0]), float.Parse(args[1]));
            Log.Info("camera " + Camera.Centre + " zoom " + Camera.Zoom + " tile " + Camera.TileWidth + "x" + Camera.TileHeight + " view " + Camera.ViewSize + "; player " + World.Player.Position + " screen " + World.Player.ScreenBounds);
            return "cam";
        });
        Commands.Add(this, "wt.pip", "wt.pip: a window with a live world view 6 tiles from the player", args =>
        {
            Commands.CloseConsole();
            var w = Ui.Window(this, "pip", "Remote view", 440, 300);
            var p = World.Player.Position;
            var view = Ui.WorldView(420, 260, new Microsoft.Xna.Framework.Vector2(p.X + 6, p.Y + 6));
            if (view == null) return "no view";
            Ui.Add(w, view, 10, w.Top + 2);
            return "pip at " + view.Centre;
        });
        Commands.Add(this, "wt.areas", "wt.areas: the zone's areas: size, tiles discovered / with anything", args =>
        {
            foreach (var ar in World.ZoneAreas)
            {
                int seen = 0, any = 0;
                for (int x = 0; x < ar.Width; x++) for (int y = 0; y < ar.Height; y++) { if (ar.KindAt(x, y) != TileKind.Nothing) seen++; if (ar.KindAt(x, y, false) != TileKind.Nothing) any++; }
                Log.Info("area " + ar.Name + " " + ar.Width + "x" + ar.Height + ": discovered " + seen + " of " + any);
            }
            return "areas";
        });
        Commands.Add(this, "wt.moveto", "wt.moveto x y: the player walks there", args => { Commands.CloseConsole(); return "walk: " + World.Player.MoveTo(int.Parse(args[0]), int.Parse(args[1])); });
        Commands.Add(this, "wt.blueprints", "wt.blueprints: each character's guessed blueprint", args => { Log.Info("blueprints: " + string.Join("; ", World.Characters.Select(c => c.Name + " -> " + (c.Blueprint ?? "none")))); return "bp"; });
        Commands.Add(this, "wt.grudge", "wt.grudge <name>: Grudges remembers that character (Mods.Call)", args => { var c = World.Characters.FirstOrDefault(x => x.Name == string.Join(" ", args)); return "grudge: " + Mods.Call("Grudges", "Hold", c); });
        Commands.Add(this, "wt.codever", "wt.codever: which build of WorksTest's code is running (reload test)", args => { var r = "code v1, updates seen " + updates; Log.Info(r); return r; });
        GameEvents.Update.Add(this, dt => updates++);
        Commands.Add(this, "wt.edgehere", "wt.edgehere: an edge exit on the player's tile and around (to the test zone)", args =>
        {
            var p = World.Player.Position;
            Zones.AddEdgeExit(this, "here", World.ZoneId, World.Player.Area.Name, new Microsoft.Xna.Framework.Rectangle((int)p.X - 1, (int)p.Y - 1, 3, 3), "wt_testzone", "FromElevator");
            return "edge exit at " + p;
        });
        Commands.Add(this, "wt.zoo", "wt.zoo: every character in the zone: position, area, attitude, faction, combat", args =>
        {
            var lines = World.Characters.Select(c => c.Name + " @" + c.Position.X + "," + c.Position.Y + " " + (c.Area == null ? "?" : c.Area.Name) + " " + c.Attitude + " f=" + c.Faction + (c.Passive ? " passive" : "") + (c.IsInCombat ? " COMBAT" : "") + (c.IsDead ? " DEAD" : "")).ToList();
            Log.Info("zoo [" + World.ZoneId + (Combat.InCombat ? ", in combat" : "") + "] " + string.Join("; ", lines));
            return lines.Count + " characters";
        });
        Commands.Add(this, "wt.petplace", "wt.petplace [dx dy name]: a cat placement near the player (re-applied on every load of this zone)", args =>
        {
            var p = World.Player; if (p == null) return "no player";
            int dx = args.Length > 0 ? int.Parse(args[0]) : 3, dy = args.Length > 1 ? int.Parse(args[1]) : 0;
            string name = args.Length > 2 ? args[2] : null;
            World.Place(new Placement { Id = "wt_pet_" + dx + "_" + dy + "_" + name, Zone = World.ZoneId, Area = p.Area.Name, X = (int)p.Position.X + dx, Y = (int)p.Position.Y + dy, Blueprint = "creatures\\cat", Name = name });
            return "placed";
        });
        Commands.Add(this, "wt.trapslot", "wt.trapslot: the bag's first trap into quick slot 1", args =>
        {
            var p = World.Player; var trap = p == null ? null : p.Inventory.Items.FirstOrDefault(i => i.Code != null && i.Code.StartsWith("trap."));
            var ok = trap != null && p.Equipment.ToQuickSlot(trap, 0); Log.Info("trapslot: " + ok); return "slotted: " + ok;
        });
        Commands.Add(this, "wt.probe", "wt.probe: which device blueprints are usable", args =>
        {
            var pp = World.Player.Position; var t = World.FreeTilesNear((int)pp.X, (int)pp.Y, 5).Skip(4).FirstOrDefault();
            var found = new System.Collections.Generic.List<string>();
            foreach (var bp in new[] { "objects\\devices\\largelever", "objects\\devices\\levery", "objects\\devices\\sfconsole", "objects\\devices\\sfconsole1", "objects\\devices\\powerplantconsoleoff", "objects\\devices\\powerplantconsoleon" })
            {
                World.Place(new Placement { Id = "wt_probe_" + found.Count, Zone = World.ZoneId, Area = World.Player.Area.Name, X = t.X, Y = t.Y, Blueprint = bp, OnUse = who => Log.Info("probe used") });
                found.Add(bp + ": " + World.Use(World.Player, "wt_probe_" + found.Count));
            }
            return string.Join("; ", found);
        });
        grit = Feats.Add(this, "grit", "Works Grit", "A mod feat: you feel tougher. (Requires Strength 3.)", new FeatRequirements { Abilities = { ["Strength"] = 3 } }, null, new[] { StatBonus.Ability("Strength", 2), StatBonus.CarryCapacity(50), StatBonus.Skill("Guns", 10) });
        grit.OnGain += who => Log.Info("feat gained by " + who.Name);
        Commands.Add(this, "wt.feat", "wt.feat: give the test feat", args => { Commands.CloseConsole(); var before = "Str " + World.Player.Ability("Strength") + " Guns " + World.Player.Skill("Guns"); return before + " -> give: " + Feats.Give(World.Player, grit.Key) + " Str " + World.Player.Ability("Strength") + " Guns " + World.Player.Skill("Guns") + ", has: " + World.Player.HasFeat(grit.Key) + "; feats: " + string.Join(", ", World.Player.Feats); });
        Ai.ChooseTarget.Add(this, c => { if (++aiLogs < 12) Log.Info("ai: " + c.Npc + " targets " + c.Chosen); return null; });
        Ai.TakingTurn.Add(this, t => { if (aiHold && !t.Npc.Name.Contains("Rathound")) { t.EndTurn = true; if (++aiLogs < 30) Log.Info("ai: held " + t.Npc); } });
        Ai.Acting.Add(this, a => Log.Info("ai: " + a.Npc + " uses " + a.Action));
        Ai.ShouldFlee.Add(this, f => { if (aiFlee && f.Npc.Name.Contains("Rathound")) { if (++aiLogs < 40) Log.Info("ai: rathound flees (game: " + f.Fleeing + ")"); return true; } return null; });
        var wind = Abilities.Add(this, "wind", "Second Wind", "Heals 20 health.", "art/wind.png", cooldown: 3, key: Microsoft.Xna.Framework.Input.Keys.F12);
        wind.Used += (user, target) => { user.Health += 20; Log.Info("ability: second wind, health " + user.Health); };
        var zap = Abilities.Add(this, "zap", "Zap", "10 damage to the target.", "art/zap.png", apCost: 15, cooldown: 2, target: AbilityTarget.Character, key: Microsoft.Xna.Framework.Input.Keys.OemQuotes);
        zap.Range = 12;
        bool zapped = false;
        Combat.TurnStarted.Add(this, c => { if (!aiHold || zapped || !c.IsPlayer) return; zapped = true;
            Log.Info("turn start: paused " + GameClock.Paused + ", in TB combat " + GameClock.InTurnBasedCombat);
            int before = Combat.ActionPoints(c); var why = zap.Use(c); Log.Info("ability in combat: AP " + before + " -> " + Combat.ActionPoints(c) + (why == null ? "" : " (" + why + ")") + "; again: " + zap.Use(c)); });
        zap.Used += (user, target) => { var before = target.Health; var dealt = Combat.Damage(target, 10, "Electricity", user); Log.Info("ability: zap " + target.Name + " for " + dealt + ", health " + before + " -> " + target.Health); };
        Commands.Add(this, "wt.slay", "wt.slay: 500 damage to the nearest live rathound", args =>
        {
            var r = World.Characters.Where(c => c.Name.Contains("Rathound") && !c.IsDead).FirstOrDefault();
            if (r == null) return "no rathound";
            var dealt = Combat.Damage(r, 500, "Mechanical", World.Player);
            Log.Info("slay: " + dealt + " damage, dead " + r.IsDead);
            return "slay " + dealt;
        });
        Commands.Add(this, "wt.flee", "wt.flee: rathounds flee", args => { aiFlee = true; return "fleeing"; });
        Commands.Add(this, "wt.pet", "wt.pet: an allied rathound that follows you", args =>
        {
            var pp = World.Player.Position; var t = World.FreeTilesNear((int)pp.X, (int)pp.Y, 4).Skip(5).FirstOrDefault();
            pet = World.Spawn("creatures\\rathound", t.X, t.Y, SpawnSide.Ally);
            if (pet == null) return "no pet";
            pet.Follow(World.Player, 2);
            Log.Info("pet " + pet.Name + " attitude " + pet.Attitude + " faction " + pet.Faction);
            return "pet";
        });
        GameEvents.Update.Add(this, sec => { if (pet == null) return; petLog -= sec; if (petLog > 0) return; petLog = 3;
            Log.Info("pet at " + pet.Position + ", player at " + World.Player.Position + ", dead " + pet.IsDead); });
        var kit = Crafting.AddRecipe(this, "fieldkit", "Works Field Kit", "Medicine", "consumables\\medicine\\healthhypo",
            RecipeSlot.Item("Bandages", "med.bandage", 0, 2), RecipeSlot.Item("Flare", "flare", 0, 1));
        Commands.Add(this, "wt.eat", "wt.eat: eat food from the bag", args =>
        {
            var food = World.Player.Inventory.Items.FirstOrDefault(i => i.Uses.Contains("Food"));
            Log.Info("eat: " + (food == null ? "no food" : food.Name + " uses [" + string.Join(", ", food.Uses) + "], used " + Items.Use(World.Player, food)));
            return "eat";
        });
        Commands.Add(this, "wt.tp", "wt.tp: teleport the player 4 tiles south", args =>
        {
            var p = World.Player; var at = p.Position; startX = (int)Math.Round(at.X); startY = (int)Math.Round(at.Y);
            var ok = p.TeleportTo(startX, startY + 4); Log.Info("teleport " + ok + ": " + at + " -> " + p.Position); return "tp";
        });
        Commands.Add(this, "wt.walk", "wt.walk: walk the player back", args => { var ok = World.Player.MoveTo(startX, startY); Log.Info("walk " + ok + " from " + World.Player.Position); return "walk"; });
        Commands.Add(this, "wt.where", "wt.where", args => { Log.Info("player at " + World.Player.Position); return "where"; });
        Commands.Add(this, "wt.mark", "wt.mark: markers near and far", args =>
        {
            var at = World.Player.Position;
            Markers.Add(this, "near", World.ZoneId, (int)at.X + 3, (int)at.Y + 3, "Near", Microsoft.Xna.Framework.Color.Magenta);
            Markers.Add(this, "far", World.ZoneId, (int)at.X + 60, (int)at.Y - 40, "Far", Microsoft.Xna.Framework.Color.Lime);
            Log.Info("markers here: " + string.Join(", ", Markers.Here.Select(m => m.Label + "@" + m.X + "," + m.Y)));
            return "marked";
        });
        GameEvents.CharacterExporting.Add(this, path => { CharacterData.Set("exports", CharacterData.Get<double>("exports") + 1); CharacterData.Set("motto", "made in WorksTest"); Log.Info("exporting to " + path); });
        GameEvents.CharacterImported.Add(this, () => Log.Info("imported: motto " + CharacterData.Get<string>("motto") + ", exports " + CharacterData.Get<double>("exports")));
        if (System.IO.File.Exists(System.IO.Path.Combine(Info.Folder, "rules.test")))
        {
            CharacterRules.AbilityPointsAtCreation = 8; CharacterRules.AbilityMaxAtCreation = 12; CharacterRules.AbilityMinAtCreation = 1; CharacterRules.SkillPointsAtCreation = 150;
            Log.Info("rules: test rules on");
        }
        luck = CustomStats.AddBaseAbility(this, "luck", "Luck", "Fortune favours you.");
        survival = CustomStats.AddSkill(this, "survival", "Survival", "Foraging, tracking, enduring.", new System.Collections.Generic.Dictionary<string, float> { { "Constitution", 1f }, { "Perception", 0.5f } });
        CustomStats.Changed.Add(this, () => Log.Info("custom stats: Luck " + luck.Value + ", Survival " + survival.Base + " [" + survival.Value + "]"));
        Feats.AddBonuses(this, "nimble", StatBonus.Skill("Stealth", 13));
        Feats.SetRequirements(this, "nimble", new FeatRequirements { Level = 9, Abilities = { ["Agility"] = 12 } });
        Commands.Add(this, "wt.featmod", "wt.featmod", args => { var p = World.Player; Log.Info("feats " + string.Join(",", p.Feats) + "; stealth " + p.Skill("Stealth") + " (base " + p.BaseSkill("Stealth") + "); desc set " + Feats.SetDescription(this, "nimble", "Changed by WorksTest: +13 stealth.")); return "featmod"; });
        Stats.Modify.Add(this, r => { if (statsOn && r.Character.IsPlayer) r.Add(StatBonus.Dodge(7)); });
        Commands.Add(this, "wt.stats", "wt.stats: a skill before/after re-weighting, dodge bonus", args =>
        {
            var p = World.Player;
            var skill = p.SkillNames.FirstOrDefault(n => p.BaseSkill(n) > 0 && n != "Dodge") ?? "Guns";
            Log.Info("stats before: " + string.Join(", ", p.SkillNames.Where(n => p.BaseSkill(n) > 0).Select(n => n + " " + p.BaseSkill(n) + "->" + p.Skill(n))) + "; Int " + p.Ability("Intelligence") + "; defense dodge " + p.Defense.Dodge);
            Stats.Refresh(p);
            Log.Info("stats plain rebuild: defense dodge " + p.Defense.Dodge + ", " + skill + " " + p.Skill(skill));
            Skills.SetAbilities(this, skill, new System.Collections.Generic.Dictionary<string, float> { { "Intelligence", 2f } });
            statsOn = true;
            Stats.Refresh(p);
            Log.Info("stats after: " + skill + " " + p.Skill(skill) + "; defense dodge " + p.Defense.Dodge);
            return "stats";
        });
        Commands.Add(this, "wt.syn", "wt.syn: Stealth gets Melee as a synergy", args =>
        {
            var p = World.Player; var before = p.Skill("Throwing");
            Skills.SetSynergies(this, "Throwing", new System.Collections.Generic.Dictionary<string, float> { { "Melee", 1f } });
            Stats.Refresh(p); Stats.Refresh(p);
            Log.Info("synergy: Throwing " + before + " -> " + p.Skill("Throwing") + " (Melee base " + p.BaseSkill("Melee") + ")");
            return "syn";
        });
        Commands.Add(this, "wt.surv", "wt.surv [n]: show / set Survival", args => { if (args.Length > 0) survival.Base = int.Parse(args[0]); Log.Info("survival now " + survival.Base + " [" + survival.Value + "]"); return "surv"; });
        Commands.Add(this, "wt.setskill", "wt.setskill <name> <n>: a mod skill's points", args => { var sk = CustomStats.SkillsAdded.FirstOrDefault(x => x.Name == args[0]); if (sk == null) return "none"; sk.Base = int.Parse(args[1]); Log.Info(sk.Name + " " + sk.Base + " [" + sk.Value + "], Mechanics " + World.Player.Skill("Mechanics")); return "set"; });
        Commands.Add(this, "wt.weapon", "wt.weapon", args => { Log.Info("weapon set: " + (World.Player.UsingSecondaryWeapon ? "secondary" : "primary")); return "w"; });
        Commands.Add(this, "wt.mech", "wt.mech", args => { Log.Info("mechanics " + World.Player.Skill("Mechanics")); return "mech"; });
        Commands.Add(this, "wt.xp", "wt.xp: enough experience for a level", args => { World.Player.GiveExperience(5000, "testing"); return "xp"; });
        Commands.Add(this, "wt.gear", "wt.gear: what the player wears and carries", args =>
        {
            var p = World.Player;
            Log.Info("gear: " + string.Join(", ", p.Equipment.All.Select(kv => kv.Key + "=" + kv.Value.Name)) + " | bag: " + string.Join(", ", p.Inventory.Items.Select(i => i.Name + " x" + i.Stacks)));
            return "gear";
        });
        Commands.Add(this, "wt.bag", "wt.bag: the bag's item codes", args => { var t = string.Join(", ", World.Player.Inventory.Items.Select(i => i.Code + " x" + i.Stacks)); Log.Info("bag: " + t); return t; });
        Commands.Add(this, "wt.hold", "wt.hold: NPCs other than rathounds end their turns; spawns a rathound", args =>
        {
            aiHold = true; Commands.CloseConsole();
            var pp = World.Player.Position; var t = World.FreeTilesNear((int)pp.X, (int)pp.Y, 6).Skip(10).FirstOrDefault();
            return "rathound: " + World.Spawn("creatures\\rathound", t.X, t.Y, true);
        });
        GameEvents.ZoneLoaded.Add(this, (zone, first) =>
        {
            var auto = System.IO.Path.Combine(Info.Folder, "autorun.txt");
            if (!System.IO.File.Exists(auto)) return;
            foreach (var line in System.IO.File.ReadAllLines(auto)) if (line.Trim().Length > 0) autorun.Enqueue(line.Trim());
            System.IO.File.Delete(auto);
        });
        // autorun.txt: console lines, run once the zone is loaded; "wait <seconds>" pauses (game time keeps going).
        GameEvents.Update.Add(this, sec =>
        {
            // Also picked up mid-game (checked once a second), for test steps after the load.
            if ((autoPoll -= sec) <= 0 && World.Player != null)
            {
                autoPoll = 1;
                var auto = System.IO.Path.Combine(Info.Folder, "autorun.txt");
                if (System.IO.File.Exists(auto)) { foreach (var line in System.IO.File.ReadAllLines(auto)) if (line.Trim().Length > 0) autorun.Enqueue(line.Trim()); System.IO.File.Delete(auto); }
            }
            if (autoWait > 0) { autoWait -= sec; return; }
            while (autorun.Count > 0)
            {
                var line = autorun.Dequeue();
                Log.Info("autorun: " + line);
                if (line.StartsWith("wait ")) { autoWait = double.Parse(line.Substring(5), System.Globalization.CultureInfo.InvariantCulture); return; }
                Commands.Execute(line);
            }
        });
        GameEvents.TradeOpening.Add(this, e => { if (block) { e.Cancel = true; Log.Info("blocked trade with " + e.Merchant); } });
        GameEvents.ContainerOpening.Add(this, e => { if (block) { e.Cancel = true; Log.Info("blocked container"); } });
        GameEvents.WeaponSwitching.Add(this, e => { Log.Info("weapon switch: " + e.Character.Name + " to " + (e.ToSecondary ? "secondary" : "primary") + " (now " + (e.Character.UsingSecondaryWeapon ? "secondary" : "primary") + ")"); if (block && e.Character.IsPlayer) e.Cancel = true; });
        GameEvents.Crafting.Add(this, e => { if (block) { e.Cancel = true; Log.Info("blocked crafting " + e.Blueprint); } });
        GameEvents.Equipping.Add(this, e => { if (block) { e.Cancel = true; Log.Info("blocked equipping " + e.Item.Name); } });
        GameEvents.ZoneLeaving.Add(this, e => { Log.Info("leaving " + e.From + " for " + e.To + "/" + e.Entry); if (block) { e.Cancel = true; Log.Info("blocked leaving"); } });
        Commands.Add(this, "wt.travel", "wt.travel: World.Travel to lu-c1b", args => "travel: " + World.Travel("lu-c1b", "entry"));
        GameEvents.DialogStarting.Add(this, e => { if (block) { e.Cancel = true; Log.Info("blocked dialog " + e.Name); } });
        Commands.Add(this, "wt.block", "wt.block: refuse trades, containers, dialogs", args => { block = true; return "blocking"; });
        Commands.Add(this, "wt.uiclose", "wt.uiclose: the widgets window, then closed", args =>
        {
            var win = Ui.Window(this, "widgets2", "Widgets", 520, 300);
            var list = Ui.List(200, 200, 20); list.Items = Enumerable.Range(1, 30).Select(i => "Row " + i).ToList(); list.Selected += i => Log.Info("closed list selected " + i);
            Ui.Add(win, list.View, 120, win.Top + 4);
            win.Close();
            return "closed";
        });
        Commands.Add(this, "wt.ui", "wt.ui: a window with the UI widgets", args =>
        {
            var win = Ui.Window(this, "widgets", "Widgets", 520, 300);
            Ui.Add(win, Ui.Image(Assets.Texture(this, "art/pic.png"), 96, 96), 10, win.Top + 4);
            var list = Ui.List(200, 200, 20); list.Items = Enumerable.Range(1, 30).Select(i => "Row " + i).ToList(); list.Selected += i => Log.Info("list selected " + i);
            Ui.Add(win, list.View, 120, win.Top + 4);
            var slider = Ui.Slider(160, 0, 100, 25); slider.Step = 5; slider.Changed += v => Log.Info("slider " + v);
            Ui.Add(win, slider.View, 340, win.Top + 30);
            var choice = Ui.Choice(160, new[] { "Easy", "Normal", "Hard" }, 1); choice.Changed += i => Log.Info("choice " + i);
            Ui.Add(win, choice.View, 340, win.Top + 70);
            var b = Ui.Button("Hover me", () => { }); Ui.Tooltip(b, "A tooltip from a mod, wrapped if it is long enough to need it.");
            Ui.Add(win, b, 340, win.Top + 110);
            return "window";
        });
        Commands.Add(this, "wt.lever", "wt.lever: the player uses the test lever", args => { Commands.CloseConsole(); return "use: " + World.Use(World.Player, "wt_lever"); });
        Commands.Add(this, "wt.glow", "wt.glow: the test status on the player", args => { glow.Apply(World.Player, 40); Commands.CloseConsole(); Log.Info("agility after glow: " + World.Player.Ability("Agility")); return "glow on: " + glow.Has(World.Player); });
        glow.OnTick += (who, stacks) => Log.Info("glow tick on " + who.Name + " (" + stacks + ")");
        GameEvents.ZoneLoaded.Add(this, (zone, first) =>
        {
            var pp = World.Player.Position; int px = (int)pp.X, py = (int)pp.Y;
            if (trigger != null) trigger.Remove();
            trigger = Triggers.Add(this, zone, null, new Microsoft.Xna.Framework.Rectangle(px - 1, py - 1, 3, 3), who => Log.Info("trigger: entered by " + who.Name), who => Log.Info("trigger: left by " + who.Name));
            var t = World.FreeTilesNear(px, py, 4).Skip(3).FirstOrDefault();
            World.Place(new Placement { Id = "wt_lever", Zone = zone, Area = World.Player.Area.Name, X = t.X, Y = t.Y, Blueprint = "objects\\devices\\lever", OnUse = who => Log.Info("lever used by " + (who == null ? "?" : who.Name)) });
        });
        Music.Add(this, "workstest", "music/test.ogg");
        Music.Override(this, () => musicOn ? "workstest" : null);
        Items.ModifyTooltip(this, (item, tip) => { tip.AddSeparator(); tip.AddValue("Works", item.Code + (tip.InTrade ? " (trade)" : "")); });
        Log.Info("translation: " + T("hello", "player") + " / missing: " + T("nope"));
        Mods.Provide(this, new TestApi());
        GameEvents.GameLoaded.Add(this, () => Log.Info("order: last"), HookOrder.Last);
        GameEvents.GameLoaded.Add(this, () => Log.Info("order: first"), HookOrder.First);
        GameEvents.GameLoaded.Add(this, () => Log.Info("save data counter after load: " + SaveData.Get("counter", 0) + ", list " + string.Join("|", ((System.Collections.IEnumerable)(SaveData.Get("list") ?? new object[0])).Cast<object>())));
        GameEvents.Update.Add(this, s => { if (failNow) { failNow = false; throw new InvalidOperationException("test failure"); } });
        GameEvents.GameTimeAdvanced.Add(this, d => { gameTime += d; });
        GameEvents.ItemUsing.Add(this, u => { Log.Info("using " + u.Item + " (" + u.Action + ") by " + (u.User == null ? "?" : u.User.Name) + (blockUse ? ": blocked" : "")); if (blockUse) u.Cancel = true; });
        GameEvents.ItemUsed.Add(this, u => Log.Info("used " + u.Item.Name + " by " + (u.User == null ? "?" : u.User.Name) + " on " + (u.Target ?? "nothing")));
        GameEvents.ItemCrafted.Add(this, c => Log.Info("crafted " + c.Blueprint + ": " + string.Join(", ", c.Items.Select(i => i.ToString()))));
        GameEvents.ItemStolen.Add(this, t => Log.Info("stole " + t.Item + " from " + (t.Victim == null ? "?" : t.Victim.Name)));
        GameEvents.CaughtStealing.Add(this, t => Log.Info("caught stealing from " + (t.Victim == null ? "?" : t.Victim.Name)));
        Stealing.ModifySuspicion.Add(this, t => { if (++suspicions % 60 == 1) Log.Info("suspicion for " + t.Item + ": " + t.Suspicion.ToString("0.00")); });
        if (System.IO.File.Exists(System.IO.Path.Combine(Info.Folder, "art", "menu.png"))) MainMenu.SetBackground(this, "art/menu.png");
        if (System.IO.File.Exists(System.IO.Path.Combine(Info.Folder, "art", "bandage.png"))) Icons.SetItemIcon(this, "med.bandage", "art/bandage.png");
        Stealth.ModifyInstant.Add(this, d => { if (++instants % 20 == 1) Log.Info("instant check: detection " + d.ObserverDetection + " vs stealth " + d.TargetStealth + ": " + d.Instant + " (overridden to false)"); d.Instant = false; });
        Stealth.ModifyDetection.Add(this, d => { if (++detections % 20 == 1) Log.Info("detection of " + (d.Target == null ? "?" : d.Target.Name) + ": detection " + d.ObserverDetection + " vs stealth " + d.TargetStealth + ", " + d.Distance.ToString("0.0") + "/" + d.SightRange.ToString("0") + " tiles, angle " + d.Angle.ToString("0") + ", light " + d.Light.ToString("0.00") + ", rate " + d.Rate.ToString("0.000")); });
        GameEvents.ContainerOpened.Add(this, (c, who, first) => Log.Info("container opened by " + (who == null ? "?" : who.Name) + (first ? " (first time)" : "") + ": " + string.Join(", ", c.Items.Select(i => i.ToString()))));
        GameEvents.ItemTaken.Add(this, (c, item, who) => Log.Info("taken " + item + " by " + (who == null ? "?" : who.Name)));
        GameEvents.LootGenerated.Add(this, t => { Log.Info("loot rolled for " + (t.Character != null ? t.Character.Name : "a container") + ": " + string.Join(", ", t.Added.Select(i => i.ToString()))); if (t.Container != null) t.Add("currency\\StygianCoin", 7); });
        Locks.ModifyAttempt.Add(this, a => { Log.Info("lock attempt " + a.Kind + " skill " + a.Skill + "+" + a.ToolBonus + " vs " + a.Lock.Difficulty); a.Bonus += 50; });
        GameEvents.LockAttempted.Add(this, a => Log.Info("lock attempt " + (a.Success ? "opened" : "failed")));
        Dialogs.Patch(this, "characters\\bigbret1", p =>
        {
            p.AddOption("passed all the tests", "[Works] Heard anything odd lately?", "odd");
            p.Line("odd", "Odd? Some fella swears a mod framework rewired this whole station.", Outcome.SetFlag("wt_patch_seen"))
                .Option("Interesting. Anyway...", "@")
                .Option("Bye.", Dialog.End);
        });
        ContentPack.AddCondition(this, "testAlways", args => () => args.Bool("testAlways"));
        ContentPack.AddEffect(this, "testLog", args => { var text = args.Text("testLog"); return () => Log.Info(text); });
        GameEvents.Equipped.Add(this, e => Log.Info("equipped " + e.Item.Name + " on " + e.Character.Name));
        GameEvents.Unequipped.Add(this, e => Log.Info("unequipped " + e.Item.Name + " from " + e.Character.Name));
        GameEvents.Update.Add(this, seconds =>
        {
            if (Input.Typing) return;     // the console or a text box has the keyboard
            var p = World.Player;
            if (p == null || !Input.Pressed(Keys.OemQuotes)) return;
            p.Inventory.AddMoney(20);
            Log.Info("gave 20 charons: now " + p.Inventory.Money + ", HP " + p.Health + "/" + p.MaxHealth + ", level " + p.Level + ", XP " + p.Experience
                + ", Strength " + p.Ability("Strength") + ", Persuasion " + p.Skill("Persuasion") + ", feats " + string.Join(",", p.Feats));
        });
        GameEvents.Update.Add(this, seconds =>
        {
            if (Input.Typing) return;     // the console or a text box has the keyboard: no test hotkeys
            var p = World.Player;
            if (p == null) return;
            if (Input.Pressed(Keys.N))
            {
                var eq = World.Player.Equipment;
                Log.Info("gear: " + string.Join(", ", eq.All.Select(kv => kv.Key + "=" + kv.Value.Name)) + "; quick: " + string.Join(", ", eq.QuickSlots.Select(i => i == null ? "-" : i.Name)));
                var armor = eq.Unequip(EquipSlot.Armor);
                Log.Info("took off " + (armor == null ? "nothing" : armor.Name) + "; put back: " + (armor != null && eq.Equip(World.Player.Inventory.Items.First(i => i.Code == armor.Code))));
            }
            if (Input.Pressed(Keys.V)) { var inv = World.Player.Inventory; inv.Add("armor\\jkktacticalvest", 1); var vest = inv.Items.FirstOrDefault(i => i.Code == "jkkTacticalVest"); Log.Info("vest on: " + (vest != null && World.Player.Equipment.Equip(vest))); }
            if (Input.Pressed(Keys.X)) Log.Info("xp " + World.Player.Experience + " (oddity " + Progression.Oddity + "), level " + World.Player.Level + "; next levels cost " + string.Join(", ", Enumerable.Range(2, 5).Select(l => l + ":" + Progression.XpFor(l))));
            if (Input.Pressed(Keys.B) && !Input.Down(Keys.LeftControl)) { var lines = Dialogs.StockLines("characters\\bigbret1"); Log.Info("bigbret1: " + lines.Count + " lines"); foreach (var l in lines.Take(12)) Log.Info("  | " + l.Replace("\n", " ").Substring(0, Math.Min(90, l.Length))); }
            if (Input.Pressed(Keys.H) && !Input.Down(Keys.LeftControl)) Log.Info("bret dialog with the merchant: " + Dialogs.Start(World.Placed("workstest_merchant"), "characters\\bigbret1"));
            if (Input.Pressed(Keys.C) && Input.Down(Keys.LeftShift))
            {
                var pp = World.Player.Position;
                var all = World.Containers.OrderBy(c => Microsoft.Xna.Framework.Vector3.Distance(c.Position, pp)).ToList();
                Log.Info(all.Count + " containers; nearest: " + string.Join("; ", all.Take(4).Select(c => c.Position + " " + c.Items.Count + " items" + (c.Lock != null ? " lock " + c.Lock.Kind + " " + c.Lock.Difficulty + (c.Lock.Locked ? " locked" : "") : ""))));
                if (Input.Down(Keys.LeftControl)) { var locked = all.FirstOrDefault(c => c.Lock != null && c.Lock.Locked && c.Lock.Difficulty > 0); if (locked != null) Log.Info("pick the lock at " + locked.Position + ": " + locked.Lock.TryPick(World.Player) + ", still locked: " + locked.Lock.Locked); }
                if (all.Count > 0) { if (all[0].Items.Count == 0) { all[0].Add("currency\\StygianCoin", 3); all[0].Add("consumables\\medicine\\bandage", 2); } Log.Info("open nearest: " + all[0].Open()); }
            }
            if (Input.Pressed(Keys.S) && Input.Down(Keys.LeftControl)) Log.Info("sneak: " + StatusEffects.Apply(World.Player, "stealthMode", 0));
            if (Input.Pressed(Keys.T) && Input.Down(Keys.LeftControl)) Log.Info("pit master: " + Dialogs.Start(World.Placed("pit_master"), "pit/master"));
            if (Input.Pressed(Keys.H) && Input.Down(Keys.LeftControl)) { var h = World.Hovered; Log.Info("hovered: " + (h == null ? "nothing" : h.Name + " at " + h.Position) + ", mouse " + Input.Mouse); }
            if (Input.Pressed(Keys.L) && Input.Down(Keys.LeftControl)) { System.IO.File.WriteAllLines(System.IO.Path.Combine(Info.Folder, "loaded.txt"), Assets.Loaded); Log.Info("loaded textures: " + Assets.Loaded.Count); }
            if (Input.Pressed(Keys.E) && Input.Down(Keys.LeftControl)) foreach (var n in new[] { "icons\\systembar", "icons\\meds", "gui\\textures\\systembar", "Portraits\\Male\\ag1_l", "Locale\\Creatures\\pl_ds\\id" }) Log.Info("export " + n + ": " + Assets.Export(n, System.IO.Path.Combine(Info.Folder, "export", n.Replace('\\', '_') + ".png")));
            if (Input.Pressed(Keys.B) && Input.Down(Keys.LeftControl)) Log.Info("bandage: " + World.Player.Inventory.Add("consumables\\medicine\\bandage", 3));
            if (Input.Pressed(Keys.W) && Input.Down(Keys.LeftControl)) { World.Player.Health = World.Player.MaxHealth / 2; Log.Info("wounded to " + World.Player.Health); }
            if (Input.Pressed(Keys.Y) && Input.Down(Keys.LeftControl)) { var b = World.Player.Inventory.Items.FirstOrDefault(i => i.Code == "med.bandage"); Log.Info("use bandage: " + (b != null && Items.Use(World.Player, b))); }
            if (Input.Pressed(Keys.R) && Input.Down(Keys.LeftControl)) { var pp = World.Player.Position; var t = World.FreeTilesNear((int)pp.X, (int)pp.Y, 6).Skip(10).FirstOrDefault(); Log.Info("rathound: " + World.Spawn("creatures\\rathound", t.X, t.Y, true)); }
            if (Input.Pressed(Keys.P) && Input.Down(Keys.LeftControl)) { var pp = World.Player.Position; var v = World.Characters.Where(c => !c.IsPlayer && !c.IsHostile && c.CanTalk).OrderBy(c => (c.Position - pp).Length()).FirstOrDefault(); World.Player.SetBaseSkill("Pickpocketing", 120); Log.Info("pickpocket " + v + ": " + Stealing.Pickpocket(World.Player, v)); }
            if (Input.Pressed(Keys.C) && Input.Down(Keys.LeftControl) && !Input.Down(Keys.LeftShift)) { World.Player.SetBaseSkill("Tailoring", 40); Log.Info("blueprints: " + Crafting.All.Count + ", known " + Crafting.Known.Count + "; learn net: " + Crafting.Learn("throwingNet") + "; scraps: " + World.Player.Inventory.Add("components\\scraps\\fabricscraps", 60) + "; known now " + string.Join(", ", Crafting.Known.Select(k => k.Name))); }
            if (Input.Pressed(Keys.Q) && Input.Down(Keys.LeftControl)) { var b = World.Player.Inventory.Items.FirstOrDefault(i => i.Code == "med.bandage"); Log.Info("bandage " + (b == null ? "none" : b.Name + " [" + Items.DebugKind(b) + "]") + " to quick slot: " + (b != null && World.Player.Equipment.ToQuickSlot(b)) + "; quick: " + string.Join(", ", World.Player.Equipment.QuickSlots.Select(i => i == null ? "-" : i.Name))); }
            if (Input.Pressed(Keys.D) && Input.Down(Keys.LeftControl)) { int c = SaveData.Get("counter", 0) + 1; SaveData.Set("counter", c); SaveData.Set("list", new System.Collections.Generic.List<object> { "a", 2.0, true }); Log.Info("save data counter = " + c); }
            if (Input.Pressed(Keys.F) && Input.Down(Keys.LeftControl)) failNow = true;
            if (Input.Pressed(Keys.N) && Input.Down(Keys.LeftControl)) Log.Info("stim: " + World.Player.Inventory.Add(stimPath, 2) + "; def " + Items.Definition(stimPath).Name + " " + Items.Definition(stimPath).Value + "; bandage " + Items.Definition("consumables\\medicine\\bandage").Name);
            if (Input.Pressed(Keys.M) && Input.Down(Keys.LeftControl)) { musicOn = !musicOn; Log.Info("mod music " + musicOn + "; songs " + Music.Songs.Count + ", sounds " + Sound.Ids.Count); }
            if (Input.Pressed(Keys.F12) && Input.Down(Keys.LeftControl)) Commands.OpenConsole();
            if (Input.Pressed(Keys.Home) && Input.Down(Keys.LeftControl)) { glow.Apply(World.Player, 40); Log.Info("glow on: " + glow.Has(World.Player)); }
            if (Input.Pressed(Keys.G) && Input.Down(Keys.LeftControl)) Log.Info("clock: world " + GameClock.WorldTime + ", turn " + GameClock.TurnTime + " (" + GameClock.Turns.ToString("0.0") + " turns), paused " + GameClock.Paused + ", ticks summed " + gameTime);
            if (Input.Pressed(Keys.U) && Input.Down(Keys.LeftControl)) { blockUse = !blockUse; Log.Info("block item use: " + blockUse); }
            if (Input.Pressed(Keys.O)) Log.Info("content index: " + ContentIndex.Write());
            if (Input.Pressed(Keys.Y)) { Data.Set("count", Data.GetInt("count") + 1); Log.Info("mod data count = " + Data.GetInt("count")); }
            if (Input.Pressed(Keys.J) && Input.Down(Keys.LeftControl) && Input.Down(Keys.LeftShift)) { var npc = World.Placed("prospector_npc"); Log.Info("prospector " + (npc == null ? "not placed" : "at " + npc.Position + ", talk: " + Dialogs.Start(npc, "prospector/talk"))); }
            if (Input.Pressed(Keys.U) && !Input.Down(Keys.LeftControl)) Log.Info("trade with the test merchant: " + Trade.Open(World.Placed("workstest_merchant")));
            if (Input.Pressed(Keys.F11)) { p.GiveExperience(5000, "test"); Log.Info("XP now " + p.Experience + ", level " + p.Level + ", skill points " + p.SkillPoints + ", ability points " + p.AbilityPoints); }
            if (Input.Pressed(Keys.F12))
            {
                var s = World.Placed("stranger_npc");
                if (s != null) { s.Passive = false; s.SetAttitudeToPlayer(Attitude.Hostile); Log.Info("stranger now " + s.AttitudeToPlayer); }
            }
            if (Input.Pressed(Keys.F4) && box == null)
            {
                var panel = Ui.Panel(320, 50);
                box = Ui.TextBox(300);
                box.Changed += text => Log.Info("text box: \"" + text + "\"");
                Ui.Add(panel, box, 10, 10);
                Ui.Add(Hud.Root, panel, 600, 300);
                return;
            }
            if (Input.Pressed(Keys.F4))
            {
                var portrait = Hud.Get(HudPart.PlayerPortrait);
                if (portrait != null) Ui.Show(portrait, !portrait.Visible);
                Log.Info("portrait " + (portrait == null ? "missing" : portrait.Visible ? "shown" : "hidden") + ", action bar at " + (Hud.Get(HudPart.ActionBar) == null ? "?" : Hud.Get(HudPart.ActionBar).Bounds.ToString()) + ", notes opened: " + Windows.Open(Window.Notes));
            }
            if (Input.Pressed(Keys.F6))
            {
                var codes = StatusEffects.Codes;
                Log.Info(codes.Count + " effect codes: " + string.Join(",", codes.Take(60)));
                var code = codes.FirstOrDefault(c => c.ToLowerInvariant().Contains("bleed")) ?? codes.FirstOrDefault();
                if (code != null) Log.Info("apply " + code + ": " + StatusEffects.Apply(p, code, 3) + "; now " + string.Join(", ", p.StatusEffects));
            }
        });
        Combat.TurnStarted.Add(this, c => Log.Info("turn: " + c.Name + " AP " + Combat.ActionPoints(c) + ", round " + Combat.Round));
        Combat.ModifyHitChance.Add(this, h => { if (h.Attacker != null && h.Attacker.IsPlayer) Log.Info("hit chance " + h.Kind + " " + (h.Target == null ? "?" : h.Target.Name) + ": " + h.Chance.ToString("0.00")); });
        Combat.ModifyIncomingDamage.Add(this, d => Log.Info("incoming to " + (d.Target == null ? "?" : d.Target.Name) + " from " + d.AttackerName + ": hit " + d.Hit + ", crit " + d.Critical + ", " + string.Join(" + ", d.Parts)));
        Combat.AttackResolved.Add(this, o => Log.Info("resolved: " + (o.Target == null ? "?" : o.Target.Name) + " lost " + o.HealthLost + (o.Killed ? " (killed)" : "")));
        // A test merchant at the north gate (Big Bret's store).
        World.Place(new Placement { Id = "workstest_merchant", Zone = "LU-SouthGateStation-N", Area = "LU-SouthGateStation-N", X = 16, Y = 23,
            Blueprint = "creatures\\generichumans\\foundryworker", Name = "Test Merchant", Store = "bigbret" });
        GameEvents.TradeOpened.Add(this, m => Log.Info("trade with " + (m == null ? "?" : m.Name) + ": " + (m == null ? 0 : m.Stock.Count) + " items, markup " + (m == null ? 0 : m.Markup)));
        GameEvents.QuestChanged.Add(this, (id, state) => Log.Info("quest " + id + " -> " + state));
        GameEvents.Saved.Add(this, folder => Log.Info("saved " + folder));
        GameEvents.GameLoaded.Add(this, () => Log.Info("game loaded"));
        int trapScans = 0;
        Traps.ModifyDetection.Add(this, (who, score) => { if (who.IsPlayer && trapScans++ < 2) Log.Info("trap detection score " + score + " -> " + (score + 100)); return who.IsPlayer ? score + 100 : score; });
        GameEvents.TrapDisarming.Add(this, d => { if (Variables.Global.Get("wt_easy", false)) d.Difficulty = 0; Log.Info("disarming: skill " + d.Skill + " vs " + d.Difficulty + (d.OwnTrap ? " (own)" : "")); });
        GameEvents.TrapDisarmed.Add(this, r => Log.Info("disarmed: " + r.Outcome));
        GameEvents.Reloading.Add(this, r => { if (!r.Character.IsPlayer && Variables.Global.Get("wt_noreload", false)) { r.Cancel = true; Log.Info("reload cancelled for " + r.Character.Name); } });
        GameEvents.Reloading.Add(this, r => Log.Info("reloading: " + r.Character.Name + " " + (r.Weapon == null ? "?" : r.Weapon.Name) + " with " + (r.Ammo == null ? "-" : r.Ammo.Name)));
        GameEvents.Reloaded.Add(this, r => Log.Info("reloaded: " + r.Character.Name));
        GameEvents.TrapDetected.Add(this, t => Log.Info("trap spotted by " + t.Character.Name + " at " + t.Position + ", detect " + t.DetectDifficulty + ", disarm " + t.DisarmDifficulty));
        GameEvents.ZoneLoaded.Add(this, (zone, first) => Log.Info("zone " + zone + (first ? " (first visit)" : "")));
        GameEvents.DialogStarted.Add(this, (name, npc) => Log.Info("dialog " + name + " with " + (npc == null ? "?" : npc.Name)));
        GameEvents.DialogEnded.Add(this, name => Log.Info("dialog ended " + name));
        GameEvents.ItemAdded.Add(this, item => Log.Info("item added " + item + " (" + item.Code + ")"));
        GameEvents.ExperienceGained.Add(this, xp => Log.Info("XP +" + xp));
        GameEvents.CombatStarted.Add(this, () => Log.Info("combat started"));
        GameEvents.CombatEnded.Add(this, () => Log.Info("combat ended"));
        GameEvents.CharacterDied.Add(this, c => Log.Info("died: " + (c == null ? "?" : c.Name + " statuses [" + string.Join(", ", c.StatusEffects.Select(s => s.Code)) + "]")));
        GameEvents.CharacterDying.Add(this, (c, killer) => Log.Info("dying: " + c.Name + " killed by " + (killer == null ? "?" : killer.Name) + " statuses [" + string.Join(", ", c.StatusEffects.Select(s => s.Code)) + "]"));
    }
}

public sealed class TestApi
{
    public string Echo(string text) { return "echo " + text; }
}

static class TestExtensions { public static T Also<T>(this T value, System.Action a) { a(); return value; } }
