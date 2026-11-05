#!/usr/bin/env bash
# Save compatibility check for the .NET 10 build: every file the game writes must load in the stock game.
#   1. round trip: each save (and config.dat) loaded and written again on Windows .NET 10 under Proton by the game's
#      own code, with the Works runtime's compatibility layer (RoundTrip);
#   2. --play: each save loaded in the .NET 10 game, played 30 s and saved from the menu (objects .NET 10 creates
#      itself; the StringEqualityComparer bug only showed there), 4 games at a time;
#   3. every file from 1 and 2 loaded by the stock game's loader on Proton's Wine Mono (StockCheck), and scanned for
#      modern names (System.Private.CoreLib, .NET 10 versions, StringEqualityComparer).
# Builds its own game copies in $WORK: the stock game, and the stock game with dist/UnderRailWorks installed (data\
# symlinked to the Steam copy). Needs a built dist/UnderRailWorks, the .NET 10 SDK ($DOTNET), Proton (found as the
# run-underrail skill finds it) and its base prefix (~/underrail-test-prefix, made by the skill's setup.sh).
# Usage: check-saves.sh [--play]     or: check-saves.sh --only <save folder>... (the stock loader on just those)
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
REPO="$(cd "$HERE/../.." && pwd)"
SKILL="$REPO/.claude/skills/run-underrail/scripts"
DIST="$REPO/dist/UnderRailWorks"
DOTNET="${DOTNET:-$HOME/.dotnet10/dotnet}"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
SAVES="$HOME/.local/share/Steam/steamapps/compatdata/250520/pfx/drive_c/users/steamuser/Documents/My Games/Underrail"
WORK="${WORK:-$HOME/.cache/underrail-test/savecheck}"; PFX="$WORK/prefix"
. "$SKILL/common.sh" >/dev/null 2>&1
mkdir -p "$WORK"; [ -d "$PFX/pfx" ] || cp -a ~/underrail-test-prefix "$PFX"
[ -d "$DIST" ] || { echo "no $DIST: run ./build.sh first" >&2; exit 1; }

# The game copies: stock, and with the build installed.
STOCK="$WORK/stock" NET10="$WORK/net10"
ORIG="$GAME/underrail.exe"; [ -f "$GAME/underrail.exe.backup" ] && ORIG="$GAME/underrail.exe.backup"
for c in "$STOCK" "$NET10"; do
    rm -rf "$c"; mkdir -p "$c"; cp "$ORIG" "$c/underrail.exe"
    for f in "$GAME"/*.dll; do case "$(basename "$f")" in UnderrailFix*.dll|UnderRailWorks.dll|UnderRailLauncher.dll|0Harmony.dll|System.Runtime.Serialization.Formatters.dll|Mono.Cecil.dll|FNA.dll|NVorbis.dll) ;; *) cp "$f" "$c/";; esac; done
    for f in "$GAME"/sfmlnet-*.dll.backup; do [ -f "$f" ] && cp "$f" "$c/$(basename "$f" .backup)"; done
    # No steam_appid.txt: with it the game's Steam API finds the running Steam client by itself (it must never connect).
    ln -s "$GAME/data" "$c/data"
done
cp -r "$DIST"/. "$NET10/"
"$NET10/underrailworks-patcher" "$NET10" >/dev/null
printf '[UnderRailWorks]\nSettingsVersion=3\nUiInsetX=0\nUiInsetY=0\nLog=1\n' > "$NET10/UnderRailWorks.ini"     # playsave.sh's click positions: no insets
win() { echo "Z:$(echo "$1" | tr / '\\')"; }
# Not connected to Steam (common.sh's STEAM_COMPAT_CLIENT_INSTALL_PATH points at an empty folder unless WITH_STEAM=1):
# a save check must never show the user as playing.
proton_run() { STEAM_COMPAT_DATA_PATH=$PFX WINEDEBUG=-all timeout 900 nice -n 19 python3 "$PROTON" run "$@" >/dev/null 2>&1 || true; }

# Tools.
(cd "$NET10" && mcs -nologo -platform:x86 -out:"$WORK/StockCheck.exe" -r:UnderRail.Game.exe "$HERE/StockCheck.cs")
cp "$WORK/StockCheck.exe" "$STOCK/"
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 nice -n 19 "$DOTNET" build -c Release -p:GAME="$NET10" -o "$WORK/roundtrip" "$HERE/RoundTrip" >/dev/null

# --only DIR|FILE...: just the stock side for these save folders or files (e.g. saves or an exported .char a test run
# made with mods), then stop.
if [ "${1:-}" = --only ]; then
    shift; rm -rf "$WORK/only"; mkdir -p "$WORK/only"; for d in "$@"; do cp -r "$d" "$WORK/only/"; done
    args=(); for a in "$WORK/only/"*; do args+=("$(win "$a")"); done
    proton_run "$STOCK/StockCheck.exe" "$(win "$WORK/stock-results.txt")" "${args[@]}"
    cat "$WORK/stock-results.txt"
    python3 -I "$HERE/scan_names.py" "$WORK/only/"*
    grep -q "^result: .* 0 failed" "$WORK/stock-results.txt" || { echo "SAVE CHECK FAILED" >&2; exit 1; }
    echo "Save check passed."; exit 0
fi

# Inputs: the player's saves and config.dat (copies; never written).
rm -rf "$WORK/orig" "$WORK/rt" "$WORK/played"; mkdir -p "$WORK/orig"
cp -r "$SAVES/Saves/"*/ "$WORK/orig/"; cp "$SAVES/config.dat" "$WORK/orig/"

echo "== 1. round trip on .NET 10"
args=(); for a in "$WORK/orig/"*; do args+=("$(win "$a")"); done
proton_run "$NET10/dotnet/dotnet.exe" "$(win "$WORK/roundtrip/RoundTrip.dll")" "$(win "$NET10")" "$(win "$WORK/rt")" "${args[@]}"
grep -v "^OK\|fnalibs" "$WORK/rt/results.txt"

checked=("$WORK/rt/"*/ "$WORK/rt/config.dat")
if [ "${1:-}" = --play ]; then
    echo "== 2. saves made by the .NET 10 game"
    names=(); for d in "$WORK/orig/"*/; do names+=("$(basename "$d")"); done
    for ((i = 0; i < ${#names[@]}; i += 4)); do
        for ((j = i; j < i + 4 && j < ${#names[@]}; j++)); do RUN=play$j TEST_GAME="$NET10" SKILL="$SKILL" "$HERE/playsave.sh" "${names[j]}" "$WORK/played" & sleep 3; done; wait
    done
    checked+=("$WORK/played/"*/)
fi

echo "== 3. stock loader (Wine Mono) and names"
args=(); for a in "${checked[@]}"; do args+=("$(win "$a")"); done
proton_run "$STOCK/StockCheck.exe" "$(win "$WORK/stock-results.txt")" "${args[@]}"
grep -v "^OK" "$WORK/stock-results.txt"
python3 -I "$HERE/scan_names.py" "${checked[@]}"
grep -q "^result: .* 0 failed" "$WORK/stock-results.txt" && ! grep -q "^FAIL" "$WORK/rt/results.txt" || { echo "SAVE CHECK FAILED" >&2; exit 1; }
echo "Save check passed."
