#!/usr/bin/env bash
# Builds the UnderrailFix release into dist/:
#   UnderrailFix.dll               runtime fixes, loaded by the patched game (all platforms)
#   UnderrailFix-Patcher.exe       Windows patcher (.NET Framework 4, built into Windows) + Mono.Cecil.dll
#   underrailfix-patcher           Linux patcher (single binary with the Mono runtime bundled)
#   UnderrailFix64.dll, FNA.dll, x64/, NVorbis.dll, UnderrailSteamHost.exe   the 64-bit mode (the patcher's default;
#                                  --32bit installs the 32-bit mode, which uses only UnderrailFix.dll)
# ./build.sh install   also patches the local Steam copy.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
GAME="$HOME/.local/share/Steam/steamapps/common/Underrail"
CECIL=/usr/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll
OUT="$HERE/dist/UnderrailFix"
rm -rf "$HERE/dist" && mkdir -p "$OUT" "$HERE/build"

# Compile the runtime against the stock game exe (the backup when the local copy is already patched).
REF="$GAME/underrail.exe"
[ -f "$GAME/underrail.exe.backup" ] && REF="$GAME/underrail.exe.backup"
# XNA types come from FNA at compile time; Retarget then points them at the XNA 4.0 assemblies the game uses.
FNA="$(ls -d "$HOME"/.local/share/Steam/compatibilitytools.d/*/files/share/wine/mono/wine-mono-*/lib/mono/gac/WineMono.FNA/*/WineMono.FNA.dll \
    /usr/share/steam/compatibilitytools.d/*/files/share/wine/mono/wine-mono-*/lib/mono/gac/WineMono.FNA/*/WineMono.FNA.dll 2>/dev/null | sort -V | tail -1)"
[ -n "$FNA" ] || { echo "WineMono.FNA.dll not found (comes with any Proton install)" >&2; exit 1; }
# NVorbis 0.8.6 (Ms-PL, no dependencies): the Ogg Vorbis decoder for 64-bit music (Music.cs). Fetched from NuGet
# once and checked against a known hash, so the repo needn't hold a binary.
NVORBIS="$HERE/lib/NVorbis.dll"
NVORBIS_SHA=dd049b810f39240a5405ff9eda8525c6b74065f1a032a1fb8350762bd1be9c3b
if [ ! -f "$NVORBIS" ]; then
    mkdir -p "$HERE/lib" "$HERE/build/nvorbis"
    curl -fsSL https://www.nuget.org/api/v2/package/NVorbis/0.8.6 -o "$HERE/build/nvorbis/pkg.zip"
    unzip -qo "$HERE/build/nvorbis/pkg.zip" lib/net35/NVorbis.dll -d "$HERE/build/nvorbis"
    cp "$HERE/build/nvorbis/lib/net35/NVorbis.dll" "$NVORBIS"
fi
echo "$NVORBIS_SHA  $NVORBIS" | sha256sum -c --quiet - || { echo "NVorbis.dll doesn't match the expected hash" >&2; rm -f "$NVORBIS"; exit 1; }
# Harmony 2.3.6 (MIT): runtime method patching for mods (UnderRailWorks proof of concept, ModLoader.cs). Same fetch as NVorbis.
HARMONY="$HERE/lib/0Harmony.dll"
HARMONY_SHA=e605ac76a9139ddc09a0cecd8a32cbb296a58ea4aae424f6af5d3a29eeb30aec
if [ ! -f "$HARMONY" ]; then
    mkdir -p "$HERE/lib" "$HERE/build/harmony"
    curl -fsSL https://www.nuget.org/api/v2/package/Lib.Harmony/2.3.6 -o "$HERE/build/harmony/pkg.zip"
    unzip -qo "$HERE/build/harmony/pkg.zip" lib/net452/0Harmony.dll -d "$HERE/build/harmony"
    cp "$HERE/build/harmony/lib/net452/0Harmony.dll" "$HARMONY"
fi
echo "$HARMONY_SHA  $HARMONY" | sha256sum -c --quiet - || { echo "0Harmony.dll doesn't match the expected hash" >&2; rm -f "$HARMONY"; exit 1; }
# The game's SFML.Net bindings, for the 32-bit music path (the backup when the local copy was patched for 64-bit).
SFML=""
for lib in sfmlnet-audio-2 sfmlnet-window-2; do
    f="$GAME/$lib.dll"; [ -f "$f.backup" ] && f="$f.backup"; SFML="$SFML -r:$f"
