#!/usr/bin/env bash
# Co-op smoke test: two game copies on this machine (a host and a joiner, headless, without Steam), the current build
# with the shipped Co-op mod and the test mods. The host loads a copy of a save (SAVE, default dgg) and hosts on a test
# port; the joiner joins from the main menu with a character the host exported, walks, and a fight follows. The logs
# of both are checked. Before it, a joiner with other mods (another Fair Trade version, no Grudges) must be refused,
# naming them; the real joiner lacks HUD Layout, a "client" mod, which mustn't matter. Exit 0 when every check passed.
#   tools/smoketest/coop.sh            (after ./build.sh)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
SKILL="$REPO/.claude/skills/run-underrail/scripts"
DIST="$REPO/dist/UnderRailWorks"; MODS="$REPO/build/Mods"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
WORK="${WORK:-$HOME/.cache/underrail-test/coop}"; SAVE="${SAVE:-dgg}"
PORT=27659     # not Co-op's default (27650): a real co-op game on this machine is never joined by a test
HOST="$WORK/host" JOIN="$WORK/join" ODD="$WORK/odd"
[ -d "$DIST/Mods/Coop" ] && [ -d "$MODS/WorksTest" ] || { echo "run ./build.sh first" >&2; exit 1; }
. "$HERE/gamecopy.sh"
h() { TEST_GAME="$HOST" RUN=cooph "$@"; }
j() { TEST_GAME="$JOIN" RUN=coopj "$@"; }
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
o() { TEST_GAME="$ODD" RUN=coopo "$@"; }
o timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
gamecopy "$HOST" || exit 1; gamecopy "$JOIN" || exit 1; gamecopy "$ODD" || exit 1
rm -rf "$JOIN/Mods/HudLayout"     # "multiplayer": "client": players needn't match
rm -rf "$ODD/Mods/Grudges"; sed -i 's/"version": "[^"]*"/"version": "9.9.9"/' "$ODD/Mods/FairTrade/mod.json"
OL="$ODD/UnderRailWorks.log"
HL="$HOST/UnderRailWorks.log" JL="$JOIN/UnderRailWorks.log"
HA="$HOST/Mods/WorksTest/autorun.txt" JA="$JOIN/Mods/WorksTest/autorun.txt"

# Host: load the save, export the player as a .char for the joiner, host.
printf 'wait 8\nwt.load %s\n' "$SAVE" > "$HOST/Mods/WorksTest/menurun.txt"
printf 'wait 3\nwt.clone\nwait 2\nwt.unclone\ncoop.deathrule 0\nworks.var wt_god true\ncoop.host %s\n' "$PORT" > "$HA"
h timeout 100 "$SKILL/launch.sh" --save "$SAVE" 1280 720 >/dev/null 2>&1
sleep 55
# A joiner with other mods: refused.
printf 'wait 8\ncoop.join 127.0.0.1:%s odd\n' "$PORT" > "$ODD/Mods/WorksTest/menurun.txt"
o timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1
sleep 40; o timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
# Joiner: from the main menu, join, then play the exported character as "Mira".
printf 'wait 8\ncoop.join 127.0.0.1:%s tester\nwait 15\ncoop.newfrom %s Mira\n' "$PORT" "$(win "$HOST/Mods/WorksTest/clone.char")" > "$JOIN/Mods/WorksTest/menurun.txt"
j timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1
sleep 75
# Mira walks next to the host; a hostile Rathound appears by both; Mira goes down; the host's turns pass.
printf 'coop.go 32 23\n' > "$JA"; sleep 10
printf 'wt.spawnat 33 22 creatures\\rathoundalpha\nwait 4\nworks.memory Rathound Alpha\nwt.hurt Mira 999\n' > "$HA"; sleep 10
for i in 1 2 3 4; do h timeout 20 "$SKILL/inp.sh" focus key 0x20 >/dev/null 2>&1; sleep 6; done
printf 'wt.slay\nwait 4\nworks.health\n' > "$HA"; sleep 12
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1

pass=0; fail=0
check() { if grep -qE "$3" "$2"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected in $(basename "$(dirname "$2")"): $3)"; fi; }
# afterward <what> <log> <marker> <pattern>: once a line matches marker, no later line may match pattern.
afterward() { if awk -v m="$3" -v p="$4" 'f && $0 ~ p { bad=1 } $0 ~ m { f=1 } END { exit !bad }' "$2"; then fail=$((fail+1)); echo "FAIL: $1 ($4 after $3)"; else pass=$((pass+1)); fi; }
never() { if grep -qE "$3" "$2"; then fail=$((fail+1)); echo "FAIL: $1 (found in $(basename "$(dirname "$2")"): $(grep -m1 -E "$3" "$2" | cut -c1-160))"; else pass=$((pass+1)); fi; }
check "host hosts"                          "$HL" "Coop: hosting on port $PORT"
check "other mods: refused, named (joiner)" "$OL" "your mods differ from the host's: only the host has: Grudges 1.0.0; other versions: Fair Trade 9.9.9 / 1.0.0"
check "other mods: the host is told"        "$HL" "odd refused: different mods"
never "a client mod doesn't count"          "$HL" "tester refused"
check "the character file is exported"      "$HL" "exported [0-9]+ bytes"
check "Mira enters the host's world"        "$HL" "Coop: .*Mira"
check "the joiner plays Mira"               "$JL" "the player is now Mira"
check "Mira walks (host)"                   "$HL" "Mira: go 32,23"
check "the joiner sees the Rathound"        "$JL" "mirror: Rathound Alpha placed"
check "the Rathound notices both players"   "$HL" "Rathound Alpha remembers .*Mira .*asd|Rathound Alpha remembers .*asd .*Mira"
check "Mira goes down, not dead"            "$JL" "you're down"
check "the Rathound turns on the host"      "$HL" "incoming to asd from Rathound Alpha"
afterward "nothing attacks a downed player" "$HL" "hurt Mira: .* -> 1" "uses .* on Mira"
check "the fight ends with the kill"        "$HL" "dying: Rathound Alpha killed by"
check "nothing failed to start (host)"      "$HL" "parts that didn't start: none"
never "no patch failed (host)"              "$HL" "UnderRailWorks: patch .* failed"
never "no patch failed (joiner)"            "$JL" "UnderRailWorks: patch .* failed"
# Test runs never connect to Steam (run-underrail's common.sh; WITH_STEAM=1 only to test Steam itself).
[ "${WITH_STEAM:-0}" = 1 ] || for l in "$HL" "$JL"; do if grep -q "helper: InitSteam -> 1" "$l"; then fail=$((fail+1)); echo "FAIL: connected to Steam ($l)"; else pass=$((pass+1)); fi; done
echo "co-op smoke test: $pass passed, $fail failed (logs: $HL, $JL)"
[ "$fail" -eq 0 ]
