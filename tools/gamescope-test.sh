#!/usr/bin/env bash
# Runs the Steam copy of UnderRail at a resolution the monitor doesn't have, through gamescope, to test the fixes
# at e.g. 8K or portrait on any screen. gamescope scales the result to fit the real monitor.
#
#   tools/gamescope-test.sh [WIDTH HEIGHT [OUTPUT_W OUTPUT_H]]     defaults: 7680 4320 on 5120 1440
#   PROTON=/path/to/proton tools/gamescope-test.sh ...             the Proton the game's prefix was last run with
#
# Notes:
# - Use the Proton that last ran the prefix (compatdata/250520/version), or Proton may upgrade the prefix.
# - gamescope tends to abort (exit 134) when the session ends; a gamescopereaper process can be left behind.
#   Kill it by PID (pgrep -a gamescope), never with pkill -f: that matches your own shell's command line.
# - Settings and results: UnderrailFix.ini / UnderrailFix.log in the game folder.
set -euo pipefail
W=${1:-7680} H=${2:-4320} OW=${3:-5120} OH=${4:-1440}
STEAM="$HOME/.local/share/Steam"
GAME="$STEAM/steamapps/common/Underrail"
PFX="$STEAM/steamapps/compatdata/250520"
if [ -z "${PROTON:-}" ]; then
    name="$(cat "$PFX/version" 2>/dev/null || true)"
    PROTON="$(ls -d "$STEAM"/compatibilitytools.d/"$name"*/proton /usr/share/steam/compatibilitytools.d/*/proton 2>/dev/null | head -1)"
fi
[ -x "$PROTON" ] || { echo "Set PROTON=/path/to/proton (couldn't find the one in $PFX/version)" >&2; exit 1; }
# Not connected to Steam unless WITH_STEAM=1 (testing Steam integration): Steam mustn't show the user as playing.
if [ "${WITH_STEAM:-0}" = 1 ]; then
    export STEAM_COMPAT_DATA_PATH="$PFX" STEAM_COMPAT_CLIENT_INSTALL_PATH="$STEAM" SteamAppId=250520 SteamGameId=250520
else
    mkdir -p "$HOME/.cache/underrail-test/no-steam"
    export STEAM_COMPAT_DATA_PATH="$PFX" STEAM_COMPAT_CLIENT_INSTALL_PATH="$HOME/.cache/underrail-test/no-steam" PROTONFIXES_DISABLE=1
    unset SteamAppId SteamGameId
fi
export FNA3D_FORCE_DRIVER="${FNA3D_FORCE_DRIVER:-D3D11}" WINEDEBUG="${WINEDEBUG:--all}"
cd "$GAME"
echo "UnderRail at ${W}x${H} on ${OW}x${OH} with $PROTON"
exec gamescope -w "$W" -h "$H" -W "$OW" -H "$OH" -f -- python3 "$PROTON" waitforexitandrun "$GAME/underrail.exe"
