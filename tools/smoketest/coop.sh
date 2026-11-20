#!/usr/bin/env bash
# Co-op smoke test: two game copies on this machine (a host and a joiner, headless, without Steam), the current build
# with the released Co-op mod and the test mods. The host first exports its character from a copy of a save (SAVE,
# default dgg) for the joiner to play, then opens a lobby for that save on the main menu with one place for a new
# player (Host Saved Game). Start is refused while the place is free; a joiner with other mods (another Fair Trade
# version, no Grudges) is refused, naming them; the joiner takes the place with the exported character as "Mira"
# (it lacks HUD Layout, a "client" mod, which mustn't matter); the host starts, Mira enters the loaded game, walks, and
# a fight follows (all the kill's XP to each player). A player trying to join after the start is refused. The logs of
# both are checked. Exit 0 when every check passed.
#   tools/smoketest/coop.sh            (after ./build.sh)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
SKILL="$REPO/.claude/skills/run-underrail/scripts"
DIST="$REPO/dist/UnderRailWorks"; MODS="$REPO/build/Mods"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
WORK="${WORK:-$HOME/.cache/underrail-test/coop}"; SAVE="${SAVE:-dgg}"
PORT=27659     # not Co-op's default (27650): a real co-op game on this machine is never joined by a test
HOST="$WORK/host" JOIN="$WORK/join" ODD="$WORK/odd"
[ -d "$DIST/../mods/Coop" ] && [ -d "$MODS/WorksTest" ] || { echo "run ./build.sh first" >&2; exit 1; }
. "$HERE/gamecopy.sh"
h() { TEST_GAME="$HOST" RUN=cooph "$@"; }
j() { TEST_GAME="$JOIN" RUN=coopj "$@"; }
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
o() { TEST_GAME="$ODD" RUN=coopo "$@"; }
o timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
gamecopy "$HOST" || exit 1; gamecopy "$JOIN" || exit 1; gamecopy "$ODD" || exit 1
rm -rf "$JOIN/Mods/HudLayout"     # "multiplayer": "client": players needn't match
echo "Grudges=0" >> "$JOIN/Mods/mods.ini"     # installed but off: refused, then Match host switches it on
rm -rf "$ODD/Mods/Grudges"; sed -i 's/"version": "[^"]*"/"version": "9.9.9"/' "$ODD/Mods/FairTrade/mod.json"
OL="$ODD/UnderRailWorks.log"
HL="$HOST/UnderRailWorks.log" JL="$JOIN/UnderRailWorks.log"
HA="$HOST/Mods/WorksTest/autorun.txt" JA="$JOIN/Mods/WorksTest/autorun.txt"

# Host, first run: load the save and export the player as a .char for the joiner.
printf 'wait 8\nwt.load %s\n' "$SAVE" > "$HOST/Mods/WorksTest/menurun.txt"
printf 'wait 3\nwt.clone\nwait 2\nwt.unclone\n' > "$HA"
h timeout 100 "$SKILL/launch.sh" --save "$SAVE" 1280 720 >/dev/null 2>&1
sleep 50
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
cp "$HL" "$WORK/host-first.log"     # each launch starts its log afresh
# Host, second run: on the main menu, a lobby for the save with one new player; Start while the place is free.
printf 'wait 8\ncoop.deathrule 0\ncoop.hostsave %s 1 %s\nwait 2\ncoop.start\n' "$SAVE" "$PORT" > "$HOST/Mods/WorksTest/menurun.txt"
printf 'wait 3\nworks.var wt_god true\n' > "$HA"
h timeout 100 "$SKILL/launch.sh" --save "$SAVE" 1280 720 >/dev/null 2>&1
sleep 30
# A joiner with other mods: refused.
printf 'wait 8\ncoop.join 127.0.0.1:%s odd\nwait 6\ncoop.match\nwait 3\ncoop.restore\n' "$PORT" > "$ODD/Mods/WorksTest/menurun.txt"
o timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1
sleep 40; o timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
cp "$OL" "$WORK/odd-first.log"     # the same copy joins again later (a new log)
# Joiner: from the main menu, join, then play the exported character as "Mira".
printf 'wait 8\ncoop.join 127.0.0.1:%s tester\nwait 6\ncoop.match\nwait 15\ncoop.newfrom %s Mira\n' "$PORT" "$(win "$HOST/Mods/WorksTest/clone.char")" > "$JOIN/Mods/WorksTest/menurun.txt"
j timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1
sleep 60
# Every place taken: the host starts (the save loads), Mira enters it.
printf 'coop.start\n' > "$HOST/Mods/WorksTest/menurun.txt"
sleep 60
# Someone joining now: refused (the game has started).
printf 'wait 8\ncoop.join 127.0.0.1:%s late\n' "$PORT" > "$ODD/Mods/WorksTest/menurun.txt"
o timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1
sleep 35; o timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
# Mira walks next to the host; a hostile Rathound appears by both; Mira goes down; the host's turns pass.
printf 'coop.go 32 23\n' > "$JA"; sleep 10
# XP the host's character earns: all of it to Mira as well.
printf 'wt.xpto Mira\nwt.xpto asd 200\nwait 1\nwt.xpto Mira\n' > "$HA"; sleep 6
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
check "the lobby has the save and a place"  "$HL" "Coop: lobby: saved game $SAVE, 1 places \\(new\\) on port $PORT"
check "Start refused while a place is free" "$HL" "coop.start -> waiting for 1 more player"
check "the joiner, with Grudges off, refused" "$HL" "Coop: tester refused: different mods: only the host has: Grudges"
check "Match host switches Grudges on"      "$JL" "Coop: switched on Grudges"
if awk '/Coop: tester refused: different mods/ { f=1 } f && /Coop: tester connected from/ { ok=1 } END { exit !ok }' "$HL"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: ... and joins again by itself (no connection after the refusal)"; fi
check "the odd joiner is told what's missing" "$WORK/odd-first.log" "still missing: .*(Fair Trade 1\\.0\\.0.*Grudges 1\\.0\\.0|Grudges 1\\.0\\.0.*Fair Trade 1\\.0\\.0)"
check "... and gets its own mods back"      "$WORK/odd-first.log" "your own mods are back"
check "the joiner takes the place"          "$HL" "Coop: tester will play Mira"
check "the host starts the saved game"      "$HL" "Coop: starting the saved game $SAVE with tester as Mira"
check "a late joiner is refused"            "$HL" "Coop: late refused: the game has already started"
check "... and told why"                    "$OL" "the game has already started"
check "everyone gets all the XP"            "$HL" "Coop: XP 200 \\(test\\) earned by asd, 200 to each other player \\(100%\\)"
check "other mods: refused, named (joiner)" "$WORK/odd-first.log" "your mods differ from the host's: only the host has: Grudges 1.0.0; other versions: Fair Trade 9.9.9 / 1.0.0"
check "other mods: the host is told"        "$HL" "odd refused: different mods"
never "a client mod doesn't count"          "$HL" "tester refused.*HUD Layout"
check "the character file is exported"      "$WORK/host-first.log" "exported [0-9]+ bytes"
check "Mira enters the host's world"        "$HL" "Coop: tester plays Mira at"
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
