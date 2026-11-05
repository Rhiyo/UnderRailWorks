// UnderRailWorks: active abilities from mods (tModLoader's hotkeys, sfall's custom actions). The game's own abilities
// (c3n and friends) are saved in the character and would crash the stock game, so a mod ability is Works' own: an icon
// on the mod's ability bar (a HUD part, so layout mods move it), used with a click or its key. It costs AP in
// turn-based combat (on the player's turn), has a cooldown in turns (kept in the mod's per-save data), and may target
// the character under the mouse (press the key while pointing at it). The mod's handler does the effect with
// Works' APIs (damage, statuses, items...).
//   var bash = Abilities.Add(this, "bash", "Shield Bash", "Knocks the target back.", "art/bash.png",
//       apCost: 20, cooldown: 3, target: AbilityTarget.Character, key: Keys.G);
//   bash.Available = c => c.HasFeat(feat.Key);
//   bash.Used += (user, target) => Combat.Damage(target, 10, "Mechanical", user);
using System;
using System.Collections.Generic;
using System.Linq;
using Microsoft.Xna.Framework;
using Microsoft.Xna.Framework.Graphics;
using Microsoft.Xna.Framework.Input;
using UnderRailWorks.Internal;

namespace UnderRailWorks
{
    public enum AbilityTarget { Self, Character }

    public sealed class ModAbility
    {
        internal WorksMod Owner;
        public string Mod { get { return Owner.Id; } }
        public string Id { get; internal set; }
        public string Name { get; internal set; }
        public string Description { get; internal set; }
        public int ApCost { get; set; }
        public int Cooldown { get; set; }          // in turns (5 s of game time out of combat)
        public AbilityTarget Target { get; internal set; }
        public int Range { get; set; }                      // tiles, for Character targets (0: any distance)
        public Keybind Key { get; internal set; }
        internal string IconFile;
        Texture2D icon;
        // Loaded when first drawn (mods add abilities in Load, before the graphics device exists).
        internal Texture2D Icon { get { if ((icon == null || icon.IsDisposed) && IconFile != null) icon = Assets.Texture(Owner, IconFile); return icon; } }
        // Shown and usable only while true for the player (null: always), e.g. c => c.HasFeat(feat.Key).
        public Func<Character, bool> Available { get; set; }
        public event Action<Character, Character> Used;

        // Turns left before it can be used again (0: ready).
        public double CooldownLeft
        {
            get
            {
                if (Cooldown <= 0) return 0;
                var last = SaveData.Of(Owner).Get<double>("works_ability_" + Id, double.NaN);
                if (double.IsNaN(last)) return 0;
                double left = Cooldown - (GameClock.Turns - last);
                return left > Cooldown || left <= 0 ? 0 : left;     // a clock behind the stamp (another save): ready
            }
        }

        // Uses it as the player would (the checks, AP and cooldown included). Returns why not, or null when done.
        public string Use(Character target = null)
        {
            var p = World.Player;
            if (p == null || p.IsDead) return "no player";
            if (!IsAvailable(p)) return Name + " isn't available";
            if (CooldownLeft > 0) return Name + ": ready in " + Math.Ceiling(CooldownLeft) + " turns";
            if (Target == AbilityTarget.Character)
            {
                if (target == null || target.IsDead) return Name + ": point at a target";
                if (Range > 0) { var a = p.Position; var b = target.Position; if (Math.Max(Math.Abs(a.X - b.X), Math.Abs(a.Y - b.Y)) > Range) return Name + ": out of range"; }
            }
            bool turnBased = GameClock.InTurnBasedCombat;
            if (turnBased)
            {
                var current = Combat.Current;
                if (current == null || current.Entity != p.Entity) return "not your turn";
                if (Combat.ActionPoints(p) < ApCost) return Name + ": not enough action points";
            }
            try { var u = Used; if (u != null) u(p, Target == AbilityTarget.Character ? target : p); }
            catch (Exception ex) { ModErrors.Report(Mod, "ability " + Id, ex); return Name + " failed"; }
            if (turnBased && ApCost > 0) Combat.SetActionPoints(p, Combat.ActionPoints(p) - ApCost);
            if (Cooldown > 0) SaveData.Of(Owner).Set("works_ability_" + Id, GameClock.Turns);
            return null;
        }

        internal bool IsAvailable(Character p) { try { return Available == null || Available(p); } catch (Exception ex) { ModErrors.Report(Mod, "ability " + Id, ex); return false; } }
    }

    public static class Abilities
    {
        internal static readonly List<ModAbility> All = new List<ModAbility>();
        // icon: a PNG in the mod's folder (40 x 40 looks right); key: its default key (players change it in Controls).
        public static ModAbility Add(WorksMod mod, string id, string name, string description, string icon, int apCost = 0, int cooldown = 0,
            AbilityTarget target = AbilityTarget.Self, Keys key = Keys.None)
        {
            var a = new ModAbility { Owner = mod, Id = id, Name = name, Description = description, ApCost = apCost, Cooldown = cooldown, Target = target,
                IconFile = icon, Key = Keybinds.Register(mod, "ability." + id, name, key) };
            All.Add(a);
            AbilityBars.Dirty = true;
            return a;
        }
        public static IList<ModAbility> Of(WorksMod mod) { return All.Where(a => a.Owner == mod).ToList(); }
    }
}

