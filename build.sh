#!/usr/bin/env bash
# Builds UnderRailWorks into dist/UnderRailWorks/:
#   UnderRailWorks.dll             the runtime (engine fixes, mod loader, API), loaded by the patched game
#   UnderRailLauncher.*, dotnet/   the .NET 10 launcher (installed as underrail.exe) and the Windows .NET 10 runtime
#   FNA.dll, x64/, NVorbis.dll, UnderrailSteamHost.exe, 0Harmony.dll   the 64-bit game's libraries
#   Mods/UnderRailFixes/           the UnderRailFixes mod (Occluded characters)
#   UnderRailWorks-Patcher.exe     Windows patcher (.NET Framework 4) + Mono.Cecil.dll
#   underrailworks-patcher         Linux patcher (single binary with the Mono runtime bundled)
# ./build.sh install   also patches the local Steam copy.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
GAME="$HOME/.local/share/Steam/steamapps/common/Underrail"
CECIL=/usr/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll
OUT="$HERE/dist/UnderRailWorks"
DOTNET="${DOTNET:-$HOME/.dotnet10/dotnet}"     # the .NET 10 SDK (for the launcher)
rm -rf "$HERE/dist" && mkdir -p "$OUT" "$HERE/build"

# Compile the runtime against the stock game exe (the backup when the local copy is already patched).
REF="$GAME/underrail.exe"
[ -f "$GAME/underrail.exe.backup" ] && REF="$GAME/underrail.exe.backup"
# Our FNA (tools/fetch-fna.sh fills lib/fna once): the game runs on it, and Works and mods compile against it.
[ -f "$HERE/lib/fna/FNA.dll" ] || "$HERE/tools/fetch-fna.sh"
FNA="$HERE/lib/fna/FNA.dll"
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
# Harmony 2.4.3 (MIT): runtime method patching for mods (ModLoader.cs, CoreCompat.cs). Its .NET Framework build to
# compile against, its .NET 10 build to ship. Same fetch as NVorbis.
HARMONY_PKG="$HERE/lib/harmony-2.4.3.nupkg"
HARMONY_SHA=f07af11a0d62447e7e1b6e3fcf1e28fc75d9f8d1abf2d6ee79aabcdc4db43e88
if [ ! -f "$HARMONY_PKG" ]; then
    mkdir -p "$HERE/lib"
    curl -fsSL https://www.nuget.org/api/v2/package/Lib.Harmony/2.4.3 -o "$HARMONY_PKG"
