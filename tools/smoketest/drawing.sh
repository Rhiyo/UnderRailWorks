#!/usr/bin/env bash
# Drawing API test (the parts Occluded Characters in Wall Vision uses): one scripted session with the current build
# (dist/) and the test mods, Occluded Characters switched on through works.config, characters placed around the
# player (some behind scenery), and WorksTest's drawing probe (wt.drawprobe: every drawing event handled, every
# member of every handle read, some layers and tile extras drawn by the probe the game's way) with each effect in
# turn: keyhole, lines, dither, fill, outline. Then a drawing handler fails on purpose (wt.drawthrow): Works must
# stop that mod's drawing and the game go on. Screenshots go to $WORK/shots (world drawn = not black). Exit 0 when
# every check passed.   tools/smoketest/drawing.sh   (after ./build.sh)
# Also the composing parts (wt.drawcompose: LayerColour, TileMarks, a DrawLayer look, World.ToScreen) with the keyhole
# on, checked with works.drawstats: Wall Vision must route layers through WorksTest's look and carry its tile marks; and a
# look that switches the render target (wt.drawbadlook) must be stopped.
#   SIZE="1920 1080" (default; also "3840 2160" with UISCALE=200, a percentage). Headless at 2560x720 the world stayed black in
#   Nov 2026 (the game's area fade-in, also with older builds): use sizes that show it.
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
SKILL="$REPO/.claude/skills/run-underrail/scripts"
DIST="$REPO/dist/UnderRailWorks"; MODS="$REPO/build/Mods"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
WORK="${WORK:-$HOME/.cache/underrail-test/drawing}"; SAVE="${SAVE:-dgg}"; SIZE="${SIZE:-1920 1080}"
export TEST_GAME="$WORK/game" RUN=drawing
[ -d "$DIST" ] && [ -d "$MODS/WorksTest" ] || { echo "run ./build.sh first" >&2; exit 1; }
. "$HERE/gamecopy.sh"
timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
gamecopy "$TEST_GAME" || exit 1
for f in Lantern Hologram; do rm -rf "$TEST_GAME/Mods/$f"; cp -r "$MODS/$f" "$TEST_GAME/Mods/"; done
[ -n "${UISCALE:-}" ] && printf 'UiScaleFix=1\nUiScale=%s\n' "$UISCALE" >> "$TEST_GAME/UnderRailWorks.ini"
LOG="$TEST_GAME/UnderRailWorks.log"; AUTO="$TEST_GAME/Mods/WorksTest/autorun.txt"
SHOTS="$WORK/shots"; rm -rf "$SHOTS"; mkdir -p "$SHOTS"
step() { printf '%s\n' "$@" > "$AUTO"; }
# The world only fades in while the game's window is active: focus it, then load (not from the menu's first frames).
printf 'wait 8\nwt.load %s\n' "$SAVE" > "$TEST_GAME/Mods/WorksTest/menurun.txt"
timeout 100 "$SKILL/launch.sh" --save "$SAVE" $SIZE >/dev/null 2>&1
sleep 40
# Loaded from the menu, then (after a click into the window) again from in game with the window active, so the
# world fades in.
read W H <<< "$SIZE"; timeout 30 "$SKILL/inp.sh" focus click $((W/2)) $((H*3/10)) >/dev/null 2>&1; sleep 3
step "wt.load $SAVE"; sleep 2; timeout 30 "$SKILL/inp.sh" focus >/dev/null 2>&1; sleep 15
step "works.config Lantern On false" "works.config Hologram On false" "works.config WallVision ShowCovered true" "works.config WallVision KeyholeDebug 1" \
     "works.config WallVision ShowCoveredPlayer Keyhole" "works.config WallVision ShowCoveredOther Keyhole" \
     "wt.crowd 12 creatures\\rathoundalpha 9" "wait 3" "wt.drawprobe 120"; sleep 15
"$SKILL/shot.sh" "$SHOTS/keyhole.png" >/dev/null 2>&1
# The composing parts with the keyhole on: WorksTest tints characters, marks tiles and draws some layers itself while Wall
# Vision routes layers into its keyhole picture (the drawing probe's own tile router is off by then).
step "works.drawstats" "wt.drawcompose 600"; sleep 4
"$SKILL/shot.sh" "$SHOTS/compose.png" >/dev/null 2>&1
sleep 10; step "works.drawstats"; sleep 3
# The lighting example (game lighting off, its own light and shadows through LayerColour, a glow through PostProcess)
# and the low-level example (Hologram's DrawLayer look on the player) with the keyhole on, traced for one frame.
step "works.config Lantern On true" "works.config Hologram On true" "wait 2" "works.drawstats"; sleep 6
"$SKILL/shot.sh" "$SHOTS/lantern.png" >/dev/null 2>&1
step "works.drawtrace" "wait 2" "works.drawstats" "works.config Lantern On false" "works.config Hologram On false"; sleep 6
for fx in Lines Dither Fill Outline; do
    step "works.config WallVision ShowCoveredOther $fx" "wt.drawprobe 60"; sleep 6
    "$SKILL/shot.sh" "$SHOTS/$(echo $fx | tr A-Z a-z).png" >/dev/null 2>&1
done
step "works.config WallVision ShowCovered false" "wt.drawprobe 60"; sleep 6
"$SKILL/shot.sh" "$SHOTS/off.png" >/dev/null 2>&1
step "works.config WallVision ShowCovered true" "works.config WallVision ShowCoveredOther Keyhole" "wait 2" "wt.drawthrow"; sleep 6
step "wt.drawprobe 30" "works.health"; sleep 6
step "wt.drawbadlook" "wait 2" "works.drawstats"; sleep 6
"$SKILL/shot.sh" "$SHOTS/after-throw.png" >/dev/null 2>&1
alive=0; . "$SKILL/common.sh" 2>/dev/null; game_pid >/dev/null 2>&1 && alive=1
timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1

