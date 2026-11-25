#!/usr/bin/env bash
# Co-op: a new game from the Rules screen to the intro. The host's Host New Game shows the game's Rules screen first
# (Back instead of Close), Accept opens the lobby with them; host and joiner make characters; Start begins the game with
# those rules (no Rules screen) as the host's character. The new game's intro conversation must open for the host
# (it didn't when the world was saved for the joiners in the zone's first moment), the joiner gets it in its
# Conversation window and runs no trigger or conversation of its own; Skip intro takes both to the next zone.
#   tools/smoketest/coop-intro.sh            (after ./build.sh; SHOTS=<folder>: screenshots of both games on the way)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
SKILL="$REPO/.claude/skills/run-underrail/scripts"
DIST="$REPO/dist/UnderRailWorks"; MODS="$REPO/build/Mods"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
WORK="${WORK:-$HOME/.cache/underrail-test/coopintro}"; PORT=27650     # Host New Game's port (the menu's way in)
HOST="$WORK/host" JOIN="$WORK/join"
[ -d "$DIST/../mods/Coop" ] && [ -d "$MODS/WorksTest" ] || { echo "run ./build.sh first" >&2; exit 1; }
. "$HERE/gamecopy.sh"
h() { TEST_GAME="$HOST" RUN=coopih "$@"; }
j() { TEST_GAME="$JOIN" RUN=coopij "$@"; }
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
gamecopy "$HOST" || exit 1; gamecopy "$JOIN" || exit 1
HL="$HOST/UnderRailWorks.log" JL="$JOIN/UnderRailWorks.log"
# waitfor <log> <regex> [seconds]: until the log has that line (steps wait for what they need, not a fixed time).
waitfor() { local n=0; until grep -qE "$2" "$1" 2>/dev/null || [ $n -ge ${3:-60} ]; do sleep 1; n=$((n+1)); done; }
# Host New Game: the Rules screen; the first difficulty (Easy), Accept (1280x720).
printf 'wait 8\ncoop.ui hostnew\n' > "$HOST/Mods/WorksTest/menurun.txt"
h timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 24
h "$SKILL/inp.sh" focus click 214 239 sleep 300 click 902 678 >/dev/null 2>&1
waitfor "$HL" "Coop: lobby: new game"
printf 'coop.ui hostcreate\nwait 10\nworks.accept Hosty\n' > "$HOST/Mods/WorksTest/menurun.txt"
waitfor "$HL" "Coop: the host will play Hosty"
printf 'wait 8\ncoop.join 127.0.0.1:%s\nwait 6\ncoop.ui create\nwait 10\nworks.accept Bobby\n' "$PORT" > "$JOIN/Mods/WorksTest/menurun.txt"
j timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1
waitfor "$HL" "Coop: Player will play Bobby" 120; sleep 3
shot() { [ -n "${SHOTS:-}" ] && { mkdir -p "$SHOTS"; h "$SKILL/shot.sh" "$SHOTS/$1-host.png" 50 >/dev/null; j "$SKILL/shot.sh" "$SHOTS/$1-join.png" 50 >/dev/null; }; }     # SHOTS=<folder>: screenshots
shot 0-lobby
printf 'coop.start\n' > "$HOST/Mods/WorksTest/menurun.txt"; sleep 8
h "$SKILL/inp.sh" focus move 640 300 >/dev/null 2>&1; j "$SKILL/inp.sh" focus move 640 300 >/dev/null 2>&1
sleep 12
j "$SKILL/inp.sh" focus click 430 628 >/dev/null 2>&1; sleep 4     # the joiner votes for 1 (Continue): a click on it in its own window (1280x720)
shot 1-intro
h "$SKILL/inp.sh" focus key 0x32 >/dev/null 2>&1; sleep 25     # 2. Skip intro (the host decides)
shot 2-after-skip
h "$SKILL/inp.sh" focus key 0x31 >/dev/null 2>&1; sleep 8     # the host goes on (1. Continue) in the next zone's conversation
shot 3-after-continue
[ -n "${SHOTS:-}" ] && { h "$SKILL/inp.sh" focus key 0x31 >/dev/null 2>&1; sleep 4; printf 'coop.ui coop\n' > "$HOST/Mods/WorksTest/autorun.txt"; printf 'coop.ui coop\n' > "$JOIN/Mods/WorksTest/autorun.txt"; sleep 4; shot 4-coop-window; }
j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1

pass=0; fail=0
check() { if grep -qE "$3" "$2"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected in $(basename "$(dirname "$2")"): $3)"; fi; }
never() { if grep -qE "$3" "$2"; then fail=$((fail+1)); echo "FAIL: $1 (found: $(grep -m1 -E "$3" "$2" | cut -c1-160))"; else pass=$((pass+1)); fi; }
check "the Rules screen first"                   "$HL" "world: the Rules screen \\(for a game started later\\)"
check "its rules kept"                           "$HL" "world: rules chosen: Easy, Oddity experience"
check "the lobby after them"                     "$HL" "Coop: lobby: new game, 3 places"
check "Start: no Rules screen, the rules kept"   "$HL" "world: new game started as Hosty \\(host.char\\), Easy, Oddity experience"
check "the host's intro opens"                   "$HL" "dialog intro1 with"
check "the joiner gets the conversation"         "$JL" "Coop: conversation: Hadrian Tanner"
never "no trigger runs on the joiner"            "$JL" "party: .* counts as the player"
check "the joiner's own triggers are refused"    "$JL" "mirror: a map trigger refused"
check "the joiner sees it in the game's window"   "$JL" "dialog coop/listen with Hadrian Tanner"
check "the joiner's vote reaches the host"        "$HL" "Coop: Player suggests answer 1"
check "the world goes when the zone has settled"  "$HL" "Coop: zone settled: sending the world"
check "Skip intro: the host moves on"            "$HL" "zone loaded: StationAlpha-Level2"
check "... its arrival conversation opens (a transition's trigger, not only a new game's)" "$HL" "dialog sgs_vencel with"
[ "$(grep -c "zone settled: sending the world" "$HL")" -ge 2 ] && pass=$((pass+1)) || { fail=$((fail+1)); echo "FAIL: the world goes again when the next zone settled"; }
check "... and the joiner sees that conversation" "$JL" "Coop: conversation: Vencel"
# The host's next line: the joiner gets it and shows it in the game's window (it once closed and never reopened).
if awk '/Coop: conversation: Vencel: ::He picks up/ { f=1 } f && /dialog coop\/listen with Vencel/ { ok=1 } END { exit !ok }' "$JL"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: after the host's Continue the joiner shows the next line in its window"; fi
check "... and the joiner follows"               "$JL" "zone loaded: StationAlpha-Level2"
echo "co-op intro test: $pass passed, $fail failed (logs: $HL, $JL)"
[ "$fail" -eq 0 ]