namespace UnderRailWorks.Internal
{
    static class AbilityBars
    {
        internal static bool Dirty = true;
        static object builtFor;
        static readonly Dictionary<ModAbility, UiElement> icons = new Dictionary<ModAbility, UiElement>();
        static readonly Dictionary<string, UiElement> bars = new Dictionary<string, UiElement>();
        static readonly Color Back = new Color(12, 12, 12, 220), Frame = new Color(181, 92, 0), Grey = new Color(90, 90, 90);
        const int Size = 44, Gap = 4;

        internal static void Tick()
        {
            var root = Hud.Root;
            if (root == null || Abilities.All.Count == 0) { builtFor = null; return; }
            if (Dirty || !ReferenceEquals(builtFor, root.Control)) Build(root);
            var p = World.Player;
            if (p == null) return;
            foreach (var bar in bars)
            {
                var shown = Abilities.All.Where(a => a.Mod == bar.Key && a.IsAvailable(p)).ToList();
                bar.Value.Visible = shown.Count > 0 && Hud.Visible;
            }
            if (Hud.Hidden || GameClock.Paused) return;     // menus, the character sheet, dialogs, the pause: no abilities
            foreach (var a in Abilities.All)
            {
                if (!a.IsAvailable(p)) continue;
                UiElement icon;
                bool clicked = icons.TryGetValue(a, out icon) && Ui.IsHot(icon) && Input.MousePressed(MouseButton.Left);
                if (!clicked && !a.Key.Pressed) continue;
                if (clicked && a.Target == AbilityTarget.Character) { Say(a.Name + ": point at a target and press " + Keybinds.Name(a.Key.Key)); continue; }
                var why = a.Use(a.Target == AbilityTarget.Character ? World.Hovered : null);
                if (why != null) Say(why);
            }
        }

        static void Say(string text) { Notify.OverPlayer(text, new Color(230, 160, 60)); }

        static void Build(UiElement root)
        {
            Dirty = false; builtFor = root.Control;
            foreach (var b in bars.Values) Ui.Remove(b);
            bars.Clear(); icons.Clear();
            int row = 0;
            foreach (var group in Abilities.All.GroupBy(a => a.Owner))
            {
                var list = group.ToList();
                var bar = Ui.Panel(list.Count * (Size + Gap) + Gap, Size + 2 * Gap);
                Ui.Add(bar, Ui.Custom(list.Count * (Size + Gap) + Gap, Size + 2 * Gap, c => { c.Fill(c.Bounds, Back); c.Frame(c.Bounds, Frame); }), 0, 0);
                for (int i = 0; i < list.Count; i++)
                {
                    var a = list[i];
                    var icon = Ui.Custom(Size, Size, c => Draw(c, a));
                    Ui.Add(bar, icon, Gap + i * (Size + Gap), Gap);
                    Ui.Tooltip(icon, a.Name + "\n" + a.Description + "\n" + (a.ApCost > 0 ? a.ApCost + " AP" : "No AP") + (a.Cooldown > 0 ? ", cooldown " + a.Cooldown + " turns" : "")
                        + (a.Key.Key != Keys.None ? "\nKey: " + Keybinds.Name(a.Key.Key) : "") + (a.Target == AbilityTarget.Character ? "\nPoint at the target and press the key." : "\nClick or press the key."));
                    icons[a] = icon;
                }
                bars[group.Key.Id] = bar;
                Hud.AddPart(group.Key, "Abilities", bar, HudAnchor.Bottom, 0, 70 + row * (Size + 2 * Gap + 4));
                row++;
            }
        }

        static void Draw(UiCanvas c, ModAbility a)
        {
            var b = c.Bounds;
            UiElement el; bool hot = icons.TryGetValue(a, out el) && Ui.IsHot(el);
            c.Fill(b, new Color(30, 30, 30, 230));
            if (a.Icon != null && !a.Icon.IsDisposed) c.DrawTexture(a.Icon, new Rectangle(b.X + 2, b.Y + 2, b.Width - 4, b.Height - 4), Color.White);
            else c.Text(a.Name.Length > 3 ? a.Name.Substring(0, 3) : a.Name, b.X + 6, b.Y + 13, Color.White);
            double left = a.CooldownLeft;
            bool apShort = GameClock.InTurnBasedCombat && World.Player != null && Combat.ActionPoints(World.Player) < a.ApCost;
            if (left > 0 || apShort) c.Fill(b, new Color(0, 0, 0, 150));
            if (left > 0) { var t = Math.Ceiling(left).ToString(); c.Text(t, b.X + (b.Width - Ui.TextWidth(t)) / 2, b.Y + 13, new Color(230, 160, 60)); }
            if (a.ApCost > 0) c.Text(a.ApCost.ToString(), b.X + 3, b.Bottom - 17, apShort ? new Color(200, 60, 60) : new Color(215, 215, 205));
            c.Frame(b, hot ? new Color(230, 160, 60) : Grey);
        }
    }
}
