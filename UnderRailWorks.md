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
| Mods reference `underrail.exe` (as patched), `0Harmony.dll`, `FNA.dll` and the Works runtime | With 64-bit only there's one runtime assembly and one graphics library, so mods can use Works' API and XNA types directly. (The PoC still avoids our DLL: it ran on 1.x, which has two) |
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
exits, toggle key), drawn from the game's own data through Works' names (`Areas`: zone, area, tiles, entities, exits;
`Entities.AttitudeOf` / `OutlineColor`).
- Isometric like the screen (the game's projection, a7m.vq: screen x from x - y, y from x + y), rebuilt 4 times a
  second into a texture.
- What the game's data says (measured, not guessed: a grid dump and the entities' names and aspects): everything solid
  has the `cd0` aspect ("SEA"); unnamed solids form the walls, named ones are props (desks, shelves, barrels), doors
  have `cnb` ("DEA"); exits are area transitions (`b0j`, "ATT") and zone transitions (`cdo`, "ZTT").
- The player as a white dot and the characters the player can see as dots in their Tab outline colour (the game's
  `adi.ae` table): "can see" = the game drew them this frame (from the drawing events), so stealthed or out-of-sight
  characters stay hidden.
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
| Shared services | per-mod config, localization, save data (TagCompound) | settings, options UI, logging; save data (sidecar JSON) and localization still to do |

**Each area is a slice**: its patch points, its events (the model above), readable names for its objects, one real
mod that uses them, and a short doc page. Slices are independent once the foundations hold.

**Done: drawing (experimental)**: `WorldDraw` events (LayerStarting, DrawLayer, DrawTileExtras, WorldDone,
ShaderPassStarting/Done, InterfaceOffset), `Outlines` (mods request outlines for every creature Tab would outline;
the extra ones are hidden from the game's pass for the mod to draw), names for layers, entities, the world view and
the draw context. The real mod: UnderRailFixes (Occluded characters), now with no reflection of its own. The name
check found a 1.x bug on its first run: `a7m.aq` (the clip state) is static, so 1.x's instance lookup was always
null and fills/outlines would fail whenever the world's clip doesn't start at 0,0.

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
