#!/usr/bin/env bash
# Save compatibility check for the .NET 10 build: every file the game writes must load in the stock game.
#   1. round trip: each save (and config.dat) loaded and written again on Windows .NET 10 under Proton by the game's
#      own code, with the Works runtime's compatibility layer (RoundTrip);
#   2. --play: each save loaded in the .NET 10 game, played 30 s and saved from the menu (objects .NET 10 creates
#      itself; the StringEqualityComparer bug only showed there), 4 games at a time;
#   3. every file from 1 and 2 loaded by the stock game's loader on Proton's Wine Mono (StockCheck), and scanned for
#      modern names (System.Private.CoreLib, .NET 10 versions, StringEqualityComparer).
# Needs: ~/underrail-works-stock (stock game, data symlinked), ~/underrail-net10 (the .NET 10 build installed),
# ~/.dotnet10 (SDK) and ~/.dotnet10-win (Windows runtime), the run-underrail skill. Usage: check-saves.sh [--play]
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
SKILL=~/underrail/.claude/skills/run-underrail/scripts
STOCK=~/underrail-works-stock NET10=~/underrail-net10
SAVES="$HOME/.local/share/Steam/steamapps/compatdata/250520/pfx/drive_c/users/steamuser/Documents/My Games/Underrail"
WORK="${WORK:-$HOME/.cache/underrail-test/savecheck}"; PFX="$WORK/prefix"
. "$SKILL/common.sh" >/dev/null 2>&1
mkdir -p "$WORK"; [ -d "$PFX/pfx" ] || cp -a ~/underrail-test-prefix "$PFX"
win() { echo "Z:$(echo "$1" | tr / '\\')"; }
proton_run() { STEAM_COMPAT_CLIENT_INSTALL_PATH=$STEAM STEAM_COMPAT_DATA_PATH=$PFX WINEDEBUG=-all timeout 900 nice -n 19 python3 "$PROTON" run "$@" >/dev/null 2>&1 || true; }

# Tools.
(cd "$NET10" && mcs -nologo -platform:x86 -out:"$WORK/StockCheck.exe" -r:UnderRail.Game.exe "$HERE/StockCheck.cs")
cp "$WORK/StockCheck.exe" "$STOCK/"
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 nice -n 19 ~/.dotnet10/dotnet build -c Release -p:GAME="$NET10" -o "$WORK/roundtrip" "$HERE/RoundTrip" >/dev/null

# Inputs: the player's saves and config.dat (copies; never written).
rm -rf "$WORK/orig" "$WORK/rt" "$WORK/played"; mkdir -p "$WORK/orig"
cp -r "$SAVES/Saves/"*/ "$WORK/orig/"; cp "$SAVES/config.dat" "$WORK/orig/"

echo "== 1. round trip on .NET 10"
args=(); for a in "$WORK/orig/"*; do args+=("$(win "$a")"); done
proton_run "$HOME/.dotnet10-win/dotnet.exe" "$(win "$WORK/roundtrip/RoundTrip.dll")" "$(win "$NET10")" "$(win "$WORK/rt")" "${args[@]}"
grep -v "^OK\|fnalibs" "$WORK/rt/results.txt"

checked=("$WORK/rt/"*/ "$WORK/rt/config.dat")
if [ "${1:-}" = --play ]; then
    echo "== 2. saves made by the .NET 10 game"
    names=(); for d in "$WORK/orig/"*/; do names+=("$(basename "$d")"); done
    for ((i = 0; i < ${#names[@]}; i += 4)); do
        for ((j = i; j < i + 4 && j < ${#names[@]}; j++)); do RUN=play$j "$HERE/playsave.sh" "${names[j]}" "$WORK/played" & sleep 3; done; wait
    done
    checked+=("$WORK/played/"*/)
fi

echo "== 3. stock loader (Wine Mono) and names"
args=(); for a in "${checked[@]}"; do args+=("$(win "$a")"); done
proton_run "$STOCK/StockCheck.exe" "$(win "$WORK/stock-results.txt")" "${args[@]}"
grep -v "^OK" "$WORK/stock-results.txt"
python3 -I "$HERE/scan_names.py" "${checked[@]}"
