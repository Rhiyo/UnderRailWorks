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

## Stacked HUD parts; guarded locks (Nov 2026)

- Anchored mod parts sharing an anchor stack instead of covering each other: in first-added order (kept across HUD
  rebuilds), each visible part goes where it asked or just past the earlier ones it would overlap (down for top and
  middle anchors, up for bottom ones); re-stacked when one is shown, hidden or resized; a part the player moved with
  HUD Layout stays put (and is in the way where it is). Tested: Minimap and Quest Tracker both top left, stacked.
- `Lock.Guarded` / `Lock.Witnesses(thief)` (the lock's protection mode `d2b.o()` and its entity's owners), shared
  with containers (`Ownership`). Watchful Eyes' "no picking in view of a guard" now covers doors too. Tested: the
  guarded gate locker refused, no fight.

## Mod parts clear of the game's HUD at any size (Nov 2026)

Fixed offsets chosen at one resolution covered the game's parts at another (the game lays its HUD out per size
tier: at 3584x1440 the HUD is 2509 wide and every part is bigger). Now the game's visible parts are obstacles for
anchored mod parts: each overlap pushes the part past that obstacle along its anchored axis or sideways, whichever
moves it least and stays on screen; re-flowed when a game part is shown, hidden or moved. Obstacles are what a part
draws, not its control: the union of visible child controls (the portrait's control is 737x474, the target
portrait's covers the top centre while hidden), or the control itself when its children are hidden (zone security
icon; the combat panel `djx`, an 82-wide strip down the right edge: its round button, and in combat the AP bar).
Measured at 3584x1440 (logged as "hud: drawn parts"): the Layout button goes under the system bar beside the zone
icon, the Minimap beside the round button, the Status Board stays at the top.
HUD Layout's bar: snap on/off and grid size on the bar, dragged by its text (place remembered), hidden while a part
is being dragged, sized to the game's buttons at every tier. Tested at 3584x1440.

## Hovered character, resistances (Nov 2026)

`World.Hovered` (the world view's `dnn.an2()`, set each frame in `a7m.ag5` from its layer hit test, the thing the
game outlines white), `Character.Resistances` (`efk.Defense.af()[DamageType]`: `du2.a()` threshold, `du2.c()`
resistance), `Character.Detection`. Tested at 3584x1440 and 2560x720 with insets: the Scanner shows an SGS Sentry's
level, health, attitude, detection and resistances.
**Retracted (Nov 2026):** a "fix" here (`WorldHitTest`, shifting the mouse by the world view's inset offset in
`a7m.a(x, y, out bool)`) broke hovering and talking to NPCs with insets: the game's hit test was right all along.
Measured afterwards with a sweep over a stationary sentry and turret: with the shift 0 of 12 points hit, without it
11 of 12. The "proof" had been clicks aimed at the wrong pixels that the shift moved onto a character 384 pixels
away. Rule: verify a hit-test change with a sweep over a target that doesn't move, with and without the change.

## API packaging; what Works writes into saves (Nov 2026)

- Namespaces: the API is `UnderRailWorks`; plumbing the patched exe calls (UiScaleFix, FnaMode, SteamBridge, the
  display modes, the options tab, WorldDrawHooks...) is `UnderRailWorks.Internal` (the patcher finds those types in
  either place). `FixStartup` and `FixVersion` stay in the global namespace for good: exes patched by an older
  version call `FixStartup.Run` by that name and get its "patch again" message instead of a crash.
- Public data on API types are properties, not fields (a field can't become a property later without breaking
  every mod built against it). Mods' own `ModConfig` settings stay fields (they are the mod's, read by attribute).
- `Interface.Scale` / `ToPixels` / `CapturingSaveScreenshot` and `WorksSettings.Get` replace the internals the
  UnderRailFixes mod used.
- `WorksApi.Version` is 0.9: while 0.x a mod must declare the same minor ("works": "0.9").

**What Works and its mods write into saves (a promise: these names and formats stay, so saves keep their mod data):**
all are the game's own global variables with plain values, which the stock game keeps and ignores.