done
mcs -nologo -optimize+ -target:library -out:"$OUT/UnderrailFix.dll" -r:"$REF" -r:"$FNA" -r:"$NVORBIS" -r:"$HARMONY" $SFML -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Configuration.dll "$HERE"/src/Runtime/*.cs
cp "$OUT/UnderrailFix.dll" "$HERE/build/UnderrailFix.fna.dll"     # before Retarget: what mods compile against
mcs -nologo -out:"$HERE/build/Retarget.exe" -r:"$CECIL" -r:System.Core.dll "$HERE/src/Build/Retarget.cs"
MONO_PATH="$(dirname "$CECIL")" mono "$HERE/build/Retarget.exe" "$OUT/UnderrailFix.dll" "$REF"
cp "$CECIL" "$OUT/"

# Mods: Harmony next to the game, the example mod in Mods/ (built against the stock exe and Harmony only, so it runs
# in both modes). BrokenMod (throws on load) is built into build/ for testing only.
cp "$HARMONY" "$OUT/"
mkdir -p "$HERE/build/Mods/HelloWorks" "$HERE/build/Mods/BrokenMod"
mcs -nologo -optimize+ -target:library -out:"$HERE/build/Mods/HelloWorks/HelloWorks.dll" -r:"$REF" -r:"$HARMONY" "$HERE/examples/HelloWorks/HelloWorks.cs"
# HelloItems (the item overlay spike) also into build/ only: its item file is made from the player's own game data.
mkdir -p "$HERE/build/Mods/HelloItems"
mcs -nologo -optimize+ -target:library -out:"$HERE/build/Mods/HelloItems/HelloItems.dll" -r:"$REF" -r:"$HARMONY" "$HERE/examples/HelloItems/HelloItems.cs"
mcs -nologo -optimize+ -target:library -out:"$HERE/build/Mods/BrokenMod/BrokenMod.dll" -r:"$HARMONY" "$HERE/examples/BrokenMod/BrokenMod.cs"

# The 64-bit mode: a copy of the runtime pointed at FNA, FNA.dll with its native libraries in x64/ (tools/fetch-fna.sh
# fills lib/fna once), the music decoder and the 32-bit Steam helper.
OUT64="$OUT"
mkdir -p "$OUT64/x64"
cp "$NVORBIS" "$OUT64/"
# The helper also runs GOG Galaxy for the GOG version: compiled against the game's GalaxyCSharp.dll (loaded from the game
# folder at run time, only on GOG).
mcs -nologo -optimize+ -platform:x86 -out:"$OUT64/UnderrailSteamHost.exe" -r:"$GAME/GalaxyCSharp.dll" "$HERE/src/SteamHost/SteamHost.cs"
[ -f "$HERE/lib/fna/FNA.dll" ] || "$HERE/tools/fetch-fna.sh"
cp "$HERE/lib/fna/FNA.dll" "$OUT64/"
cp "$HERE"/lib/fna/x64/*.dll "$OUT64/x64/"
MONO_PATH="$(dirname "$CECIL")" mono "$HERE/build/Retarget.exe" --fna "$OUT/UnderrailFix.dll" "$OUT64/FNA.dll" "$OUT64/UnderrailFix64.dll"

# The UnderRailFixes mod (Occluded characters): compiled like the runtime, then pointed at our FNA and UnderrailFix64.
mkdir -p "$OUT/Mods/UnderRailFixes"
mcs -nologo -optimize+ -target:library -out:"$HERE/build/UnderRailFixes.dll" -r:"$REF" -r:"$FNA" -r:"$HERE/build/UnderrailFix.fna.dll" -r:"$HARMONY" -r:System.Core.dll "$HERE"/mods/UnderRailFixes/*.cs
MONO_PATH="$(dirname "$CECIL")" mono "$HERE/build/Retarget.exe" --fna-mod "$HERE/build/UnderRailFixes.dll" "$OUT64/FNA.dll" "$OUT/Mods/UnderRailFixes/UnderRailFixes.dll"
cp "$HERE/mods/UnderRailFixes/mod.json" "$OUT/Mods/UnderRailFixes/"

# The Linux patcher bundles a Mono runtime: the Mono project's own Ubuntu 20.04 build of Mono 6.12 (baseline x86-64,
# glibc 2.29, so it runs on Ubuntu 20.04+, Debian 11+, SteamOS...), never the build machine's: that may need a newer
# glibc (Arch's: 2.38) or be built for newer CPUs (CachyOS's x86-64-v4 mono has AVX-512 and crashed on Zen 3).
# Fetched once into lib/mono-generic, each package checked against its known hash.
MONO_URL=https://download.mono-project.com/repo/ubuntu/pool/main/m/mono
MONO_VER=6.12.0.200-0xamarin2+ubuntu2004b1
MONO_GENERIC="$HERE/lib/mono-generic"
if [ ! -f "$MONO_GENERIC/mono-sgen" ] || [ ! -f "$MONO_GENERIC/libmono-native.so" ]; then
    rm -rf "$HERE/build/mono-pkg"; mkdir -p "$HERE/build/mono-pkg/x" "$MONO_GENERIC"
    for p in "mono-runtime-sgen 1604a2a7f241be38265931dbe8af0ab6638bc98cf7d2dccf25e4f1fe7b357ce0" \
             "mono-runtime-common dc92fc399d9c75b97e4ce1e43de98837311504cd36dcdee538aae4875818c7f2"; do
        set -- $p
        deb="$HERE/build/mono-pkg/${1}_${MONO_VER}_amd64.deb"
        curl -fsSL -o "$deb" "$MONO_URL/${1}_${MONO_VER/+/%2B}_amd64.deb"
        echo "$2  $deb" | sha256sum -c --quiet - || { echo "$(basename "$deb") doesn't match the expected hash" >&2; exit 1; }
        (cd "$HERE/build/mono-pkg/x" && bsdtar -xOf "$deb" 'data.tar.*' | bsdtar -xf -)
    done
    cp "$HERE/build/mono-pkg/x/usr/bin/mono-sgen" "$MONO_GENERIC/mono-sgen"
    cp "$HERE/build/mono-pkg/x/usr/lib/libmono-native.so.0.0.0" "$MONO_GENERIC/libmono-native.so"
fi

# The patchers: Windows (.NET Framework 4) and Linux. The Linux one is the patcher, Cecil and the Mono runtime bundled
# into one executable (no Mono install needed), plus libmono-native.so (System.Native: file and console I/O), which
# mkbundle --simple can't load from inside the bundle. The launch script puts its own folder on the library path;
# src/Bundle/mono.config maps System.Native to the file.
patchers() {     # <dist folder> <Windows exe name> <Linux name> [mcs flags]
    local dir="$1" exe="$2" name="$3"; shift 3
    mcs -nologo -optimize+ "$@" -out:"$dir/$exe" -r:"$CECIL" -r:System.Core.dll "$HERE"/src/Installer/*.cs
    mkdir -p "$dir/linux"
    mkbundle --simple --runtime "$MONO_GENERIC/mono-sgen" --i18n none --deps --machine-config /etc/mono/4.5/machine.config --config "$HERE/src/Bundle/mono.config" \
        -L "$(dirname "$CECIL")" -L /usr/lib/mono/4.5 -L /usr/lib/mono/4.5/Facades -o "$dir/linux/$name.bin" "$dir/$exe" "$dir/Mono.Cecil.dll" >/dev/null
    cp "$MONO_GENERIC/libmono-native.so" "$dir/linux/libmono-native.so"
    cat > "$dir/$name" <<SH
#!/bin/sh
# Linux patcher for UnderRailFixes. Usage: ./$name [game folder] [--32bit] [--uninstall]
here="\$(cd "\$(dirname "\$0")" && pwd)"
# No folder given: the folder this script is in (it's meant to be copied into the Underrail folder).
case " \$* " in *" /"*|*" ."*|*" ~"*) ;; *) [ -f "\$here/underrail.exe" ] && set -- "\$here" "\$@" ;; esac
# TERM=dumb: Mono 6.12 can't read the newer terminfo format (ncurses 6.1+, e.g. xterm-256color) and crashes at the
# first console write (mono/mono#21862); with TERM=dumb it doesn't read terminfo at all. The patcher only prints lines.
TERM=dumb LD_LIBRARY_PATH="\$here/linux\${LD_LIBRARY_PATH:+:\$LD_LIBRARY_PATH}" exec "\$here/linux/$name.bin" "\$@"
SH
    chmod +x "$dir/$name" "$dir/linux/$name.bin"
    cp "$HERE/README.md" "$dir/"
}
patchers "$OUT" UnderrailFix-Patcher.exe underrailfix-patcher
# Windows: a double-click way to the 32-bit mode.
printf '@echo off\r\nrem Installs UnderRailFixes in the 32-bit mode (the game'"'"'s own Microsoft XNA) instead of the default 64-bit mode.\r\n"%%~dp0UnderrailFix-Patcher.exe" "%%~dp0." --32bit\r\npause\r\n' > "$OUT/UnderrailFix-Patcher-32bit.bat"
echo "Built $OUT"

if [ "${1:-}" = install ]; then
    if pgrep -ix 'underrail.exe' >/dev/null; then echo "Close Underrail first." >&2; exit 1; fi
    cp "$OUT/UnderrailFix.dll" "$GAME/"
    "$OUT/underrailfix-patcher" "$GAME"
fi
