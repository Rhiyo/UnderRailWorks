---
name: run-underrail
description: Run UnderRail (with the current UnderRailFixes build) headless (no window, no focus, silent) in gamescope and drive it yourself - launch, screenshots, mouse clicks, hovering, keys, mouse wheel - to test a change in game without the user. Use when asked to run, test, check or screenshot the game, start a new game / create a character, go through dialogue, see a fix working in game, or check whether a graphics driver (OpenGL, Vulkan, D3D11 via FNA3D_FORCE_DRIVER) renders correctly.
---

# Running and driving UnderRail

Everything is in `scripts/` next to this file (`<repo>/.claude/skills/run-underrail/scripts/` in the main checkout;
the skill isn't tracked by git, so a git worktree doesn't have it). Paths below use the defaults in `common.sh`
(Steam in `~/.local/share/Steam`, test files in `~`, cache in `~/.cache/underrail-test`); override them there or
in the environment (`STEAM`, `STEAM_GAME`, `TEST_GAME`, `BASE_PFX`, `CACHE`, `PROTON`) on another machine.
Needs: Steam with the game and a Proton (the one the game's prefix last ran with is found automatically),
Runs are **not connected to Steam** by default (an empty client folder for Proton, no app id, GE's protonfixes off):
Steam doesn't show the user as playing and the game runs as its no-Steam build does. Set `WITH_STEAM=1` only to test
Steam integration (achievements, the Steam helper).
gamescope (headless backend), PipeWire/`pactl`, `mcs` (Mono), ImageMagick `magick`, Python 3 with numpy.
It runs a **test copy** of the game in a **fresh, throwaway Proton prefix** (never the player's real saves, config.dat
or their running game) in **headless gamescope**:
- **Fresh every launch**: each `launch.sh` copies a clean base prefix (no config.dat, no saves) and `stop.sh` deletes
  it, so every run starts from the game's default settings and nothing from an earlier test carries over. Bring the
  player's config.dat or saves in only when the test needs them (`--config`, `--saves`, `--save NAME`); those are
  copies, and the player's own files are never written.
- **No window**: it never takes focus from the user's desktop. Input goes to gamescope's own X server and
  screenshots come from gamescope itself, so the user keeps working while you drive the game. (Inside gamescope the
  game must still be the active window: see "Focus matters".)
- **Silent**: no sound reaches PipeWire at all (SDL's dummy audio driver, set in common.sh; `WITH_SOUND=1` to hear it).
  Never create sinks for tests: a new sink made the desktop switch the player's output to it, and test sound was
  heard over their own games (Nov 14 2026). Don't mute or change the user's own devices.
- **One run per session, and you only touch your own**: several Claude sessions may be testing at the same time
  (see "Runs: track and close only your own").
- `VISIBLE=1 launch.sh` shows it in a window instead, only when the user wants to watch (that window takes focus).

| Script | Does |
|---|---|
| `setup.sh [--refresh-game] [--refresh-prefix]` | test copy `~/underrail-test` (the Steam folder's files, `data/` symlinked), base prefix `~/underrail-test-prefix` (the real prefix copied once, its `My Games/Underrail` emptied), builds the input helper. `--refresh-game` after installing a new build into the Steam folder. |
| `launch.sh [--config] [--saves \| --save NAME] [W H]` | fresh run folder and prefix (`~/.cache/underrail-test/runs/<run>/`), then starts the game headless at W x H (default **1280x720**), no sound; returns once the process exists. `--config` copies the player's config.dat in, `--saves` all their saves, `--save NAME` one save folder (repeatable, e.g. `--save quicksave`) |
| `shot.sh OUT.png [SCALE%]` | screenshot of the game's screen (`gamescopectl screenshot`; no mouse cursor in it) |
| `inp.sh CMD...` | input to the game, in game pixels |
| `compare.sh [launch options] DRIVER...` | the same scene on several FNA3D drivers at once (e.g. `--saves D3D11 OpenGL Vulkan`): full-size shots, a stacked image, a pixel diff against D3D11; stops its runs (see "Checking a rendering bug") |
| `stop.sh` | ends **this run only**: its game, the gamescope process group launch.sh started, the Wine processes in its prefix, its sink; then deletes its folder (`KEEP_PREFIX=1` keeps it, to inspect what the game wrote) |

### Runs: track and close only your own

Several Claude sessions can test at once, from the same test copy. Everything a launch creates belongs to one
**run**, named after your Claude session (`CLAUDE_CODE_SESSION_ID`, first 8 characters) and kept in
`~/.cache/underrail-test/runs/<run>/`: the prefix, `game.log`, gamescope's display name, its process-group id
(`pgid`) and the sink's module number. Every script finds only your run: the game by the prefix it runs in, gamescope
by the process group launch.sh started with `setsid`, the sink by the module it loaded.

- Close what you started with `stop.sh`, and nothing else. **Never kill another session's gamescope, game, Wine or
  sink**, not even one that looks stuck or left over: `pgrep`/`pkill` by name or by the test copy's path would hit
  them (that's how two sessions used to close each other's games). Other folders in `runs/` belong to other
  sessions; `stop.sh` names any that are still going, so leave them alone.
- A second game in the same session needs its own run name: `RUN=b launch.sh`, and the same `RUN=b` on every
  `shot.sh` / `inp.sh` / `stop.sh` call for it.
- `setup.sh --refresh-game` / `--refresh-prefix` replace files every run shares, so they refuse while any run's
  gamescope is alive. Stop your own run, then wait for the others to finish; don't stop them.
- Wine processes in the **real** prefix (`compatdata/250520`) are the player's or Steam's, never a test's: leave them.

### Settings and saves

Default to a clean run: no options, so the game starts as on a fresh install (its own default settings, no saves)
and a test isn't skewed by whatever an earlier test or the player changed. Add `--config` only when the test is
about the player's own setup (their resolution, shader, display mode...), and `--save NAME` / `--saves` when it
needs to start from a saved game (e.g. `--save quicksave`, then Load Game) instead of a new character. The player's
saves are the folders in `$REAL_PFX/$GAME_DOCS/Saves/` (common.sh: the game's Proton prefix,
`steamapps/compatdata/250520`, then `pfx/drive_c/users/steamuser/Documents/My Games/Underrail`). With `--saves`,
**Resume Last Saved** (1280x720: 960, 395) loads the newest one: the quickest way to the place the player means.

Changing settings in game during a test is fine: they go to the throwaway prefix and are gone after `stop.sh`.
Check a fresh config's settings you depend on (e.g. Options -> Video -> Shader) instead of assuming them.

### Reproducing a bug the player reports

When the player describes a bug they ran into, the clean-run default doesn't apply: **reproduce their setup first**,
before theorising or changing code. A test with default settings can look fine while their game is broken (a keyhole
bug only showed with several keyhole characters on a 5120-wide screen; the default ini had one, and the test
"passed").

1. **Their mod settings**: copy `$STEAM_GAME/UnderrailFix.ini` over `$TEST_GAME/UnderrailFix.ini` and set `Log=1`.
   Read it: the player may have changed settings since (they test things themselves), so ask if it doesn't match
   what they describe. The test copy's ini is shared by every run: put the defaults back afterwards if other sessions
   test from it.
2. **Their game settings**: `launch.sh --config` (their config.dat: shader, display mode...).
3. **Their screen**: launch at their resolution (e.g. `launch.sh ... 5120 1440`), not 1280x720: scale, zoom and
   anything width-dependent change with it. Their monitor size is in the ini (`Width`/`Height`, or `native`) and the
   log's "added WxH" lines.
4. **The place**: ask them to quicksave (F5) where it happens, then `--save quicksave` and Resume Last Saved. A
   screenshot of it (`~/Pictures/Screenshots`, newest file) shows what to look for.
5. **The same spot and moment**: go where their screenshot was taken (not just the save's start) and do what they did
   (walking, standing, a fight). Compare the same spot with the fix's switch off **before** calling a difference a
   bug: the game has its own darkening (shadow rectangles, lighting) that looks like an artifact in a crop.
6. **The same build**: compare `md5sum` of `UnderrailFix*.dll` in `$STEAM_GAME` and `$TEST_GAME`; install the
   player's build into the test copy if they differ. A game started before an install still runs the old build.

**Comparing two builds (or fix on/off) while walking**: run the same script on both: launch, Resume, wait ~25 s
for the save to finish loading (clicks during the load or the fade-in are dropped: the character never moves and
every frame "matches"), then the same clicks and a burst of `shot.sh` right after each one. Check in a shot that the
character actually moved before comparing. Frames then line up within a few hundred ms; diff them
(`magick compare -fuzz 5% A B -compose src -highlight-color red -lowlight-color none D`, composite D over a dimmed
copy) and crop the red spots at full size. To compare with the last release, build its tag in a worktree
(`git worktree add DIR vX.Y.Z`, copy `lib/`, `./build.sh`) and patch a copy of the test folder with it.

Then narrow it down **one setting at a time** (e.g. one effect back to its default) until it goes away, and check it
is ours at all: the same save with the fix's switch off (`ShowCovered=0`, ...) shows what the stock game draws there.
Artifacts that come and go (while walking, for one frame) need a burst of full-size shots right after the input
(`shot.sh` several times in a row) and a scan rather than a look: a 1-pixel line is invisible in a scaled shot. Crop
the same area from each shot and compare, or search for thin lines with a few lines of numpy (rows brighter than
the rows above and below over a run of pixels).

## Resolution: keep it small

**Always launch at the default 1280x720** (`launch.sh` with no size) unless the test is about a resolution:
screenshots cost tokens by their size, and a bigger screen makes every step more expensive. 1280x720 is enough to
read every menu and dialogue line, and `shot.sh OUT.png 50` (640x360) is enough to see where you are between
steps. Take full-size shots only when reading small text or checking pixels; for a detail, crop
(`magick IN -crop WxH+X+Y`) rather than enlarging the whole screen. **Never judge rendering (seams, gaps,
bleeding, flicker) from a scaled-down shot**: 1-pixel faults vanish at 50%. Crop at full size instead.

For resolution work, launch at the size needed (`launch.sh 5120 1440`, `3840 2160`, `7680 4320`, `2160 3840`...):
headless, any size works and the game's native resolution is exactly W x H. Scale those screenshots down
(`shot.sh OUT.png 25` for 8K) and crop full-size only where detail matters (`magick IN -crop WxH+X+Y`).

## Coordinates

With `launch.sh`, **game pixels = screenshot pixels (at 100%) = input coordinates**. From a scaled shot, multiply by
100/SCALE (x2 for 50%). The test copy's `UnderrailFix.ini` is created with defaults on first launch (resolution
`native` = the W x H screen). Don't change the player's own ini in the Steam folder.

## Input (`inp.sh`)

Several commands per call (each call starts a Wine process, ~1 s):

```
move X Y            hover (tooltips, Tab-like highlights need the cursor over things)
click X Y           left click (moves there first, so the game sees the hover)
rclick X Y          right click
ldown X Y / lup X Y press / release the left button there (a drag: ldown X Y, move X Y..., lup X Y)
wheel N             mouse wheel, N notches (+ up / - down); hold Shift for zoom: down 0x10 wheel -2 up 0x10
key VK              press a key          down VK / up VK    hold until released     hold VK MS
type TEXT           letters, digits, spaces (for the character name)
sleep MS            pause between commands
focus               make the game the active window (writes what it found to the run's pfx/drive_c/input.log)
```

**Focus matters.** The game only gets keys, and only runs its area fade-in (the world stays black with just the HUD
after a map change or a loaded save), while its window is active. `input.exe` is built as a window-less Windows
program (`-target:winexe`) for that reason: built as a console program, every `inp.sh` call opened a Wine console
window that took focus from the game, which made keys do nothing and left the world black. If keys don't register
or the world stays black, send `focus` (`inp.sh focus click X Y`) and check `input.log`.

Common VK codes: Escape `0x1B`, Enter `0x0D`, Space `0x20`, Tab `0x09`, Shift `0x10`, Ctrl `0x11`, Alt `0x12`,
Backspace `0x08`, `1`-`9` = `0x31`-`0x39`, F1-F12 = `0x70`-`0x7B`, letters = their uppercase ASCII (`I` = `0x49`).

The cursor isn't drawn in screenshots (gamescope composites it separately), but hovering works: check hover
effects (highlights, tooltips) rather than looking for the cursor.

How it works: `input.cs` is a tiny Windows program run with `proton run` in the same prefix, on gamescope's X display
(read from the game's `/proc/PID/environ`); its `SetCursorPos`/`mouse_event`/`keybd_event` reach the game as real
input. `inp.sh` unsets `SteamAppId`: with it set, Proton's `steam.exe` wrapper never returns.

## Waiting

Foreground `sleep` is blocked: wait with `timeout N tail -f /dev/null`. Typical waits:
- main menu after `launch.sh`: 15-25 s;
- a new screen after a click: 1-3 s;
- loading a map: 5-15 s.

Screenshot after each step; if nothing changed, wait and look again before clicking again (double clicks skip things).

## A typical run

```bash
S=<repo>/.claude/skills/run-underrail/scripts   # main checkout
$S/setup.sh                      # once; --refresh-game after `./build.sh install` or a new patch in the Steam folder
$S/launch.sh && timeout 25 tail -f /dev/null
$S/shot.sh $SCRATCH/menu.png 50
$S/inp.sh click 960 335          # 1280x720 main menu: New Game
...
$S/stop.sh
```

### Main menu (1280x720)

New Game (960, 335) · Resume Last Saved (960, 395) · Load Game (960, 455) · Options (960, 515) · Exit (960, 635).

### New game and character creation

1. **Rules**: Game Difficulty (five icons in a row) and Experience System (two icons). Pick one of each (a
   highlighted icon is chosen), then **Accept** (bottom right; at 1280x720 about (902, 678)).
2. **Character creation**: several pages (abilities, skills, feats, appearance/name...). The game expects every
   page to be filled in: spend the points it offers, pick what it asks for, and give a name (`type NAME`), or
   Accept is refused or warns. Screenshot each page, read what it asks for, then go on.

Record the coordinates of each page here once you've worked them out, so the next run doesn't have to.

Worked out at **2560x1440** only (an earlier run with the player's config.dat). They **don't simply halve** for
1280x720: the game picks a hand-made interface size by resolution (Small at heights <= 800, see AGENTS.md), so at
720 the layout differs. Use them as a guide to what each page asks for, and record the 1280x720 positions here once
measured:
- Main menu: New Game (1920, 864) · Options (1920, 1134). Options tabs: Video (786, 206); Video's Shader dropdown
  (1300, 574) -> Legacy (1256, 616) / Advanced (1266, 652); options Accept (1704, 1198), Cancel (1968, 1198).
- Rules: first difficulty (570, 488), second XP system (770, 986), Accept (1704, 1198).
- Base: male (768, 376); `+` buttons at x 1178, Strength y 658, Dexterity 712, Agility 768, Constitution 822 (5
  points to spend); name field (1032, 380) then `type NAME`. Tabs: Skills (906, 206), Feats (1224, 206).
- Skills: 120 points, at most 15 per skill at level 1; `+` at x 1164, rows y 352 (Guns), 394, 438, 480, 566, 610,
  696, 738: 15 clicks on each of those 8 (~45 s in one `inp.sh` call).
- Feats: 2 to pick; one click on a row picks it (Conditioning (856, 326), Expertise (856, 406)); then Accept
  (1704, 1198) starts the intro.
- Intro dialogue: "2. Skip intro" (614, 1216); the lab's "Continue." (606, 1312); "[Skip the tutorial]" (1000,
  1352); "Continue." (606, 1176); "Leave." (700, 1176). After Leave the world stayed black with only the HUD:
  that was the console-window focus bug (see "Focus matters"), not the game.

Problems seen on these runs:
- The **unpatched** game opens at config.dat's resolution, not the screen size. That run used the player's
  config.dat (2560x1440) on a 1280x720 screen, and Wine clamped the cursor to the screen, so the bottom/right
  couldn't be clicked. The fresh prefix has no config.dat; if the first screenshot still doesn't fill the screen,
  set the resolution in Options -> Video, or launch at the game's size.
- `key` presses (`0x32`, Escape) didn't register in game, only clicks: the helper's console window had taken focus.
  Fixed (window-less helper, `focus` command). The HUD's system-menu icon (2504, 56 at 1440p) also opens the Escape menu.
- A different test copy (e.g. a stock, unpatched game) works with `TEST_GAME=~/<folder>` on every script call. The
  game is found by its run's prefix, so any folder name works, and `stop.sh` ends that run's gamescope too (it kills
  the whole process group with SIGKILL when headless gamescope ignores SIGTERM).
- Comparing drivers, flicker and seams: see "Checking a rendering bug" below. Follow it; the short version that
  used to be here missed a real bug.

### Intro and tutorial: skip both through dialogue

The game starts with an **intro**, and a **dialogue offers to skip it**; after that comes a **tutorial**, which
**another dialogue option** also skips. Both skips are dialogue options, not buttons or keys.

Dialogue: the speaker's text is above, your options are a numbered list below it. Choose one by clicking its
text (hover first: the option under the cursor is highlighted), or by pressing its number key (`key 0x31` for 1,
`0x32` for 2...). Read the options in a full-size screenshot before choosing; the skip option's wording can sit
anywhere in the list.

## Checking a rendering bug (driver X is broken / fine)

A wrong "it's fine" is worse than no answer: a player reports it, and an upstream report gets corrected in public.
This happened (FNA3D#274): OpenGL was declared fine from one scene and a 20-shot flicker count, but it draws 1-pixel
seams between floor-tile rows in other areas, like Vulkan; D3D11 doesn't. Before saying a driver is fine or broken:

1. **Compare against D3D11 at the same spot, side by side, not against the other suspect driver.** OpenGL and
   Vulkan share FNA3D/MojoShader code paths and can be wrong the same way; D3D11 is the reference (what the game
   looks like on Windows). Run them at once, each in its own run, from the same save:
   ```bash
   CROP=760x480+0+40 $S/compare.sh --saves D3D11 OpenGL Vulkan    # add TEST_GAME=... / driver variables as needed
   ```
   It loads the newest save on each driver, keeps focus, takes full-size shots, stacks the crops top to bottom in
   the order given and diffs each against D3D11. Look at the stacked image; some difference is normal (lights,
   animation), so the numbers only back up what you see. Two drivers can be pixel-identical and still both wrong.
   To check a run really used its driver: in its `game.log` (`KEEP=1` keeps the runs), Vulkan and D3D11 (DXVK)
   runs create `[Gamescope WSI]` swapchains, OpenGL runs don't.
2. **Look at full-size pixels.** Seams, gaps and bleeding are 1 pixel wide: a 50% screenshot hides them. Crop at
   100% (or enlarge a crop with `-scale 200%`), never judge rendering from a scaled-down shot.
3. **More than one scene, and the scenes the player means.** Floors, walls, lit and dark areas, characters, outlines
   (hover), a map after a transition. Static bugs (seams) don't show in a flicker count, and a flicker can be
   absent in a still scene. The intro lab alone is not enough; also load the player's save (`--saves`) where they
   saw the problem, and an area after the tutorial.
4. **Flicker** is a separate check: ~20 full-size shots in a row of a still scene, count changed pixels in the world
   area (numpy over `magick ... rgb:-`); a still scene gives 0. Do it on D3D11 too.
5. **If the control looks wrong too, suspect the test setup first, not the game.** A black world on every driver
   was the helper stealing focus (see "Focus matters"), not "a game state". Anything the player doesn't see on
   their screen is a test problem until shown otherwise; if unsure, ask them what they see there.
6. **A replay of an FNA3D trace (fna3d_replay) only has OpenGL and Vulkan on Linux**, no D3D11: matching each other
   there proves nothing about correctness. Check in game against D3D11 before claiming a driver is fixed.
7. **Report what was checked, not more**: which drivers, which scenes, at what size, and against what. "OpenGL
   matched Vulkan in the lab" is not "OpenGL is fine".

## Testing another build (stock game, an experiment)

The default test copy is whatever the Steam folder holds at `setup.sh` time. Check what that is before testing
(the patched exe leaves `underrail.exe.backup`; the 64-bit patch adds `FNA.dll`, `x64/` and `UnderrailFix64.dll`):
the player or Steam may have changed it since. For anything else, make your own folder and pass
`TEST_GAME=<folder>` to **every** script call (the game is found by its run's prefix, so any folder name works):

- **Stock game** (e.g. "is this a mod bug or the game's?"): copy the Steam folder's files except `data`, which you
  symlink. If it's patched, put the `.backup` files back over `underrail.exe` and the `sfmlnet-*.dll`s, and leave
  out the mod's files. Check the exe's MD5 against the stock one in AGENTS.md. Stock under Proton runs on Wine
  Mono's own 32-bit FNA/FNA3D, not the mod's.
- **Your own build** (e.g. from a worktree): start from a stock copy as above, copy `dist/UnderrailFix/` or
  `dist/UnderrailFix-64bit/` into it, and run its Linux patcher there (`./underrailfix-patcher[-64bit]`). Never
  `./build.sh install` for an experiment: that patches the player's Steam copy.
- **Swapping a native** (e.g. a patched or tracing `FNA3D.dll` in a 64-bit copy's `x64/`): back up the original
  first and put it back when done.

**Environment passes through** `launch.sh`, gamescope and Proton to the game: `FNA3D_FORCE_DRIVER`
(`D3D11`, `OpenGL`, `Vulkan`), Mesa/driver options (Mesa reads its driconf options from same-named environment
variables, e.g. RADV's `radv_disable_trunc_coord=true`), and any switch an experimental build reads with
`Environment.GetEnvironmentVariable`. That makes A/B tests cheap: the same build, two runs, one variable apart.

**Worktree-isolated sessions**: when the session is isolated in a git worktree, its Bash guard refuses to run
scripts (`bash file.sh`, `$S/launch.sh`), and the skill isn't in the worktree anyway. Build in the worktree, then
leave it (ExitWorktree, keep) to run the game, using the built `dist/` from the worktree's path.

## Going deeper: FNA3D traces and minimal test programs

When the question is "why does FNA3D draw this differently", two tools beat staring at the game:

- **FNA3D traces.** Build FNA3D with `-DTRACING_SUPPORT=ON`: it records every call to `FNA3D_Trace.bin` in the
  current folder (the game folder), and `fna3d_replay` plays a trace back with any driver. For the 64-bit copy,
  cross-build a Windows `FNA3D.dll` with mingw-w64: SDL3's headers (the SDL `include/` folder) plus an import
  library made from the shipped `SDL3.dll` (export names from `x86_64-w64-mingw32-objdump -p`, into a `.def`, then
  `x86_64-w64-mingw32-dlltool -d SDL3.def -l libSDL3.dll.a`), passed as `-DSDL3_INCLUDE_DIRS` / `-DSDL3_LIBRARIES`.
  Traces are large (~150 KB a frame), so record only the moments you need.
  - **Run `fna3d_replay` inside headless gamescope** (`setsid gamescope --backend headless -w W -h H -- ...`), never
    on the user's desktop: it opens a window and takes their focus. End its process group when it's done.
  - **A trace cut off by stopping the game has no end marker**, and the stock replay loops forever on it. Make the
    replay's `READ` macro quit when `SDL_ReadIO` returns short.
  - Useful additions to `replay.c`: dump the backbuffer before `SwapBuffers` (`FNA3D_ReadBackbuffer`); log a
    frame's render-target switches, effect parameters (`MatrixTransform` is in `effectData->params` at
    `MARK_APPLYEFFECT`), sampler states and the vertex data of each draw. The real numbers (matrix values, texture
    coordinates, atlas sizes) are what a synthetic repro gets wrong.
  - The replay has no D3D11 on Linux (see 6. above).
- **Minimal FNA programs under Proton.** A few dozen lines of C# compiled with `mcs -target:winexe -platform:x64
  -r:FNA.dll`, next to the FNA.dll and `x64/` natives the 64-bit build ships, run with `proton run` in a throwaway
  copy of the base prefix inside headless gamescope. They test all three drivers with the exact binaries the game
  uses, under your control (encode texel rows in colours, read the target back, print what was sampled). Headless
  gamescope can hang when the program exits: write results to a file, then kill your own process group.
  The game's effects load with FNA's `ContentManager` from the game's `data` folder (`shaders/...`).
  Use the game's real values: a repro with exact numbers (e.g. power-of-two textures) can hide a rounding bug.

## Cleanup and safety

- Always `stop.sh` when done: it ends your run's gamescope with its whole process group (gamescope often exits with
  134 or ignores SIGTERM, and used to leave `gamescopereaper` behind), your sink and your prefix. Never `pkill -f` a
  pattern that's in your own command line, and never kill by name at all (see "Runs").
- Never kill or click into the **player's own** game or another session's: `game_pid` only matches underrail.exe
  running in your run's prefix.
- Saves and config.dat go to the throwaway prefix, never the real one, and `stop.sh` deletes them.
  `setup.sh --refresh-prefix` remakes the base from the real prefix (e.g. after a Proton change).
- The test copy runs what the Steam folder had at `setup.sh` time (standard or 64-bit patch). Re-run
  `setup.sh --refresh-game` after installing another build.
