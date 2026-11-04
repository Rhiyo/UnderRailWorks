#!/usr/bin/env bash
# launch.sh [--config] [--saves | --save NAME]... [W H]   default 1280 720
# Runs the test copy in gamescope's headless backend: no window, so it never takes the desktop's focus and needs none
# (input goes to gamescope's own X server). The game's screen is exactly W x H, so its native resolution, mouse
# coordinates and screenshot pixels are all the same. VISIBLE=1 launch.sh shows it in a window instead (for the user
# to watch; that window takes focus when it opens).
# One run per Claude session (RUN = the session id, or RUN=name for a second run): everything it creates is in
# $CACHE/runs/$RUN (prefix, log, gamescope's display and process group, silent sink name) and only this run's
# stop.sh removes it, so sessions never stop or drive each other's games.
# Prefix: a fresh copy of the base prefix every time, so the game starts with its default settings and no saves.
# Only when a test needs them, copy the player's own over (copies: theirs are never written):
#   --config      config.dat (their video/game settings)
#   --saves       every save            --save NAME   one save folder (repeatable), e.g. --save quicksave
# Sound: the game plays normally, into this run's own silent PipeWire sink, so nothing is heard.
# Returns when the game's process exists; the game keeps running in the background (log: $RUN_DIR/game.log).
set -euo pipefail
. "$(dirname "$0")/common.sh"
config=0 saves=() all_saves=0
while [ $# -gt 0 ]; do
    case "$1" in
        --config) config=1; shift;;
        --saves) all_saves=1; shift;;
        --save) saves+=("$2"); shift 2;;
        *) break;;
    esac
done
W=${1:-1280} H=${2:-720}
[ -x "$PROTON" ] || { echo "Proton not found; set PROTON=/path/to/proton" >&2; exit 1; }
[ -d "$TEST_GAME" ] && [ -d "$BASE_PFX/pfx" ] || { echo "run setup.sh first" >&2; exit 1; }
game_pid >/dev/null && { echo "already running (run $RUN, pid $(game_pid))"; exit 0; }
[ -f "$RUN_DIR/pgid" ] && kill -0 "$(cat "$RUN_DIR/pgid")" 2>/dev/null && { echo "run $RUN's gamescope is still up: stop.sh first" >&2; exit 1; }
[ -z "$(prefix_pids)" ] || { echo "Wine is still running in $TEST_PFX: stop.sh first" >&2; exit 1; }
# Fresh run folder and prefix (mostly symlinks: the copy takes well under a second).
rm -rf "$RUN_DIR"; mkdir -p "$RUN_DIR"
cp -a "$BASE_PFX" "$TEST_PFX"
real="$REAL_PFX/$GAME_DOCS" test="$TEST_PFX/$GAME_DOCS"
mkdir -p "$test/Saves"
[ "$config" = 1 ] && cp -a "$real/config.dat" "$test/"
[ "$all_saves" = 1 ] && cp -a "$real/Saves/." "$test/Saves/"
for s in "${saves[@]}"; do
    [ -d "$real/Saves/$s" ] || { echo "no save '$s' in $real/Saves" >&2; exit 1; }
    cp -a "$real/Saves/$s" "$test/Saves/"
done
cd "$TEST_GAME"
pactl load-module module-null-sink sink_name="$SINK" sink_properties=device.description="UnderRail-test-$RUN" > "$RUN_DIR/sink_module"
backend=(--backend headless); [ "${VISIBLE:-0}" = 1 ] && backend=(-W "$W" -H "$H")
# Its own process group (setsid): stop.sh ends exactly this gamescope, its reaper and Proton, nothing else.
PULSE_SINK="$SINK" setsid nohup gamescope "${backend[@]}" -w "$W" -h "$H" -- python3 "$PROTON" waitforexitandrun "$TEST_GAME/underrail.exe" >"$RUN_DIR/game.log" 2>&1 &
echo $! > "$RUN_DIR/pgid"
for i in $(seq 90); do
    # This gamescope's control socket (gamescope-0, -1... when several run), for shot.sh.
    [ -s "$RUN_DIR/display" ] || sed -n "s/.*Running compositor on wayland display '\([^']*\)'.*/\1/p" "$RUN_DIR/game.log" | tail -1 > "$RUN_DIR/display"
    game_pid >/dev/null && break
    timeout 1 tail -f /dev/null || true
done
game_pid >/dev/null || { echo "game didn't start within 90 s; see $RUN_DIR/game.log" >&2; exit 1; }
echo "running: run $RUN, pid $(game_pid), ${W}x${H}, fresh prefix$([ "$config" = 1 ] && echo ' + config.dat')$([ "$all_saves" = 1 ] && echo ' + all saves')$([ ${#saves[@]} -gt 0 ] && echo " + saves: ${saves[*]}"); the main menu takes ~15-25 s"
