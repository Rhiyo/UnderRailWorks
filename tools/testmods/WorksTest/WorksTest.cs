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
    bool aiHold, block;
    int aiLogs;
    TimeSpan gameTime;
    bool blockUse;
    public override void PostLoad() { Log.Info("cross-mod call: " + Mods.Call("WorksTest", "Echo", "hi") + ", typed: " + Mods.Api<TestApi>("WorksTest").Echo("there")); }
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
        Commands.Add(this, "wt.hold", "wt.hold: NPCs other than rathounds end their turns; spawns a rathound", args =>
        {
            aiHold = true; Commands.CloseConsole();
            var pp = World.Player.Position; var t = World.FreeTilesNear((int)pp.X, (int)pp.Y, 6).Skip(10).FirstOrDefault();
            return "rathound: " + World.Spawn("creatures\\rathound", t.X, t.Y, true);
        });
        GameEvents.ZoneLoaded.Add(this, (zone, first) =>
        {
            var auto = System.IO.Path.Combine(Info.Folder, "autorun.txt");
            if (System.IO.File.Exists(auto)) foreach (var line in System.IO.File.ReadAllLines(auto)) if (line.Trim().Length > 0) { Log.Info("autorun: " + line); Commands.Execute(line); }
        });
        GameEvents.TradeOpening.Add(this, e => { if (block) { e.Cancel = true; Log.Info("blocked trade with " + e.Merchant); } });
        GameEvents.ContainerOpening.Add(this, e => { if (block) { e.Cancel = true; Log.Info("blocked container"); } });
        GameEvents.DialogStarting.Add(this, e => { if (block) { e.Cancel = true; Log.Info("blocked dialog " + e.Name); } });
        Commands.Add(this, "wt.block", "wt.block: refuse trades, containers, dialogs", args => { block = true; return "blocking"; });
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
            if (p == null || !Input.Pressed(Keys.F10)) return;
            p.Inventory.AddMoney(20);
            Log.Info("gave 20 charons: now " + p.Inventory.Money + ", HP " + p.Health + "/" + p.MaxHealth + ", level " + p.Level + ", XP " + p.Experience
                + ", Strength " + p.Ability("Strength") + ", Persuasion " + p.Skill("Persuasion") + ", feats " + string.Join(",", p.Feats));
        });
        GameEvents.Update.Add(this, seconds =>
        {
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
            if (Input.Pressed(Keys.F9) && Input.Down(Keys.LeftControl)) Commands.OpenConsole();
            if (Input.Pressed(Keys.Home) && Input.Down(Keys.LeftControl)) { glow.Apply(World.Player, 40); Log.Info("glow on: " + glow.Has(World.Player)); }
            if (Input.Pressed(Keys.G) && Input.Down(Keys.LeftControl)) Log.Info("clock: world " + GameClock.WorldTime + ", turn " + GameClock.TurnTime + " (" + GameClock.Turns.ToString("0.0") + " turns), paused " + GameClock.Paused + ", ticks summed " + gameTime);
            if (Input.Pressed(Keys.U) && Input.Down(Keys.LeftControl)) { blockUse = !blockUse; Log.Info("block item use: " + blockUse); }
            if (Input.Pressed(Keys.O)) Log.Info("content index: " + ContentIndex.Write());
            if (Input.Pressed(Keys.Y)) { Data.Set("count", Data.GetInt("count") + 1); Log.Info("mod data count = " + Data.GetInt("count")); }
            if (Input.Pressed(Keys.J)) { var npc = World.Placed("prospector_npc"); Log.Info("prospector " + (npc == null ? "not placed" : "at " + npc.Position + ", talk: " + Dialogs.Start(npc, "prospector/talk"))); }
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
        GameEvents.ZoneLoaded.Add(this, (zone, first) => Log.Info("zone " + zone + (first ? " (first visit)" : "")));
        GameEvents.DialogStarted.Add(this, (name, npc) => Log.Info("dialog " + name + " with " + (npc == null ? "?" : npc.Name)));
        GameEvents.DialogEnded.Add(this, name => Log.Info("dialog ended " + name));
        GameEvents.ItemAdded.Add(this, item => Log.Info("item added " + item + " (" + item.Code + ")"));
        GameEvents.ExperienceGained.Add(this, xp => Log.Info("XP +" + xp));
        GameEvents.CombatStarted.Add(this, () => Log.Info("combat started"));
        GameEvents.CombatEnded.Add(this, () => Log.Info("combat ended"));
        GameEvents.CharacterDied.Add(this, c => Log.Info("died: " + (c == null ? "?" : c.Name)));
    }
}

public sealed class TestApi
{
    public string Echo(string text) { return "echo " + text; }
}
