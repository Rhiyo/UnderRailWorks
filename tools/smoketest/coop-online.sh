#!/usr/bin/env bash
# Co-op through a store lobby (Works' Online) instead of an address: two game copies on this machine, headless, without
# Steam, sharing Works' test store (UNDERRAILWORKS_TEST_STORE: lobbies and messages as files; the real Steam / GOG
# calls are the same commands in the helper). The host opens a new game's lobby (which also makes a store lobby), the
# joiner joins the first friend hosting (coop.join friend), gets the character list over the store, makes a place for a
# new character, then leaves: the host sees them go.
#   tools/smoketest/coop-online.sh            (after ./build.sh)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
SKILL="$REPO/.claude/skills/run-underrail/scripts"
DIST="$REPO/dist/UnderRailWorks"; MODS="$REPO/build/Mods"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
WORK="${WORK:-$HOME/.cache/underrail-test/coopol}"; SAVE="${SAVE:-dgg}"
PORT=27661
HOST="$WORK/host" JOIN="$WORK/join" STORE="$WORK/store"
[ -d "$DIST/../mods/Coop" ] && [ -d "$MODS/WorksTest" ] || { echo "run ./build.sh first" >&2; exit 1; }
. "$HERE/gamecopy.sh"
h() { UNDERRAILWORKS_TEST_STORE="$(win "$STORE")" UNDERRAILWORKS_TEST_NAME=Hosty TEST_GAME="$HOST" RUN=coolh "$@"; }
j() { UNDERRAILWORKS_TEST_STORE="$(win "$STORE")" UNDERRAILWORKS_TEST_NAME=Joiny TEST_GAME="$JOIN" RUN=coolj "$@"; }
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
gamecopy "$HOST" || exit 1; gamecopy "$JOIN" || exit 1
rm -rf "$STORE"; mkdir -p "$STORE"
HL="$HOST/UnderRailWorks.log" JL="$JOIN/UnderRailWorks.log"

printf 'wait 8\ncoop.online\ncoop.hostnew 2 %s\nwait 3\ncoop.online\n' "$PORT" > "$HOST/Mods/WorksTest/menurun.txt"
h timeout 100 "$SKILL/launch.sh" --save "$SAVE" 1280 720 >/dev/null 2>&1; sleep 30
printf 'wait 8\ncoop.online\ncoop.friends\ncoop.join friend\nwait 6\ncoop.new Bob\nwait 6\ncoop.leave\n' > "$JOIN/Mods/WorksTest/menurun.txt"
j timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 45
printf 'coop.stop\nwait 2\ncoop.online\n' > "$HOST/Mods/WorksTest/menurun.txt"; sleep 8
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1

pass=0; fail=0
check() { if grep -qE "$3" "$2"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected in $(basename "$(dirname "$2")"): $3)"; fi; }
check "the host's store"                       "$HL" "online: Test store as Hosty"
check "the lobby made with the game's lobby"   "$HL" "online: hosting lobby [0-9]+"
check "the joiner sees the host's game"        "$JL" "friends: Hosty: online:[0-9]+"
check "... and enters its lobby"               "$JL" "online: in lobby [0-9]+ \(host [0-9]+\)"
check "the host: the joiner came in"           "$HL" "Coop: Joiny came in through Test store"
check "the handshake over the store"           "$HL" "Coop: Joiny (is choosing a character|has a place; making a character)"
check "the character list over the store"      "$JL" "Coop: characters: .* \(host\)"
check "a place for a new character"            "$HL" "Coop: Joiny will play Bob"
check "the joiner left: the host knows"        "$HL" "Coop: Joiny (left|disconnected)"
check "the host's lobby closed with hosting"   "$HL" "coop.online -> Test store as Hosty, no lobby"
[ "$(ls "$STORE/lobbies" 2>/dev/null | wc -l)" = 0 ] && pass=$((pass+1)) || { fail=$((fail+1)); echo "FAIL: no lobby left behind"; }
for l in "$HL" "$JL"; do if grep -q "helper: InitSteam -> 1" "$l"; then fail=$((fail+1)); echo "FAIL: connected to Steam ($l)"; else pass=$((pass+1)); fi; done
echo "co-op online test: $pass passed, $fail failed (logs: $HL, $JL)"
[ "$fail" -eq 0 ]
