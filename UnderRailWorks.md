# UnderRailWorks

The source of truth for UnderRailWorks: what it is, the rules it keeps, what's decided, and what's next. Change this
file when a decision changes; keep it to one page.

## What it is

A mod framework for UnderRail (Styg). Two parts:

1. **The organiser**: gets mods into the game and manages them. It covers installing (the patcher), the loader, the
   `Mods` folder, enabling and disabling mods, load order, and showing errors.
2. **The modding layer**: opens the game up for mods. It covers readable names for the game's scrambled classes, hooks
   and events, access to the game's data (items, characters, maps, dialogs), and the engine fixes.

Mods are .NET DLLs that change the game at runtime with **Harmony** (MIT).

**UnderRailFixes** becomes a mod on UnderRailWorks, keeping the game-side fixes (Occluded characters). The engine fixes
move into Works: resolution, zoom, UI scale and insets, frame rate, 64-bit mode, window icon, DPI, save screenshots.

## Rules (never break these)

- **No game code or files are shipped.** Mods patch the player's own game at runtime; no decompiled code goes in the
  repo or a release.
- **Every file the stock game writes stays readable by the stock game**: saves, config.dat, global.dat. Players
  uninstall, and Steam restores the stock exe.
- **One broken mod never stops the game or the other mods.** It's logged, skipped, and shown to the player.
- **64-bit only, on our FNA.** UnderRailWorks always runs the game as a 64-bit process on the FNA we ship (the
  1.x 64-bit mode), on Windows and under Proton alike. There's no 32-bit or Microsoft XNA mode to support.
- **Everything Works writes is readable text**: `.ini` for settings people edit (Works' and each mod's), JSON for
  data Works and mods write (manifests, save sidecars, per-mod data), plain text logs. Open it in a text editor and see what's there. Only the game's own files stay in its binary
  format (gzip'd .NET BinaryFormatter), because the stock game must read them.
- **Versions:** UnderRailWorks is its own product, starting at 1.0, and doesn't need to work with UnderRailFixes 1.x
  (which stays maintained on its own line).

## Decisions

| Decision | Why |
|---|---|
| Harmony for mods, not a rebuilt game (tModLoader style) | Styg hasn't allowed shipping a rebuilt game; Harmony patches in memory |
| Keep a small patcher that changes `underrail.exe` | Steam starts the exe directly; launch options, proxy DLLs and config tricks all fail under Proton (AGENTS.md, "Why a patcher") |
| The loader starts from the existing startup hook (`FixStartup.Run`) | No new patch point; it runs before the window exists |
| Harmony is loaded by reflection | Our runtime has no compile-time reference to Harmony, so a missing `0Harmony.dll` only turns mods off |
| Mods reference the Works runtime and `FNA.dll` only, not `underrail.exe` (Nov 2026; replaces "mods reference the patched exe") | Works is a decoupled layer over the game, tied to game versions in one place: a game update means fixing Works' names (checked at startup), not every mod. tModLoader has no such layer (mods use Terraria's readable classes directly); UnderRail's names are scrambled and change between versions, and we can't ship a renamed game. Harmony on raw game types stays possible as a last resort, but a gap in the API is fixed in Works |
| Not now: a name map applied by the patcher (Minecraft's MCP/Yarn approach) | It would give mods the whole game under readable names, but thousands of names to remap on every game update, and mods would break with each one. The layer wraps only what mods need and keeps them stable |
| 64-bit on our FNA only; no 32-bit/XNA mode | One graphics library everywhere: mods and Works can use XNA types directly against our `FNA.dll`, with no Reach limits (2048 px targets, keyhole tiles) or type retargeting. Same game on Windows and Proton |
| A mod is any public class with `static OnLoad(Harmony)` or `OnLoad(Harmony, Action<string> log)` | Simplest entry point that works with no shared interface |
| No Godot port | About 8,800 types and 60,000 methods to rewrite, and the data files are the game's own .NET objects. Reconsider once the naming layer and data tools exist |
| Map editor is a separate, later project | Needs the data access and naming layer first |

## How the game is opened up (the tModLoader model, with Harmony)

tModLoader puts hook calls into Terraria's code, routes them to static "Loader" classes that loop over mods, gives
mod content IDs after vanilla's, lets "Global" classes change vanilla content, and saves mod data in sidecar files
so vanilla saves stay vanilla. Works does the same, adapted:

| tModLoader | UnderRailWorks |
|---|---|
| Patch files against the decompiled game, shipped rebuilt | Works' own Harmony patches on the player's game; nothing rebuilt or shipped |
| Static Loaders (`ItemLoader`, `NPCLoader`...) loop over mods | Works' Loaders (items, characters, maps, combat, UI) loop over registered mods |
| New content gets IDs after vanilla's | **Data overlay**: the game loads content by path (`Rules\Items\<path>.item`...); Works checks each mod's `data` folder first, so mods add or replace files |
| `GlobalItem` / `GlobalNPC` | Per-item / per-character hooks that also run on vanilla content |
| `ModSystem` | Events (Part 2) |
| Mod data in `.twld`/`.tplr`; "unloaded item" placeholders | Saves already store items by path only; a missing item loads as a "?" placeholder (proven, below). Extra mod state goes in sidecar files |
| MonoMod detours for the rest | Harmony directly |
| Readable source | Scrambled names: a **mapping file** per game version (as Minecraft's Forge/Fabric do); Works and mods use the readable names |

**Proven (spike, Nov 2026, 64-bit under Proton)**:
- **Item overlay:** items load through one method, `ez2.b(string path, bool logError)`, which reads
  `<data>\Rules\Items\<path>.item` with `bk3.LoadFromBinaryFile<bk3>`. The `HelloItems` mod adds a Harmony prefix
  that serves `Mods\<mod>\data\Rules\Items\<path>.item` first. A new item, `works\hellothrowingknife`, loaded and
  went into the player's inventory, given the way the game's own console command `playerGiveItem` does it:
  `ez2.a(path)`, then `Bag.z3(item)`.
- **Making mod data without shipping game data:** a small tool loads a vanilla `.item` with the game's own classes,
  changes it, and writes the new file. It needs the game's type resolver set up first, as `tb.Initialize` does:
  create `af9`, call `b(true)`, `a(true)`, `av1()`, `d()`, then set `dds.a`. Real mods would make their files this
  way, on the player's machine or from code at load time.
- **Saves:** the item went into the save as `LIDP:P = works\hellothrowingknife` (`LibraryItemDefinitionProvider`), the
  path only. No mod types and none of the item's data are stored.
- **The stock, unmodded game loads that save.** The item shows as a "?" placeholder; the game logs the missing
  definition and returns a default one. The provider writes its path back unchanged when saved, so the item should
  come back when the mod is reinstalled (not tested yet).
- **Text:** item names and descriptions come from `qv.b("ItemNames/" + name)`, already patched in HelloWorks, so mods
  can add text the same way.

**Still to check:** the other loaders (characters `.char`, stores, dialogs `.udlg`, maps `.uzl`/`.uz`) and whether
one shared point covers them all. Most go through the generic `eoq.DeserializeFromBinaryFileCompressed<T>`, and
patching a generic method with Harmony is unreliable, so the plan is one patch per loader. Also: a stock save
round trip (mod item → stock save → mod back), and mod items that need new code, not just new data.

## Settings and UI for mods (as tModLoader)

Two separate systems, as tModLoader's ModConfig and its UI toolkit:

1. **Settings: `ModConfig`** (`src/Runtime/ModConfig.cs`). The mod declares a class with public fields and attributes
   (`[Header]`, `[Label]`, `[Tooltip]`, `[Range]`, `[Increment]`, `[ReloadRequired]`, `[Hidden]`; `[Label]` on enum
   members names the choices). Works finds it, writes `settings.ini` with comments and defaults the first time, builds
   the Mods tab rows (bool: tick box, enum: dropdown, int: slider) with a Restore defaults button, writes changed values
   on Accept and calls `OnChanged`. The mod writes no UI code; `GetConfig<T>()` gives it the values. `ModPage` stays for
   anything a config can't express. UnderRailFixes uses a config class (`FixesConfig`).
2. **UI toolkit** (`src/Runtime/Ui.cs`). The game's own controls under readable names (`Ui.Label`, `Ui.Button`,
   `Ui.TickBox`, `Ui.Panel`, `Ui.Add/Remove/Move/Resize/Show/SetText/SetColor`), so mod UI looks like the game's and
   follows Works' interface scale and insets. The game has no named interface layers like Terraria's; instead mods
   attach to screens as the game builds them: `UiEvents.HudCreated` (the in-game screen, rebuilt after each load or
   relayout), `MainMenuCreated`, `OptionsCreated`; `GameEvents.Update(seconds)` each update; `Input.Pressed(Keys.X)`.
   These hooks are Harmony patches Works makes itself (new hooks are Harmony, not the patcher). Elements are the game's
   own control types, so a mod can go further with the game's API or Harmony. Example: `examples/HelloUI` (a session
   clock panel on the HUD, toggled by a key from its config).
   Tested in game: the panel, its border, the key toggle, the Hide button, both mods' generated settings pages, and a
   setting changed in the Mods tab applying at once. Not yet: keys in the game's own key-binding screen (the mod's
   config holds its key for now), dragging panels, tooltips, windows that take keyboard focus.

