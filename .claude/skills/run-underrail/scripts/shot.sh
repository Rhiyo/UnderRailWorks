#!/usr/bin/env bash
# shot.sh OUT.png [SCALE%]
# Screenshot of the game's screen through gamescopectl (works headless; the mouse cursor isn't in it), optionally
# scaled down (e.g. 50 to save tokens; then multiply coordinates read from it by 100/SCALE).
set -euo pipefail
. "$(dirname "$0")/common.sh"
out=$1 scale=${2:-100}
game_pid >/dev/null || { echo "the test game isn't running" >&2; exit 1; }
# Never a guessed display: another session's gamescope may be gamescope-0; this run's is in RUN_DIR.
GAMESCOPE_WAYLAND_DISPLAY=$(cat "$RUN_DIR/display" 2>/dev/null || true)
[ -n "$GAMESCOPE_WAYLAND_DISPLAY" ] || { echo "unknown gamescope display (see $RUN_DIR/game.log)" >&2; exit 1; }
export GAMESCOPE_WAYLAND_DISPLAY
raw="$RUN_DIR/shot.png"; rm -f "$raw"
gamescopectl screenshot "$raw" >/dev/null 2>&1
for i in $(seq 20); do [ -s "$raw" ] && break; timeout 0.25 tail -f /dev/null || true; done
[ -s "$raw" ] || { echo "no screenshot (is the game running? display $GAMESCOPE_WAYLAND_DISPLAY)" >&2; exit 1; }
timeout 0.3 tail -f /dev/null || true     # the file is written in one go after it appears; let it finish
magick "$raw" -resize "${scale}%" "$out"
echo "$out (scaled ${scale}%)"
