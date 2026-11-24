#!/usr/bin/env bash
# Co-op: a new game from the Rules screen to the intro. The host's Host New Game shows the game's Rules screen first
# (Back instead of Close), Accept opens the lobby with them; host and joiner make characters; Start begins the game with
# those rules (no Rules screen) as the host's character. The new game's intro conversation must open for the host
# (it didn't when the world was saved for the joiners in the zone's first moment), the joiner gets it in its
# Conversation window and runs no trigger or conversation of its own; Skip intro takes both to the next zone.
#   tools/smoketest/coop-intro.sh            (after ./build.sh)
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
# Host New Game: the Rules screen; the first difficulty (Easy), Accept (1280x720).
printf 'wait 8\ncoop.ui hostnew\n' > "$HOST/Mods/WorksTest/menurun.txt"
h timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 24
h "$SKILL/inp.sh" focus click 214 239 sleep 300 click 902 678 >/dev/null 2>&1; sleep 6
printf 'coop.ui hostcreate\nwait 10\nworks.accept Hosty\n' > "$HOST/Mods/WorksTest/menurun.txt"; sleep 25
printf 'wait 8\ncoop.join 127.0.0.1:%s\nwait 6\ncoop.ui create\nwait 10\nworks.accept Bobby\n' "$PORT" > "$JOIN/Mods/WorksTest/menurun.txt"
j timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 50
printf 'coop.start\n' > "$HOST/Mods/WorksTest/menurun.txt"; sleep 8
h "$SKILL/inp.sh" focus move 640 300 >/dev/null 2>&1; j "$SKILL/inp.sh" focus move 640 300 >/dev/null 2>&1
sleep 12
h "$SKILL/inp.sh" focus key 0x32 >/dev/null 2>&1; sleep 25     # 2. Skip intro
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
check "Skip intro: the host moves on"            "$HL" "zone loaded: StationAlpha-Level2"
check "... and the joiner follows"               "$JL" "zone loaded: StationAlpha-Level2"
echo "co-op intro test: $pass passed, $fail failed (logs: $HL, $JL)"
[ "$fail" -eq 0 ]
