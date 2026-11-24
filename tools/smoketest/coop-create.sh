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
printf 'wait 8\ncoop.join 127.0.0.1:%s\nwait 8\ncoop.ui coop\nwait 4\ncoop.ui create\n' "$PORT" > "$JOIN/Mods/WorksTest/menurun.txt"
j timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 45
j "$SKILL/inp.sh" focus click 1036 678 >/dev/null 2>&1; sleep 8     # creation's Back (1280x720)
printf 'coop.ui create\nwait 10\nworks.accept Bobby\n' > "$JOIN/Mods/WorksTest/menurun.txt"; sleep 25
# A second joiner without a name: "Player (2)".
printf 'wait 8\ncoop.join 127.0.0.1:%s\n' "$PORT" > "$TWO/Mods/WorksTest/menurun.txt"
t timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 40
t timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; sleep 3
# Start is refused until the host has a character; the host makes one, then Start: the Rules screen, then the game
# starts as that character (no creation), with Bobby.
printf 'coop.start\nwait 2\ncoop.ui hostcreate\nwait 10\nworks.accept Hosty\nwait 8\ncoop.ui hostcreate\n' > "$HOST/Mods/WorksTest/menurun.txt"; sleep 30
h "$SKILL/inp.sh" focus click 1036 678 >/dev/null 2>&1; sleep 8     # Recreate character, then Back: still Hosty
printf 'coop.start\n' > "$HOST/Mods/WorksTest/menurun.txt"; sleep 8
h "$SKILL/inp.sh" focus click 902 678 >/dev/null 2>&1; sleep 25
j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1     # the joiner first

pass=0; fail=0
check() { if grep -qE "$3" "$2"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected in $(basename "$(dirname "$2")"): $3)"; fi; }
never() { if grep -qE "$3" "$2"; then fail=$((fail+1)); echo "FAIL: $1 (found: $(grep -m1 -E "$3" "$2" | cut -c1-160))"; else pass=$((pass+1)); fi; }
check "the joiner sees the lobby's places and settings" "$JL" "ui: window \"Join a game\" .* fits"
check "creation opens without the Rules screen"   "$JL" "world: character creation \\(no rules screen\\)"
check "Accept sends the character"                "$JL" "Coop: sending Bobby to"
check "... and goes back to the main menu"        "$JL" "saves: Bobby made in character creation; back to the main menu \\(no game started\\)"
check "a joiner without a name is Player"        "$HL" "Coop: Player has a place; making a character"
check "a second one without a name is Player (2)" "$HL" "Coop: Player \\(2\\) has a place; making a character"
check "Start refused without the host's character" "$HL" "coop.start -> create your character first"
check "creation's Cancel says Back"              "$JL" "world: character creation's Cancel says Back"
check "Back: the joiner is back in the lobby"     "$JL" "Coop: back from character creation: nothing made"
check "Recreate, then Back: the host keeps Hosty" "$HL" "Coop: back from character creation: nothing made, still Hosty"
check "the host makes a character in the lobby"   "$HL" "Coop: the host will play Hosty"
check "the game starts as the host's character"   "$HL" "world: new game started as Hosty"
check "Bobby enters the host's game"              "$HL" "Coop: Player plays Bobby"
never "no stray session end on the host"          "$HL" "another game was loaded while hosting"
check "the host has the joiner as Bobby"          "$HL" "Coop: Player will play Bobby"
check "the joiner waits in the lobby"             "$JL" "Coop: You'll play Bobby"
# Making the character starts no game: no zone and no quitting before the host's world arrives.
if awk '/receiving the host.s world/ { exit } /saves: zone loaded|world: quitting to the main menu/ { bad=1 } END { exit !bad }' "$JL"; then fail=$((fail+1)); echo "FAIL: a game started on the joiner while making the character"; else pass=$((pass+1)); fi
never "the character file was written"            "$JL" "can't export|couldn't export"
if awk '/You.ll play Bobby/ { f=1 } /You play Bobby/ { f=0 } f && /disconnected from the host/ { bad=1 } END { exit !bad }' "$JL"; then fail=$((fail+1)); echo "FAIL: the joiner stayed connected"; else pass=$((pass+1)); fi
echo "co-op create test: $pass passed, $fail failed (logs: $HL, $JL)"
[ "$fail" -eq 0 ]
