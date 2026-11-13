#!/usr/bin/env bash
# Drawing API test (the parts Occluded Characters in UnderRailFixes uses): one scripted session with the current build
# (dist/) and the test mods, Occluded Characters switched on through works.config, characters placed around the
# player (some behind scenery), and WorksTest's drawing probe (wt.drawprobe: every drawing event handled, every
# member of every handle read, some layers and tile extras drawn by the probe the game's way) with each effect in
# turn: keyhole, lines, dither, fill, outline. Then a drawing handler fails on purpose (wt.drawthrow): Works must
# stop that mod's drawing and the game go on. Screenshots go to $WORK/shots (world drawn = not black). Exit 0 when
# every check passed.   tools/smoketest/drawing.sh   (after ./build.sh)
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
step "works.config UnderRailFixes ShowCovered true" "works.config UnderRailFixes KeyholeDebug 1" \
     "works.config UnderRailFixes ShowCoveredPlayer Keyhole" "works.config UnderRailFixes ShowCoveredOther Keyhole" \
     "wt.crowd 12 creatures\\rathoundalpha 9" "wait 3" "wt.drawprobe 120"; sleep 15
"$SKILL/shot.sh" "$SHOTS/keyhole.png" >/dev/null 2>&1
for fx in Lines Dither Fill Outline; do
    step "works.config UnderRailFixes ShowCoveredOther $fx" "wt.drawprobe 60"; sleep 6
    "$SKILL/shot.sh" "$SHOTS/$(echo $fx | tr A-Z a-z).png" >/dev/null 2>&1
done
step "works.config UnderRailFixes ShowCovered false" "wt.drawprobe 60"; sleep 6
"$SKILL/shot.sh" "$SHOTS/off.png" >/dev/null 2>&1
step "works.config UnderRailFixes ShowCovered true" "works.config UnderRailFixes ShowCoveredOther Keyhole" "wait 2" "wt.drawthrow"; sleep 6
step "wt.drawprobe 30" "works.health"; sleep 6
"$SKILL/shot.sh" "$SHOTS/after-throw.png" >/dev/null 2>&1
alive=0; . "$SKILL/common.sh" 2>/dev/null; game_pid >/dev/null 2>&1 && alive=1
timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1

pass=0; fail=0
check() { if grep -qE "$2" "$LOG"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected: $2)"; fi; }
never() { if grep -qE "$2" "$LOG"; then fail=$((fail+1)); echo "FAIL: $1 (found: $(grep -m1 -E "$2" "$LOG" | cut -c1-200))"; else pass=$((pass+1)); fi; }
ok() { if [ "$2" = 1 ]; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1"; fi; }
# Bright pixels' share of a screenshot's world area (the HUD's corners left out): a drawn world isn't black.
lit() { magick "$1" -gravity center -crop 60%x60%+0+0 -colorspace gray -threshold 8% -format '%[fx:mean>0.05?1:0]' info: 2>/dev/null || echo 0; }
check "works.config sets a mod's setting"     "console: works.config UnderRailFixes ShowCovered true -> UnderRailFixes.ShowCovered = 1"
check "characters placed around the player"   "crowd: [1-9][0-9]* of 12"
probes=$(grep -c "drawprobe: frames" "$LOG")
ok "every probe before the throw finished (6, got $probes)" "$([ "$probes" = 6 ] && echo 1)"
check "probe: layers, characters, entities"   "drawprobe: frames 120/120, layers [1-9][0-9]* .*characters [1-9]"
check "probe: layers drawn by a handler"      "drawprobe: frames 120/120.*replaced [1-9][0-9]*, tiles [1-9][0-9]* \(own [1-9]"
check "probe: hidden outlines (requested)"    "drawprobe: frames 120/120.*outlines [1-9][0-9]* \(hidden [1-9]"
check "probe: shader passes"                  "drawprobe: frames 120/120.*shader passes [1-9]"
never "probe: no errors reading the API"      "drawprobe: .*errors [1-9]"
check "keyhole made"                          "UnderRailFixes: keyhole (circle|layer|: front picture)"
never "occluded characters never failed"      "occluded characters: .* failed"
check "a failing drawing handler: reported"   "WorksTest: its DrawLayer handler failed"
check "... and its drawing stopped"           "WorksTest: an error while drawing; its drawing is off until the game restarts"
last=$(grep -n "drawprobe: frames" "$LOG" | tail -1 | cut -d: -f1); thrown=$(grep -n "drawthrow: throwing now" "$LOG" | head -1 | cut -d: -f1)
ok "... its other drawing handlers removed too (no probe finishes after the throw)" "$([ -n "$thrown" ] && [ "${last:-0}" -lt "$thrown" ] && echo 1)"
check "the game goes on (commands after it)"  "console: works.health -> "
ok "the game still running at the end"       "$alive"
for f in keyhole lines dither fill outline off after-throw; do ok "world drawn in $f.png" "$(lit "$SHOTS/$f.png")"; done
# Test runs never connect to Steam (run-underrail's common.sh; WITH_STEAM=1 only to test Steam itself).
[ "${WITH_STEAM:-0}" = 1 ] || { if grep -q "helper: InitSteam -> 1" "$LOG"; then fail=$((fail+1)); echo "FAIL: connected to Steam"; else pass=$((pass+1)); fi; }
echo "drawing: $pass passed, $fail failed (log: $LOG, shots: $SHOTS)"
[ "$fail" = 0 ]
