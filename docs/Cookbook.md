# UnderRailWorks cookbook

Short examples, one per API area. They are compiled on every build (tools/cookbook/Cookbook.cs), so they match
the current API. Each is a method of a `WorksMod`: call it from `Load()`. The full list of types and members is
[API.md](API.md); how each part works in the game is in UnderRailWorks.md.

## Settings

A ModConfig class becomes the mod's page in Options -> Mods and its settings.ini.

```csharp
void SettingsExample()
{
    var c = GetConfig<CookbookConfig>();          // fields: bool -> tick box, enum -> dropdown, int/double -> slider
    c.Changed += () => Log.Info("bonus is now " + c.Bonus);
}
```

## Keys

Registered keys appear in Options -> Controls, are checked against the game's, and skip typing.

```csharp
void Keys_()
{
    var key = Keybinds.Register(this, "hello", "Say hello", Keys.F11);
    GameEvents.Update.Add(this, seconds => { if (key.Pressed) Notify.OverPlayer(T("hello")); });
}
```

## Per-save data

Plain values (strings, numbers, bools, lists, dictionaries) kept with each save.

```csharp
void PerSave()
{
    GameEvents.CharacterDied.Add(this, who =>
    {
        if (!who.IsHostile) return;
        SaveData.Set("kills", SaveData.Get<double>("kills") + 1);
    });
}
```

## Events

React to the game (all handlers are isolated: one failing doesn't stop the others).

```csharp
void Events()
{
    GameEvents.GameLoaded.Add(this, () => Log.Info("loaded, zone " + World.ZoneId));
    GameEvents.GameStarted.Add(this, () => Log.Info("another game: reset per-game state"));     // new game or load
    GameEvents.ZoneLoaded.Add(this, (zone, firstVisit) => { if (firstVisit) Notify.Log("New place: " + zone); });
    Combat.ModifyIncomingDamage.Add(this, d => { if (!d.Target.IsPlayer) d.Scale(1 + config.Bonus / 100f); });
}
```

## Refusing things

Cancellable events (trade, containers, dialogs, crafting, equipping, leaving a zone).

```csharp
void Refuse()
{
    GameEvents.Equipping.Add(this, e => { if (e.Item.Kind == ItemKind.Armor && e.Character.Ability("Strength") < 4) { e.Cancel = true; Notify.OverPlayer("Too heavy"); } });
    GameEvents.ZoneLeaving.Add(this, e => { if (Combat.InCombat) e.Cancel = true; });
}
```

## Items

Edit the game's items, define new ones, sell them, drop them.

```csharp
void ItemsExample()
{
    Items.EditDefinition(this, "consumables\\medicine\\bandage", d => d.Value *= 2);
    var path = Items.Define(this, "stim", "consumables\\medicine\\healthhypo", d => { d.Name = "Field Stim"; d.Value = 300; });
    Stores.AddRestock(this, "bigbret", path, 2);
    Loot.Add(this, (dead, box) => dead != null && dead.Name.Contains("Rathound"), path, 0.1, 1);
    Items.ModifyTooltip(this, (item, tip) => { if (item.Uses.Contains("Food")) tip.AddLine("Tasty."); });
}
```

## Crafting

A recipe in the game's crafting window (known per save).

```csharp
void Recipes()
{
    var r = Crafting.AddRecipe(this, "kit", "Field Kit", "Medicine", "consumables\\medicine\\healthhypo",
        RecipeSlot.Item("Bandages", "med.bandage", 10, 2), RecipeSlot.Item("Flare", "flare"));
    r.KnownByDefault = false;
    GameEvents.QuestChanged.Add(this, (quest, state) => { if (state == QuestState.Completed) r.Learn(); });
}
```

## Status effects and feats

With the game's own stat bonuses (never in the game's save files).

```csharp
void StatusAndFeats()
{
    var focus = ModStatuses.Define(this, "focus", "Focused", "+10 Guns.", "Status.PsiBoost", StatBonus.Skill("Guns", 10));
    var feat = Feats.Add(this, "steady", "Steady Hands", "+5 Guns, +5 Dodge.", new FeatRequirements { Level = 3 }, null,
        new[] { StatBonus.Skill("Guns", 5), StatBonus.Dodge(5) });
    GameEvents.CombatStarted.Add(this, () => { if (World.Player.HasFeat(feat.Key)) focus.Apply(World.Player, 3); });
}
```

## Abilities

An icon on the mod's ability bar, AP cost, cooldown, a target under the mouse.

```csharp
void Ability()
{
    var shove = Abilities.Add(this, "shove", "Shove", "8 damage to the target.", "art/shove.png", apCost: 15, cooldown: 2,
        target: AbilityTarget.Character, key: Keys.F11);
    shove.Range = 1;
    shove.Used += (user, target) => Combat.Damage(target, 8, "Mechanical", user);
}
```

## Characters

Spawn, allies, following, moving.

