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
- [ ] **Saves from Steam's real launch** (inside Steam's runtime container), not only headless test runs.
- [ ] **Real Windows**: .NET 10 on Windows writing saves, the stock game on Windows (Microsoft .NET Framework, XNA)
  loading them. Wine Mono stands in for .NET Framework in the check today.
- [ ] **Run check-saves.sh from release.sh**, so no .NET 10 release can ship without it.
- [ ] **Mods' save data** (works.json and per-mod JSON, when built): the stock game must still load saves that have them.

## Housekeeping

- [x] **Commit the Works code**: moved from the UnderRailFixes `works-poc` branch into this repo (github.com/Rhiyo/UnderRailWorks).
- [x] Repo layout: its own repo; the UnderRailFixes mod lives in `mods/UnderRailFixes` here.
- [ ] Cherry-pick the 1.0.9 and 1.0.10 fixes (`cacda0c`, `48a1bfe` on `release/1.0.9`) onto `main`, once the other
  session's work there is committed.
- [ ] Names for 2.x: `UnderRailWorks.dll` instead of `UnderrailFix64.dll`, the patcher's name, the version label (it still
  says UnderrailFix 1.0.8), and recognising and replacing a 1.x install.

## Finish the move to .NET 10

- [ ] build.sh: publish the launcher, fetch the Windows runtime and Harmony 2.4.3 with pinned hashes.
- [ ] The patcher installs and uninstalls the new layout (game -> UnderRail.Game.exe, launcher as underrail.exe,
  dotnet\); today it is done by hand. Running the old patcher on a .NET 10 install must fail safely.
- [ ] Untested on .NET 10: Steam achievements through the helper, GOG Galaxy, clipboard, the dev console, long sessions.
- [ ] Performance: frame times on .NET 10 against Wine Mono in the same scenes.
- [ ] Download size: the runtime is 76 MB zipped; check what can be trimmed.

## Foundations

- [ ] Port the engine fixes into Works: Harmony where possible; keep in the patcher only the loader hook, the FNA
  retargeting, the 64-bit flags and the SFML/Steam/Galaxy replacements.
- [ ] Declarative mod settings: a config class with attributes that produces the Mods tab rows and settings.ini
  (tModLoader's ModConfig, with our readable ini).
- [ ] Mods' save data: works.json per save, per-mod JSON, data of a missing mod kept, a warning icon on the Load Game
  screen, a backup before saving.
- [ ] Reload mods without restarting (collectible AssemblyLoadContext, possible on .NET 10).
- [ ] Debug tools in Works, not in the mod: the draw-order log and render-target dumps (KeyholeDebug today), and a
  documented "draw exactly like the world pass" helper.
- [ ] Measure the fills/outlines mask: built the same plain-batch way the keyhole's was, probably a pixel or two off.

## Opening up the game (one area per slice: patch points, events, names, a real mod, a doc page)

- [x] Drawing (experimental): WorldDraw, Outlines, names; UnderRailFixes uses it.
- [ ] Items: the proven overlay made real: JSON recipes, icons, text, adding to stores and loot.
- [ ] Characters, stores, dialogs: the same overlay; their loaders still to find.
- [ ] Maps: zone overlay, patches on every zone load (visited zones come from the save), cross-zone waypoints, what the
  stock game does with a transition to a missing map.
- [ ] Later: combat, UI, quests.

## For modders

- [ ] A mod template / SDK: reference assemblies and a build script.
- [ ] Docs: the framework rules, the API per area, a walkthrough for a first mod.
- [ ] The map editor, as its own project, once the item and map slices exist.

## Testing gaps (from 1.x, still open)

- [ ] Real Windows, GOG, the expansions, portrait screens, older Linux distros.
