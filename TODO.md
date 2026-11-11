# TODO

Working list for UnderRailWorks. Design, decisions and status are in UnderRailWorks.md.

## More save testing (.NET 10)

Every file the game writes must load in the stock, unmodded game. `tools/savecheck/check-saves.sh [--play]` checks
that, but so far only against the player's 8 saves (Nov 2026: all pass). Types that appear in none of them are untested.

- [ ] **More varied saves**, so more of the game's types get written and read:
  - other areas, all the way through the game;
  - the expansions (Expedition: boats and the island; Heavy Duty);
  - vehicles, crafting, merchants and their stores, quests at different stages, companions;
  - combat in progress, status effects, traps, mines, psi;
  - a new game straight after character creation, and an old long-running save.
- [ ] **Scripted play on .NET 10 that creates new state**, not just load-and-save: new game through character creation,
  picking up and crafting items, trading, an area transition (autosave), a fight. Today's StringEqualityComparer bug
  only showed in objects .NET 10 created itself.
- [ ] **config.dat written in game on .NET 10** (changing options, key bindings), then loaded by the stock game in game,
  not only offline.
- [ ] **Saves from Steam's real launch** (inside Steam's runtime container), not only headless test runs. (The game
  itself was played from Steam on .NET 10 in Nov 2026; its autosaves were part of the checked set.)
- [ ] **Real Windows**: .NET 10 on Windows writing saves, the stock game on Windows (Microsoft .NET Framework, XNA)
  loading them. Wine Mono stands in for .NET Framework in the check today.
- [x] **Run check-saves.sh from release.sh** (with --play), so no release can ship without it. It builds its own game
  copies from dist/.
- [x] **Mods' save data** (works.json and per-mod JSON, when built): the stock game must still load saves that have them.

## Housekeeping

- [x] **Commit the Works code**: moved from the UnderRailFixes `works-poc` branch into this repo (github.com/Rhiyo/UnderRailWorks).
- [x] Repo layout: its own repo; the UnderRailFixes mod lives in `mods/UnderRailFixes` here.
- [ ] Cherry-pick the 1.0.9 and 1.0.10 fixes (`cacda0c`, `48a1bfe` on `release/1.0.9`) onto `main`, once the other
  session's work there is committed.