fi
echo "$HARMONY_SHA  $HARMONY_PKG" | sha256sum -c --quiet - || { echo "Lib.Harmony 2.4.3 doesn't match the expected hash" >&2; rm -f "$HARMONY_PKG"; exit 1; }
mkdir -p "$HERE/build/harmony"
unzip -qo "$HARMONY_PKG" lib/net452/0Harmony.dll lib/net10.0/0Harmony.dll -d "$HERE/build/harmony"
HARMONY="$HERE/build/harmony/lib/net452/0Harmony.dll"
# A reference copy of the game exe with its XNA references pointed at FNA (src/Build/Retarget.cs): Works and mods
# compile against it and FNA.dll directly, as the game runs.
GAMEREF="$HERE/build/ref/underrail.exe"
mcs -nologo -out:"$HERE/build/Retarget.exe" -r:"$CECIL" -r:System.Core.dll "$HERE/src/Build/Retarget.cs"
MONO_PATH="$(dirname "$CECIL")" mono "$HERE/build/Retarget.exe" "$REF" "$FNA" "$GAMEREF" >/dev/null
# The runtime (assembly UnderRailWorks), straight into dist.
WORKS="$OUT/UnderRailWorks.dll"
mcs -nologo -optimize+ -target:library -out:"$WORKS" -r:"$GAMEREF" -r:"$FNA" -r:"$NVORBIS" -r:"$HARMONY" -r:System.Core.dll -r:System.Drawing.dll -r:System.Windows.Forms.dll -r:System.Configuration.dll "$HERE"/src/Runtime/*.cs
# A mod: against FNA and Works only, never the game (a mod that uses Works' API can't reach the game's scrambled names,
# which change between game versions; any it used would fail to compile here). mod <name> <into> <source dir or file>
mod() {
    local name="$1" into="$2" src="$3"
    mkdir -p "$into/$name"
    if [ -d "$src" ]; then set -- "$src"/*.cs; else set -- "$src"; fi
    mcs -nologo -optimize+ -target:library -out:"$into/$name/$name.dll" -r:"$FNA" -r:"$WORKS" -r:System.Core.dll "$@"
    local dir="$src"; [ -d "$dir" ] || dir="$(dirname "$src")"
    # mod.json and everything else the mod ships (sounds, pictures, i18n/, content/...): all but the source.
    if [ -d "$src" ]; then (cd "$src" && find . -type f ! -name '*.cs' ! -name 'settings.ini' -exec cp --parents {} "$into/$name/" \;); else cp "$dir/mod.json" "$into/$name/"; fi
}
cp "$CECIL" "$OUT/"

# Harmony next to the game (its .NET 10 build). The examples (HelloWorks, HelloItems, BrokenMod: throws on load) are
# built into build/Mods for testing, not shipped.
cp "$HERE/build/harmony/lib/net10.0/0Harmony.dll" "$OUT/"
mkdir -p "$HERE/build/Mods/HelloWorks" "$HERE/build/Mods/BrokenMod"
mcs -nologo -optimize+ -target:library -out:"$HERE/build/Mods/HelloWorks/HelloWorks.dll" -r:"$GAMEREF" -r:"$HARMONY" "$HERE/examples/HelloWorks/HelloWorks.cs"
# HelloItems (the item overlay spike) also into build/ only: its item file is made from the player's own game data.
mkdir -p "$HERE/build/Mods/HelloItems"
mcs -nologo -optimize+ -target:library -out:"$HERE/build/Mods/HelloItems/HelloItems.dll" -r:"$GAMEREF" -r:"$HARMONY" "$HERE/examples/HelloItems/HelloItems.cs"
mcs -nologo -optimize+ -target:library -out:"$HERE/build/Mods/BrokenMod/BrokenMod.dll" -r:"$HARMONY" "$HERE/examples/BrokenMod/BrokenMod.cs"

# The game's libraries: FNA.dll with its native libraries in x64/, the music decoder and the 32-bit Steam helper.
OUT64="$OUT"
mkdir -p "$OUT64/x64"
cp "$NVORBIS" "$OUT64/"
# The helper also runs GOG Galaxy for the GOG version: compiled against the game's GalaxyCSharp.dll (loaded from the game
# folder at run time, only on GOG).
# Against .NET Framework's reference assemblies, not Arch's Mono (whose mscorlib has types .NET keeps elsewhere:
# Queue<T> is in System.dll; built against Mono's, the helper died loading GalaxyRunner under Wine Mono).
API=/usr/lib/mono/4.8-api
mcs -nologo -optimize+ -platform:x86 -nostdlib -r:"$API/mscorlib.dll" -r:"$API/System.dll" -r:"$API/System.Core.dll" -out:"$OUT64/UnderrailSteamHost.exe" -r:"$GAME/GalaxyCSharp.dll" "$HERE/src/SteamHost/SteamHost.cs"
cp "$FNA" "$OUT64/"
cp "$HERE"/lib/fna/x64/*.dll "$OUT64/x64/"

# The shipped mods, into dist's Mods/.
for m in UnderRailFixes Minimap QuestTracker WordGetsAround DifficultyDial AfterActionReport ContractBoard HudLayout VendorDirectory FairTrade NeuralReallocation MovableWindows MerchantLedger StatusBoard Loadouts Pacing WatchfulEyes ThePit Scanner FieldMedic BountyHunter Beastmaster Hunger Ambushes; do mod "$m" "$OUT/Mods" "$HERE/mods/$m"; done
cp "$HERE/mods/FieldMedic/treat.wav" "$OUT/Mods/FieldMedic/"
# HelloUI (the UI toolkit and ModConfig example): built like UnderRailFixes, into build/Mods for testing.
mod HelloUI "$HERE/build/Mods" "$HERE/examples/HelloUI"
# HelloQuest (quests, dialogs, variables): built without the game, into build/Mods for testing.
mod HelloQuest "$HERE/build/Mods" "$HERE/examples/HelloQuest"
# TheStranger (a placed character, a dialog and a quest): built without the game, into build/Mods for testing.
mod TheStranger "$HERE/build/Mods" "$HERE/examples/TheStranger"
# TheProspector: a content pack (JSON only, no code), into build/Mods for testing.
rm -rf "$HERE/build/Mods/TheProspector"; cp -r "$HERE/examples/TheProspector" "$HERE/build/Mods/TheProspector"
# WorksTest (test keys, never shipped): into build/Mods.
mod WorksTest "$HERE/build/Mods" "$HERE/tools/testmods/WorksTest"
rm -rf "$HERE/build/Mods/WorksTest/content"; cp -r "$HERE/tools/testmods/WorksTest/content" "$HERE/build/Mods/WorksTest/"; cp -r "$HERE/tools/testmods/WorksTest/i18n" "$HERE/tools/testmods/WorksTest/music" "$HERE/build/Mods/WorksTest/"

# The .NET 10 launcher (installed as underrail.exe; src/Launcher) and the Windows .NET 10 runtime it runs the game on
# (Microsoft.NETCore.App + Microsoft.WindowsDesktop.App, from Microsoft's releases, each checked against the SHA-512
# Microsoft publishes), in dotnet/.
DOTNET_VER=10.0.14
DOTNET_WIN="$HERE/lib/dotnet-win-$DOTNET_VER"
if [ ! -f "$DOTNET_WIN/dotnet.exe" ]; then
    rm -rf "$HERE/build/dotnet-zip" "$DOTNET_WIN"; mkdir -p "$HERE/build/dotnet-zip" "$DOTNET_WIN"
    for p in "Runtime/$DOTNET_VER/dotnet-runtime-$DOTNET_VER-win-x64.zip a176095f1db69b42c81acc60940744078d6bf2d2ac643e418342aa9c81889f4bd13e42f4621b131f9e77e4bc781ad46e375956219df9a4f979031d2a5a8d93cd" \
             "WindowsDesktop/$DOTNET_VER/windowsdesktop-runtime-$DOTNET_VER-win-x64.zip 3214913702264452ac4b283b6fb2f6c14ab748cea36c7561608245c4a7d8cc10436a160032038865d039a016eab4975f65f1ae906de7615953fbec760f764ac4"; do
        set -- $p
        zip="$HERE/build/dotnet-zip/$(basename "$1")"
        curl -fsSL -o "$zip" "https://builds.dotnet.microsoft.com/dotnet/$1"
        echo "$2  $zip" | sha512sum -c --quiet - || { echo "$(basename "$zip") doesn't match the expected hash" >&2; exit 1; }
        unzip -qo "$zip" -d "$DOTNET_WIN"
    done
fi
rm -rf "$OUT/dotnet"; cp -r "$DOTNET_WIN" "$OUT/dotnet"
DOTNET_CLI_TELEMETRY_OPTOUT=1 DOTNET_NOLOGO=1 "$DOTNET" publish "$HERE/src/Launcher" -c Release -o "$HERE/build/launcher" >/dev/null
for f in UnderRailLauncher.exe UnderRailLauncher.dll UnderRailLauncher.runtimeconfig.json UnderRailLauncher.deps.json System.Runtime.Serialization.Formatters.dll; do
    cp "$HERE/build/launcher/$f" "$OUT/"
done

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
# Linux patcher for UnderRailWorks. Usage: ./$name [game folder] [--uninstall]
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
# The cookbook's examples must build against the current API (not shipped), then docs/Cookbook.md from them.
mod Cookbook "$HERE/build/Mods" "$HERE/tools/cookbook/Cookbook.cs"
python3 "$HERE/tools/cookbook/cookbook.py" >/dev/null || echo "docs/Cookbook.md not written" >&2
# The API reference (docs/API.md) from the source comments.
python3 "$HERE/tools/apidoc/apidoc.py" >/dev/null || echo "docs/API.md not written" >&2
patchers "$OUT" UnderRailWorks-Patcher.exe underrailworks-patcher
echo "Built $OUT"

if [ "${1:-}" = install ]; then
    if pgrep -ix 'underrail.exe' >/dev/null; then echo "Close Underrail first." >&2; exit 1; fi
    "$OUT/underrailworks-patcher" "$GAME"
fi
