# Shared paths for the run-underrail scripts (sourced). Override any of these in the environment.
STEAM="${STEAM:-$HOME/.local/share/Steam}"
STEAM_GAME="${STEAM_GAME:-$STEAM/steamapps/common/Underrail}"
REAL_PFX="$STEAM/steamapps/compatdata/250520"
TEST_GAME="${TEST_GAME:-$HOME/underrail-test}"          # test copy of the game folder (data symlinked), shared by all runs
BASE_PFX="${BASE_PFX:-$HOME/underrail-test-prefix}"     # clean template: the real prefix minus the game's settings and saves
CACHE="${CACHE:-$HOME/.cache/underrail-test}"
GAME_DOCS="pfx/drive_c/users/steamuser/Documents/My Games/Underrail"   # config.dat, Saves/, CustomPortraits/ (inside a prefix)
# One run per Claude session: everything a run creates lives in RUN_DIR and only that run's scripts touch it, so
# several sessions can test at once without stopping or driving each other's games.
RUN="${RUN:-${CLAUDE_CODE_SESSION_ID:-manual}}"; RUN=${RUN:0:8}
RUN_DIR="$CACHE/runs/$RUN"
TEST_PFX="$RUN_DIR/prefix"          # ephemeral: a fresh copy of BASE_PFX per launch, deleted by stop.sh
SINK="underrail_test_$RUN"          # this run's silent PipeWire sink
if [ -z "${PROTON:-}" ]; then
    name="$(cat "$REAL_PFX/version" 2>/dev/null || true)"
    PROTON="$(ls -d "$STEAM"/compatibilitytools.d/"$name"*/proton 2>/dev/null | head -1)"
fi
# Not through Steam unless asked (WITH_STEAM=1, for testing Steam integration): Proton finds the Steam client through
# STEAM_COMPAT_CLIENT_INSTALL_PATH and the app id in SteamAppId; with an empty folder and no app id the game's Steam
# helper can't reach Steam (it runs as the GOG/no-Steam game does), and Steam never shows the test run as you playing.
if [ "${WITH_STEAM:-0}" = 1 ]; then
    export STEAM_COMPAT_CLIENT_INSTALL_PATH="$STEAM" SteamAppId=250520 SteamGameId=250520
else
    mkdir -p "$HOME/.cache/underrail-test/no-steam"
    export STEAM_COMPAT_CLIENT_INSTALL_PATH="$HOME/.cache/underrail-test/no-steam"
    unset SteamAppId SteamGameId SteamOverlayGameId SteamClientLaunch STEAM_COMPAT_APP_ID
    export PROTONFIXES_DISABLE=1     # GE-Proton's fixes look the game up by app id (UnderRail needs none)
fi
export STEAM_COMPAT_DATA_PATH="$TEST_PFX" WINEDEBUG="${WINEDEBUG:--all}"     # WINEDEBUG=+keyboard,... launch.sh: Wine debug output into game.log
# Wine processes (wineserver included) running in this run's prefix: only ours, whatever exe they run.
prefix_pids() {
    local p
    for p in $(pgrep -f 'wine|\.exe'); do
        tr '\0' '\n' 2>/dev/null < /proc/$p/environ | grep -Eqx "WINEPREFIX=$TEST_PFX/pfx/?" && echo "$p"
    done
    return 0
}
# This run's game: underrail.exe in this run's prefix (never another session's game or the player's).
game_pid() {
    local p
    for p in $(pgrep -f 'underrail.exe'); do
        case "$(tr '\0' ' ' 2>/dev/null < /proc/$p/cmdline)" in Z:*) ;; *) continue;; esac
        tr '\0' '\n' 2>/dev/null < /proc/$p/environ | grep -Eqx "WINEPREFIX=$TEST_PFX/pfx/?" && { echo "$p"; return 0; }
    done
    return 1
}
# Runs of any session whose gamescope is still alive (setup.sh must not replace the test copy under them).
live_runs() {
    local d
    for d in "$CACHE"/runs/*/; do
        [ -f "$d/pgid" ] && kill -0 "$(cat "$d/pgid")" 2>/dev/null && basename "$d"
    done
    return 0
}
