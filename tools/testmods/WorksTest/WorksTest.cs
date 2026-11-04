using System;
// Test-only mod (not shipped): keys that set up game state for testing Works in game.
//   F10: give the player 20 charons (Inventory.AddMoney). F11: 5000 XP. U: trade with the test merchant. J: talk to the prospector (content pack test). F12: the stranger turns hostile. F4: hide the portrait,
//   open the notes. F6: list status effect codes and apply one. Logs every Works event.
using System.Linq;
using Microsoft.Xna.Framework.Input;

[assembly: System.Reflection.AssemblyVersion("1.0.0")]

public sealed class WorksTest : WorksMod
{
    UiTextBox box;
    public override void Load()
    {
        GameEvents.StatusApplying.Add(this, c => { Log.Info("status applying " + c.Code + " on " + (c.Character == null ? "?" : c.Character.Name)); if (c.Code == "Hidden" && Data.GetBool("noHide")) c.Cancel = true; });
        GameEvents.StatusApplied.Add(this, c => Log.Info("status applied " + c.Code + " (" + c.Name + ")"));
        GameEvents.StatusRemoved.Add(this, c => Log.Info("status removed " + c.Code));
        GameEvents.LevelledUp.Add(this, l => Log.Info("levelled up to " + l));
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
            if (Input.Pressed(Keys.B)) { var lines = Dialogs.StockLines("characters\\bigbret1"); Log.Info("bigbret1: " + lines.Count + " lines"); foreach (var l in lines.Take(12)) Log.Info("  | " + l.Replace("\n", " ").Substring(0, Math.Min(90, l.Length))); }
            if (Input.Pressed(Keys.H)) Log.Info("bret dialog with the merchant: " + Dialogs.Start(World.Placed("workstest_merchant"), "characters\\bigbret1"));
            if (Input.Pressed(Keys.O)) Log.Info("content index: " + ContentIndex.Write());
            if (Input.Pressed(Keys.Y)) { Data.Set("count", Data.GetInt("count") + 1); Log.Info("mod data count = " + Data.GetInt("count")); }
            if (Input.Pressed(Keys.J)) { var npc = World.Placed("prospector_npc"); Log.Info("prospector " + (npc == null ? "not placed" : "at " + npc.Position + ", talk: " + Dialogs.Start(npc, "prospector/talk"))); }
            if (Input.Pressed(Keys.U)) Log.Info("trade with the test merchant: " + Trade.Open(World.Placed("workstest_merchant")));
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