pass=0; fail=0
check() { if grep -qE "$2" "$LOG"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected: $2)"; fi; }
never() { if grep -qE "$2" "$LOG"; then fail=$((fail+1)); echo "FAIL: $1 (found: $(grep -m1 -E "$2" "$LOG" | cut -c1-200))"; else pass=$((pass+1)); fi; }
ok() { if [ "$2" = 1 ]; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1"; fi; }
# Bright pixels' share of a screenshot's world area (the HUD's corners left out): a drawn world isn't black.
lit() { magick "$1" -gravity center -crop 60%x60%+0+0 -colorspace gray -threshold 8% -format '%[fx:mean>0.05?1:0]' info: 2>/dev/null || echo 0; }
check "works.config sets a mod's setting"     "console: works.config WallVision ShowCovered true -> WallVision.ShowCovered = 1"
check "characters placed around the player"   "crowd: [1-9][0-9]* of 12"
probes=$(grep -c "drawprobe: frames" "$LOG")
ok "every probe before the throw finished (6, got $probes)" "$([ "$probes" = 6 ] && echo 1)"
check "probe: layers, characters, entities"   "drawprobe: frames 120/120, layers [1-9][0-9]* .*characters [1-9]"
check "probe: layers drawn by a handler"      "drawprobe: frames 120/120.*replaced [1-9][0-9]*, tiles [1-9][0-9]* \(own [1-9]"
check "probe: hidden outlines (requested)"    "drawprobe: frames 120/120.*outlines [1-9][0-9]* \(hidden [1-9]"
check "probe: shader passes"                  "drawprobe: frames 120/120.*shader passes [1-9]"
never "probe: no errors reading the API"      "drawprobe: .*errors [1-9]"
check "keyhole made"                          "WallVision: keyhole (circle|layer|: front picture)"
never "occluded characters never failed"      "occluded characters: .* failed"
check "compose: tints, looks, ToScreen (no errors)"  "drawcompose: tinted [1-9][0-9]*, looks [1-9][0-9]*, errors 0"
check "compose: Wall Vision routed layers through WorksTest's look, and tile marks" \
    "drawstats: layers coloured [1-9][0-9]*, routed [1-9][0-9]* \(WallVision [1-9][0-9]*\), drawn by a mod's look [1-9][0-9]* \(WorksTest [1-9][0-9]*\) \(inside a route [1-9][0-9]*\), tile marks drawn [1-9][0-9]* \(inside a route [1-9]"
check "lighting: Lantern coloured, Wall Vision routed, Hologram's look, Lantern's effect drawn each frame" \
    "drawstats: layers coloured [1-9][0-9]*, routed [1-9][0-9]* \(WallVision [1-9][0-9]*\), drawn by a mod's look [1-9][0-9]* \(Hologram [1-9][0-9]*\).*post-processed frames [1-9][0-9]* \(effects drawn [1-9]"
TRACE="$TEST_GAME/drawtrace.txt"
tr() { if grep -qE "$2" "$TRACE" 2>/dev/null; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected in drawtrace.txt: $2)"; fi; }
check "drawtrace written"                     "drawtrace: 1 frame\(s\) written to"
tr "trace: the pass starts into Works' picture" "world pass starts \(into Works' picture"
tr "trace: layers coloured by Lantern"        "coloured by Lantern"
tr "trace: layers routed by Wall Vision"      "routed by WallVision"
tr "trace: Hologram's look"                   "drawn by Hologram's look"
tr "trace: game lighting off"                 "game lighting at 0"
tr "trace: Lantern's effect after WorldDone"  "post-processing: Lantern"
tr "trace: the outline pass after it"         "outline pass starts"
never "Lantern and Hologram never failed"     "mods: (Lantern|Hologram): its .* handler failed|post-processing: .*off until restart|tile marks: .*off until restart"
check "a look that switches the render target: stopped" "WorksTest: its DrawLayer handler failed: .*changed the render target"
ok "... and WorksTest's drawing off again (two stops)" "$([ "$(grep -c 'WorksTest: an error while drawing' "$LOG")" -ge 2 ] && echo 1)"
check "a failing drawing handler: reported"   "WorksTest: its DrawLayer handler failed"
check "... and its drawing stopped"           "WorksTest: an error while drawing; its drawing is off until the game restarts"
last=$(grep -n "drawprobe: frames" "$LOG" | tail -1 | cut -d: -f1); thrown=$(grep -n "drawthrow: throwing now" "$LOG" | head -1 | cut -d: -f1)
ok "... its other drawing handlers removed too (no probe finishes after the throw)" "$([ -n "$thrown" ] && [ "${last:-0}" -lt "$thrown" ] && echo 1)"
check "the game goes on (commands after it)"  "console: works.health -> "
ok "the game still running at the end"       "$alive"
for f in keyhole compose lantern lines dither fill outline off after-throw; do ok "world drawn in $f.png" "$(lit "$SHOTS/$f.png")"; done
# Test runs never connect to Steam (run-underrail's common.sh; WITH_STEAM=1 only to test Steam itself).
[ "${WITH_STEAM:-0}" = 1 ] || { if grep -q "helper: InitSteam -> 1" "$LOG"; then fail=$((fail+1)); echo "FAIL: connected to Steam"; else pass=$((pass+1)); fi; }
echo "drawing: $pass passed, $fail failed (log: $LOG, shots: $SHOTS)"
[ "$fail" = 0 ]
