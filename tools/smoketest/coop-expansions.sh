#!/usr/bin/env bash
# Co-op and expansions (as coop.sh: game copies on this machine, headless, without Steam). The game counts an expansion
# as installed when its marker file Expansions\<id> exists (the expansion's content itself isn't needed for these
# checks; in data\\Expansions), so: the host "has" Expedition and Heavy Duty, the joiner only Expedition, a third copy neither.
#   1. Host New Game: Heavy Duty is greyed (the joiner doesn't have it), Expedition on; the host switches Expedition off
#      too; the joiner switches it off on its side; Start switches both off on the host. The host stops: the joiner,
#      back on the main menu with no session, has its Expedition on again.
#   2. The host saves a game (made with both expansions) and hosts it (Host Saved Game): the copy without expansions
#      is refused, naming them.
#   tools/smoketest/coop-expansions.sh            (after ./build.sh)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
SKILL="$REPO/.claude/skills/run-underrail/scripts"
DIST="$REPO/dist/UnderRailWorks"; MODS="$REPO/build/Mods"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
WORK="${WORK:-$HOME/.cache/underrail-test/coopexp}"; SAVE="${SAVE:-dgg}"
PORT=27659
HOST="$WORK/host" JOIN="$WORK/join" BARE="$WORK/bare"
[ -d "$DIST/../mods/Coop" ] && [ -d "$MODS/WorksTest" ] || { echo "run ./build.sh first" >&2; exit 1; }
. "$HERE/gamecopy.sh"
h() { TEST_GAME="$HOST" RUN=cooph "$@"; }
j() { TEST_GAME="$JOIN" RUN=coopj "$@"; }
b() { TEST_GAME="$BARE" RUN=coopo "$@"; }
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; b timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
gamecopy "$HOST" || exit 1; gamecopy "$JOIN" || exit 1; gamecopy "$BARE" || exit 1
# The markers go in data\Expansions (the game's content folder). A copy's data is a link to the player's own: these
# copies get a data folder of their own instead (links to each of its folders) so nothing is written into the game.
owndata() { local d="$1/data" src; src="$(readlink -f "$d")"; rm "$d"; mkdir "$d"; for e in "$src"/*; do ln -s "$e" "$d/"; done; mkdir "$d/Expansions"; }
owndata "$HOST"; owndata "$JOIN"
touch "$HOST/data/Expansions/xpbl" "$HOST/data/Expansions/xphw" "$JOIN/data/Expansions/xpbl"
HL="$HOST/UnderRailWorks.log" JL="$JOIN/UnderRailWorks.log" BL="$BARE/UnderRailWorks.log"
HM="$HOST/Mods/WorksTest/menurun.txt"

# 1. A new game's lobby.
printf 'wait 8\ncoop.hostnew 2 %s\n' "$PORT" > "$HM"
h timeout 100 "$SKILL/launch.sh" --save "$SAVE" 1280 720 >/dev/null 2>&1; sleep 25
printf 'wait 8\ncoop.join 127.0.0.1:%s tester\nwait 5\ncoop.new Bob\n' "$PORT" > "$JOIN/Mods/WorksTest/menurun.txt"
j timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 30
printf 'coop.expansion xpbl off\nwait 3\ncoop.start\n' > "$HM"; sleep 15
# The rules screen is open: closed again, hosting stopped.
h timeout 20 "$SKILL/inp.sh" click 1037 679 >/dev/null 2>&1; sleep 3
printf 'coop.stop\n' > "$HM"; sleep 10
cp "$HL" "$WORK/host-first.log"     # the host is launched again below (a new log)
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1

# 2. A save made with both expansions, hosted.
printf 'wait 8\nwt.load %s\n' "$SAVE" > "$HM"
printf 'wait 3\nwt.save dlcsave\nwait 3\nwt.tomenu\n' > "$HOST/Mods/WorksTest/autorun.txt"
h timeout 100 "$SKILL/launch.sh" --save "$SAVE" 1280 720 >/dev/null 2>&1; sleep 50
SAVES="${CACHE:-$HOME/.cache/underrail-test}/runs/cooph/prefix/pfx/drive_c/users/steamuser/Documents/My Games/Underrail/Saves"
python3 "$HERE/coopsave.py" "$SAVES/dlcsave" Mira >/dev/null     # a co-op game's save (only those are hosted)
printf 'wait 3\ncoop.hostsave dlcsave %s\n' "$PORT" > "$HM"; sleep 10
printf 'wait 8\ncoop.join 127.0.0.1:%s bare\n' "$PORT" > "$BARE/Mods/WorksTest/menurun.txt"
b timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 35
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; b timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1

pass=0; fail=0
check() { if grep -qE "$3" "$2"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected in $(basename "$2"): $3)"; fi; }
H1="$WORK/host-first.log"
check "the joiner says it has Expedition"          "$H1" "Coop: tester is choosing a character; expansions they have: Expedition"
check "the joiner sees the game's set"             "$JL" "Coop: expansions for this game: none \(off: Expedition\)"
check "Start: both off on the host"                "$H1" "Coop: expansions for this game: none \(off: Expedition, Heavy Duty\)"
check "Works switched them off (host)"             "$H1" "expansions: switched off (xpbl, xphw|xphw, xpbl)"
check "the joiner's Expedition on again after"     "$JL" "Coop: expansions: all installed ones on again \(Expedition\)"
check "the save records both expansions"           "$HL" "Coop: lobby: saved game dlcsave \(expansions: Expedition, Heavy Duty\), 1 places \(Mira\)"
check "a player without them is refused (host)"    "$HL" "Coop: bare refused: this game uses Expedition, Heavy Duty, which you don't have"
check "... and told why"                           "$BL" "this game uses Expedition, Heavy Duty, which you don't have"
[ "${WITH_STEAM:-0}" = 1 ] || for l in "$H1" "$HL" "$JL"; do if grep -q "helper: InitSteam -> 1" "$l"; then fail=$((fail+1)); echo "FAIL: connected to Steam ($l)"; else pass=$((pass+1)); fi; done
echo "co-op expansions test: $pass passed, $fail failed (logs: $WORK)"
[ "$fail" -eq 0 ]