## The Minimap mod (`mods/Minimap`)

A larger test of the API: a HUD panel (UI toolkit) with a ModConfig (size, tiles across, corner, opacity, characters,
exits, toggle key), drawn from the game's own data through Works' world API (`World`, `Area`, `Character`,
`Ui.Custom`). Its first version reached past Works with scrambled names (aspect classes, a name field, a subclass of
the game's control); those became Works' `TileKind` classification, `World.Visible` and `Ui.Custom`, and the mod is
now built without the game.
- Isometric like the screen (the game's projection, a7m.vq: screen x from x - y, y from x + y), rebuilt 4 times a
  second into a texture.
- What the game's data says (measured, not guessed: a grid dump and the entities' names and aspects): everything solid
  has the `cd0` aspect ("SEA"); unnamed solids form the walls, named ones are props (desks, shelves, barrels), doors
  have `cnb` ("DEA"); exits are area transitions (`b0j`, "ATT") and zone transitions (`cdo`, "ZTT").
- The player as a gold dot and the characters the player can see as dots in their Tab outline colour (as eg0 picks it:
  gold for the player, sandy brown for passive, else the `adi.ae` table): "can see" = the game drew them this frame
  (from the drawing events), so stealthed or out-of-sight characters stay hidden.
- Exits in the game's transition colours (a23: blue to another area, red to another zone), only while open.
- The shroud: with the game's fog of war on (`dnn.h.a == edg.b`), only what the observer (`dnn.c.d`) has discovered
  (`b0b.b(observer)`) is drawn, as the world view does. Measured: 1,952 of 6,241 tiles hidden in a part-explored area.
- Placed bottom right by default, beside the action bar (judged from a screenshot at the player's 32:9 screen with
  30 % side insets: the top right covered the view ahead and an icon).
- Tested in game: layout against the screen in three places, the player dot, three NPC dots (blue: allies), exits.

## Saves and mods (copying tModLoader)

**What tModLoader does** (read from its source: `WorldIO.cs`, `WorldFileData.tML.cs`, `UIWorldListItem.TML.cs`,
and the same for players):
- Vanilla data stays in the vanilla file (`.wld`/`.plr`); mod data goes in a sidecar (`.twld`/`.tplr`) holding:
  `usedMods` (the names of the mods enabled when last saved), `usedModPack`, the mod versions at world creation,
  per-mod header data, and any mod save errors.
- **No warning when loading.** Instead, the world/player list shows a warning icon on any entry whose mods differ:
  hover gives a short line ("Missing 2 mods; 1 new mod"), click gives the full list of missing and new mods (and a
  changed mod pack). It compares names only, not versions. A second icon shows if a mod failed while saving.
- Content from a missing mod is kept as "unloaded" data and written back on every save, so nothing is lost when the
  mod returns.
- Before saving it copies the file to `.bak`; a corrupt save can be restored from it.

**How UnderRail saves** (from the decompiled code)
- A save is a folder (`Saves/<name>/`: `info.dat`, `global.dat`, `chunk0.dat`, `.map` files, `screenshot.png`).
- Saving deletes the old folder, or moves it to `<name> (previous)`, then writes a fresh one (`d6f`, the save
  method). Any extra file in the folder is lost on every save and must be written again after it.
- Items are stored by path only (`LIDP:P`), so a missing mod's items load as "?" placeholders (proven in the spike)
  and keep their path when saved: the game's own "unloaded item".

**Plan: the same, in UnderRail's terms**
- [ ] **Sidecar per save**, `Saves/<name>/works.json` (readable JSON, see the Rules), written right after each save:
      the enabled mods (`usedMods`, ids), their versions, the mods and versions when the character was created, Works
      and game versions, and mod save errors. Per-mod data (a Works API like tModLoader's `SaveData`/`LoadData`, as
      key/value data) goes in `works/<mod id>.json` beside it. Values are only strings, numbers, booleans, lists and
      objects (like tModLoader's `TagCompound`), never game objects. Files are written to a temp file and renamed, so
      a crash can't leave half a file. Floats are written round-trip exact. Large data (e.g. a custom map) is never put in JSON: it
      gets its own file in the game's format next to it, and the JSON names that file.
- [ ] **Unloaded mod data kept**: data belonging to a mod that isn't loaded is read and written back unchanged.
- [ ] **Load Game screen**: a warning icon on saves whose mods differ (missing or new mods). Hover: a short line;
      click: the full list in the game's dialog (`bnr`). A second icon for mod save errors. No prompt when loading.
- [ ] **Backup before saving**: copy the folder first. It must go outside `Saves` (e.g. `Saves Backup/<name>/`), or
      the game would list it as a save. The game's own `(previous)` copies only cover some saves.
- [ ] **Vanilla access stays (a Rule):** a save made with Works loads in the stock game; mod items show as "?". If
      the stock game saves over it, the game drops the sidecar (it rewrites the folder), but mod item paths survive.
      Works then treats it as a save without Works info: no warning.
- [ ] **To prove:** a mod item survives stock save → mod reinstalled; the game ignores an extra file in a save folder;
      the hook points for "after save", "load screen entry" and "before load".

**Built (Nov 2026): the state in plain variables, mod objects never saved** (`src/Runtime/Saves.cs`). Measured from
the game's code: a save folder is `global.dat` (the game state `cyt`, written by `d6f.b(folder, transitional)`) plus
bulk storage, where each visited zone is stored by `bl1.yp` (on every save, and when the player leaves the zone; the
zone-change autosave runs after that); the stock binder throws on any unknown type. So:
- **Placed characters are never saved.** A mod declares placements (`World.Place`); Works spawns them when their
  zone loads (after a save loads: once the player is placed, on the first update) and takes them out right before
  `bl1.yp` stores the zone, putting them back after a save (and re-applying the placement once the save's own
  redeploy has run: AutoUpdate blueprints reload their template data on deploy).
- **Mod quests** are taken out of the quest lists while `global.dat` is written; their state is kept in a global
  variable per quest (`works_quest_<id>` = `"active|completed|failed,read,index"`) and rebuilt after a load. The
  stock game sees a string variable, nothing else (the five "fail all active quests" scripts would otherwise hit a
  null quest definition).
- **Mods keep their own state in `Variables`** (bool, int, float, string only): saved by the game, ignored by the
  stock game, and kept even if the stock game re-saves the file.
- **SaveGuard**: the binder hook (`e6e.av5` -> `FnaMode.SavedName`) refuses any type from Works or a mod: the save
  fails (the game writes `SaveError-*.txt`) rather than becoming unloadable.
- Proven: a quicksave with a mod quest active and a placed character: `global.dat` holds only the variable, the
  stranger is in neither file, and the stock loader (`check-saves.sh --only <folder>`) reads all three files; after
  quickload the quest is active again and the stranger is back.
- The sidecar plan above stays for what variables can't hold (large or structured mod data) and the Load Game
  warning icon; not needed by anything yet.

## The content API (Nov 2026)

Researched per domain from the decompiled code (four specs with file:line evidence), then built and tested in game
through two example mods. Mods see only Works' types; the game's are inside.
- **World** (`World.cs`, `Characters.cs`): `World.Player`, `World.Visible`, `World.FogOfWar`, `World.ZoneId`,
  `World.Place(Placement)` / `World.Placed(id)`; `Area` (Name, Width, Height, `KindAt`, `Exits`); `Character`
  (Id, Name (settable: the data's and the entity's, the hover label), Level, Faction, Passive, IsDead, Kill, Health,
  MaxHealth, `Ability`/`SetBaseAbility`, `Skill`/`SetBaseSkill` by name (the game's own stat objects, then `efk.bl()`
  as its console does), Feats, Experience/GiveExperience (`awq.a`), `AttitudeToPlayer`/`SetAttitudeToPlayer`,
  `CanTalk`, `Variables`, `Inventory`). Spawning is the game's own helper (`vq.a(blueprint, zone, bt4)`, as its
  spawnEntity command); a placement's dialog is the game's dialog aspect (`bn5`), added as the game's scripts do.
- **Inventory**: Count/Has by code, Add by path (the game's GiveItemsJob way: lootable, "Item acquired"), Take by
  code (RemoveItemFromPlayerAction's way: "Item lost"), Money/AddMoney/TakeMoney (charons, `currency.sc`).
- **Variables** (`Content.cs`): `Variables.Global` (where all story state lives), `Variables.Zone`,
  `character.Variables`; primitives only.
- **Quests**: the game defines quests in code (`b4m`: `ao5.a(group, name, xp, oddityXp, text)`), has no stages (a
  multi-step quest is several quests), runs them through its quest controller (`ek2`: start, complete with XP, fail).
  `Quests.Define/Start/Complete/Fail/Advance/State/All/Active...`; the journal shows mod quests under their group.
- **Text**: `Text.Get("category/key")`, `Text.Set(...)` (a hook at the start of `qv.b`: Works' text first, in every
  language, in memory only). **Notify**: `Notify.Log` (the chat line), `Notify.OverPlayer` (floating text).
- **Dialogs** (`Dialogs.cs`): a builder (lines, options with `Condition`s and `Outcome`s, greyed options, branches,
  Next/End, `$(context.var)` tokens) from which Works builds the game's own dialog model (`azm`: Question = NPC
  line, Answer = option, junctions) on every opening; Works' conditions and outcomes are nodes of their own (dialog
  models are never saved). One hook: the start of `dka.amu` (open a dialog by name), which also serves
  `Dialogs.Override(character, name)`. The game's dialog window, speaker name and portrait, numbered options.
- **Events** (`GameEvents`): Update, NewGame, GameLoaded, ZoneLoaded(zoneId), Saving/Saved(folder),
  QuestChanged(id, state), CharacterDied, CombatStarted/Ended (Harmony on `d6f.arf/ari/arp/arg`, `ek2`, `amc`, `crp`).
- **Examples**: `examples/HelloQuest` (a quest and a conversation with whoever is nearest), `examples/TheStranger` (a
  new character at South Gate Station, a conversation, a quest paid in charons with an item reward), built without
  the game. `tools/testmods/WorksTest`: test keys (F10: 20 charons) and event logging.
- **Tested in game** (Nov 2026, test copy, 2560x720): spawn, Talk by clicking, branches by quest state, hidden and
  greyed options, money check/take/give, item reward, quest XP, journal entry, chat lines, quicksave (stock-loadable,
  checked), quickload (quest and stranger back).
- **Events added**: ZoneLoaded(zoneId, firstVisit) (first visit = the game's own test, `d6f.a(bfc)`, which copies the
  map's layer only then), DialogStarted/Ended (`zq.n1/n2`), ItemAdded to the player's bag (`dcw.g`),
  ExperienceGained (`amc.a(int, string)`).
- **More**: `Ui.Wrap/TextWidth` (the interface font), `Quests.Info/JournalText` (as the journal formats it, `eb2`),
  `Character.Say` (speech bubbles, `c0s`; the game also logs them), `Items.Code/Name` (item path -> code, name).

## Combat, HUD, windows, trade, effects (Nov 2026)

Researched from the game's code (specs with file:line evidence), built in `Combat.cs` and `Hud.cs`, tested in game.
- **Combat** (`crp`, the `d02` service): `TurnStarted`, `TurnEnding`, `RoundEnded`, `ModifyTurnPoints` (AP/MP after
  the refill, `d4n.a()`), `ModifyHitChance` (the three builders `euf.a` -> `bg3.a`; the game's targeting preview
  shows the changed value, measured: 0.62 + 30 points = 0.92 on screen), `ModifyIncomingDamage` (prefix of
  `nw.a(cwt)`, the one damage pipeline every source goes through: parts per damage type, `Scale`, `Cancel` = miss),
  `AttackResolved` (its postfix: HP lost, killed; a miss returns an outcome with a null damage list), `ByPlayer`
  from the attacker's uid (`cwt.i.d` vs the player's `efk.Uid`), `Combat.ActionPoints`, `Combat.Log` (the combat
  feed: `vr` + `bey.a` + `a7v.a`; `Notify.Log` goes to the chat feed, which a fight hides), `Combat.Float`.
  Character data has no link back to its entity: Works finds it by scanning the zone once, cached.
- **HUD** (`ci3` builds its parts once, in its constructor; a new `ci3` after loads and resolution changes):
  `Hud.Get(HudPart)` handles for the portrait, action bar, system bar, combat panel... (move/hide with `Ui`),
  `Hud.Visible`, `Hud.VisibilityChanged` (the game's F2 toggle resets every part). The decompiler names a field
  `m_x` when a method is also called `x`: the real field is `x` (found because Hud.Get returned nothing; the startup
  name check now also checks tables of lookups).
- **Points**: `Character.SkillPoints / AbilityPoints / FeatPoints` (the player's unspent points, `e3q` g/h/j and
  b/d/f; the game only lets you spend them on its level-up screen), `SkillNames`.
- **Merchants**: `World.Characters` (everyone in the zone), `Character.Stock`, `Markup`, `GameEvents.TradeOpened`,
  `Character.MakeMerchant(store)` / `Placement.Store` (placed characters only: the merchant aspect plus the dialog
  aspect the barter screen takes, `aim(..., j7)`; without it the screen crashed), `Item.Value/Weight`.
- **Text boxes**: `Ui.TextBox(width, maxLength)` -> `UiTextBox` (Text, Changed: checked once per update): the game's own
  field (c68, set up as its split-stack dialog does). `Input.Typing`: a text field has the keyboard (a focused t3:
  mod boxes, the game's fields, its console; checked only when a key changed); Keybind.Pressed/Down are false
  meanwhile, as the game's hotkeys are. A click outside a mod's focused box gives the focus back to what had it before
  (focus service d1a: aa7 focused, aa4 set): the game's hotkeys arrive through the focused control, and with none (a
  box on the HUD kept it) no key reached the game. Tested: typing "kkk" doesn't fire the minimap's K; after a click
  away K and I work. (Test trap: a click on the world at some spots leaves the game without keys in the headless
  runner; click the bottom HUD edge with focus first.)
- **Key bindings go through the game's Controls tab** (Nov 2026; every mod key): `Keybinds.Register(mod, id, label,
  defaultKey)` -> `Keybind` (Key, Pressed, Down). The Controls list (ar4) holds Hotkey values and builds a row (cbx)
  per value; mod actions get Hotkey values from 100000 up, appended before the rows are built (prefix on ar4.a(size));
  Works answers their label (d1o.c, "Minimap: Show/hide") and key (d1o.a), and saves them on Accept in the mod's
  settings.ini (key.<id>; prefix on cbx.a(settings)), so config.dat never holds a mod's key. The game's own capture
  row and its conflict question work for them as for its own. A mod's settings page lists its keys ("change them in
  Options -> Controls"); the log notes two mods with the same default. Tested: rows shown, K captured, saved in the
  mod's file, K then toggles the minimap. (Trap: an enum default value on a public parameter, Keys defaultKey =
  Keys.None, made the build's retargeting resolve XNA: overloads instead.)
- **No buttons in mod settings** (decided Nov 2026, tried and removed): a settings page applies on Accept and can be
  cancelled; a button acts at once, can't be undone with Cancel, and needs a game in some cases (none on the main
  menu). Actions belong in the mod's own UI (e.g. HUD Layout's Layout button on the HUD). `Windows.CloseOptions(save)`
  stays (closes Options as Accept/Cancel would).
- **Options tab headers fit the window**: with the Mods tab added the headers ran past the tab control, and the part
  outside took no clicks (the game passes a click only to a control whose parent contains the point; measured:
  the Mods tab only answered in its first few pixels at 2560x720 with insets). Works narrows the headers so the last
  ends inside (`dpg.b(size)`).
- **Mod elements hide with the HUD**: the game hides its HUD part by part (`ci3.a(false)` from `dka.j()`: while the game
  menu, Options or the character sheet is open, or with the HUD toggle off). Every element a mod puts directly on the
  HUD (`Ui.Add` to the root, `Hud.AddPart`) is tracked and hidden with it, then shown again as the mod last wanted
  (a mod's own show/hide while the HUD is hidden is kept for later). Mods do nothing for it.
- **Mod HUD parts**: `Hud.AddPart(mod, name, element, x, y)` puts a mod's element on the HUD as a named part
  (`Hud.ModParts`, `Hud.PartAdded`), so layout mods move mods' panels like the game's; a rebuilt panel is added again
  and the layout re-applies the player's position. The Minimap and the Quest Tracker use it.
- **Open windows**: the game's windows with a title bar are `a9t` (title label, close button at the right, title height
  `a9t.i`), built anew as children of the in-game screen each time they open. `Windows.OpenWindows` lists them as
  `OpenWindow` (Kind: Inventory, Notes, Crafting, Oddities, Psi, Map, CombatStats, Barter, Container; Element;
  TitleHeight). The character sheet (`iw`) has no title bar of that kind.
- **Windows**: `Windows.Open/Close(Window.Inventory | Notes | Map | ...)` (the `ps` methods the HUD buttons call,
  refused while `ps.alg()`). **Trade.Open(merchant)** (`ps.amw`), **Prices.BuyMarkup / SellFactor** (postfixes of
  `dvp.aqd`, `b6o.d`). `Character.IsMerchant`.
- **Status effects**: no registry by code in the game (types obfuscated, the console's lookup fails): Works builds one
  from the effect types it can construct (127 codes); `StatusEffects.Apply(character, code, turns)` through the
  game's own `br5.a` (stacking rules intact; tested: Agility Boost, its icon and countdown by the portrait),
  `character.StatusEffects`, `HasStatusEffect`, `Remove`.

## The drawing API in Works types (Nov 2026)

`DrawApi.cs`: struct handles around the game's objects (no allocations: the world pass runs them for every layer of
every frame): `WorldView` (a7m: Zoom, Clip, ShaderLayers, BeginWorldBatch), `DrawContext` (cp9: Device, EndBatch,
ResumeBatch, Batch), `GameBatch` (ob: Begin, End), `LayerShader` (cmk: Find, Effect, SetParameters), `Layer` (a2f:
Bounds, Source, Tint/SetTint, Id, Owner, Entity, IsFlat, Drawable, ShaderName/ShaderParameter, Draw, DrawAsGame,
IsPlainSprite), `LayerSet` (bym: Entity, HasOutline), `MapObject` (b0b: MapPosition, IsFlat, Character), `MapTile`,
`LayerView`. Equality is the game object's identity, `Key` gives it for identity tables, `IsNull` replaces null
checks. WorldDraw's events and Outlines take these; the game-typed helpers (Layers, Entities, Areas, WorldViews,
DrawContexts) are internal to Works now. UnderRailFixes was ported (mechanical) and is built without the game.
Proven pixel for pixel against the build before (old vs new, same save, 2560x720): keyhole 7 px apart (two shots
of the same build differ by 10), lines 0.6 px, fill 2.4 px, a scene without effects 0.

## Content packs: mods as data (Nov 2026)

Most mods in the most moddable comparable games are data, not code (Cataclysm DDA's JSON, RimWorld's XML Defs, Caves of
Qud's blueprints, WeiDU's dialog patches). A Works mod may hold `content/*.json` (with or without a DLL):
`text`, `quests`, `dialogs` (nodes with text, options, branches, `if` / `enabledIf` conditions, `do` effects) and
`placements`. Conditions and effects are small objects (`{ "money": 10 }`, `{ "skill": "Biology", "min": 10 }`,
`{ "takeMoney": 10 }`, `{ "startQuest": "id" }`...), all mapped onto the C# API, so packs follow the same save rules.
Format and the full list: the header of `src/Runtime/ContentPacks.cs`. Example: `examples/TheProspector` (no code: an
NPC, a conversation with a hidden and a greyed option, a quest, a reward). Tested in game: the conversation, the
options' conditions, the quest started and completed, the bandage taken, 25 charons paid. A bad entry is logged with
the file and skipped.
**Triggers** (CDDA's effect-on-condition, in small): `{ "on": event, filters, "if": condition, "do": effects,
"once": true }` for zoneEntered, questChanged, levelUp, itemAdded, equipped, statusApplied, characterDied,
dialogEnded, combatStarted/Ended, gameLoaded, newGame. Code mods extend the vocabulary with
`ContentPack.AddCondition` / `AddEffect` (`PackArgs` reads the arguments); packs load after all mods' code so they
can use them. Tested: status, equip (with a DLL condition) and zone triggers fired; a "once" trigger did not fire
again after a quicksave and quickload.
Research on what else those games offer, ranked (per-save data, item/status/feat definitions, cancellable hooks,
equipment, loot, stealth, AI, crafting, dialog patches, a content registry): see TODO.md.

## Status effect events, levels, per-save data, the content index (Nov 2026)

- `GameEvents.StatusApplying` (set `Cancel` to refuse the effect), `StatusApplied`, `StatusRemoved` (removed, expired
  or replaced): `br5.a(els, ...)` and `br5.c(els)`, which every removal goes through. Tested: Agility Boost applied,
  expired 15 s later (3 turns out of combat).
- `GameEvents.LevelledUp(level)`: the character screen in level-up mode (`iw`, `a9m.b`) closed with Accept
  (`amm.a(cku.a)`, the base class's close-with-result; Harmony must patch it there, not on iw). Tested.
- `WorksMod.Data`: the mod's own values for the current game, saved with it as plain global variables
  (`md_<mod>_<key>`), so the stock game loads the save. Tested: quicksave at 2, quickload restores 2.
- `ContentIndex.Write()`: `UnderRailWorks-content.txt` with every item path = code (name), blueprint, store, dialog
  and status effect code (about 4,000 lines), for writing content packs and mods.
- Hook groups now install separately: one that fails (moved by a game update) leaves the others working (a failing
  group used to stop every group after it).

## Equipment (Nov 2026)

`Character.Equipment`: `[EquipSlot]` (Helmet, Armor, Belt, Boots, Shield, PrimaryWeapon, SecondaryWeapon), `All`,
`QuickSlots`, `Equip(item[, secondary])` (from the bag, the game's `bn.a` with Primary/Secondary: `None` equips
nothing), `Unequip(slot)` (into the bag). `GameEvents.Equipped` / `Unequipped` for every slot change of any character
on the map (acd's private notifiers: `a(coh)` gets the **old** item, `b(coh)` the new one, because `c1.b()` is the
old value). Tested: armour off and on twice, events in order, the bag consistent.

## Progression (Nov 2026)

`Progression.ModifyXpNeeded` (postfix on `c2h.a(level, ExperienceSystem, intelligence)`: the level-up check and the
deduction on level-up both use it), `ModifyLevelPoints` (around e3q's private `a()`, the per-level grant; level 1 is
`b()` and untouched), `ModifyExperience` (prefix on `e3q.c(amount)`), `XpFor(level)`, `Oddity`. Tested: level 2
gave 40 + 10 skill points and an extra feat; the Oddity costs 4, 6, 8, 9, 12 became 2, 3, 4, 4, 6 at 50 %.

## Patching the game's conversations (Nov 2026)

`Dialogs.Patch(mod, "characters\\bigbret1", p => { p.AddOption(atLineText, text, goTo, when, effects); p.Line(...) })`
(WeiDU's EXTEND, in small): options on every stock NPC line whose English text contains `atLineText`, leading into the
patch's own lines; `"@"` goes back to that stock line, `"@text"` to another stock line. Applied in a postfix on
`aji.a(model, name)` (the loader `bmz.y3` reads the file fresh every time, then aji fills in the texts), so nothing is
written to the dialog files or the save. `Dialogs.StockLines(name)` lists a dialog's NPC lines. Content packs:
`"dialogPatches"`. Tested: WorksTest (code) and TheProspector (JSON) both added an option to Big Bret's first line;
the mod line showed, and "@" returned to Bret's stock line with all his options.

## Containers, loot and locks (Nov 2026)

- `World.Containers` (the zone's `c7j` containers), `Container` (Items, Add stock items, Remove, Position, Searched,
  Lock, `Open()` for the player without walking or the lock).
- `GameEvents.ContainerOpened(container, opener, firstTime)` (`c7j.mz`), `ItemTaken(container, item, taker)`
  (`c7j.kl`), `LootGenerated(target)` (around `ery.mx`, which rolls each entity's loot once, at its first deploy:
  first zone visit or spawn; the loot is saved, so only stock items may be added).
- `Lock` (Locked, Kind, Difficulty, Pickable, KeyCode, `TryPick(character)`), `Locks.ModifyAttempt` (Bonus or
  Force), `GameEvents.LockAttempted` (around `d2b.a(amc, LockType, int)`: no dice, skill + tool bonus vs difficulty).
- `Container.Owned`, `IsOwner(character)`, `Witnesses(thief)`: the game's theft rule without running it (`c3g.a(amc)`
  is not a check: it provokes; the owner test is `c3g.a(efk)`; protectors are `c3g.g()` ids and `c3g.d()` factions,
  and in mode `c2n.b` only those in the area whose vision `bm6.b(thief, false)` sees the thief count).
- Tested: loot rolled for the spawned NPCs; the nearest locker opened with 2 added stacks; a difficulty-30 lock
  opened with skill 16 + 50. The game's own rules held: picking a guarded locker, and taking from an owned one in
  view of its protector, both turned a guard hostile.
- Mapped but not yet in the API (`scratchpad/specs/loot-locks-stealth.md`, not committed): pickpocketing (`b87`),
  detection (`bui.a`, `cn8.agf`), traps (`qw.a`, `ds.fr`), crafting (`cwp.a`, `ahu.i`), corpse loot (`asy.a`).

## Stealth (Nov 2026)

`Stealth.ModifyDetection` (postfix on `bui.a(target, detection, stealth, distance, sight, angle, light,
darkvision)`: awareness gained per scan step) and `Stealth.ModifyInstant` (`bui.a(detection, stealth)`: "seen at
once"). Sneaking is the status effect `stealthMode` (its `df` state sets `SpecialStates.g`). Tested: the instant
check ran (detection 90 vs stealth 16: seen at once) and was overridden; it runs about 300 times a second near a group
of guards, so handlers must be cheap. **Not yet seen in game:** the rate hook (the guards had already detected the
character before it sneaked, so the game skipped the rate); Difficulty Dial's "how fast enemies notice you" uses it.

## Places that survive resolution changes (Nov 2026)

HUD Layout and Movable Windows 1.0 saved pixel positions per HUD size (`Part@2560x720=x,y`): after a resolution or
interface-scale change the layout was simply gone (and nothing kept a part on screen on a smaller HUD).
`Ui.Place(element)` now writes a place relative to the nearest edge or the centre on each axis (`R20,B150`: 20 from
the right, 150 from the bottom; `C`/`M` for the centre), `Ui.Restore(element, place)` puts it back on any size and
keeps it inside its parent, `Ui.Place(x, y, w, h, parentW, parentH)` converts old pixel entries, `Ui.KeepInside`
clamps while dragging. Both mods convert their 1.0 entries on first use. The game rebuilds its HUD after a resolution
change (new `ci3`), so HudCreated re-applies everything. Tested at 2560x720 and 1600x900 (a 1.0 entry from 1792x720
converted and kept its place relative to the edges).

## HUD anchors, grid snapping (Nov 2026)

- `HudAnchor` (nine), `Hud.AnchorOf(part)`: the game's own anchors, read from ci3's layout (portrait top left at
  10,10; system bar, zone security, alert top right; action, XP and ammo bars and the speed indicator bottom centre;
  combat panel bottom right; zoom indicator centre; feed and the speed/zoom buttons bottom left).
- `Hud.AddPart(mod, name, element, anchor, dx, dy)`: mods anchor their parts (offsets inward from the anchored
  edges); `Hud.At(anchor, element, dx, dy)`; `HudModPart.Anchor`. Minimap, Quest Tracker, Status Board use it.
- `Ui.Place(element, anchor)`, `Ui.AnchorOfPlace`, `UiCanvas.Text` (the interface font), `Input.Wheel`. `Ui.Place`
  returns null for an element not on screen (a removed panel), so pixels are never written.
- HUD Layout: a part keeps its natural anchor unless dropped in the opposite third; the wheel over a part picks the
  anchor yourself (Auto, then each of the nine); the overlay labels the anchor and draws a line to the screen's
  anchor point; snapping to a grid counted from the anchor (settings: on/off, size 2-40, default 10 like the game's
  margins; Shift drags freely). A drag follows a part by name when its mod rebuilds it. Tested in game.

## Mod windows, HUD lifecycle, tolerant hooks (Nov 2026)

- `Ui.Window(mod, id, title, w, h)` -> `UiWindow`: a title bar, a close box, dragged by the title (its place kept per
  mod in settings.ini as an anchored `Ui.Place`), Escape closes the topmost one: Works intercepts Escape in the game's
  key handler `dka.a(object, ane)` (`ane.a == 27`; the handler's `e()` closes the game's topmost screen or opens the
  menu), so the game menu doesn't open meanwhile. `Top` is where content starts; `Closed`; windows close with their
  HUD. Merchant Ledger, Vendor Directory, Loadouts and the After-Action Report use it. Tested: open (K), drag, Escape.
- `UiEvents.HudDestroyed` and `Hud.Exists` false on returning to the main menu (Hud.Current used to stay set, and
  mods kept acting on the dead HUD).
- A failing handler is logged (first 3 times) and only removed after 10 failures.
- `Progression.MaxLevel`, `CanLevelUp`.
- From a review of all mods: After-Action Report resets on load / new game; Vendor Directory skips hostile merchants
  and combat, and no longer rebuilds its window every second (that swallowed clicks on Trade); Contract Board
  bounties count only hostile kills; Neural Reallocation explains the level cap instead of taking the money;
  Minimap's hidden state survives HUD rebuilds and merchant squares follow zone changes at once; Difficulty Dial
  announces the detection setting; Loadouts opens with K by default.

## Temporary characters; switching mods (Nov 2026)

- `World.Spawn(blueprint, x, y, hostile | SpawnSide)`: a character now, in the player's area, created once (a
  placement marked Temporary: taken out around saves, forgotten with its zone or on load, never re-created);
  `World.Despawn`; `World.FreeTilesNear(x, y, radius)`.
- `SpawnSide.PlayerOnly`: fights the player and no one else. An NPC's attitude to a faction is its own override
  (`efk.an`), else the faction table (`oj.abu`), else neutral; the spawn gets a faction no table knows plus an
  override hostile to "player". Measured: hostile rathounds at South Gate were killed by the guards in two seconds;
  making the guards Passive didn't stop them; giving the spawns the player's own faction turned the whole station on
  the player; with PlayerOnly the guards stayed out and the rathounds fought the player alone.
- Mods load at start. In a game the Mods tab's switches are greyed ("switched on and off from the main menu's
  Options"); at the main menu they apply at the next start, and the status says "On at restart" / "Off at restart".

## The test mods (Nov 2026)

Built to find what the framework lacks (each gap went into Works, never into the mod), all built without the game:
| Mod | What | Exercises |
|---|---|---|
| Minimap | the area around you, exits, visible characters, merchants (gold squares) | World, Area, Ui.Custom, ModConfig, World.Characters |
| QuestTracker | active quests on the HUD with journal text, done/failed lines | Quests, Ui.Wrap, events |
| WordGetsAround | nearby friendly characters remark on your deeds; `AddLine` library API for other mods | Character.Say, Variables, CharacterDied |
| DifficultyDial | damage taken/dealt, hit chance, AP as settings | Combat hooks, ModConfig |
| AfterActionReport | a summary after each fight, career totals | Combat events, Ui panel, Variables |
| ContractBoard | generated bounties and deliveries as quests from a placed contractor | Placement, dynamic dialogs, Quests, Text, Items, events |
| VendorDirectory | F6: the zone's merchants, nearest first, Trade from the list (in reach, or anywhere as a setting) | World.Characters, Stock, Trade.Open, TradeOpened |
| FairTrade | merchants' buy markup and sell price as settings (proven on the barter screen: 750 -> 375 at 50%) | Prices hooks |
| NeuralReallocation | a placed neurotechnician resets skills or abilities for charons; the points are the game's own unspent points, placed on its level-up screen (tested: 120 refunded + 40 for level 2 = 160 on the screen) | Character skills/abilities/points, dynamic dialog, Inventory |
| MovableWindows | the game's titled windows dragged by their title bar, reopened where left (a setting), right-click a title to put it back; per HUD size | Windows.OpenWindows, Input mouse, Settings |
| Loadouts | three gear sets per game, saved with the save; Wear swaps from the bag, not in combat | Equipment, WorksMod.Data, Keybinds (unbound by default), UiPanel.Clear |
| Pacing | XP per level, XP earned, extra skill / feat / ability points per level | Progression (ModifyXpNeeded, ModifyExperience, ModifyLevelPoints) |
| WatchfulEyes | opening a container that isn't yours names who would see a theft; optional: no lock picking in view of a guard | ContainerOpened, Container.Owned / IsOwner / Witnesses, Locks.ModifyAttempt (Force) |
| ThePit | an arena: pay the pit master, waves of creatures that fight only the player, pay per wave, best run per save, a HUD part | World.Spawn (SpawnSide.PlayerOnly), FreeTilesNear, Despawn, CharacterDied, anchored Hud.AddPart, WorksMod.Data |
| MerchantLedger | every merchant's stock and prices recorded on trade (plain variables), L opens a search over all of them | TradeOpened, Stock/Markup, Ui.TextBox, Keybinds, Character.Key |
| StatusBoard | health, status effects in words with turns left, AP/MP and whose turn in combat; a HUD part | StatusEffects, Combat.ActionPoints/MovementPoints (any fighter: crp.ahu), Hud.AddPart |
| HudLayout | a Layout button on the HUD (a setting, off by default; movable itself) or a key (none by default): drag the HUD's parts, Reset all, Done (the game's, and mods' named parts: Minimap, Quest Tracker), right-click to put one back; saved per HUD size | Hud handles, Ui.Custom overlay (catches the clicks), Input.Mouse, Settings |
| TheStranger, HelloQuest (examples) | a character, a conversation, a quest | the content API |
Tested together in one save, then a quicksave with a contract active, both placed characters and every mod's
variables: the stock loader reads it (`check-saves.sh --only`), and after quickload the contract is back.
Ideas researched (tModLoader's most popular mods, other RPGs' mods, UnderRail players' complaints): the full list,
ranked, is in TODO.md.

## What the modder writes vs what Works does

Like tModLoader: Works carries the weight, mods declare what they want. Code (and Harmony) only for new behaviour.

| Mod adds | The modder writes | Works does |
|---|---|---|
| An item | A JSON recipe: id, base item, changes, icon PNG, where it appears | Builds it from the player's own files, serves it, text and icon, stores/loot, save placeholder |
| A new map | The map file (from the map editor) | Serves it through the overlay, registers its waypoints |
| An exit on a vanilla map | A short patch entry: zone, area, tiles, destination waypoint | Applies it on every zone load (data and saves), keeps saves vanilla-safe |
| New behaviour (effects, UI, quests) | C# code against Works' readable API and events; Harmony as the fallback | Loads it, isolates errors, names, events |
| Settings | An `.ini` with defaults | Shows them in the Mods tab |
| Save data | Key/value pairs through the API | Writes `works/<mod>.json`, keeps it when the mod is missing |

## Add, modify, replace

Mods never ship copies of the game's files. Three ways to touch content, all applied by Works at load:

| | How | Example |
|---|---|---|
| **Add** | New id under the mod's name (`works\<mod>\<id>`), built from a recipe or the mod's own file | A new knife; a new map |
| **Modify** (the normal way) | A patch: Works loads the vanilla object, then applies the listed changes to it | "crawlerpoisonthrowingknife: damage 12-18"; "zone X: add an exit at (x, y)" |
| **Replace** (last resort) | The mod's whole file served instead of the game's | Only for content the mod fully owns; discouraged for vanilla files |

- **Items:** the game caches each definition after loading it (`ez2.m_a`), and saves store items by path only, so a
  modified definition applies to every copy, including ones already in saves. Values that belong to one copy
  (stack size, quality, condition) stay with that copy.
- **Maps:** a patch applies to the zone object each time it loads, from the data files or from a save (visited zones
  live in the save).
- **Several mods, same thing:** patches apply in load order and can touch different fields of the same item or map.
  Two mods setting the same field: the later one wins, and the Mods tab shows the conflict (like a mod manager
  does for Skyrim).
- **Removing a mod:** nothing to undo. Its patches are simply not applied on the next load, because the game's own
  files were never changed.

## Adding content: items and maps

**A new item** (the overlay is proven, the rest is the plan):
1. **Defined as a recipe, not a game file**: a JSON entry with an id, a vanilla item to start from, and what to
   change (name, description, stats, icon). Works builds the item at load from the player's own vanilla files, the
   way the spike's `MakeItem` tool does, so a mod ships no game data at all.
2. **Served by the overlay**: the hook on `ez2.b(path)` returns it for `works\<mod>\<id>`, like any vanilla item.
3. **Text and icon**: name and description through the `qv.b` text hook; a new icon from a PNG in the mod (the
   texture loader hook is still to find).
4. **Getting it into the world**: added to stores, loot or containers by a patch ("add to store X"), or given by
   a mod's event code.
5. **Saves**: stored by path; "?" placeholder if the mod is missing (proven).

**How maps are put together** (from the decompiled code; not yet tested)
- The world: `Maps\World\SouthUnderrail.world` and `GlobalMap.uz`. Each zone is a `.uz` file with `.uzl` level
  files in `Maps\Locale\Static` (also `Maps\Events`, `Maps\Transports`), loaded by path (`c1a.a(path)`,
  `doq.a(path)`).
- A zone (`c1a`) holds areas (`aud`). An area holds triggers; an area transition (`b0j`) names a destination
  **waypoint** (`ATT:DW`), found by name in the zone (`c1a.g(name)` → `csd`: area, location, direction). How a
  transition reaches a waypoint in another zone is still to find (the world file is the likely index).
- **Visited zones are stored in the save**: the game keeps working copies of zones it has loaded (`cgtemp`) and
  copies them into the save. So a zone's file only matters until the player first enters it.

**A new map**
1. Made in the map editor (the later project) or built in code from the game's parts, and saved in the game's own
   format (`.uz`/`.uzl`), since it's large data. It's the author's own map, using the game's tiles and objects by
   reference.
2. Served by the same overlay, on the zone loader: `Mods\<mod>\data\Maps\...`.

**Changing a vanilla map (e.g. a new exit to the new map)**
1. **A patch, never a modified copy** of the vanilla file (that would ship game data, and two mods changing one map
   would overwrite each other). The patch is JSON or code: "in zone X, area Y, add a transition at tiles (x, y)
   to waypoint Z in my map", plus a door or sign object if wanted.
2. **Applied every time the zone loads**, from the data files *and* from a save, because visited zones come from
   the save. Each addition has an id, so it's only added once.
3. **Vanilla access**: the added trigger is a vanilla type, so the stock game should still load the save; stepping on
   a transition to a missing map is the unknown. Possibly Works removes its additions from zones before each
   save and puts them back after, so saves never hold them. To test.

**To prove next**: the zone loader overlay with one small new zone; adding a transition to an existing zone at load;
a cross-zone waypoint; what the stock game does with a save that has a transition to a missing zone.

## How the API grows

The game is opened up one **area** at a time (drawing, characters, items, maps, combat, dialog, UI), not all at once.
That stays coherent because every area follows the same framework rules, taken from tModLoader where they fit:

| Rule | tModLoader | UnderRailWorks |
|---|---|---|
| Mod entry | one `Mod` class: `Load`, `PostSetupContent`, `Unload`, `Logger` | one `WorksMod` class (`WorksMod.cs`): `Load`, `PostLoad`, `Log`, `Settings`, `Harmony`, `Info`. A bare `static OnLoad(Harmony)` still works for tiny mods |
| Hooks | named by kind: `Pre…` (false skips vanilla), `Post…`, `Modify…`, `Can…` | `Hook<T>` (every handler runs) and `ReplaceHook<T>` (the first handler that takes the step replaces the game's; never two drawings) (`Hooks.cs`). Modify/Can kinds added when an area needs them |
| Hook order | mod load order | mod load order (requirements first) |
| A hook throws | usually takes the game down | the handler is removed and logged with the mod's name; the game and other mods carry on |
| Versions | mods built for a tModLoader version | `"works": "1.0"` in mod.json against `WorksApi.Version`: same major, at least that minor. New areas start **experimental**; after that only additions |
| Dependencies | `build.txt`: modReferences, weakReferences, sortAfter/Before | `mod.json`: requires (`Id@1.2`), optional, sortAfter/Before |
| Reload | in game, without restarting | **not possible**: .NET Framework / Mono can't unload a DLL; switches apply at the next start |
| Settings | `ModConfig` classes: fields with attributes, UI generated, JSON | `ModPage` rows + `settings.ini` today; next: a declarative config class (fields + attributes → rows and ini), tModLoader's idea with our readable ini |
| Game names | the game's real source names | scrambled names, so Works keeps readable accessors (`GameNames.cs`) checked at startup: **every** member Works looks up must resolve, or the log says which (`GameNames.Check`; on its first run it found two 1.x lookups with wrong flags). Harmony on raw game types is the fallback |
| Mods and game names | mods use tModLoader's API (`Player`, `NPC`, `UIElement`), not Terraria internals | **mods compile against Works and FNA only, never the game exe**: a scrambled name in a mod is a compile error, not a judgement call. Works hands out its own types (`Character`, `Area`, `UiElement`...) with the game's objects inside. A mod that needs something the API lacks is the signal to add it to Works (with a name check and a doc line), never to reach past it. Every shipped mod is built this way (build.sh), UnderRailFixes included since the drawing API was wrapped (Nov 2026) |
| Shared services | per-mod config, localization, save data (TagCompound) | settings, options UI, logging; save data (sidecar JSON) and localization still to do |

**Each area is a slice**: its patch points, its events (the model above), readable names for its objects, one real
mod that uses them, and a short doc page. Slices are independent once the foundations hold.

**Done: drawing (experimental)**: `WorldDraw` events (LayerStarting, DrawLayer, DrawTileExtras, WorldDone,
ShaderPassStarting/Done, InterfaceOffset), `Outlines` (mods request outlines for every creature Tab would outline;
the extra ones are hidden from the game's pass for the mod to draw), names for layers, entities, the world view and
the draw context. The real mod: UnderRailFixes (Occluded characters), now with no reflection of its own. The name
check found a 1.x bug on its first run: `a7m.aq` (the clip state) is static, so 1.x's instance lookup was always
null and fills/outlines would fail whenever the world's clip doesn't start at 0,0.

**Done: the world, read-only (experimental)** (`World.cs`), found by building the Minimap: `World.Player` (as the
game finds it: `cnk.aqu().v().b()`), `World.Visible` (the characters the game drew last frame: in sight, not
stealthed; tracked only once a mod asks), `World.FogOfWar` (the world view's shroud, through `ps.akw()`, so it holds
before the first frame), `Character` (Position, Area, Attitude, OutlineColor as Tab, Name, IsPlayer), `Area` (Width,
Height, `KindAt(x, y)` = Nothing / Floor / Wall / Prop / Door with the shroud applied, `Exits` with `GameColour`).
UI handles: `UiElement`, `UiPanel`, `UiTickBox`, and `Ui.Custom(w, h, canvas => ...)` for elements a mod draws itself
(`UiCanvas`: Bounds, Device, DrawTexture, Fill, Frame), so mods never subclass the game's controls.

## Modern .NET (spike, Nov 2026)

**Result: UnderRail runs on .NET 10** (10.0.14, the latest LTS) under Proton, with no Wine Mono: main menu, your
quicksave, Occluded characters (keyhole pixel-identical to the Wine Mono build), both mods, Harmony patches. Saves
work both ways: an old save loads on .NET 10, and a save written on .NET 10 loads in the **stock, unmodded game**
(its assembly names are only mscorlib, System, underrail and XNA, like the stock game's). config.dat written on .NET 10
reads back with the game's own loader under .NET Framework rules.

How it runs (`src/Launcher`, `src/Runtime/CoreCompat.cs`):
- `underrail.exe` is our .NET 10 launcher (apphost with `AppHostDotNetSearch=AppRelative`: the runtime is in the
  game folder's `dotnet\`, so no install and no launch options). It loads the patched 64-bit game, renamed
  `UnderRail.Game.exe`, and runs its Main; the game's references resolve from the game folder.
- Runtime: Microsoft.NETCore.App + Microsoft.WindowsDesktop.App 10.0.14 win-x64 (WinForms for the game's window,
  clipboard, monitor and dev console code): 76 MB zipped, 173 MB unpacked, hash-checked against Microsoft's releases.
- BinaryFormatter: the `System.Runtime.Serialization.Formatters` compatibility package (unsupported by Microsoft but
  the full implementation) with `EnableUnsafeBinaryFormatterSerialization`.
- Harmony: 2.4.3's net10.0 build (2.3.6 refuses .NET 10's runtime).
- What modern .NET lacks, found by scanning every framework member the exe uses (about 5,200; ~70 missing):
  WinForms and System.Drawing (supplied by WindowsDesktop), AppDomains and NetDataContractSerializer (dead code in
  the game), and for data: `System.Random` isn't serializable any more (a surrogate reads and writes .NET Framework's
  inext/inextp/SeedArray; a seeded Random runs the same algorithm), and modern assembly names in written type names
  (`FrameworkNames`: TypeForwardedFrom, else mscorlib, inside generics too). The game's event records (DEI) store
  delegates as method name + target + type string, not .NET delegate serialization, so they work.

Gains: RyuJIT/modern GC instead of Mono; mod reload becomes possible (collectible AssemblyLoadContext); current C#
for mods; one runtime on Windows and Proton.

**Save check** (`tools/savecheck/check-saves.sh [--play]`): (1) every save and config.dat loaded and written again on
Windows .NET 10 under Proton by the game's own code (RoundTrip); (2) with --play, each save loaded in the .NET 10 game,
played and saved from the menu, which covers objects .NET 10 creates itself (the StringEqualityComparer bug only showed
there); (3) every result loaded by the stock game's loader on Proton's Wine Mono (StockCheck) and scanned for modern
names. First full run (Nov 2026, the player's 8 saves): baseline 25/25, round trip 25/25, played 24/24 load in the stock
loader, no modern names. System Mono is not a stand-in for the stock runtime (it rejects System.Random); Wine Mono is.

Still to do before it ships: build.sh (dotnet publish, runtime fetch + hash), the patcher's install/uninstall of the
new layout (game -> UnderRail.Game.exe, launcher as underrail.exe, dotnet\), the save check in release.sh, Steam/achievements through the helper, the dev console and clipboard, performance numbers,
real Windows, GOG, long sessions, and the stock game loading a .NET 10 config.dat in game.

## Status

**Proof of concept done** (branch `works-poc`, Nov 2026):
- `ModLoader.cs` loads `Mods/<name>/*.dll`.
- The `HelloWorks` example mod patches `qv.b` (the main-menu version text) and `tb.Draw` (a frame counter).
- Proven under Proton in 64-bit and 32-bit mode; a throwing mod is contained.
- Running in the user's Steam copy.
- Item overlay and save spike done: see "How the game is opened up".
- **Occluded characters as a mod** (`mods/UnderRailFixes/`): runs on Works' world-drawing events (`WorldDraw.cs`:
  the existing patch points call it; with no handler each does exactly what the game does; a throwing handler is
  removed). Its settings are in `Mods/UnderRailFixes/settings.ini` (taken from `UnderrailFix.ini` on first start).
- **Mods tab** (replaces the Fixes tab: the game's tab row has no room for an eighth tab), after tModLoader's Mods
  menu: a card per mod (switch, name, version, author, Loaded / Off / Not loaded, why, description, Requires /
  Works with, a settings button). Each mod's settings and Works' engine fixes are views of their own in the same tab
  with a Back button (`TabScroll` shows one view at a time). Mods describe settings with `ModPage` (`ModOptions.cs`:
  Toggle, Choice, Slider, Button, Subsection, Set, Custom for their own controls). Switches go to `Mods/mods.ini`
  (next start).
- **Manifests and dependencies** (`mod.json`, `ModLoader.cs`), as tModLoader's `build.txt`: `requires`
  (`modReferences`: `"Id@1.2"` = at least 1.2; must be installed, on, new enough and loadable, and loads first),
  `optional` (`weakReferences`: loads first when present), `sortAfter` / `sortBefore`. Unmet requirements skip the
  mod with the reason in the log and on its card; a requirement that fails while loading skips its dependents; a
  load-order loop is reported. Switching a mod on in the tab switches on what it requires. Tested: missing mod,
  too-old version, failing mod, switched-off requirement, auto-enable.

Not tested: Windows (the 64-bit mode on Microsoft's .NET Framework has never run on real Windows; this is the
biggest risk of going 64-bit only), GOG.

## Part 1: the organiser

- [ ] A mod manifest (`mod.json` or `.ini`): id, name, version, author, game version, dependencies, load order hints.
- [ ] Enable/disable without deleting, kept in our own settings file.
- [ ] Load order from dependencies, and a clear error for missing or incompatible ones.
- [ ] An in-game **Mods** tab: list, enable/disable, version, errors, per-mod settings. Built from the Fixes tab code
      (`src/Runtime/Options.cs`).
- [ ] Per-mod settings stored in the mod's own file, shown in the Mods tab.
- [ ] Installing mods: drop a folder into `Mods`. Later: install from a zip, check for updates (GitHub or Nexus; there's
      no Steam Workshop for UnderRail).
- [ ] Optional outside launcher app, if it earns its place. The game must still start correctly from Steam.

## Part 2: the modding layer

- [ ] **Naming map**: readable names for the scrambled classes and members (e.g. `a7m` is the world view, `t1` the
      options screen), kept per game version, with a check that fails loudly when a game update moves something.
- [ ] **Events** mods can subscribe to without Harmony: game started, map loaded, frame/update, item created, damage
      dealt, dialog opened, options applied.
- [ ] **Test the 64-bit mode on real Windows** first: it is now the only mode.
- [ ] **Build directly against FNA**: compile Works against our `FNA.dll` and drop the 32-bit build, `Retarget.cs`, the
      XNA Reach code (keyhole tiles, `ShowCovered.Fits`, the 2048 limits) and the `--32bit` option.
- [ ] **Data overlay**: mods add or replace data files from their own `data` folder. Items done in the spike
      (`ez2.b`); next characters, stores, dialogs, maps.
- [ ] **Data tools**: make and edit the game's data files with its own classes (the spike's item tool), the basis for
      mod authoring and the map editor.
- [ ] **Port the engine fixes**: rebuild today's ~50 patcher hooks as Harmony patches (call-site edits become
      transpilers). Keep in the patcher only what Harmony can't do: the loader hook, pointing the exe at FNA, the
      64-bit flags, and the SFML/Steam/Galaxy replacements that change field types.
- [ ] **UnderRailFixes as a mod**: Occluded characters on the Works API.

## Open questions

- Repo layout: a new `UnderRailWorks` repo, or this one renamed? Where does the UnderRailFixes mod live?
- (Settled) Names: `UnderRailWorks.dll`, `UnderRailWorks-Patcher`; Works starts at 1.0.0, the UnderRailFixes mod at 2.0.0; a 1.x install is detected and replaced.
- The manifest format, and how per-mod settings look in the Mods tab.
- How Harmony patches behave across game updates, and how mods state which game versions they support.