- [x] Names: `UnderRailWorks.dll`, `UnderRailWorks.ini`/`.log` (1.x's `UnderrailFix.ini` read on the first start),
  `UnderRailWorks-Patcher`, version 1.0.0 (Works is its own product; the UnderRailFixes mod is 2.0.0, its line's next major
  version); an UnderRailFixes 1.x install is recognised and replaced.

## Finish the move to .NET 10

- [x] build.sh: publishes the launcher, fetches the Windows runtime (SHA-512) and Harmony 2.4.3 (SHA-256).
- [x] The patcher installs and uninstalls the new layout (game -> UnderRail.Game.exe, launcher as underrail.exe,
  dotnet\), over a stock game, an existing install, a game update or a 1.x install; tools/check-release.sh tests all
  of it. (Running the 1.x patcher on a Works install: it reads the launcher as a broken exe and stops.)
- [ ] Untested on .NET 10: Steam achievements through the helper, GOG Galaxy, clipboard, the dev console, long sessions.
- [ ] Performance: frame times on .NET 10 against Wine Mono in the same scenes.
- [x] Download size: WPF removed from the bundled runtime (173 -> 119 MB unzipped; tools/trim-runtime.py).
- [x] The launcher gets the game's icon from the player's own exe at install (Windows; LauncherIcon.cs).
- [ ] Port the engine fixes into Works: Harmony where possible; keep in the patcher only the loader hook, the FNA
  retargeting, the 64-bit flags and the SFML/Steam/Galaxy replacements.
- [x] Declarative mod settings: ModConfig (tModLoader's, with our readable ini); UnderRailFixes uses it.
- [x] UI toolkit (first version): Ui.* controls, UiEvents (HUD, main menu, options), GameEvents.Update, Input;
  examples/HelloUI.
- [~] UI toolkit, next (done: key bindings, tooltips, text input, lists, sliders, choices, images, windows; still: draggable panels as a widget): mod keys in the game's key-binding screen, draggable panels, tooltips, text input, more
  attachment points (inventory, dialog, character screen), readable wrappers for dropdowns and sliders outside Options.
- [x] Mods' save data: works.json per save, per-mod JSON, data of a missing mod kept, a warning icon on the Load Game
  screen, a backup before saving.
- [ ] Reload mods without restarting (collectible AssemblyLoadContext, possible on .NET 10).
- [ ] Debug tools in Works, not in the mod: the draw-order log and render-target dumps (KeyholeDebug today), and a
  documented "draw exactly like the world pass" helper.
- [ ] Measure the fills/outlines mask: built the same plain-batch way the keyhole's was, probably a pixel or two off.

## Opening up the game (one area per slice: patch points, events, names, a real mod, a doc page)

- [x] Drawing (experimental): WorldDraw, Outlines, names; UnderRailFixes uses it.
- [x] Items: the proven overlay made real: JSON recipes, icons, text, adding to stores and loot.
- [ ] Characters, stores, dialogs: the same overlay; their loaders still to find.
- [x] Maps: zone overlay, patches on every zone load (visited zones come from the save), cross-zone waypoints, what the
  stock game does with a transition to a missing map. (Nov 2026: new zones from a mod folder, Zones.Add; exits into them from any zone, Zones.AddExit; saves made inside a mod zone need the mod. Edge exits too: Zones.AddEdgeExit. Still: a map editor.)
- [ ] Later: combat, UI, quests.

## Mods

- [x] Minimap (`mods/Minimap`): HUD panel, isometric, walls/props/doors/exits, the player and visible characters in
  their Tab colours; ModConfig settings.
- [ ] Wrap the drawing API (WorldDraw passes a7m/cp9/a2f) in Works types, so UnderRailFixes can be built without the
  game too, like Minimap and HelloUI.
- [x] Minimap next: click to open a larger map, remember explored tiles per area, other floors (Z) of the same area, (Nov 2026: click for a large map, marker labels on hover. Then: other floors on the large map; explored tiles are the game's own, saved per area.)
  map markers for quests or containers.

## Content API (Nov 2026: built, see UnderRailWorks.md "The content API")

- [x] World, characters, inventory, placements; variables, quests, text, notifications; dialogs; lifecycle events;
  saves kept stock-loadable (placements never saved, mod quests as variables, SaveGuard).
- [x] Wrap the drawing API in Works types; UnderRailFixes built without the game (pixel-identical).
- [x] Stores (merchant stock), status effects, equipment; skill/ability conditions as named helpers.
- [x] Dialog started/ended events; zone first-visit flag; combat events tested in game.
- [x] New items (the item overlay), new maps.
- [ ] A save with a completed mod quest and many placements through `check-saves.sh --only`; the stock game
  re-saving a modded save, then Works loading it.

## More test mods (ranked from research: what popular mods do, what UnderRail players ask for)

- [x] Quest Tracker, Word Gets Around, Difficulty Dial, After-Action Report, Contract Board.
- [x] Shot Breakdown: hit chance and damage parts in the tooltip (needs the hit chance's components, not just the total). (done Nov 2026: range and darkness, by the game's formula; mods/ShotBreakdown)
- [x] Vendor Directory (the zone's merchants, trade from the list) and Fair Trade (price settings).
- [x] Merchant Ledger: vendor stock and prices remembered across zones, searchable (Ui.TextBox).
- [x] Pack Mule: sell tagged junk in one click (needs selling through the barter screen's own transaction). (done Nov 2026: mark with Delete, "Offer junk" in barter; mods/PackMule)
- [x] Stealth Lens: show where enemies would notice you (needs detection rules: research). (done Nov 2026: notice distance per enemy while sneaking; mods/StealthLens)
- [x] Survey Pins: the Minimap marks merchants; still: pins with notes, workbenches, locked containers. (done Nov 2026: notes with Insert, locked containers; mods/SurveyPins)
- [x] Neural Reallocation: a respec NPC (points refunded to the game's own unspent points).
- [x] Workbench Planner: recipes, what can be crafted now (mods can now add recipes: Crafting.AddRecipe). (done Nov 2026: key D; mods/WorkbenchPlanner)
- [x] Gauntlet: opt-in challenge modifiers; Grudges: enemies who return; Collector's Checklist. (done Nov 2026)
- [x] HUD Layout: drag the game's HUD parts around, kept relative to an anchor (survives resolution changes) (Works: Input.Mouse, UiElement.X/Y).

- [x] Key bindings for mods in the game's Controls tab (Keybinds); every shipped mod moved to it.

## API gaps (ranked from the moddable-CRPG research: CDDA, RimWorld, Caves of Qud, ToME, sfall, WeiDU, DOS2 SE)

- [x] JSON content packs (text, quests, dialogs, placements).
- [x] Content index dump (items, blueprints, stores, dialogs, status codes). Zones still to add.
- [x] Per-save mod data (WorksMod.Data, plain values in the save).
- [x] Status effect events (applying with a veto, applied, removed). Custom status effects still to do.
- [x] Level-up event; XP curve, XP gains and points per level (Progression).
- [x] New item definitions (clone a stock item, override name/stats; the save holds the stock base). (done: Items.Define, see UnderRailWorks.md.)
- [x] New feats / abilities; level-up and progression hooks (XP curve, points per level): mod feats, mod abilities, CustomStats, CharacterRules, Stats.Modify.
- [x] Cancellable hooks with a set order (HookOrder; trade, containers, taking, dialogs, crafting, equipping, leaving a zone, weapon switching, disarming).
- [x] Equipment (slots, equip/unequip, events). A veto still to do.
- [x] Containers, loot, locks (events, contents, loot rolled, lock attempts).
- [x] Stealth detection hooks (rate untested in game: test sneaking up on hostiles unseen).
- [x] Pickpocketing (ItemStolen), traps (TrapDisarming/TrapDisarmed, Traps.ModifyDetection), crafting events, AI hooks. TrapDetected, Traps.InZone, Trap.Disarm; all tested through in game (lu-b22u).
- [x] Area triggers and interactables (Triggers, usable objects).
- [x] Content packs: triggers; DLL-registered conditions and effects.
- [x] Patching stock dialogs (Dialogs.Patch, content packs' dialogPatches).
- [x] Content packs: stores, item tweaks. (done: "stores", "itemEdits", "recipes" sections.)

## For modders

- [ ] A mod template / SDK: reference assemblies and a build script.
- [ ] Docs: the framework rules, the API per area, a walkthrough for a first mod.
- [ ] The map editor, as its own project, once the item and map slices exist.

## Testing gaps (from 1.x, still open)

- [ ] Real Windows, GOG, the expansions, portrait screens, older Linux distros.

## Built 2026-11-05 (see UnderRailWorks.md for each)

AI hooks (target, actions, turns, flight), cancellable events (trade, containers, taking, dialogs, crafting, equipping,
leaving a zone), UI widgets, item kinds/uses/values, decimal/string settings, content-pack items/stores/loot/music/
sounds/recipes, works.reload, docs/API.md, StatBonus for feats and statuses, mod abilities (ability bars), Combat.Damage,
CharacterDying, allies and followers, MoveTo/TeleportTo, mod crafting recipes, key clashes with the game's keys,
startsOff, works.perf. Mods: Bounty Hunter, Beastmaster, Hunger, Ambushes.

## Next

- [x] Nov 2026: House Pets (Jyles' catalogue, living room, feed/follow/home), Campfire Cooking (recipes content pack),
  PlayerHouse API, works.dialog (Dialogs.Describe), Dialogs.Patch on lines that continue to their options, Ui.Ask /
  AskNumber / Confirm / Choose on the game's popups, Ui.Window on the game's titled window, Outlines.PickThrough
  (select characters behind scenery), Zones.Add / AddExit, Container.Name, followers keep their dialog. UI rules: UI.md.
- [x] Mod text and sizes follow the interface size (Ui.LineHeight, Ui.S; UI.md "Sizes").


- [x] Fewer allocations per world layer: hook dispatch done; the wrappers (Layer, WorldView, DrawContext) are structs. Measured
  again (Nov 2026, works.perf 8 in LU-A1, Sprite Target Mask on): 16 MB/s with no mods, 26 with UnderRailFixes, 33 with
  the usual set, no gen 2 collections. The 124-143 MB/s earlier were sessions with the mask off (GPU read-back hit test).
- [x] Followers through zone changes (Follow(..., travels: true)).
- [x] AI weapon choice: switching is hookable (WeaponSwitching); reloading too (Reloading/Reloaded, nh.fr). Tested in game with the player and an NPC in combat, cancel included; Items.Use on an item.
- [x] Map markers from mods (Markers; the Minimap draws them). Journal entries: the game's journal is its quest log, which mod quests already fill (Quests, content packs).
- [x] A modder's cookbook (docs/Cookbook.md, compiled and run).
- [ ] Real Windows: untested. Steam: the helper connects (InitSteam -> 1 with WITH_STEAM=1, Nov 2026); unlocking an
  achievement not tried on purpose (it would change the player's real profile). Sound output: measured from the test run's
  audio sink (Nov 2026): menu music RMS ~3100, in-game music and ambience ~1500 (NVorbis streaming works on .NET 10);
  single sound effects not isolated.
