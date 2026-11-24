#!/usr/bin/env bash
# Co-op: a joiner makes a new character in the game's creation. The host opens a new game's lobby; the joiner joins,
# gets a place straight away (no name given: the host calls them Player), Create character (coop.ui create), creation opens without the Rules screen, Accept (works.accept,
# without the point checks) sends the character to the host and goes straight back to the main menu and the lobby
# window: no game of the joiner's own starts, and the joiner stays connected.
#   tools/smoketest/coop-create.sh            (after ./build.sh)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
SKILL="$REPO/.claude/skills/run-underrail/scripts"
DIST="$REPO/dist/UnderRailWorks"; MODS="$REPO/build/Mods"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
WORK="${WORK:-$HOME/.cache/underrail-test/coopcreate}"; PORT=27662
HOST="$WORK/host" JOIN="$WORK/join" TWO="$WORK/two"
[ -d "$DIST/../mods/Coop" ] && [ -d "$MODS/WorksTest" ] || { echo "run ./build.sh first" >&2; exit 1; }
. "$HERE/gamecopy.sh"
h() { TEST_GAME="$HOST" RUN=coopch "$@"; }
j() { TEST_GAME="$JOIN" RUN=coopcj "$@"; }
t() { TEST_GAME="$TWO" RUN=coopct "$@"; }
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; t timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
gamecopy "$HOST" || exit 1; gamecopy "$JOIN" || exit 1; gamecopy "$TWO" || exit 1
HL="$HOST/UnderRailWorks.log" JL="$JOIN/UnderRailWorks.log"
printf 'wait 8\ncoop.hostnew 4 %s\n' "$PORT" > "$HOST/Mods/WorksTest/menurun.txt"
h timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 25
printf 'wait 8\ncoop.join 127.0.0.1:%s\nwait 8\ncoop.ui coop\nwait 4\ncoop.ui create\nwait 10\nworks.accept Bobby\n' "$PORT" > "$JOIN/Mods/WorksTest/menurun.txt"
j timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 60
# A second joiner without a name: "Player (2)".
printf 'wait 8\ncoop.join 127.0.0.1:%s\n' "$PORT" > "$TWO/Mods/WorksTest/menurun.txt"
t timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 40
t timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1     # the joiners first: they stay connected until then

pass=0; fail=0
check() { if grep -qE "$3" "$2"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected in $(basename "$(dirname "$2")"): $3)"; fi; }
never() { if grep -qE "$3" "$2"; then fail=$((fail+1)); echo "FAIL: $1 (found: $(grep -m1 -E "$3" "$2" | cut -c1-160))"; else pass=$((pass+1)); fi; }
check "the joiner sees the lobby's places and settings" "$JL" "ui: window \"Join a game\" .* fits"
check "creation opens without the Rules screen"   "$JL" "world: character creation \\(no rules screen\\)"
check "Accept sends the character"                "$JL" "Coop: sending Bobby to"
check "... and goes back to the main menu"        "$JL" "saves: Bobby made in character creation; back to the main menu \\(no game started\\)"
check "a joiner without a name is Player"        "$HL" "Coop: Player has a place; making a character"
check "a second one without a name is Player (2)" "$HL" "Coop: Player \\(2\\) has a place; making a character"
check "the host has the joiner as Bobby"          "$HL" "Coop: Player will play Bobby"
check "the joiner waits in the lobby"             "$JL" "Coop: You'll play Bobby"
never "no game started on the joiner"             "$JL" "saves: zone loaded|world: quitting to the main menu"
never "the character file was written"            "$JL" "can't export|couldn't export"
if awk '/You.ll play Bobby/ { f=1 } f && /disconnected from the host/ { bad=1 } END { exit !bad }' "$JL"; then fail=$((fail+1)); echo "FAIL: the joiner stayed connected"; else pass=$((pass+1)); fi
echo "co-op create test: $pass passed, $fail failed (logs: $HL, $JL)"
[ "$fail" -eq 0 ]