| Variable | Written by | Value |
|---|---|---|
| `md_<modId>_<key>` | `WorksMod.Data` | string / int / bool |
| `works_quest_<questId>` | `Quests` (a mod quest's state; the quest itself is taken out of the save) | string "active\|completed\|failed,read,index" |
| `cp_<modId>_<triggerId>` | content packs' `"once"` triggers | bool |
| any name a mod passes to `Variables.Global` / `Zone` / a character | mods | string / int / bool / float |

Placed and spawned characters are never in a save (taken out around saving); mod dialogs, dialog patches, keybinds,
HUD parts and windows are never saved; mod settings live in `Mods/<mod>/settings.ini`, mods on/off in `mods.ini`.

## Assets: pictures, sounds, texture packs, icons, menu art (Nov 2026)

The game loads every texture by name through one resource manager (`a4q`, the `cne` service: `GetResource<T>(name)`
-> a cache of `b5s` entries, `c == -1` never expires). Works makes that cache case-insensitive and puts a mod's
texture into it under the game's name, so the game draws it everywhere it would draw its own; a prefix on a4q's
private static `a(b5s)` keeps those entries from being disposed. Replacements are applied each update until all are
in, and before a menu background is built (`czo.a`). Hit-test masks stay the game's (keyed by name), so a sprite
sheet must keep its layout.
- `Assets.Texture(mod, file)` (PNG/JPEG, premultiplied on load like the game's content), `Assets.Sound(mod, file)`
  (WAV), `Assets.Replace(mod, gameAsset, file | texture)`, `Assets.Export(gameAsset, png)` (un-premultiplied, to
  edit), `Assets.Loaded` (what the game has loaded now: find the texture behind something on screen),
  `Assets.GameTextures`.
- **Texture packs without code**: `Mods/<mod>/textures/<game asset path>.png`, e.g.
  `textures/Portraits/Male/ag1_l.png`, `textures/Locale/Creatures/pl_ds/id.png`.
- Icons: names like `armors.12` are cells of atlases (`Icons\armors`, 33x33, ten a row) resolved by `ejh.b(name)`;
  most of the game asks it with the item definition's icon name `bk3.a3()`. `Icons.Register(mod, id, file)` gives a
  `works:<mod>:<id>` name `ejh.b` answers; `Icons.SetItemIcon(mod, itemCode, file)` makes `a3()` return it for that
  code. The quick-slot proxy saves the name it copies (`av.i`, `CP:I`, in `ejh.a(av, ...)`): a postfix puts the
  game's own name back, so saves never hold a mod's icon name (**not yet tested with an item in a quick slot**).
- `MainMenu.SetBackground(mod, file)`: replaces every main-menu concept art variant (`concept\*mm\<aspect>`); the
  logo is part of the art. BackgroundFit decides crop or letterbox.
- Tested in game: the menu background; the bandage's icon in the inventory; a texture pack recolouring the player's
  HUD portrait and the player's sprite sheet in the world; exports of icon atlases, a portrait, a sprite sheet.
  Never commit exported game textures: they are the game's files.

## Clock, item use, crafting, stealing (Nov 2026)

From `~/UnderRailWorks-research/specs/clock-items-crafting.md` (not committed):
- `GameClock.WorldTime` (the save screen's play time; restocks and quest timers; runs in real time in combat),
  `TurnTime` / `Turns` (frozen in turn-based combat but for 5 s per player turn), `Paused` (`ps.ak4()`: dialogs, the
  menu, the character sheet; not inventory, loot, barter or crafting), `InTurnBasedCombat`;
  `GameEvents.GameTimeAdvanced(delta)` (`d6f.a(object, c4i)`, once per un-paused frame). The game has no day/night,
  rest, hunger or fatigue.
- `Items.Use(user, item, target)`: the item's first capability (`Definition.a2()[0].Value`) through a proxy
  (`ecf.a`) queued with `cp.b`, as the game's AI uses bandages; AP, cooldowns and the game's checks apply.
  `GameEvents.ItemUsing` (cancellable; prefix on `cp.a(eu7, c5x, object)`, every user, NPCs too) and `ItemUsed`
  (postfix on `dea.dt()` of a consumable's action `a62`, the stack already taken).
- `GameEvents.ItemCrafted` (postfix `ahu.i()`: the blueprint's name, the items).
- `GameEvents.ItemStolen` / `CaughtStealing` (`rc.a(coh)` / `rc.a()`), `Stealing.ModifySuspicion` (`b87.a(efk,
  efk, coh, double)`: per item; also the window's preview every frame; caught at 1.5 in all, no dice).
- `Crafting.All` (119 blueprints: id, name, category), `Crafting.Known`, `Crafting.Learn(id or name)` (`e3q.b(Type)`,
  stock blueprints only). `Stealing.Pickpocket(thief, victim)` (the Pickpocket ability `eag` through a proxy, as
  `Items.Use`). `Equipment.ToQuickSlot(item, slot)` (`bn.SwapIn`; `bn.a` skips medicine: a bandage's quick-slot flag
  `I:QQ` is unset in the data, yet players can slot it).
- Tested: the clocks (world time and the summed ticks agree to the millisecond over 5 s), `Items.Use` on a bandage
  (90 -> 180 health), the veto, both item events; crafting (Throwing Net learned, crafted in the window, ItemCrafted
  fired); pickpocketing (four items stolen from a Sentry at 0.33 suspicion each, then CaughtStealing);
  a bandage with a mod icon in a quick slot (icon shown) then a quicksave: no `works:` name anywhere in the save, the
  slot's icon field `CP:I` empty. **Not verified:** using an item from a quick slot in the headless runner (neither
  the key nor a click used it).

## Save data files, translations, mod APIs, hook order, error reports (Nov 2026)

- `SaveData.Of(mod)` / `WorksMod.SaveData`: structured per-game data (strings, bools, numbers, lists, dictionaries)
  in `Saves/<save>/UnderRailWorks.json` (folder: `d5x.d(name)`), written after each save, read when one loads,
  cleared on a new game; sections of mods not installed are kept. The stock game never reads it. `WorksMod.Data`
  (plain variables inside the save) stays for small values. Tested: written, restored by a quickload.
- `WorksMod.T(key, args)` / `Translations`: `Mods/<mod>/i18n/<language>.json`; the language is the game's
  (`aji.a.a`, the Localization folder name; null: "english"), falling back to english.json, then the key.
- `Mods.Provide(mod, api)`, `Mods.Api<T>(id)`, `Mods.Call(id, method, args)`, `Mods.Event(name)` / `Raise`.
- `HookOrder` (First, Early, Normal, Late, Last) on every hook's `Add`.
- A failing handler is always logged (whatever Log= says), counted on the mod's card in the Mods tab, and the player
  is told once per mod per session in the game's log.
- mod.json: `incompatible` (a mod that loads with an incompatible one is skipped with the reason), `homepage`,
  `issues`, `license`, `game`; `icon.png` (ModInfo.Icon).

## Item definitions, new items, tooltips (Nov 2026)

From `~/UnderRailWorks-research/specs/items-stores.md`: every stat is on the definition (`bk3`); data-file items
share a cached definition per path and save only the path; crafted items (and `coh.c()`) carry their own copy.
- `Items.Definition(path)` -> `ItemDefinition` (Code, Name, Description, Icon, Value, Weight, MaxStack; weapons:
  Damage ranges, ActionPoints, CritChance, CritDamage, MinStrength; armour: Resistances / SetResistance,
  ArmorPenalty). `Items.EditDefinition(mod, path, edit)`: applied to the cached definition and whenever `ez2.b(path,
  bool)` loads it; never saved.
- `Items.Define(mod, id, basePath, edit)` -> `works\<mod>\<id>`, code `works.<mod>.<id>`: a prefix on `ez2.b` answers
  that path with the base definition's clone (`tw()`), edited. Saves hold the path; the stock game loads such a save
  (checked with tools/savecheck --only: global.dat, chunk0.dat, info.dat OK) and shows the item as "Unknown".
- `Item.Definition`, `Item.MakeUnique()` (`coh.c()`: its own definition, saved whole as stock types).
- `Items.ModifyTooltip(mod, (item, tooltip) => ...)`: postfix on `cq4.a(coh, efk, object, bool, bool, eg, c3n,
  double?, bool)`; `Tooltip.AddLine / AddValue / InsertLine / Find / RemoveLine / AddSeparator / InTrade`.
- Tested: a defined "Works Stim" (name, description, value 1234, weight) given and shown; the stock bandage renamed
  and revalued; a tooltip line on both.

## Music and sounds (Nov 2026)

- `Music.Replace(mod, id, ogg)`, `Music.Add(mod, id, ogg)` (the song table built in `b6d.c()`: `za.SourcePath` may
  be absolute), `Music.Override(mod, () => id or null)` (prefix on `c4m.ai6()`, the per-frame choice; ours is marked
  `works:<id>` in its current choice; the game takes over again when no mod answers), `Music.Songs`, `Music.Current`.
- `Sound.Replace(mod, id, wav)` / `Add` (the sound table: `d6c`'s SoundEffect set to the WAV's), `Sound.Play(id)`,
  `Sound.Remap(old, new)` (prefix on `b6d.aeb`), `Sound.Ids` (638).
- Trap hit while building it: `"x".StartsWith("\u0001")` is true under ICU culture comparison (control characters
  are ignored): the marker cleared the game's choice every frame and the menu music restarted ~50 times a second.
  Always compare game strings Ordinal.
- Tested: the mod's OGG played by the override, the area music back when it stopped.

## Console commands (Nov 2026)

The game has a developer console in the release build (the ` key in game: `ci3.k()` opens `j9`; Enter runs
`bdg.c(line)`; output `bdg.a(format)` with [cmd] [val] [param] [war] [error] markup). `Commands.Add(mod, name, help,
args => reply)` is answered in a prefix on `bdg.c(string)` before the game's table; `Commands.Print`,
`Commands.OpenConsole()`. Built in: `works`, `works.give <path> [n]`, `works.spawn <blueprint> [hostile]`,
`works.var <name> [value]`, `works.mods`, `works.content`. Tested: works.var and works.mods typed in the console.

## Stores and loot rules (Nov 2026)

- `Stores.AddRestock(mod, store, path, count)` / `AddOnce`: shipments (`d6w` with a `dcw` of the item; unique for
  once) appended to the store definition the merchant reads afresh on every (re)stock (postfix `c8j.a(string)`). Saved:
  the delivered items and the shipment name (a string the stock game drops at its next restock).
- `Loot.Add(mod, (character, container) => bool, path, chance, count)`: appended to the `c9y.c()` list while
  `ery.mx()` rolls an entity's loot (so the game applies durability and NPC auto-equip); Works' own RNG.
- Tested: the test merchant (store bigbret) sold the mod's Works Stim x3 and a JKK jacket; a spawned rathound's loot
  had the rule's Fabric Scraps x7.

## Mod status effects (Nov 2026)

`ModStatuses.Define(mod, id, name, description, icon)` -> `ModStatus` (`Apply(who, turns)`, `Has`, `Remove`,
`OnTick(who, stacks)`; code `mod:<mod>:<id>`). A food buff (`c9i`, whose name, icon and description are data) with its
code field rewritten. Ticks: postfix `els.o()` (an owner's combat turn) and `els.e(double)` (true = a tick, every 5 s
outside combat). **Never in a save file:** Works takes every `mod:` effect out of the player's and the loaded
characters' effect lists (found by code: the game applies a copy of the effect it is given) before `d6f.b` (global)
and `bl1.yp` (zone) write, puts them back after, records owner / time left / stacks in UnderRailWorks.json at
`d6f.arg`, and re-applies them after a load. Icons: stock names such as `Status.Poison`, `Status.PsiBoost`,
`Status.5`. Tested: shown by the portrait, ticking, absent from global.dat and chunk0.dat, recorded in the sidecar,
restored after loading the quicksave; the save passed the stock loader. Field-name trap again: `br5.m_c` is `c` in
the binary (the decompiler adds `m_` on clashes); every reflected field needs the fallback.

## Area triggers, usable objects (Nov 2026)

- `Triggers.Add(mod, zone, area, tiles, entered, left)` -> `AreaTrigger` (`PlayerOnly`, `Remove`): Works checks
  where characters stand 5 times a second (the game's own triggers are saved objects of the area: not used).
- `Placement.OnUse`: an object placed by a mod runs the handler when used (prefix on `bim.a(c5x, string, bool,
  bool)`); an object without a use of its own gets a plain `bim` (placed entities are never saved). `World.Use(user,
  placementId)`. Stock "device" blueprints (levers, consoles) have no use aspect: their uses come from the maps.
- Tested: the trigger's enter, a placed lever used through `World.Use` and with a real mouse click (the handler ran
  both times).

## Mod feats (Nov 2026)

`Feats.Add(mod, id, name, description, FeatRequirements { Abilities, Skills, Level }, icon)` -> `ModFeat` (`Key`
"mod_<mod>_<id>", `OnGain`, `OnLose`), `Feats.Give(character, key)`, `Character.HasFeat(name or key)`. A feat is a
class deriving `ai8` in the registry `ck.m_a` (key = type name lower case without a `d29` attribute); Works has one
`WorksFeat : ai8` (requirements as the game's `awr`/`bap`, gain/lose callbacks) and emits a subclass per mod feat
(System.Reflection.Emit, works on .NET 10) named by the key; texts via `FeatNames/<name>` and `Feats/<wn()>`; icons
answered for "Feat.<key>". A save keeps the key string (`ctu FR:FTN`); an unknown key is skipped by the game.
Tested: the feat given, listed on the character sheet with its description, its key in global.dat, the save
passes the stock loader, and loaded with the mod switched off it plays with the feat simply absent.
Not done: real stat modifiers (`cy()` static effects): a mod feat's effect is the mod's code (events, hooks,
HasFeat checks).

## NPC AI hooks (Nov 2026)

- `Ai.ChooseTarget` (postfix on the target node `bq1.f7()`: a handler may return another character; Works writes it
  as the AI's "PrimaryTarget" with the game's `bzv.a(amc)` and takes the "found" branch; never a Works-placed
  character, which leaves the map around saves), `Ai.Acting` (prefix on `cp.b(proxy, user, target)`, NPCs only:
  `Cancel` makes the node fail and the tree try something else), `Ai.TakingTurn` (prefix on `e1k.avi()`: `EndTurn`).
- `Commands.Execute(line)` runs a console line from code (mods' and Works' commands, else the game's own).
- Tested: with every NPC but a rathound ending its turn, the rathound targeted and fought the player alone
  (180 -> 69 health) while the station guards stood by. Not covered: weapon choice.
- `Ai.ShouldFlee` (true / false / null): postfix on `fh.f7()` when it is a tactic's root selector `or.j`; true picks the
  flee branch `or.k`, false the selector's next branch whose condition holds. Tested: the same rathound told to flee
  ran off and never attacked (health stayed 180).

## Cancellable events (Nov 2026)

`GameEvents.TradeOpening` (prefix `dka.amw`), `ContainerOpening` (`c7j.mz`), `ItemTaking` (`c7j.kj`, the window's
"can remove"), `DialogStarting` (in the `dka.amu` prefix; `Name` can be changed to open another dialog; not barks),
`Crafting` (`ahu.i`): a handler sets `Cancel`. With the earlier ones (StatusApplying, ItemUsing, Locks.ModifyAttempt,
Ai.Acting) most player actions can be refused. Tested: trade, container and dialog refused.

- `GameEvents.Equipping` (the player putting an item into a gear or quick slot): a prefix on the screen's move
  `c69.a(source, target, item)` when the target is a `PlayerGearItemSlotControl<T>`; refused, the move doesn't start.
  **Trap, twice:** `c69.a` takes the item out of the source (`A_0.yd()`, inside its condition) *before* it asks the
  target (`A_1.ye`), so refusing anywhere inside the move loses the item: first in the data slot's check
  (`ItemSlot<T>.c`), later in the target's `ye` once that was limited to "inside the move" (a screenshot looked right;
  logging the bag and gear showed the armour gone from both). Verify item moves by listing bag and gear, not by eye.
  Tested: refused -> armour in the bag, shirt worn; not refused -> armour worn, shirt in the bag.
- `GameEvents.ZoneLeaving` (From, To, Entry; prefix on `a0l.a(c5x, c2w, int, Point?)`, which every exit and script uses
  to queue the ChangeZone workflow) and `World.Travel(zone, entry)` (the same call). Tested: travel to lu-c1b refused,
  and done without the refusal.

## UI widgets (Nov 2026)

`Ui.Image(texture, w, h)`, `Ui.Tooltip(element, text)` (after 0.5 s, wrapped, kept on screen), `Ui.List(w, h, row)`
-> `UiList` (Items, Selection, Selected, wheel scroll, scroll bar), `Ui.Slider(w, min, max, value)` -> `UiSlider`
(Step, Changed, drag or click), `Ui.Choice(w, options, index)` -> `UiChoice` (click / right-click, Changed). Drawn
with `Ui.Custom` in the game's colours; input in Works' update (weak references, so a removed widget costs nothing).
Tested in a mod window: all five.

## Item kinds; decimal settings; content packs for the new APIs (Nov 2026)

- `Item.Kind` (Weapon, Armor, Shield, Component, Ammo, Currency, Throwable, Consumable, Other: from the definition's
  class), `Item.Tier`, `Item.Condition`, `Item.GetValue / SetValue(key)` (the item's own CustomProperties: saved with
  it, primitives only, ignored by the stock game), `Items.All(kind)`.
- ModConfig: float/double fields get sliders (Range, Increment as the step); string fields show their value with a
  pointer to settings.ini.
- Content packs: `items`, `itemEdits`, `stores`, `loot`, `music`, `sounds` (packs without code get a stand-in mod for
  the APIs). TheProspector defines "Prospector's Lucky Pick" (a boning knife renamed, 15 % crit), sells it once at
  bigbret, edits the bandage's description and adds a loot rule. Tested: the knife in the merchant's stock with the
  pack's name, description and crit chance.

## Hot reload (Nov 2026)

`works.reload` (console) re-reads mods' pictures and texture packs, translations and every ModConfig from disk. Code
needs a restart (.NET can't unload a mod's DLL that Harmony has patched into; content packs' dialogs and quests are
registered once). Tested: a texture pack portrait changed on disk from red to green, shown after works.reload.

## Stat bonuses for mod feats and statuses (Nov 2026)

`StatBonus.Ability / Skill / Evasion / Dodge / Fortitude / Resolve / Initiative / Detection / Health / CarryCapacity /
Psi / PsiRegeneration`: the game's own flat modifier objects (`ax6`, `dpw`, `a6z`, `bel`, `ddl`, `ciw`, `b5x`, `ds1`,
`dxd`, `bgl`, `d72`, `emn`; matched by their StaticEffects description keys). `Feats.Add(..., bonuses:)` returns them
from the feat's `cy(efk)` (collected by `efk.bl()` while held, never saved); `ModStatuses.Define(..., bonuses)` puts
them into the status carrier (stripped from saves with the status). Tested: Strength +2 from a feat and Agility +1
from a status shown green on the character sheet; a save with both passes the stock loader.

## Mod abilities (Nov 2026)

The game's own abilities (`c3n` and co.) are saved in the character and would crash the stock game, so a mod ability
is Works' own: `Abilities.Add(mod, id, name, description, icon, apCost, cooldown, target, key)` -> `ModAbility`
(`Available`, `Range`, `Used(user, target)`, `CooldownLeft`, `Use(target)`). Each mod with abilities gets an ability
bar (a HUD part "Abilities", anchored bottom, so HUD Layout moves it): icons with AP cost, cooldown count, greyed when
short of AP, tooltips; a click or the key uses it. Character targets: point at the character and press the key
(Works never has to swallow a world click). AP is spent in turn-based combat on the player's turn; cooldowns in
turns (`GameClock.Turns`) are stored in the mod's per-save data (`works_ability_<id>`). Icons load when first drawn
(no graphics device during Load). Tested: a click healed 90 -> 110 and the second click was refused by the cooldown;
F7 on a sentry did 10 damage once; in combat 15 AP spent (50 -> 35) and a second use refused.

## Combat.Damage (Nov 2026)

`Combat.Damage(target, amount, type, source, resistances)`: `Defense.a(new cwt { b = true (hit), q = resistances,
f = [csk(type, amount)], i = new ap9(source) })`, as the game's scripted damage does (bv7, a5e bleeding). Goes
through evasion/shields/resistances, the combat log, death and Works' damage events; returns the health taken.
Tested: 500 to a rathound -> 499 ("evades 1"), died, CharacterDied fired; 10 electricity shown in the combat log.

## Bounty Hunter, and what it (and a review) found (Nov 2026)

Bounty Hunter (mods/BountyHunter) uses an ability with a character target, a mod status with stat penalties on an
NPC, a feat with requirements and a bonus, death events, money, a window with a list, translations and a decimal
setting. Tested: mark a rathound (B), kill it, 12 charons paid, ledger (Y) with the translated texts. Found and fixed:
- **Statuses are gone at CharacterDied** (measured: none left; the game clears them while the character dies). New
  `GameEvents.CharacterDying(victim, killer)`: a prefix on the same death method (`amc.a(cwt, bool)`; Dead is already
  set by the blow there); the killer is found by the blow's recorded Uid (`cwt.i.d`).
- **HUD parts were keyed by name only**: two mods' "Abilities" bars knocked each other out of the stacking.
  `HudModPart.Key` = mod/name; HUD Layout uses it and takes over entries saved under the old "mod:<name>" keys.
- **Two mods' default keys**: both actions fired. Now the second mod's action starts unbound (logged); a key the player
  chose is kept. WorksTest's F6/F7 had clashed with Vendor Directory and Minimap all along.
- **build.sh's mod() copied only the DLL and mod.json**: now every file but the source (i18n/, art, sounds).
- i18n: `en.json` accepted for English; a missing text is logged once per mod.
- `ModConfig.Changed` event; `ModAbility.ApCost` / `Cooldown` settable.

An independent review of the day's code found, all fixed:
- Widgets and ability icons in closed or hidden windows still took clicks and tooltips; tooltips kept removed
  elements alive. `Ui.IsShown` (every parent visible up to the live HUD / Options) and `Ui.IsHot` (Works' controls
  track the game's own MouseEnter/Leave, so a game window above them blocks them too).
- Ability clicks and keys while the HUD is hidden or the game paused (checked: not paused in turn-based combat).
- `works.reload` didn't re-read settings.ini (ModSettings.ReadAgain), and scanned switched-off mods' texture packs.
- `Equipping` was asked every frame while dragging (the cursor's drop check): now once, before the move (`c69.a`).
- `Item.Condition` was 0 for items without durability; `Item.SetValue` refuses non-plain values (they're saved in the
  item, II:CP, and would break the save for the stock game); sliders format with the invariant culture (U+2212 minus
  under ICU); a widget handler that throws is reported for its mod and doesn't stop the others.

## Allies, followers, game key clashes; Beastmaster (Nov 2026)

- `SpawnSide.Ally`: the spawn joins the game's "playerNativeAlly" faction (the one it gives the player's force fields),
  so the faction table makes it the player's friend and the player's enemies' enemy, both ways. Tested: an allied
  rathound targeted and killed a hostile one ("killed by Rathound") and survived.
- `Character.Follow(leader, distance)` / `StopFollowing` / `IsFollowing`: out of combat, every 0.5 s a follower more than
  distance tiles behind is ordered (the movement aspect `ale.a(tile)`, as the game's AI moves) to a free tile near its
  leader; in combat its own AI fights. (The game's own follow goal `aqs`, AI properties FollowTarget/FollowDistance,
  exists only in the "soldier" AI.) Tested: followed the player across the station.
- **Mod keys against the game's**: once the game's key table exists (`cnk.aq1().e().c()`), a mod default on a game key
  starts unbound (logged; a key the player set is kept and reported) and the game's whole table is logged once. It
  caught G (Crafting), B (Recharge Shield), Y (quick slot 6), F9 (Quick Load) in my own mods. Mods should use
  `Keybinds.Register`; keys read with `Input.Pressed` can't be checked.
- Beastmaster (mods/Beastmaster): Call Companion (J) summons an allied rathound for N turns, follows, HUD part with its
  health and time left; one at a time; the cooldown is in the save data. Tested: called, HUD part, quicksave with it
  present -> stock loader OK, quickload: companion not in the save, cooldown kept.
- WorksTest's autorun.txt takes `wait <seconds>` lines (whole test sequences without typing in the console).

## Mod recipes (Nov 2026)

`Crafting.AddRecipe(mod, id, name, category, resultPath, RecipeSlot.Item(label, code, skill, count) / AnyOf(...))` ->
`ModRecipe` (Count, KnownByDefault, Known / Learn / Forget per save in the mod's save data), and a `recipes` section in
content packs. A game recipe is a blueprint class (`cwp`) known by type in the save (PC1:B), so a mod one learned the
game's way would break the save for the stock game. Works' blueprint object (one emitted subclass: `cwp`'s abstract
Name / TechnicalName / Quality / Category getters have obfuscated names, overridden with DefineMethodOverride) is
added to the crafting window's lists in a postfix on `eed.c()` (its private `i` all, `j` craftable, `k` rows, through
its own `a(cwp)` / `c(cwp)`); its slots are the game's `cgo(eh0(codes, skill), label, optional, count)` and its result
`ez2.a(path)`. Crafting is the game's own (component slots, skill checks, components used up, ItemCrafted). Tested: 2
bandages + 1 flare -> a health hypo (5 -> 3, 3 -> 2, ItemCrafted fired), a JSON recipe for a mod item, a save after
crafting passes the stock loader.

## Hunger; Item.Uses (Nov 2026)

- `Item.Uses`: the names of the game's capabilities on the item's definition (`Definition.a2()`: "Food" for food,
  the capability `b0p`...), so mods can tell food from medicine.
- Hunger (mods/Hunger): satiety drops with GameTimeAdvanced, food (ItemUsed + Uses "Food") refills it, Hungry /
  Starving mod statuses with StatBonus penalties, a HUD meter, per-save state written in `Saving`, `hunger.set`.
  Tested: 25 % hungry, 5 % starving (icon, red meter), eating canned fish -> 45 %, status gone; quicksave at 20 %,
  changed to 90 %, quickload -> 20 % and one Hungry status; the save passes the stock loader, the state is only in
  UnderRailWorks.json.
- WorksTest's autorun.txt is also picked up mid-game (checked every second): test steps after the load without the
  console.

## mod.json "startsOff" (Nov 2026)

`"startsOff": true`: a mod mods.ini doesn't name yet starts switched off (written as `<id>=0`, logged); the player
switches it on in the Mods tab. For gameplay rules a player should choose (Hunger has it). Tested: Hunger removed from
mods.ini -> "starts switched off", Hunger=0 written.

## Moving characters (Nov 2026)

`Character.MoveTo(x, y)` (the movement aspect `ale.a(tile)`: pathfinding at its own speed; not in turn-based combat)
and `Character.TeleportTo(x, y)` (`a0l.a(c5x, area, position)`, the game's scripted placement). Tested: the player
teleported (16,1) -> (16,5) and walked back to (16,1).

## works.perf, hook dispatch, a second review (Nov 2026)

- `works.perf [seconds]`: times every mod's handlers per hook (Stopwatch only while on), then logs the busiest by mod
  and hook, process allocation (MB/s, via .NET's GC.GetTotalAllocatedBytes by reflection) and GC counts. Measured at
  2560x720 with the keyhole on: UnderRailFixes ~94 ms/s (its DrawLayer is the keyhole drawing layers into its picture,
  work the game would do anyway), the game alone allocates ~84 MB/s and does gen2 collections by itself.
- `Hook.Run` no longer allocates a closure per call, and the per-layer world hooks loop over their handlers directly:
  process allocation 143 -> 124 MB/s. The wrapper objects per layer remain (~13 MB/s).
- Second review, fixed: "Craft all" looped forever when a mod refused crafting (eed.a loops while its button is
  enabled; eed.b() re-enabled it; now a refused craft leaves it off; tested, one refusal, game responsive); a gear
  swap could lose the item coming back (now asked once, before the move); HUD Layout brought a reset part back from its
  migrated old keys (old keys cleared after migration); followers re-pathed every 0.5 s (now only when the leader moved
  on or the follower stopped). Checked safe by the review: mod blueprints never reach a save (only the selected
  blueprint's name string, which the stock game ignores), CheckGameKeys runs after config.dat's bindings.

## Ambushes (Nov 2026)

mods/Ambushes (startsOff): on returning to a zone, a chance of a pack (World.Spawn PlayerOnly, 5-8 tiles away,
gathered round one tile); clearing it gives experience; an `ambush` console command. Tested: 3 rathounds sprang, the
station's guards stayed out, killing all three gave 100 XP ("You gain 100 XP for clearing an ambush").

## Map markers (Nov 2026)

`Markers.Add(mod, id, zone, x, y, label, colour, area)` / `Remove` / `Of(mod)` / `All` / `Here`, `Markers.Changed`: kept
in each mod's per-save data (`works_markers`), so per game and never in the game's files. The Minimap draws `Here` as
diamonds, on its edge in the marker's direction when farther away ("Show markers"). Found here: SaveData returned a
value as it was stored this session (a typed list) but as JSON read it back after a load (List<object>), so the same
Get gave null before and a list after. `SaveData.Set` now stores the JSON shape at once (lists as List<object>,
dictionaries as Dictionary<string, object>, numbers as double). Tested:
a near marker on the map, a far one on the edge, both back after quicksave + quickload.

## Cookbook (Nov 2026)

`tools/cookbook/Cookbook.cs`: one example per API area, built by build.sh as a mod (not shipped), turned into
`docs/Cookbook.md` by `tools/cookbook/cookbook.py`; its `cookbook` console command runs every example (all 15 ran in
game). Writing it found: `Tooltip.AddLine` needed a colour (now optional), and `UiEvents.HudCreated` misses the HUD
that already exists when a mod subscribes late: `Hud.WhenReady(mod, build)` runs now and on every rebuild.

## Exported characters (Nov 2026)

The game exports the player at its end (a workflow step, `b7j` -> `d6f.arv()`) as an `ajk` (version, DLC list, the
player's `e3q`, date) in `My Games/Underrail/Characters/<name>.char`: BinaryFormatter with the save binder, read by a
new game's Import (`e7u` dialog, `ajk.a(path)`; the creation screen `iw` takes `ajk.a` as its character). So an
export follows the save rules: the stock game must read it.
- Around `ajk.b(path)` (writing): `GameEvents.CharacterExporting(path)`, mod statuses stripped (and put back), and the
  mods' `CharacterData` (a SaveData section per mod, "character:<mod>", saved with the game) written to
  `<name>.char.works.json`.
- `ajk.a(path)` postfix reads the side file; when the creation screen takes that character (`iw.a(object, el5)`), its
  data goes into the new game (the game makes its new-game state before the creation screen), and
  `GameEvents.CharacterImported` fires when that game's first zone has loaded. Loading a save instead drops it.
- Mod feats travel inside the .char as their key (the stock game skips unknown keys, as in saves).
- `World.ExportPlayer()` / `works.export`; `check-saves.sh --only` takes a .char (read as the import reads it).
- Tested: a player with a mod feat and a mod status exported: the .char has the feat's key, no mod status, no Works
  types, and the stock game reads it (`asd.char -> ajk`); New Game -> Import -> the mod feat on the Feats tab, and the
  new game started with the exported CharacterData. Not handled: deleting an export in the dialog leaves its side file.

## Stat rules: Stats.Modify, skill weights, the game's feats (Nov 2026)

- `Stats.Modify` (StatRebuild: Character, Add(StatBonus)): a prefix on `efk.bl()` (the rebuild of every stat) adds the
  handlers' bonuses to the character's effect list `z` (not saved) and a finalizer takes them out again.
  `Stats.Refresh(c)` rebuilds now. Flat bonuses go in before the game's percentages (+7 dodge showed as +8 with
  Nimble's percentage), as the game's own.
- `Skills.SetAbilities(skill, {ability: weight})` / `SetSynergies`: a skill's value is points x (1 + the best related
  ability's weight x factor); each skill class names those in `fi()` / `fj()`, postfixed for every skill class.
  Tested: Melee 22 -> 17 with Intelligence x2 instead of its own.
- The game's feats: `Feats.SetRequirements(key, ...)` (postfix `j0`), `Feats.AddBonuses(key, ...)` (postfix `cy`),
  `Feats.SetDescription(key, text)`; only the classes of changed feats are patched (when changed; queued before Works'
  hooks are in). Mod feats' Level requirement now reaches the game (`c6x`). Tested on Nimble: Stealth 16 -> 29 with
  +13, the sheet shows the new text and "Agility 12, Level 9".
- `Character.Defense` (Dodge, Evasion, Fortitude, Resolve: the Defense tab's values) and `Character.Initiative`.

## Character rules (Nov 2026)

`CharacterRules`: AbilityPointsAtCreation (game 5, over abilities starting at 5), SkillPointsAtCreation (120),
FeatsAtCreation (2), SkillPointsPerLevel (40), AbilityPointEveryLevels (4, below the level cap), FeatEveryLevels (2),
AbilityMinAtCreation (3), AbilityMaxAtCreation (10). The game's numbers are `aiy` static readonly fields, which .NET
compiles into the methods using them, so Works adds differences after the game's steps: postfixes on `e3q.b()`
(creation) and `e3q.a()` (level up) on the point counters (fields `c`/`d`/`e`: the decompile's `m_c`... are ILSpy's
renames), `c3p.a()` (the creation screen's "can raise", up to the maximum at level 1) and `e44.a(e3q)` (the creation
rows' minimum). The results are plain numbers in the save and exports. Tested: 8 points at creation, Strength to 12
(+ gone), Dexterity to 1 (- gone), 150 skill points.
**Trap found:** an exception in a postfix goes into the game: the first version (a null field) made New Game do
nothing, silently. Every rules postfix now catches and logs.
`Skills.SetSynergies` tested: Throwing 0 -> 15 from Melee (a synergy only adds below the level's skill cap, as the game).

## The Steam helper crashed (Nov 2026)

`UnderrailSteamHost.exe` (the 32-bit helper for Steam / GOG calls in the 64-bit mode) was built with Arch's Mono `mcs`,
whose mscorlib holds `Queue<T>` (in .NET Framework it is in System.dll): under Wine Mono the helper died loading
`GalaxyRunner` (TypeLoadException in the game's console log), Works started a new helper for every call and every
answer was an error, so Steam calls (achievements) never reached Steam. Built now against
/usr/lib/mono/4.8-api (`-nostdlib`): one helper, InitSteam answers. The same build line is in UnderRailFixes 1.x
(~/underrail build.sh), which shipped it in v1.0.5-v1.0.10's 64-bit mode.
With the helper working, a test run's game found the running Steam client by itself through `steam_appid.txt` in the
game folder (InitSteam -> 1 with no Steam environment): the test copies have no steam_appid.txt now (run-underrail's
launch.sh removes it unless WITH_STEAM=1; check-saves.sh no longer copies it). Measured: InitSteam -> 0 in test runs.

## New abilities and skills (Nov 2026)

The game's abilities and skills are fixed lists of its own classes inside the character (bn2 / ech), saved in saves
and exports, so mod ones are Works' own (player only): `CustomStats.AddBaseAbility(mod, id, name, desc, start)` /
`AddSkill(mod, id, name, desc, {ability: weight})` -> `Base` (points, in the mod's CharacterData: saved, exported,
imported) and `Value` (a skill: points x (1 + best weight x the game's ability factor `c2h.d`), abilities game or mod).
The character screen (`iw`) gets an "Other" tab after the game's (postfix `iw.c()`): +/- spend the game's own ability /
skill points (the counters' setters `e3q.b(int)` / `d(int)`, which raise the change events; the game's Base / Skills
tabs' refresh `a()` is called so their "remaining" stays right), creation range from CharacterRules, the game's
per-level skill cap `csn.a(level)`; Cancel (`amm.a(cku.b)` on the screen) restores the values it opened with, as the
game restores its points. The game's "spend all points" checks count them. Effects are the mods' (Stats.Modify...).
Tested: creation (Luck 5 -> 8, Survival capped at 15 at level 1), level up (Survival +5 then Cancel -> 0 and 40 points
back; +10 and the rest on game skills, a feat, Accept), quicksave, changed to 99 (value 107 with Constitution),
quickload -> 10; the save passes the stock loader.
New: `Ui.SmallButton(text, w, h, click)`; `Ui.IsShown` also accepts the game's current screen (bcm.aau / aaw / aa0),
so widgets work outside the HUD and Options (the character screen at creation).
Traps: `amm.a(cku)` is the base window's close (patch it there, filter on iw); the character screen's tabs are added
in `iw.c()`, not `e()`.

## A review of the character code, fixed (Nov 2026)

- Stats.Modify: bonuses went into an effect list the game threw away on a character's first rebuild after a load
  (`bl()` starts with `ak()`, which makes a new `z` then): `ak()` is called first. Nested rebuilds kept one shared
  static list (a handler calling Stats.Refresh recursed forever, others leaked entries): now Harmony's `__state` per
  call and a per-character re-entry guard. And the game's rebuild while loading runs before the player is known, so
  Works rebuilds the player once the loaded zone is ready (measured: Mechanics 16 -> 26 after a quickload).
- Import: cancelling the import dialog wiped the already-imported character's data (now ignored); cancelling the
  creation screen now drops the imported data (it leaked into the next new game); no name check (renaming an imported
  character lost its data). Tested: import then cancel -> "data dropped".
- The Other tab's "-" floor is taken again when the tabs are rebuilt (after an import: no free respec).
- Feat requirements on the creation screen: ability/skill types came from the player's data, absent there, so
  requirements vanished; now fixed tables (abilities' classes, skills' `[a1("Name")]`). Nimble with changed
  requirements is no longer offered at creation.
- Feat bonuses / requirements are added only by the feat's own nearest `cy` / `j0` (a feat calling its patched base
  would get them twice); cached per type.

## Scavenger (Nov 2026)

mods/Scavenger (startsOff): a mod skill, Scavenging (Perception 1, Intelligence 0.5), on the Other tab; its value sets
the chance of extra salvage in loot (new `Loot.Add` overload with a chance worked out per roll), and from 40 it gives
+10 Mechanics (Stats.Modify; CustomStats.Changed -> Stats.Refresh). Loot rules now log what they add. Tested:
Scavenging 80 [86 with the abilities], Mechanics 16 -> 26, rathounds dropping repair kits, scraps and a fusion cell from
its rules.

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
| Scanner | point at a creature: level, health, attitude, threshold/resistance per damage type, detection vs your stealth (always / in combat / while a key is held) | World.Hovered, Character.Resistances / Detection, anchored Hud.AddPart |
| FieldMedic | after a fight, if hurt, uses a bandage (above 40 % health) or a health hypo from the bag, with a sound from its folder | CombatEnded, Items.Use, Assets.Sound |
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
| Versions | mods built for a tModLoader version | `"works": "0.9"` in mod.json against `WorksApi.Version`: while 0.x the same minor; from 1.0 the same major, at least that minor. New areas start **experimental**; after that only additions |
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
- [x] **Build directly against FNA** (Nov 2026): `Retarget.cs` now only makes `build/ref/underrail.exe`, a copy of the
      game exe pointed at our `FNA.dll`; Works and every mod compile against that and `FNA.dll`, with no rewriting
      afterwards (they reference only `FNA`). Wine Mono's FNA, SFML and the music's SFML branch are gone from the build;
      UnderRailFixes lost its Microsoft XNA / Reach branches (the 2048 limits, `KeyholeTiles`; the keyhole's front
      picture is always one screen-sized target). The patcher's `--32bit` only prints that there is no such mode.
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
