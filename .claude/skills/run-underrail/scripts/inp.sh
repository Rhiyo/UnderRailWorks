#!/usr/bin/env bash
# inp.sh COMMANDS...   input to the running test game, in game pixels (= screenshot pixels)
#   move X Y | click X Y | rclick X Y | wheel N (+ up, - down) | key VK | down VK | up VK | hold VK MS
#   type TEXT | sleep MS          VK: hex (0x1B) or decimal; see SKILL.md for common codes
# One Wine process per call (~1-2 s), so batch several commands into one call.
set -euo pipefail
. "$(dirname "$0")/common.sh"
pid=$(game_pid) || { echo "game not running" >&2; exit 1; }
unset SteamAppId SteamGameId     # with them set, Proton's steam.exe wrapper never returns
# The game's own display and prefix (a helper in another prefix gets its own Wine server: its input goes nowhere).
export DISPLAY=$(tr '\0' '\n' < /proc/$pid/environ | sed -n 's/^DISPLAY=//p')
# WINEPREFIX is the prefix the game really runs in (its STEAM_COMPAT_DATA_PATH can say otherwise).
pfx=$(tr '\0' '\n' < /proc/$pid/environ | sed -n 's/^WINEPREFIX=//p'); pfx=${pfx%/}
export STEAM_COMPAT_DATA_PATH=${pfx%/pfx}
python3 "$PROTON" run "$CACHE/input.exe" "$@" 2>&1 | grep -v -i "lsfg\|protonfixes\|ntsync\|fsync\|esync" | tail -3 || true     # Proton usually swallows the helper's output: no lines is fine
