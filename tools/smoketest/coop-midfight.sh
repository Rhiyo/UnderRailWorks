#!/usr/bin/env bash
# Co-op mid-fight test (as coop.sh): the host is in a fight (Waylon turned hostile) when the joiner joins: the joined
# character must get turns. Joining writes the host's "coop-host" save mid-fight (deleted once sent, as the joiner's
# "coop-join" once loaded); the host saves mid-fight with Mira in it, the joiner quits, the host stops hosting and loads
# that save: the fight must go on without the missing player (no turns for them, not in the party).
#   tools/smoketest/coop-midfight.sh            (after ./build.sh)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
SKILL="$REPO/.claude/skills/run-underrail/scripts"
DIST="$REPO/dist/UnderRailWorks"; MODS="$REPO/build/Mods"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
WORK="${WORK:-$HOME/.cache/underrail-test/coopmid}"; SAVE="${SAVE:-dgg}"
PORT=27659     # not Co-op's default (27650): a real co-op game on this machine is never joined by a test
HOST="$WORK/host" JOIN="$WORK/join"
[ -d "$DIST/Mods/Coop" ] && [ -d "$MODS/WorksTest" ] || { echo "run ./build.sh first" >&2; exit 1; }
. "$HERE/gamecopy.sh"
h() { TEST_GAME="$HOST" RUN=cooph "$@"; }
j() { TEST_GAME="$JOIN" RUN=coopj "$@"; }
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
gamecopy "$HOST" || exit 1; gamecopy "$JOIN" || exit 1
HL="$HOST/UnderRailWorks.log" JL="$JOIN/UnderRailWorks.log"
HA="$HOST/Mods/WorksTest/autorun.txt" JA="$JOIN/Mods/WorksTest/autorun.txt"

# Host: load the save, export the player as a .char for the joiner, host.
printf 'wait 8\nwt.load %s\n' "$SAVE" > "$HOST/Mods/WorksTest/menurun.txt"
printf 'wait 3\nwt.clone\nwait 2\nwt.unclone\ncoop.deathrule 0\nworks.var wt_god true\ncoop.host %s\n' "$PORT" > "$HA"
h timeout 100 "$SKILL/launch.sh" --save "$SAVE" 1280 720 >/dev/null 2>&1
sleep 50
printf 'wt.goto 19 3\nwait 2\nwt.anger Waylon\n' > "$HA"; sleep 10
# Joiner: from the main menu, join, then play the exported character as "Mira".
printf 'wait 8\ncoop.join 127.0.0.1:%s tester\nwait 15\ncoop.newfrom %s Mira\n' "$PORT" "$(win "$HOST/Mods/WorksTest/clone.char")" > "$JOIN/Mods/WorksTest/menurun.txt"
j timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1
sleep 75
for i in 1 2; do printf 'coop.end\n' > "$JA"; h timeout 20 "$SKILL/inp.sh" focus key 0x20 >/dev/null 2>&1; sleep 7; done
printf 'wt.save midfight\n' > "$HA"; sleep 5
R="$HOME/.cache/underrail-test/runs"; SAVES="$R/cooph, $R/coopj: Documents/My Games/Underrail/Saves"
left=$(find "$R/cooph" "$R/coopj" -mindepth 1 -type d -path '*/Documents/My Games/Underrail/Saves/coop-*' 2>/dev/null | wc -l)
found=$(find "$R/cooph" -type d -path '*/Documents/My Games/Underrail/Saves/midfight' 2>/dev/null | wc -l)
j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; sleep 5
echo "=== LOAD ===" >> "$HL"
printf 'coop.stop\nwait 2\nwt.load midfight\n' > "$HA"; sleep 25
printf 'works.memory Waylon\nwt.near\n' > "$HA"; sleep 3
for i in 1 2 3 4 5; do h timeout 20 "$SKILL/inp.sh" focus key 0x20 >/dev/null 2>&1; sleep 7; done
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
pass=0; fail=0
check() { if grep -qE "$3" "$2"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected: $3)"; fi; }
# in order: <what> <first> <then>: a line matching then comes after one matching first
inorder() { if awk -v a="$2" -v b="$3" 'f && $0 ~ b { ok=1 } $0 ~ a { f=1 } END { exit !ok }' "$HL"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 ($3 after $2)"; fi; }
afterward() { if awk -v m="$2" -v p="$3" 'f && $0 ~ p { bad=1 } $0 ~ m { f=1 } END { exit !bad }' "$HL"; then fail=$((fail+1)); echo "FAIL: $1 ($3 after $2)"; else pass=$((pass+1)); fi; }
inorder "the fight started before the join"   "combat started" "Coop: tester plays Mira"
check   "the joiner got the world mid-fight"  "$HL" "Coop: tester is in the world"
inorder "Mira gets turns in that fight"       "Coop: tester plays Mira" "turn: Mira AP"
check   "the mid-fight save loads"            "$HL" "saves: loading midfight"
check   "co-op's save deleted once sent"      "$HL" "Coop: sending the world to tester"
never() { if grep -qE "$3" "$2"; then fail=$((fail+1)); echo "FAIL: $1 (found: $(grep -m1 -E "$3" "$2" | cut -c1-160))"; else pass=$((pass+1)); fi; }
never   "... no co-op save left behind (host)"  "$HL" "couldn't delete coop-host"
never   "... no co-op save left behind (joiner)" "$JL" "couldn't delete coop-join"
if [ "$found" = 1 ] && [ "$left" = 0 ]; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: co-op saves on disk: $left, test save found: $found (in $SAVES)"; fi
inorder "the fight goes on after the load"    "=== LOAD ===" "turn: Waylon AP"
afterward "no turns for the missing player"   "=== LOAD ===" "turn: Mira"
inorder "the party is the host alone"         "=== LOAD ===" "party ids: asd [0-9a-f-]+; "
check   "nothing failed"                      "$HL" "mods: [0-9]+ game members checked"
# Test runs never connect to Steam (run-underrail's common.sh; WITH_STEAM=1 only to test Steam itself).
[ "${WITH_STEAM:-0}" = 1 ] || for l in "$HL" "$JL"; do if grep -q "helper: InitSteam -> 1" "$l"; then fail=$((fail+1)); echo "FAIL: connected to Steam ($l)"; else pass=$((pass+1)); fi; done
echo "co-op mid-fight test: $pass passed, $fail failed (log: $HL)"
[ "$fail" -eq 0 ]