```csharp
void CharactersExample()
{
    var p = World.Player.Position;
    var tile = World.FreeTilesNear((int)p.X, (int)p.Y, 3).FirstOrDefault();
    var dog = World.Spawn("creatures\\dog", tile.X, tile.Y, SpawnSide.Ally);     // fights the player's enemies
    if (dog != null) dog.Follow(World.Player, 2);
    // World.Spawn(..., SpawnSide.PlayerOnly): fights the player only (the station's guards stay out of it).
    // c.MoveTo(x, y) walks, c.TeleportTo(x, y) places; World.Despawn(c) removes a spawn.
}
```

## AI

Who NPCs target, what they do, whether they flee.

```csharp
void AiExample()
{
    Ai.ChooseTarget.Add(this, c => c.Npc.Name.Contains("Rathound") ? World.Player : null);
    Ai.Acting.Add(this, a => { if (a.Action == "Throw" && !Combat.InCombat) a.Cancel = true; });
    Ai.ShouldFlee.Add(this, f => f.Npc.Health < f.Npc.MaxHealth * 0.2f ? (bool?)true : null);
}
```

## UI

Windows, lists, sliders, tooltips, HUD parts that layout mods can move.

```csharp
void UiExample()
{
    var win = Ui.Window(this, "notes", "Notes", 320, 260);
    var list = Ui.List(300, 160, 20);
    list.Items = new[] { "First", "Second" }.ToList();
    list.Selected += i => Log.Info("picked " + i);
    Ui.Add(win, list.View, 10, win.Top + 4);
    var slider = Ui.Slider(200, 0, 100, 50);
    Ui.Tooltip(slider.View, "How much.");
    Ui.Add(win, slider.View, 10, win.Top + 190);
    Hud.WhenReady(this, root =>     // now if the HUD is there, and on every rebuild (UiEvents.HudCreated: rebuilds only)
    {
        var badge = Ui.Custom(80, 20, c => c.Text("Hi!", c.Bounds.X + 4, c.Bounds.Y + 2, Color.White));
        Hud.AddPart(this, "Badge", badge, HudAnchor.TopRight, 10, 120);
    });
    var readout = HudText.Add(this, "Readout", HudAnchor.TopRight, 10, 150);     // lines of text; survives HUD rebuilds
    GameEvents.Update.Add(this, s => { var p = World.Player; readout.Show(p == null ? null : new HudLines().Add("HP " + (int)p.Health).Add(Text.Count(3, "turn"), Color.Orange)); });
}
```

## Map markers

Places on the Minimap (per save).

```csharp
void MarkersExample()
{
    Markers.Add(this, "cache", World.ZoneId, 20, 14, "Hidden cache", Color.Gold);
    // Markers.Remove(this, "cache") when found.
}
```

## Console commands

Typed in the game's console (`), also callable with Commands.Execute.

```csharp
void ConsoleExample()
{
    Commands.Add(this, "kills", "kills: hostiles killed in this game", args => "kills: " + SaveData.Get<double>("kills"));
}
```

## Character rules

Points at creation and per level, the ability range at creation.

```csharp
void RulesExample()
{
    CharacterRules.AbilityPointsAtCreation = 7;     // the game: 5
    CharacterRules.AbilityMaxAtCreation = 12;       // the game: 10
    CharacterRules.SkillPointsPerLevel = 45;        // the game: 40
}
```

## Stat formulas

Bonuses during the game's stat rebuild, skill weights, the game's feats.

```csharp
void StatFormulas()
{
    Stats.Modify.Add(this, r => { if (r.Character.IsPlayer && r.Character.Ability("Strength") >= 8) r.Add(StatBonus.CarryCapacity(25)); });
    Skills.SetAbilities(this, "Throwing", new System.Collections.Generic.Dictionary<string, float> { { "Dexterity", 1f }, { "Strength", 0.5f } });
    Feats.AddBonuses(this, "nimble", StatBonus.Skill("Stealth", 5));
    Feats.SetRequirements(this, "nimble", new FeatRequirements { Level = 4, Abilities = { ["Agility"] = 8 } });
}
```

## New abilities and skills

An "Other" tab on the character screen, points from the game's counters.

```csharp
void NewStats()
{
    var luck = CustomStats.AddBaseAbility(this, "luck", "Luck", "Fortune favours you.");
    var survival = CustomStats.AddSkill(this, "survival", "Survival", "Foraging and tracking.",
        new System.Collections.Generic.Dictionary<string, float> { { "Constitution", 1f }, { luck.Id, 0.5f } });
    Stats.Modify.Add(this, r => { if (r.Character.IsPlayer) r.Add(StatBonus.Health(survival.Value / 5)); });
}
```

## Exported characters

Data that travels with an export into a new game.

```csharp
void Exports()
{
    GameEvents.CharacterExporting.Add(this, path => CharacterData.Set("title", "the Hunter"));
    GameEvents.CharacterImported.Add(this, () => Notify.OverPlayer("Welcome back, " + CharacterData.Get<string>("title")));
}
```

## Other mods

Offer an API, call another's (optional dependencies stay optional).

```csharp
public interface IGreeter { string Greet(string name); }
sealed class Greeter : IGreeter { public string Greet(string name) { return "Hello, " + name; } }
void CrossMod()
{
    Mods.Provide(this, new Greeter());
    var other = Mods.Api<IGreeter>("SomeOtherMod");
    if (other != null) Log.Info(other.Greet("asd"));
}
```
