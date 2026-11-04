#!/usr/bin/env bash
# setup.sh [--refresh-game] [--refresh-prefix]
# Makes the test copy (~/underrail-test: the Steam folder's files as they are now, i.e. whatever is patched there,
# with data/ symlinked) and the base prefix (~/underrail-test-prefix: a copy of the real one with the game's own
# folder, My Games/Underrail, emptied). launch.sh runs every game in a fresh copy of that base, so each run starts
# with default settings and no saves. Builds the input helper. Safe to re-run.
set -euo pipefail
. "$(dirname "$0")/common.sh"
# The test copy and the base prefix are shared by every session's runs: never replace them under a running game.
busy=$(live_runs)
want_game=0 want_pfx=0
{ [ "${1:-}" = --refresh-game ] || [ "${2:-}" = --refresh-game ] || [ ! -d "$TEST_GAME" ]; } && want_game=1
{ [ "${1:-}" = --refresh-prefix ] || [ "${2:-}" = --refresh-prefix ] || [ ! -d "$BASE_PFX/pfx" ]; } && want_pfx=1
if [ -n "$busy" ] && { [ "$want_game" = 1 ] || [ "$want_pfx" = 1 ]; }; then
    echo "runs still going ($(echo $busy)): refreshing now would change files under them. Stop your own run;" >&2
    echo "wait for other sessions' runs to end (never stop them yourself)." >&2
    exit 1
fi
if [ "$want_game" = 1 ]; then
    mkdir -p "$TEST_GAME"
    for f in "$STEAM_GAME"/*; do
        n=$(basename "$f")
        case "$n" in data|UnderrailFix.ini|UnderrailFix.log) continue;; esac
        rm -rf "$TEST_GAME/$n"; cp -r "$f" "$TEST_GAME/"
    done
    ln -sfn "$STEAM_GAME/data" "$TEST_GAME/data"
    echo "test copy: $TEST_GAME (from $STEAM_GAME)"
fi
if [ "$want_pfx" = 1 ]; then
    rm -rf "$BASE_PFX"; cp -a "$REAL_PFX" "$BASE_PFX"
    echo "base prefix: $BASE_PFX"
fi
# The base never holds the game's settings or saves (also cleans a base made before this rule).
rm -rf "$BASE_PFX/$GAME_DOCS"; mkdir -p "$BASE_PFX/$GAME_DOCS"
mkdir -p "$CACHE"
if [ ! -f "$CACHE/input.exe" ] || [ "$(dirname "$0")/input.cs" -nt "$CACHE/input.exe" ]; then
    mcs -nologo -target:winexe -platform:x86 -out:"$CACHE/input.exe" "$(dirname "$0")/input.cs"
fi
echo "ready (Proton: $PROTON)"
