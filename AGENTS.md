# AGENTS.md: working on UnderRailWorks

Notes for whoever (person or AI agent) works on this next. The game's title is stylized **UnderRail**.

**UnderRailWorks** (this repo) is the engine upgrade and mod framework: the game on .NET 10 and FNA, the mod loader,
the Mods tab, the API (events, readable names, settings), and the engine fixes. **UnderRailFixes** is now a mod on
it (`mods/UnderRailFixes`: Occluded characters). Works started from UnderRailFixes 1.0.8
(github.com/Rhiyo/UnderRailFixes, the released 1.x line, kept separately), so much of the code, and this file below,
still describe the 1.x structure and names (`UnderrailFix*`): see `UnderRailWorks.md` for the framework's design,
decisions and status, and `TODO.md` for the work list. The game knowledge below (obfuscated names, how the game draws,
saves, traps) applies to both.

## What it was (UnderRailFixes 1.x; the engine fixes still work this way)

Display and quality-of-life fixes for UnderRail (Styg, Steam app 250520), distributed as a **patcher**: it backs up the player's own
`underrail.exe` to `underrail.exe.backup` and patches it in place (Mono.Cecil) to call `UnderrailFix.dll`, which
holds all the fix logic. No launch options, no other game files touched, `--uninstall` restores the backup.
Every fix is switched at runtime from `UnderrailFix.ini`, so toggling never needs a re-patch.

Fixes (README.md has the player-facing description):

| Fix | ini | What |
|---|---|---|
| Resolution | `ResolutionFix`, `Width`, `Height`, `SaveScreenshots` | resolution list from the monitor's modes; resolution kept in the ini (default native), config.dat keeps a vanilla value; long dropdowns scroll; no odd aspect labels; save screenshots above 2048 px show the 1080p view instead of the stock top-left crop |
| Zoom | `ZoomFix`, `ZoomDefault`, `ZoomMin`, `ZoomMax` | zoom as a 1080p screen would need it for the same view (numbers match the stock 1080p indicator: start 150, 100-200), wider limits, steps scaled |
| Interface | `UiScaleFix`, `UiScale`, `UiInsetX`, `UiInsetY` | whole-UI scale (auto = whole multiples of the shorter side / 2160), insets that centre the UI while the world stays full-screen |
| Menu backgrounds (Interface group) | `BackgroundFit` (only with `UiScaleFix`) | 0 = the game's crop; 1 = whole image, letterboxed (backgrounds ignore insets either way) |
| DPI | `DpiAware` | Windows only: per-monitor DPI awareness before the window exists |
| Window icon | `WindowIcon` | WM_SETICON with the exe's icon (resource 32512, 64 + 32 px): FNA's SDL window has none, and Wine's Wayland driver (unlike X11) doesn't fall back to the exe's icon |
| Frame rate | `FrameRateFix` (**default off**: not tested thoroughly), `MaxFps` | no fixed 60 Hz timestep (0) or a fixed one at MaxFps |
| Gameplay: Occluded characters | `GameplayFixes`; `ShowCovered` (**default off**), `ShowCoveredPlayer/Hostile/Other` (off, keyhole, lines, dither, fill, outline), `ShowCoveredTransparency`, `KeyholeTransparency`, `KeyholeSize` | characters hidden behind scenery shown where hidden (ShowCovered.cs); see "World drawing" below |
| Log | `Log` | 0 (default) = no log file; 1 = `UnderrailFix.log` |

