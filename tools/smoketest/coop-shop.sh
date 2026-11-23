#!/usr/bin/env bash
# Co-op shared shop test (as coop.sh: two game copies, headless, without Steam). The host and the joiner (Mira, from a
# co-op save made by coopsave.py) are in the game; the host spawns a trader and both get charons. The host opens the
# shop: it opens for the joiner too. The host buys: the joiner's shop gets the new stock. The joiner buys an item and
# pays: the host makes that deal with the real merchant. The joiner closes their window; the host's stays.
# Then a race: the host gives Mira bandages while she takes her armour off on her machine: nothing may be lost.
#   tools/smoketest/coop-shop.sh            (after ./build.sh)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
SKILL="$REPO/.claude/skills/run-underrail/scripts"
DIST="$REPO/dist/UnderRailWorks"; MODS="$REPO/build/Mods"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
WORK="${WORK:-$HOME/.cache/underrail-test/coopshop}"; SAVE="${SAVE:-dgg}"
PORT=27659
HOST="$WORK/host" JOIN="$WORK/join"
[ -d "$DIST/../mods/Coop" ] && [ -d "$MODS/WorksTest" ] || { echo "run ./build.sh first" >&2; exit 1; }
. "$HERE/gamecopy.sh"
h() { TEST_GAME="$HOST" RUN=cooph "$@"; }
j() { TEST_GAME="$JOIN" RUN=coopj "$@"; }
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
gamecopy "$HOST" || exit 1; gamecopy "$JOIN" || exit 1
HL="$HOST/UnderRailWorks.log" JL="$JOIN/UnderRailWorks.log"
HA="$HOST/Mods/WorksTest/autorun.txt" JA="$JOIN/Mods/WorksTest/autorun.txt"
SAVES="${CACHE:-$HOME/.cache/underrail-test}/runs/cooph/prefix/pfx/drive_c/users/steamuser/Documents/My Games/Underrail/Saves"

# The host's character as a .char (first run), then a co-op save with Mira, its lobby, the joiner takes Mira, start.
printf 'wait 8\nwt.load %s\n' "$SAVE" > "$HOST/Mods/WorksTest/menurun.txt"
printf 'wait 3\nwt.clone\nwait 2\nwt.unclone\n' > "$HA"
h timeout 100 "$SKILL/launch.sh" --save "$SAVE" 1280 720 >/dev/null 2>&1; sleep 50
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
printf 'wait 8\n' > "$HOST/Mods/WorksTest/menurun.txt"
printf 'wait 3\nworks.var wt_god true\n' > "$HA"
h timeout 100 "$SKILL/launch.sh" --save "$SAVE" 1280 720 >/dev/null 2>&1; sleep 20
python3 "$HERE/coopsave.py" "$SAVES/$SAVE" Mira "$HOST/Mods/WorksTest/clone.char" >/dev/null
printf 'coop.hostsave %s %s\n' "$SAVE" "$PORT" > "$HOST/Mods/WorksTest/menurun.txt"; sleep 5
printf 'wait 8\ncoop.join 127.0.0.1:%s tester\nwait 15\ncoop.pick 1\n' "$PORT" > "$JOIN/Mods/WorksTest/menurun.txt"
j timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 45
printf 'coop.start\n' > "$HOST/Mods/WorksTest/menurun.txt"; sleep 60

