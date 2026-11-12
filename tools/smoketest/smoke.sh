#!/usr/bin/env bash
# API smoke test: one scripted game session with the current build (dist/) and the test mods (build/Mods), checking
# UnderRailWorks.log for what each step must produce. It covers the loader's rules (a mod built for a newer Works is
# refused by name, a mod failing in Load leaves nothing behind, Harmony use noted), and the API areas mods use most:
# items, gear, containers, doors, XP, feats, mod statuses, equipment, camera, areas, markers, stats, character export
# and clones, merchants, spawning, combat and deaths, translations, cross-mod calls, hook order, save data through a
# save and a load. Exit 0 when every check passed. Runs headless and without Steam (the run-underrail skill), from a
# copy of a save (SAVE, default dgg: a South Gate save in the player's saves; only copied, never written).
#   tools/smoketest/smoke.sh            (after ./build.sh)
set -uo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"; REPO="$(cd "$HERE/../.." && pwd)"
SKILL="$REPO/.claude/skills/run-underrail/scripts"
DIST="$REPO/dist/UnderRailWorks"; MODS="$REPO/build/Mods"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
WORK="${WORK:-$HOME/.cache/underrail-test/smoke}"; SAVE="${SAVE:-dgg}"
export TEST_GAME="$WORK/game" RUN=smoke
[ -d "$DIST" ] && [ -d "$MODS/WorksTest" ] || { echo "run ./build.sh first" >&2; exit 1; }

# The game copy (gamecopy.sh).
. "$HERE/gamecopy.sh"
timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1
gamecopy "$TEST_GAME" || exit 1
LOG="$TEST_GAME/UnderRailWorks.log"; AUTO="$TEST_GAME/Mods/WorksTest/autorun.txt"
inp() { timeout 30 "$SKILL/inp.sh" "$@" >/dev/null 2>&1; }

# The session. No clicks: the test mod loads the save from the main menu (menurun.txt), then runs autorun.txt.
printf 'wait 8\nwt.load %s\n' "$SAVE" > "$TEST_GAME/Mods/WorksTest/menurun.txt"     # the save list isn't ready in the menu's first frames
cat > "$AUTO" <<'STEPS'
wait 3
works.health
wt.money 50
wt.bag
wt.gear
wt.containers
wt.doors
wt.xp
wt.feat
wt.glow
wt.fx asd
wt.sd set
wt.gun
wt.cam
wt.areas
wt.mark
wt.stats
wt.clone
wait 2
wt.unclone
wt.merchant
wt.spawnat 33 22 creatures\rathoundalpha
wait 3
wt.slay
wait 3
works.health
STEPS
timeout 100 "$SKILL/launch.sh" --save "$SAVE" 2560 720 >/dev/null 2>&1
sleep 90
printf 'wt.save smoketest\n' > "$AUTO"; sleep 10
printf 'wt.load smoketest\n' > "$AUTO"; sleep 25
printf 'wt.sd\n' > "$AUTO"; sleep 5
timeout 60 "$SKILL/stop.sh" >/dev/null 2>&1

# The checks: each a description and a regular expression the log must (or, with !, must not) match.
pass=0; fail=0
check() { if grep -qE "$2" "$LOG"; then pass=$((pass+1)); else fail=$((fail+1)); echo "FAIL: $1 (expected: $2)"; fi; }
never() { if grep -qE "$2" "$LOG"; then fail=$((fail+1)); echo "FAIL: $1 (found: $(grep -m1 -E "$2" "$LOG" | cut -c1-160))"; else pass=$((pass+1)); fi; }
check "game members all found"            "mods: [0-9]+ game members checked"
check "a mod for a newer Works refused"   "NeedsNewer not loaded .*not here: UnderRailWorks.World.FutureFeature"
check "a failed mod's registrations go"   "HalfBroken: what it registered before failing was removed"
never "nothing of a failed mod runs"      "must never appear"
check "Harmony use noted"                 "HelloWorks: patches code with Harmony"
check "works.health"                      "console: works.health -> UnderRailWorks .*API"
check "translations"                      "translation: Hello, player!"
check "cross-mod calls"                   "cross-mod call: echo hi"
check "hook order (first)"                "order: first"
check "hook order (last)"                 "order: last"
check "items into the bag"                "item added Stygian Coin x50"
check "unaware mods see the player"       "unaware: .* got Stygian Coin x50"
check "gear"                              "gear: Armor="
check "containers"                        "containers: [0-9a-f]{8} "
check "doors"                             "doors: [0-9a-f]{8} at"
check "experience"                        "XP \+5000"
check "experience event"                  "unaware: .* gained 5000 XP"
check "feats"                             "feat gained by"
check "mod statuses"                      "status applied mod:WorksTest:glow"
check "stat bonuses"                      "agility after glow: [0-9]+"
check "equipment"                         "equipped Bieretta 99 on"
check "camera"                            "camera \{X:"
check "areas"                             "discovered [0-9]+ of [0-9]+"
check "markers"                           "markers here: Near@"
check "stats rebuild"                     "stats after: "
check "character export"                  "exported [0-9]+ bytes"
check "clone from a .char"                "clone at .* sheet True"
check "clone removed"                     "unclone True"
check "merchants"                         "merchant: True, [0-9]+ items"
check "spawning"                          "spawned at 33,22: Rathound Alpha"
check "combat started"                    "combat started"
check "deaths"                            "dying: Rathound Alpha killed by"
check "saving"                            "console: wt.save smoketest -> saved: True"
check "loading"                           "saves: loading smoketest"
check "save data survives a load"         "savedata probe=v1.0.0 writtenBy=1.0.0"
check "nothing failed to start"           "parts that didn't start: none"
never "no patch failed"                   "UnderRailWorks: patch .* failed"
never "no hook group failed"              "hooks failed \(mods using them"
echo "smoke test: $pass passed, $fail failed (log: $LOG)"
[ "$fail" -eq 0 ]