In game: **Options -> Fixes** tab with a group per enabled fix (Gameplay has the subsection Occluded Characters, its
heading and labels indented, with a Reset to recommended button beside the heading); applies on Accept; scale/inset
changes get a 15 s keep/revert countdown (the game's own dialog). Dropdowns for named choices, the game's tick boxes for
on/off, the game's sliders for amounts (snapping to steps: 25 for zoom, 5 for the rest). The tab is one column and
scrolls above Accept/Cancel (TabScroll); long or low dropdowns open upwards (UnderrailDropdownScroll).

**How fixes are organised (keep to this when adding one):**
- Fixes come in groups (Resolution, Interface, Zoom, Frame rate...). Every group has one on/off switch in the ini
  (`ResolutionFix`, `UiScaleFix`, `ZoomFix`, `FrameRateFix`) that turns the whole group off: with it at 0 the game
  behaves as stock for everything in that group, whatever the group's other settings say.
- A group's other settings only apply while its switch is on, and are written under it in the ini.
- Any option that can be set in game can appear in the Fixes tab, under its group's heading; the group is only shown
  while its switch is on. The switches themselves stay ini-only.

### Why a patcher (alternatives that were tried and fail)

Goal was "drop in, no launch options, delete to remove". Under Proton every no-patch route fails:
- Proxy DLLs (version/dinput8/winmm): need `WINEDLLOVERRIDES`. The exe only imports mscoree.
- `underrail.exe.config` `appDomainManager*` and `APPDOMAIN_MANAGER_*` env vars: ignored by Mono and by Wine Mono (tested).
- Image File Execution Options `Debugger`: ignored by Wine (tested).
- `dllmap` on the game's native `SteamHelper.dll`: Steam-only, and first called after the loading screen is up.
- A launcher exe that patches in memory worked (an earlier version) but needs a Steam launch option.
- Shipping a patched exe = redistributing the game. Don't.

## Repository

```
build.sh                 build into dist/ (and `./build.sh install` patches the local Steam copy)
release.sh               the only way to make a release (see Releasing)
src/Release/             licence texts shipped in the release zip
tools/check-release.sh   automated patcher checks (release.sh runs them)
src/Runtime/*.cs         UnderrailFix.dll: everything the patched game calls
src/Installer/*.cs       UnderrailFix-Patcher.exe: backup + Cecil patches + uninstall; installs the 64-bit mode,
                         or with --32bit the 32-bit mode
src/Build/Retarget.cs    build step: build/ref/underrail.exe, the game exe pointed at our FNA.dll, to compile against
src/Build/GameVersion.cs, ListHooks.cs   release helpers
src/Bundle/mono.config   dllmap for the bundled Linux patcher
src/SteamHost/           UnderrailSteamHost.exe, the 32-bit Steam helper for the 64-bit mode
lib/                     NVorbis.dll and mono-generic/ (fetched by build.sh), fna/ (tools/fetch-fna.sh); not committed
tools/fetch-fna.sh       FNA built from its release + the pinned official native libraries + their licences
tools/gamescope-test.sh  run the game at a virtual resolution (8K, portrait...)
```

Never commit decompiled game code or game files (`.gitignore` blocks exe/dll/xnb).

**Every file the original game can write must always stay readable by the original, unpatched game**, whatever mode
the mod runs in: saves, config.dat, and anything else the stock game itself writes. (The mod's own files, such as
UnderrailFix.ini/.log, are not meant here.) Players uninstall,
Steam restores the stock exe after updates, and the files are shared between Windows, Proton and the
64-bit mode (same Documents folder). The mod keeps its own state in its own files (UnderrailFix.ini); config.dat
keeps vanilla values (ResolutionStore). Saves and config.dat are BinaryFormatter data whose type names include the
assembly that wrote them; the game's binder (`dds` -> `e6e.av3` read / `e6e.av5` write) resolves them, and the stock
one only understands `FNA`/`WineMono.FNA` names for Point, Vector2, Vector3, Color and Matrix. Key bindings (`Keys`,
in config.dat and global.dat) are not among them. So anything that changes which assembly the game's types come from
must write the stock XNA 4.0 names (FNA mode does this in `FnaMode.SavedName`). After a change in this area, check
the files with a script that lists the assembly names in the gzip'd data.

### Building (Linux)

Needs: `mono` + `mcs` + `mkbundle` (Arch: `mono`), Mono.Cecil 0.11 in the GAC
(`/usr/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/`), a stock `underrail.exe` (the build compiles against
the Steam copy, or its `.backup`); FNA comes from `tools/fetch-fna.sh` (lib/fna). Paths are at the top of build.sh.

Output `dist/UnderrailFix/`: `UnderrailFix.dll`, `UnderrailFix-Patcher.exe` + `Mono.Cecil.dll` (Windows, .NET
Framework 4), `underrailfix-patcher` + `linux/` (Linux: mkbundle'd Mono, needs glibc >= 2.29, no AVX), README.md.
The same folder holds the 64-bit mode's files: `UnderrailFix64.dll` (made by `Retarget.cs --fna`), `FNA.dll`, `x64/`
(FNA3D, FAudio, SDL3), `NVorbis.dll`, `UnderrailSteamHost.exe`, and `UnderrailFix-Patcher-32bit.bat` (Windows: the
patcher with `--32bit`). The Linux patcher's launch script sets `TERM=dumb` (Mono 6.12 crashes on newer terminfo
files, mono/mono#21862).

### Releasing

Always through `./release.sh X.Y.Z "description"` (or `-F notes.md`), never by hand, so every release is made the
same way. It refuses to run unless on `main`, clean, in sync with origin, with a new version and UnderRail closed.
Then it stamps `FixVersion.Value` (src/Runtime/Version.cs; committed only after everything passed), builds from
scratch, runs `tools/check-release.sh`, packages `UnderRailFixes-X.Y.Z.zip` (the dist folder + LICENSE.txt +
THIRD-PARTY-NOTICES.txt from src/Release/ and lib/fna/licenses/), writes the notes (description + game version read from the exe by
src/Build/GameVersion.cs + Steam build + install steps + SHA-256), tags `vX.Y.Z`, pushes and runs `gh release create`.

Release lines: `main` holds everything. When `main` has work that shouldn't ship yet, a fix for the current release
line goes on `main` first, then is cherry-picked onto `release/X.Y` (branched from the last tag) and released from
there: v1.0.2 was released from `release/1.0` (v1.0.1 + the insets clip fix). From v1.0.3 to v1.0.7 the 64-bit mode
was its own experimental download; since v1.0.8 there's one download, with the 64-bit mode as the patcher's default.
`release.sh` accepts `main` and `release/*`.

`tools/check-release.sh` (also usable after any `./build.sh`) works on a temporary copy of the game folder: the Linux
patcher with Mono hidden (install, re-run gives the same exe, simulated game update, uninstall restores the original
byte for byte), the exact set of hooks in the patched exe (src/Build/ListHooks.cs; update its EXPECTED list when hooks
are added or removed), and the Windows patcher producing the identical exe. No CI: building needs the real game.

If a setting's **meaning** changed in the release, bump `FixSettings.Version` and add the key to `Reset`
(Settings.cs) first: older ini files are then regenerated with current comments, other values kept, those keys reset.

## How the game is put together (what we learned)

**Built against UnderRail 1.3.1.2** (`dm9.d`, shown on the main menu), Steam build 21973456 (Feb 2026), original
`underrail.exe` MD5 `0046fd2c81955dff092f2cf00d1f60f8`. All obfuscated names below are from that build. After a game
update, run the patcher on a copy first: an `Expect` failure names the patch point that moved.

- .NET 4 / XNA 4.0, 32-bit with LARGE_ADDRESS_AWARE. Obfuscated: types and members are names like `t1`, `efw`,
  `a`, `b`; they can change between game versions. Decompile with `ilspycmd` (into a scratch folder, never the repo).
- **Under Proton it runs on Wine Mono 11.x, which replaces XNA with FNA/FNA3D/SDL3.** On Windows it's Microsoft XNA.
- Native DLLs it P/Invokes: `SteamHelper.dll` (9 functions, Styg's Steam wrapper; also `IsRunningOnWine`), user32.
- Every patch point is checked with `Expect(what, found, expected)` in the patcher; a different game version fails
  before anything is written. Current counts: zoom limits 3, loading-screen size reads 2, SpriteBatch.Begin 2,
  scissor reads/writes 11, Mouse.GetState 7, world projection 1, zoom label 1, zoom wheel step 1, zoom buttons 2,
  world view sizing 1, shake moves 2, shake check 1, floor at() 1.

### Resolutions and settings

- `TimelapseVertigo.Common.Settings.DisplayResolution` enum (`r1024x768=5` ... `r3840x2160=200`, all < 1000).
  `efw`'s static ctor parses the names into `Dictionary<DisplayResolution, egd>` (`efw.m_a`); `efw.a()` lists,
  `efw.a(key)` looks up (stock throws on unknown keys).
- `egd`: `a` width, `b` height, `c` InterfaceElementSize2, `d` key, `e` System.Drawing.Point aspect, `f` Auto's
  UiSize, `g` largest allowed UiSize, `h` fixed zoom. Tiers by height: UiSize Huge > 1200, Small <= 800 or w < 1200,
  else Large; zoom 2 above 1440, 1.5 for 1080-1440, else 1. `egd.ToString` shows the ratio only if it reduces to <= 20:x.
- Our extra modes use synthetic keys `width << 16 | height` (in memory only).
- config.dat: 24-byte header + gzip'd BinaryFormatter. Video settings class `cnt` (keys `VS:R` resolution, `VS:D`
  mode, `VS:S` shader, `VS:M` sprite target mask...). `cnt`'s default ctor runs on every settings **copy**, so the
  resolution hook there is `ResolutionStore.Default`, and only the `VS:R` unbox in the deserializing ctor is `Load`.
- `ls.a()` **re-reads config.dat from disk**. Never call it per frame; the live settings are
  `bpo.GetService<cnk>().aq1()` (cnk is implemented by d6f).
- Options screen `t1`: tabs built once in `t1.h()`; per tab `s = page; rows...; f(); g();` (`s` current page,
  `u` tab control `dpg`, `t` dropdowns added last). Row builder `a(string label, euz control, euz, bool, bool, int,
  bool)`, heading `a(string)`, separator `a(bool)`, styled dropdown `CreateComboBoxFromEnum<T>`. Accept:
  `t1.a(object, b25)` -> `d()` (writes options) -> `cnk.arb(ls)` -> `d6f.a(ls)` applies; when resolution/UI size
  changed the game re-lays-out with `bl9.b(); bpo.GetService<ps>().anc();` (in game: new `ci3`, map kept).
- Keep/revert dialog: `bnr.a(esj size, eyp callback, title, text, null, defaultButton, Color?, Color?, ms, cpk[] buttons, owner, owner)`;
  `eyp.g0(string button)`; on timeout the default button is passed.
- Sprite Target Mask (`VS:M`): pixel hit-testing from mask files instead of GPU readbacks; the game only turns it on
  under Wine for fresh configs. Keep it on.

### Drawing, input, layout

- One draw context `cp9` (SpriteBatch `Begin(..., PointClamp, ...)`), drawing calls through `ob` (`bc2` wraps
  SpriteBatch, `drl` = the world's `UrGenericSpriteBatchBase`, which builds its own pixel-to-clip `MatrixTransform`
  from the viewport and passes `TargetSize`).
- **The world view is a UI control**: `mv : a7m : dnn : euz`, a child of the in-game surface `ci3` (`daj`), sized by
  `a2x.c(m_t)`. Screen shake moves it in `ci3.iv` (and resets it when `Location != Point.Zero`, every frame).
- UI screen size comes from `bef.k0()` (back buffer) / `bef.k4()` (requested), implemented by `tb`.
- Hit-testing is hierarchical (`euz.a(MouseState)`): children are only tried if the parent's `iz()` accepts;
  `daj.iz` accepts 0,0 + k4.
- `a7m.b(int,int)` (mouse -> floor tile) subtracts `at()` from the mouse but compares with tiles projected in screen
  coordinates (`m_b` includes `at()`): a stock bug that is harmless while the view is at 0,0.
- Cursor warps: `bso.abb(Point)` -> Win32 `SetCursorPos` (right-drag camera re-centres the cursor every frame).
- Menu backgrounds: `czo.a(esj size)` picks the concept art closest to the size's aspect (several ship), crops it
  from the top-left (`bg1.d`), places it at 0,0 (main menu `a8k`, options `t1`, `iw`, `c9v` pass k0).
- Popups dim the screen with `d0p.a(cp9, Color, Rectangle(0,0,k0), ...)` (Escape menu `cef`, `bnr`, `tg`, `e4u`, `pz`).
- Off-screen render targets exist (e.g. `by8` lights); the screenshot path renders the UI into a target with a
  screen-sized viewport. Hence UI mapping applies only while the viewport is back-buffer-sized.
- Interface sizes are hand-made per tier: atlases `gui/ui_s|l|h_gen1`, cursors `cursors_s|l|h`, bitmap fonts in
  `data/fonts`, ~330 lines of pixel layout per tier in `wj`. No continuous scale exists in the game.

### World drawing (Show Covered, the keyhole)

- (History: UnderRailWorks runs only on our FNA, which doesn't enforce Reach; the Reach rules below were for
  UnderRailFixes 1.x's Microsoft XNA path and no longer apply to Works or its mods.)
- **Windows / Microsoft XNA uses the Reach profile and enforces it** (the game sets `GraphicsProfile` 0 = Reach in
  `tb`; FNA doesn't enforce it): textures and render targets
  <= 2048 a side, wrap addressing only on power-of-two textures, no separate alpha blend (colour and alpha factors
  must match). Anything drawn by us must keep to it; screen-sized targets only where `ShowCovered.Fits`. Save
  screenshots above 2048 are drawn straight into a 1920x1080 target (`SaveScreenshot.Target`; UiScaleFix maps the
  screen's centre into it while it is bound), so they never need a large one. XNA (D3D9) also refuses a render target that is
  still bound as a texture: clear `Textures[0]` first.
- Our drawing never leaves the game's state broken: batches go through `ShowCovered.BeginGame/EndGame/BeginOwn`
  so `CloseOpen` can end them, render targets/viewport/scissor are restored in `finally`, the keyhole always puts the
  screen back as the target (`Keyhole.Abort`), and any exception sets `ShowCovered.Broken` (off until restart, one log
  line).
- Cost: only scenery (not floors, not characters) counts as cover; every pass is limited to the creature's area;
  settings are re-read only when `FixSettings.Changes` moves. Render targets: the fills/outlines' shared mask + work
  (2 screen-sized, PreserveContents, only where `Fits`), the keyhole's front picture (screen-sized, or tiles) and its
  circle-sized snapshot/behind/mask. The keyhole's front pass only runs on frames where someone was covered the frame
  before, starting at the first such target.
- **The keyhole** (Keyhole in ShowCovered.cs; the class comment has the details):
  - Targets: characters the game would outline (`ShowCovered.HasOutline`; stats alone took explosive barrels) with the
    Keyhole effect. Covered = standing scenery drawn after the target over its body, noted in `Draw` every frame
    (keyed by entity `bym.b`: a creature's `bym` is new every frame) and used the next frame; other frames cost nothing.
    Holes ease in/out over 0.2 s.
  - Split: the world pass continues into a front picture from the first layer after a covered target, or earlier, from
    the first standing piece in front of last frame's hole. "In front" = inside its circle and at least the target's
    square on both map axes (`b0b.d` X and Y, each against `Math.Floor` of the target's): a walking character is drawn
    after the wall piece on the square it's leaving. X + Y alone also took pieces diagonally beside it (a wall running
    south-west to north-east faded when walking along its front). Only scenery drawn after the target opens a hole. One screen-sized target (the code keeps a list of
    "tiles" from the XNA days; there is always one).
  - Floor after the split near last frame's holes goes onto the screen as the game draws it (`AlsoOnScreen`, no depth
    test), with its tile's extras, and only erases in the front picture (`EraseInFront`, Cut blend): drawn normally into
    both, see-through floor (transition red) counted twice. It is never redrawn later. At WorldDone, per hole: cover = standing pieces in the circle in front of the target; behind =
    everything else in the circle since the split, redrawn in order with the tint it was drawn with; mask = cover
    silhouettes (x4, so soft edges count fully) x hole shape x fade; front = front x (1 - mask) + behind x mask (Cut,
    then Additive); then the front picture onto the screen.
  - **Tried and dropped (Nov 2026), don't redo:** a hole-area-only front picture (seams, bands, notches, sliced walls,
    dark tiles, holes switched off past 2048); cutting scenery behind the target by sprite rectangles; chaining
    overlapping pieces to "fade as one" (bounding rectangles are coarse: walls far away faded); redrawing floor at the
    end of the frame (dark/black tiles); blending behind onto the screen under the front (dark ring); hooking the
    game's visibility checks (didn't matter). See "The keyhole walking-floor bug" below.
- `a7m`'s world pass: `iu(cp9, ...)` starts the world batch (`mv.iu`: `drl` with the lighting shader, LocaleOmniShader
  or the night-vision/camera ones; plain bc2 batch without Advanced shaders), then tiles in isometric order with the
  entities on them (`vp`), each layer through `a7m.a(cp9, a2f, bool shaderPass, bkx)` -> `a2f.f3` -> `chf.avw`. So
  whatever is drawn after a creature is in front of it. After the loop: `cp9.a()` (end), `SetRenderTarget(null)`,
  then `a7m.b(cp9)`: layers with their own shader (`a2f.u`, e.g. "Outline") collected in `a7m.x` and drawn after the
  world, which is why Tab outlines show through walls.
- `cp9`: `b` the plain SpriteBatch, `f` bc2 (wraps it), `g` an override batch (the world's drl while set); `b()` is
  `g ?? f`; `a()` ends the active one; `a(RasterizerState)` begins the plain one; `a(ob)` sets g.
- `a2f` (a layer): `m` where it is drawn (layout units), `c` its part of the texture, `d` its texture source (`chf`;
  `by8` = a layered character composited into a cached render target, rebuilt on demand, `e4m` = a plain sprite),
  `e` tint, `i` rotation, `k` flip, `n` its owner `bym` (`n.b` the entity), `a` its id ("SHD" shadow, "marker",
  "shield"...), `q` the fade flag (`f6.f4` multiplies by Unobscuration alpha), `u` shader name, `v` shader params
  ("OutlineColor"). `a2f.f5` copies drop `u`/`v`/`n`.
- Entities `b0b`: `f` IsFloor, `g` IsGround (drawn in the same order as everything else: many floor tiles around a
  character come **after** it). Walls, props and creatures are all `c5x`; characters are the ones with the `amc`
  aspect (`amc.aa()` -> `efk`). `c5x.IsPlayer`. Attitude: `efk.a(cnk.aqu().ac(), asp.a)` -> `h`: a hostile (red),
  b unfriendly (orange), c neutral (grey), d friendly (green), e ally (blue) (`adi.ae`); `efk.Passive`.
- Creature outlines (`eg0`, the method with ldstr "Outline"): built only when the view takes input (`mv.w`, off during
  zone transitions), Character Outlines isn't Never, and (combat + Always in combat) or hovered or Tab / reverse
  highlight held; colour (corrected Nov 2026, from eg0): gold for the player's character (data `e3q`), white for the
  hovered or targeted one, sandy brown for passive ones, `adi.ae[attitude]` otherwise (Works: `Entities.OutlineColor`). The layer goes into the
  creature's `bym` with `bym.a(a2f)`.
- The world view's zoom: `dnn.y` (a field that shares its name). Unobscuration alpha: `cnk.aq1().b().i()`.
- Render targets: `by8` caches save and restore the current target and restart the world batch with `iu` (the
  keyhole switches to its front picture the same way). **FNA clears a RenderTargetUsage.DiscardContents target whenever it is set
  again**: anything switched away from and back to must be PreserveContents.

### Options window (t1)

- Rows: `a(label, control, extra, centre, wide, offset, tag)` (dropdown rows use wide labels: control at 2.5 x the label
  width, else 1 x); heading `a(string)`; separator `a(bool)`; slider `a(key, label, value 0-1, min, max, small, large,
  percent, changed, text, wide)` into `t1.a9[key]` (`oz`, `l()` value, `e(int)` set, `f(handler)` changed); tick box
  `b(bool)` -> `cao` (`Checked`); button style `static a(dak, wj)`; `dak.c(handler)` click, `e4z.j(button)` its sound.
  A new column starts when a row passes the page bottom (`r + h > s.Size.d - m_d`), so our page is made tall while
  building. Cancel is `t1.ac`, Accept `t1.ab`, both laid over the tab area's bottom.
- Dropdown `aw9`: `a()` opens (rows `zt` below its header), `b()` closes; header = its `dak` field (child at 0,0).
  Controls draw in their parent's list order (`euz.p`, guarded by `euz.q`); text labels `avo`/`d7w` (`d7w.g()` font
  name; fonts via `dr3.a().GetResource<SpriteFont>(name)`).

### Timing

- The game never sets `IsFixedTimeStep`/`TargetElapsedTime`, so XNA's default fixed 1/60 s caps it at 60 FPS. Its
  systems take elapsed time from `GameTime` (passed down via `e6d`; e.g. `d6f.d1` -> `es6` world ticks), nothing reads
  `TargetElapsedTime`, and in game it plays at normal speed at 240 FPS (frame-counting logic would have run 4x fast), so a
  variable timestep is safe. Not yet tried: a 30 FPS cap (would show frame-bound logic as half speed).
- VSync is the game's option `VS:V` -> `GraphicsDeviceManager.SynchronizeWithVerticalRetrace` (`tb.k7`).

### Zoom

- `dka.b` (per frame) sets the camera limits: `ud(1f)` min, `uf(2f)` and `uf(3f)` when the view is wider than 1920.
  Camera `a3u`: `h` zoom, `d` min, `e` max; `uq` clamps.
- Current zoom stored in `d6f` (`aq8` get, `aq9` set, `ar1(float, bool)`), started from `egd.h`.
- Indicator: `b5r` shows `aq8() * 100 + "%"`. SHIFT+wheel: `mv` calls `an9().uo(notches * e.b)` with `e.b = 0.25`
  (set by ci3); buttons: `ci3` `aq9(aq8() -/+ 0.25f)`. Stock 1080p: starts 150%, 100-200%, 25 per step.
- The visible tile range accounts for zoom (`a7m`: Size / (tile * 2 * zoom)), so zooming out below 1x just draws more.

## Our hooks (src/Installer/Installer.cs)

| Game | Hook | Purpose |
|---|---|---|
| Game subclass ctor (`enu`, first `newobj` in Main) | `FrameRate.Apply(object)` | IsFixedTimeStep / TargetElapsedTime |
| same, after it | `WindowIcon.Apply(object)` | window icon (the window exists once Game's ctor ran) |
| entry `Main` (`b7i.a`) | `FixStartup.Run(version)` | version check, DPI awareness. **Signature must never change.** |
| `cnt` ctors / `GetObjectData` | `ResolutionStore.Default/Load/Save` | resolution via ini; config.dat stays vanilla-valid |
| `efw.a()` / `efw.a(key)` | `UnderrailDisplayModes.List/Get` | monitor modes, nearest-size fallback |
| `egd.ToString` | `UnderrailDisplayModes.Label` | no aspect suffix (stock when off) |
| `aw9.a()` / `aw9.b()` (dropdown open/close) | `UnderrailDropdownScroll.Opened/Closed` | fit list in tab page, game's `dg8` scrollbar |
| `dka.b` `ud(1f)/uf(2f)/uf(3f)` | `MinZoom/MaxZoom` | zoom limits |
| `b5r` `aq8()` | `ShownZoom` | indicator in 1080p-equivalent % |
| `mv` `uo(step)`, `ci3` `aq8() -/+ 0.25` | `ZoomStep` | 25 points per step on any screen |
| `tb.k0/k4` | `UiScaleFix.Virtual` | layout size = interface area / scale |
| `tb.Draw` PreferredBackBuffer reads | `VirtualWidth/Height` | loading screen |
| `SpriteBatch.Begin` (cp9, bc2) | `Begin5/Begin6` | UI-to-pixel transform |
| world batch `MatrixTransform` | `Transform` | same for the world renderer |
| `GraphicsDevice.ScissorRectangle` get/set | `GetScissor/SetScissor` | clip rects |
| `Mouse.GetState()` | `GetMouse` | mouse in UI coordinates |
| `bso.abb` | `ToScreen` | cursor warp in pixels |
| `ci3.a` `a2x.c(m_t)` | `FullScreen` | world view stays full-screen with insets |
| `ci3.iv` Location set / get | `WorldLocation` / `WorldLocationGet` | shake relative to the screen |
| `daj.iz` | `InScreen` | world clickable in inset margins |
| `a7m.b` `at()` | `FloorOrigin` | floor clicks with insets (the stock bug above) |
| `d0p.a(cp9, Color, Rectangle, bool, bool)` | `DimRect` | full-screen dims with insets |
| `czo.a(esj)` -> `bg1` (start / return) | `BackgroundSize` / `BackgroundPlace` | menu backgrounds ignore insets; `BackgroundFit=1` shows the whole image (letterboxed) |
| `bg1.iv` first Rectangle (draw target) | `BackgroundDraw` | the picture moves, not the control: the main menu panel is a **child** of the background |
| end of `t1.h` / `t1.d` / `d6f.a(ls)` | `UnderrailOptions.AddTab/Save/Applied` | Fixes tab, save, live apply + confirm |
| `tb.Draw` `ldc.i4 2048` (x2), `new RenderTarget2D`, `stfld x` | `SaveScreenshot.Limit` / `Target` / `Taken` | save screenshot: the screen's centre drawn straight into a 1080p picture (the stock code crops to the top-left 2048 px) |
| `eg0` creature outline: the `mv.w` read, the reverse-highlight `IsKeyDown`, the `bym.a(outline)` | `ShowCovered.Interactive` / `Reverse` / `Add` | outline layers built for every creature Tab would outline (and during transitions), marked as ours |
| start of `a7m.a(cp9, a2f, bool, bkx)` | `ShowCovered.Drawn` | world layers in drawing order; keyhole targets, the split |
| `a7m.a`'s `a2f.f3(cp9, bkx)` call | `ShowCovered.DrawLayer` | after the split, into the keyhole's front picture (every tile it reaches), floor also onto the screen; covered marking |
| `a7m.b(cp9, ak1)` call (each tile's extras after its floor: the overlay `ob.w7`, `cgr.tu` tile renderers; not layers) | `ShowCovered.TileExtras` | after the split, into the front tiles that floor reached, and onto the screen |
| `a7m`'s `SetRenderTarget(null)` after the world loop | `ShowCovered.WorldDone` | keyhole holes, then the front picture onto the screen |
| start / end of `a7m.b(cp9)` (shader pass) | `ShowCovered.BeforePass` / `AfterPass` | our outlines out of the game's pass; fills/outlines on the hidden part after it |

64-bit mode only (`Patch64`):

| Game | Hook | Purpose |
|---|---|---|
| SFML `Music` uses in `bxt` (10) | `FixMusic.*` | NVorbis + DynamicSoundEffectInstance in 64-bit |
| `efl.SteamHelper_*` (11 call sites) | `SteamBridge.*` | 32-bit helper process |
| `enu`: `new axn()` (GOG Galaxy manager, only created when Galaxy.dll exists) | `GalaxyBridge` (implements `acv`) | Galaxy run by the same helper |
| `tb`: `(Form)Control.FromHandle(Window.Handle)` (4) + Form members | `FnaWindow.*` | FNA's window is no WinForms Form |
| end of `tb.lg()` | `FnaWindow.Hwnd` | SDL window -> HWND for `bso`'s ClientToScreen |
| start of `e6e.av3` / end of `e6e.av5` | `FnaMode.SaveTypeName` / `SavedName` | saves read any XNA/FNA name, write XNA names |
| `be3` (game formatter) `Assembly.FullName` / `Type.FullName` | `FnaMode.WrittenAssembly` / `WrittenTypeName` | same for its fallback naming |
| all type references | (Cecil) `ToFna` | XNA 4.0 assemblies -> FNA |

## The keyhole walking-floor bug (Nov 2026): what happened, and how not to repeat it

**Symptom (as the player described it, accurately, from the start):** walking along a wall with a keyhole open, one
floor tile next to the character (first the one south-west of it, later the one it was walking onto) stayed dark or
occluded while moving and only showed properly once the character stopped. Its neighbours were fine. When something in
the code changed, the bad tile moved by exactly one tile.

**The actual causes (a chain, each fix exposing the next):**
1. A walking character is drawn on the tile it walks to, so later in the game's order than when standing. The wall
   piece on the row it's leaving is then drawn *before* it. The keyhole only cut scenery drawn after the target and
   that piece stayed solid. Fix: a piece drawn before the target counts as in front if its map depth (b0b.d X + Y)
   is at least the target's row (`Math.Floor`), and the split starts early, at the first such piece next to last
   frame's hole (`InFrontOfFeet`, `lastNear`).
2. With the split starting before the target, floor drawn between the split and the target lives only in the front
   picture. A cut piece drawn under it cut it away too, and "behind" (an end-of-frame redraw of floor) came out
   dark. Fix: every floor layer (and its tile's extras, `TileExtras`) is also drawn onto the screen at the moment
   the game draws it (`AlsoOnScreen`), and floor is never redrawn into behind. The hole then shows the game's own
   floor from the screen.
3. `AlsoOnScreen` was first gated on `Targets.Count > 0`. Floor drawn after an early split but before the target
   failed that, so the tile went pure black. The sampled colour (1,1,2) was the screen's clear colour, which means
   nothing was drawn there. Fix: no gate. (Screen draws also skip the depth test, `DepthStencilState.None`.)

**Why it took most of a night, and the rules that follow:**
- **I guessed instead of measuring.** Lighting, game visibility/fog of war, shaders, tile tints, blend formulas,
  "the game draws it darker", "it's normal, v1.0.7 does it too". Each guess got a build, the player tested it, and
  it was wrong. Four visibility hooks were added and removed again for nothing.
- **I dismissed the player's description.** It said "only while moving, at the hole's edge, moves one tile when you
  change things". That is draw order, not lighting. Several times I answered "it's the game's lighting/colour" and
  asked to leave it. Don't call something "the game" without proving it with the fix's switch off, on the same
  build.
- **What actually found it:**
  - a per-frame log of draw order: each layer's index vs the target's last layer vs the split index, plus map
    depth and whether it was cut;
  - pixel captures of the front picture, the screen before the composite, and the final screen, taken only on
    walking frames;
  - sampling the exact RGB of the bad pixels in the player's screenshot (pure clear colour = nothing drawn;
    darker = drawn wrong).
- **Rules:**
  - If a glitch depends on moving vs standing, log draw order first.
  - If something is black, sample the pixel before theorising.
  - Compare with v1.0.7 to *locate* a bug, never to dismiss it.
  - When the player says "it's not that", stop defending the theory.
  - Change one thing per build and say what each build is testing.

## Traps (each of these cost time)

- **Obfuscated property accessors**: `euz.Location`'s setter is named `g`. Find accessors through the
  `PropertyDefinition` (Cecil) or `GetProperty` (reflection), never by `set_Name`.
- **Field/method name clashes**: `esj` (size) has fields `c`/`d` and methods `c()`/`d(...)`; C# can't pick, so the
  runtime reads those fields by reflection.
- **XNA type identity**: Works and mods compile against `build/ref/underrail.exe` (the game exe pointed at our FNA)
  and `FNA.dll`, the way the patched game runs. (Before, against Wine Mono's FNA and the stock exe, game members with
  XNA types in their signatures didn't compile (CS0012) and were called by reflection; some of that reflection is
  still in the runtime and now optional.)
- Cecil drops IMAGE_FILE_LARGE_ADDRESS_AWARE; the patcher copies the original COFF characteristics.
- An exe-loading launcher must not touch game types before loading the patched copy (the stock exe would load).
- **The Linux patcher must bundle a baseline x86-64 Mono runtime, not the build machine's**: this machine's mono is
  CachyOS's x86-64-v4 build, and bundling it shipped AVX-512 in v1.0.0-1.0.6 (crashed on Zen 3 and most CPUs).
  build.sh bundles `lib/mono-generic`: `mono-sgen` + `libmono-native.so` from the Mono project's Ubuntu 20.04
  packages (6.12.0.200, hash-pinned; baseline x86-64 and glibc 2.29, where Arch's packages need 2.38), with the
  class libraries from the local Mono (`mkbundle --simple --runtime ... --i18n none -L /usr/lib/mono/4.5 -L .../Facades`).
  check-release.sh fails on any ymm/zmm instruction. Shipped natives otherwise: FNA3D/FAudio have no AVX, SDL3 has
  AVX2 paths behind its own CPU detection; all .NET code is JIT-compiled on the player's CPU.
- `mkbundle --simple` can't load embedded native libraries: `libmono-native.so` ships next to the binary, with a
  wrapper script setting `LD_LIBRARY_PATH` and `src/Bundle/mono.config` mapping `System.Native`.
- Under zsh, `$VAR` with spaces doesn't word-split; use bash for test loops.
- **Never `pkill -f` a pattern that appears in your own command line**: it kills your shell. Kill by PID.
- Process names are cut to 15 chars (`underrailfix.ex`).
- **Never look XNA types up by name** (`Type.GetType("...GraphicsAdapter, Microsoft.Xna.Framework.Graphics...")`):
  in the 64-bit FNA process that loads real XNA (Windows) or Wine Mono's second FNA and SDL (Proton). Use `typeof`;
  the build points the reference at the right assembly for each download.
- **Arch's Mono `mcs` puts some types in mscorlib that .NET Framework keeps elsewhere** (`Queue<T>` is in System.dll).
  FNA built with plain `mcs` failed on Wine Mono (TypeLoadException) and would on .NET; `fetch-fna.sh` builds it
  against `/usr/lib/mono/4.8-api`. Our own DLLs happen to be fine; check new code with a reference-mscorlib diff if
  in doubt.
- Cecil can't rewrite an assembly inside the bundled Linux patcher when that needs resolving mscorlib (e.g. enum
  constants): it has no mscorlib on disk. Do such rewrites at build time (that's why `UnderrailFix64.dll` is built,
  not made by the patcher).
- **Test launches from Steam, not just `proton` directly**: Steam runs the game inside the Steam Linux Runtime
  container (`SteamLinuxRuntime_4/_v2-entry-point --verb=waitforexitandrun -- proton ...`). There the 64-bit Wine
  Mono can't load .NET config ("Couldn't impersonate token"), which killed the game at its first `Trace.WriteLine`
  (`FnaMode.DiagnosticsSettings` works around it); outside the container it worked. The game's console output from
  a Steam launch ends up in `~/.local/share/Steam/logs/console-linux.txt`.
- **Walls and props are `c5x` like creatures**: anything per-character must check for the `amc` aspect (once every
  wall counted as a keyhole target: 1,128 "targets" a frame).
- **Floor tiles are drawn after characters** in the world order: anything that cuts "what's drawn after the character"
  must keep floors (`b0b.IsFloor`/`IsGround`) out of it, or holes show black.
- **Don't redraw characters**: their `by8` composite rebuilds its cached render target on every draw (an earlier keyhole
  did, and lagged massively with two keyholes); the keyhole redraws only floors and silhouettes, and a second tile
  gets a character from its cached sprite (`avw` without `avv`).
- **The game's static helpers**: `GetMethod` with `BindingFlags.Instance | NonPublic` misses static ones (`t1.a(dak,
  wj)`); a null MethodInfo there silently dropped the rest of the Gameplay rows.
- More field/method name clashes, read by reflection: `b0b.ac`/`ad` (scale), `a2f.a` (id), `dnn.y` (zoom),
  `bym.a`; mcs crashes (CS0584) or picks wrongly on some of them.
- The test copy and the Steam game share one Proton prefix, so **a test build writes the player's real config.dat
  and saves**. Check what a change writes before testing it there (see the rule on files the game writes).
- **A fresh config.dat is not the player's**: test runs start without one, and the game then had Sprite Target Mask
  off (its Wine check never fires), so the world's hover hit test read pixels back from the GPU: 10-30 ms per update,
  lost key presses at 120 FPS. Works now turns the mask on for new configs (ConfigDefaults). When input or timing looks
  wrong, measure update times first (UnderRailWorks.md, "Lost key presses at 120 FPS"), and try the player's config
  (`launch.sh --config`).

## Testing

- Offline: build, then run the patcher on a **copy** of the game folder (install / re-run / game update / uninstall
  -> byte-identical). Runtime rules without XNA (ScaleRules, ResolutionStore, settings migration) can be called by
  reflection from a tiny mono program in that folder. Hide Mono (`bwrap --tmpfs /usr/lib/mono --tmpfs /etc/mono`)
  to prove the Linux patcher is self-contained.
- In game, with `Log=1` in the ini (off by default; `Log.Always` is only used for the version-mismatch exit):
  `UnderrailFix.log` logs resolution decisions, display modes, "interface: ..." lines once scaling/insets
  are active, option changes, and errors.
- Other resolutions: `tools/gamescope-test.sh 7680 4320` (8K), `3840 2160`, `2160 3840` (portrait). Compare
  screenshots: 4K and 8K should look identical.
- Done: 5120x1440 native, 4K and 8K (UI 200%, right-drag fixed), insets (floor clicks, edge clicks, Escape dim),
  options tab basics, keep/revert countdown, zoom steps (wheel and buttons equal), save screenshots (1080p view; since the direct 1080p render: 64-bit at 5120x1440),
  window icon under Wayland, Occluded characters in the 64-bit build under Proton (all effects, keyholes for several
  characters, transitions, Tab), the Fixes tab's sliders/tick boxes/scrolling/upward dropdowns.
- Not yet: Occluded characters on real Windows (.NET 10 + FNA), its performance with many keyholes on a slower GPU, the Frame rate cap dropdown in game, the latest background change in game (picture drawn full-screen/letterboxed while the main menu's
  panel stays inset), portrait, the Windows patcher and `DpiAware` on real Windows, the keep/revert countdown in all paths,
  older glibc distros for the Linux patcher, the GOG version, the Expedition / Heavy Duty expansions.

## Open ideas

- **64-bit mode (the patcher's default since v1.0.8; its own experimental download in v1.0.3-v1.0.7):** see the hooks
  above. How it works:
  - Microsoft XNA is 32-bit only, so the 64-bit mode points underrail.exe (Cecil `ToFna`) and a copy of the runtime
    (`UnderrailFix64.dll`, assembly `UnderrailFix64`, made at build time) at our `FNA.dll` (FNA 26.08, the version
    Wine Mono 11.3 ships, built by `tools/fetch-fna.sh`). The same files run on Windows (.NET Framework) and under
    Proton (Wine Mono; Wine Mono's own XNA remapping is then never used). `FnaMode.Startup` adds `x64\` to the DLL
    search path (natives can't sit in the game folder: a 32-bit Wine Mono FNA would load them) and sets
    `FNA3D_FORCE_DRIVER=D3D11` unless set (the SDL_GPU flicker, FNA3D#274).
  - It clears 32BITREQUIRED on underrail.exe (Cecil) and, by flipping the CLI header flag bytes in place
    (`ClearClr32Bit`; strong names aren't checked), on the game's `sfmlnet-audio-2.dll`/`sfmlnet-window-2.dll`
    (backed up to `.backup`, restored by `--uninstall` and by installing the 32-bit mode).
  - SFML is only used for streamed music (`bxt`: Music ctor/Loop/Volume/Pitch/Status/Play/Stop/Dispose, 10 calls).
    The patch turns bxt's Music field into object and sends the calls to `FixMusic` (Music.cs): real SFML in a 32-bit
    process, otherwise `OggMusic` = NVorbis 0.8.6 (Ms-PL, no deps; fetched by build.sh, hash-checked) decoding the
    `.ogg` into XNA `DynamicSoundEffectInstance`. **Trap:** UnderRail's `Update` never calls the base `Game.Update`,
    so FNA's `FrameworkDispatcher.Update` never runs: no `BufferNeeded`, and `PendingBufferCount` never drops (FNA
    only collects finished buffers in the instance's internal `Update()`). A background thread calls that internal
    `Update()` by reflection and keeps ~3 x 0.1 s buffers queued. Sound effects are unaffected (fire-and-forget).
  - Steam: the game's 32-bit `SteamHelper.dll`/`steam_api.dll` can't load in 64-bit and `steam_api64.dll` may only be
    redistributed by Steamworks partners, so `SteamBridge` sends the 9 `SteamHelper_*` calls (11 call sites, incl. 2
    in `tb.Initialize`) to `UnderrailSteamHost.exe`, a 32-bit helper process loading the game's own SteamHelper.dll,
    over stdin/stdout. `IsRunningOnWine`/`IsOnSteamDeck` aren't exported by the game's SteamHelper.dll
    (EntryPointNotFound, which the stock game treats as false too); they're cached. No helper is restarted after
    `ShutdownSteam`.
  - GOG: one exe for both stores; `enu` registers the Galaxy manager `axn` (`acv`) if `Galaxy.dll` exists.
    GalaxyCSharp.dll is AnyCPU but its natives (`Galaxy.dll`, `GalaxyCSharpGlue.dll`) are 32-bit, so in 64-bit
    `axn` would fail to init (caught; retried every 5 min) and GOG achievements wouldn't unlock. The patch creates
    `GalaxyBridge` instead, which sends `Galaxy*` commands to the helper; the helper runs Galaxy like `axn` (sign-in,
    stats retrieved before any change, Store after each) on one thread that calls `ProcessData` every 20 ms. The
    game's Galaxy client ID/secret are read at run time from the first two `ldstr`s in `axn.sv` and passed over the
    pipe (redacted in the log). **Never copy the game's credentials into this repo.** Tested: the strings are found
    (17-digit ID, 64-hex secret), and the helper without Galaxy answers errors/0 and exits cleanly. Not tested on a
    GOG copy.
  - Saves/config.dat: see the rule on files the game writes. `chunk0.dat` still names our FNA as the *library* of
    types the binder names by code ("X" names): .NET writes that itself, the stock game does the same with
    `WineMono.FNA` under Proton, and the reader ignores it (checked: such a save loads in the stock game).
  - Tested under Proton (test copy `~/underrail-x64`: game files copied, `data` symlinked): 64-bit process on our FNA
    only, D3D11, menu and area music, master volume, save/load, saves and config.dat loading in the stock game,
    save screenshots, clean exit. Not yet: real Windows / Microsoft's .NET (a Wine prefix with .NET 4.8 would be the
    closest stand-in), display mode switching and right-drag in FNA mode, achievements, long sessions.
- Sharper scaled text: re-render the bitmap fonts from their original TTFs at the scaled size; UI art would need
  upscaling (it's painted, not vector).
- Naming: rename files/assembly to UnderRailFixes. The patcher detects a patched exe by its reference to the
  `UnderrailFix` assembly, so a rename must keep recognising old patches (and bump the version).

## Linux / Proton notes (not part of the mod)

- Advanced shader tiles flicker black on FNA3D's SDLGPU (Vulkan) and OpenGL backends; `FNA3D_FORCE_DRIVER=D3D11`
  fixes it. Reported upstream: https://github.com/FNA-XNA/FNA3D/issues/274
- On GE-Proton the game can hang on exit in `winepulse.so: pulse_stop` (GE's proton-ds5-haptic patches change
  winepulse); official Proton and Proton-CachyOS exit fine. Workaround on GE (confirmed):
  `WINEDLLOVERRIDES="winepulse.drv=d"` (Wine falls back to winealsa). Not needed with the 64-bit mode.
- The game's own logger (`aa5`) only calls `Trace.WriteLine`, which no listener writes anywhere in release builds,
  so there is no game log to add to; `UnderrailFix.log` is the only log.