# A trader and charons for both (the joiner's sheet follows within seconds).
printf 'wt.merchant\nwait 2\nwt.giveto Mira currency\\StygianCoin 500\nwt.giveto asd currency\\StygianCoin 500\nwait 2\nwt.merchants\n' > "$HA"; sleep 14
# The host opens the shop: it opens for the joiner.
printf 'wt.opentrade Trader\n' > "$HA"; sleep 8
h "$SKILL/shot.sh" "$WORK/host-shop.png" >/dev/null 2>&1; j "$SKILL/shot.sh" "$WORK/join-shop.png" >/dev/null 2>&1
# The host buys the first item and pays 50: the joiner's shop gets the new stock.
printf 'works.tradebuy *\nworks.tradeoffer currency.sc 50\nwait 1\nworks.tradeaccept\n' > "$HA"; sleep 10
# The joiner buys the first item and pays 100.
printf 'works.tradebuy *\nworks.tradeoffer currency.sc 100\nwait 1\nworks.tradeaccept\n' > "$JA"; sleep 10
j "$SKILL/shot.sh" "$WORK/join-shop-after.png" >/dev/null 2>&1
# The joiner closes their window.
printf 'wt.closetrade\n' > "$JA"; sleep 5
printf 'wt.bagof Mira\nwt.merchants\n' > "$HA"; sleep 4
# The race: the host gives Mira 3 bandages while Mira takes her armour off on her machine. Whichever sheet arrives
# first, the bandages must stay (the joiner's sheet, made before them, is refused and the host's sent back).
printf 'wt.giveto Mira consumables\\medicine\\bandage 3\n' > "$HA"; printf 'wt.unwear Mira Armor\n' > "$JA"; sleep 14
printf 'wt.bagof Mira\n' > "$HA"; sleep 4
# Rejoining: the joiner quits; Mira stays, passing her turns; the same player connects again and gets her back.
j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; sleep 8
cp "$JL" "$WORK/join-first.log"
printf 'wait 8\ncoop.join 127.0.0.1:%s tester\n' "$PORT" > "$JOIN/Mods/WorksTest/menurun.txt"
j timeout 100 "$SKILL/launch.sh" 1280 720 >/dev/null 2>&1; sleep 45
h timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1; j timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1

pass=0; fail=0
check() { if grep -qE "$3" "$2"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected in $(basename "$(dirname "$2")"): $3)"; fi; }
count() { grep -cE "$2" "$1"; }
check "the host's shop opens for everyone"   "$HL" "Coop: shop with Trader opened for everyone"
check "... the joiner's window opens"        "$WORK/join-first.log" "Coop: shop open: Trader, [0-9]+ items"
check "the joiner's deal goes to the host"   "$WORK/join-first.log" "Coop: deal sent to the host: buying .+; selling currency.sc\|[0-9]+\|100"
check "... the host makes that deal"         "$HL" "Coop: tester traded with Trader: bought .+ x[0-9]+; gave .+ x100"
check "the host's own deal reaches the shop" "$HL" "Coop: the host traded with Trader"
if [ "$(count "$WORK/join-first.log" "Coop: shop (updated|open): Trader")" -ge 3 ]; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: the joiner's shop updated after both deals (opened, the host's deal, again after their own)"; fi
check "the joiner closes their window"        "$HL" "Coop: tester closed the shop"
check "the race: the bandages stay with Mira"  "$HL" "bagof Mira: .*med.bandage x3"
# Either way round is right: the joiner's older sheet refused, or the host's sheet reached the joiner first (taken).
if awk '/wt.unwear Mira Armor/ { f=1 } f && /Coop: your change crossed one made on the host|world: the player is now Mira/ { ok=1 } END { exit !ok }' "$WORK/join-first.log"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: ... the joiner's crossing change refused, or the host's sheet taken"; fi
check "the player who left comes back to Mira" "$HL" "Coop: tester is back, playing Mira"
check "... and is told"                        "$JL" "Coop: You're back as Mira"
check "Mira paid 100 and has the item"        "$HL" "bagof Mira: .*currency.sc x400"
[ "${WITH_STEAM:-0}" = 1 ] || for l in "$HL" "$JL"; do if grep -q "helper: InitSteam -> 1" "$l"; then fail=$((fail+1)); echo "FAIL: connected to Steam ($l)"; else pass=$((pass+1)); fi; done
echo "co-op shop test: $pass passed, $fail failed (logs: $HL, $JL; shots: $WORK)"
[ "$fail" -eq 0 ]
