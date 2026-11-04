#!/usr/bin/env bash
# Automated checks on a built dist/UnderRailWorks, run by release.sh (and fine to run by hand after ./build.sh).
# Works on temporary copies of the game folder; the real install is only read.
#   - the Linux patcher, with Mono hidden (it must be self-contained): install, re-run, simulated game update, an
#     existing patched install, uninstall; the original exe and sfmlnet DLLs restored byte for byte, nothing of ours left
#     but the files the player deletes by hand (README)
#   - the layout: underrail.exe is the launcher, UnderRail.Game.exe the patched game, dotnet\ the runtime
#   - the patched game calls exactly the expected set of hooks and references no XNA assembly
#   - the Windows patcher (run with mono) produces the identical game and uninstalls too
#   - the Linux patcher's bundled Mono has no AVX
set -euo pipefail
HERE="$(cd "$(dirname "$0")/.." && pwd)"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
DIST="$HERE/dist/UnderRailWorks"
CECIL_DIR=/usr/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756
[ -d "$DIST" ] || { echo "no $DIST: run ./build.sh first" >&2; exit 1; }

# The stock exe: the backup if the local copy is patched.
ORIG="$GAME/underrail.exe"; [ -f "$GAME/underrail.exe.backup" ] && ORIG="$GAME/underrail.exe.backup"

WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT
mcs -nologo -out:"$WORK/ListHooks.exe" -r:"$CECIL_DIR/Mono.Cecil.dll" -r:System.Core.dll "$HERE/src/Build/ListHooks.cs"
hooks() { MONO_PATH="$CECIL_DIR" mono "$WORK/ListHooks.exe" "$1"; }
fail() { echo "CHECK FAILED: $*" >&2; exit 1; }
md5() { md5sum "$1" | cut -d' ' -f1; }

# The game's managed and native DLLs (the patcher resolves references next to the exe), plus the stock exe.
# The sfmlnet DLLs as the game ships them (the .backup if a local 64-bit install changed them).
game_copy() {     # <folder> [dist folder]
    mkdir -p "$1"
    cp "$ORIG" "$1/underrail.exe"
    for f in "$GAME"/*.dll; do case "$(basename "$f")" in UnderrailFix*.dll|UnderRailWorks.dll|UnderRailLauncher.dll|0Harmony.dll|System.Runtime.Serialization.Formatters.dll|Mono.Cecil.dll|FNA.dll|NVorbis.dll) ;; *) cp "$f" "$1/";; esac; done
    for f in "$GAME"/sfmlnet-*.dll.backup; do [ -f "$f" ] && cp "$f" "$1/$(basename "$f" .backup)"; done
    cp -r "${2:-$DIST}"/. "$1/"
}
STOCK=$(md5 "$ORIG")

EXPECTED="FixStartup.Run
FrameRate.Apply
ResolutionStore.Default
ResolutionStore.Load
ResolutionStore.Save
SaveScreenshot.Limit
SaveScreenshot.Taken
SaveScreenshot.Target
UiScaleFix.BackgroundDraw
UiScaleFix.BackgroundPlace
UiScaleFix.BackgroundSize
UiScaleFix.Begin5
UiScaleFix.Begin6
UiScaleFix.DimRect
UiScaleFix.FloorOrigin
UiScaleFix.FullScreen
UiScaleFix.GetMouse
UiScaleFix.GetScissor
UiScaleFix.InScreen
UiScaleFix.SetScissor
UiScaleFix.ToScreen
UiScaleFix.Transform
UiScaleFix.Virtual
UiScaleFix.VirtualHeight
UiScaleFix.VirtualWidth
UiScaleFix.WorldLocation
UiScaleFix.WorldLocationGet
UnderrailDisplayModes.Get
UnderrailDisplayModes.Label
UnderrailDisplayModes.List
UnderrailDisplayModes.MaxZoom
UnderrailDisplayModes.MinZoom
UnderrailDisplayModes.ShownZoom
UnderrailDisplayModes.ZoomStep
UnderrailDropdownScroll.Closed
UnderrailDropdownScroll.Opened
UnderrailOptions.AddTab
UnderrailOptions.Applied
UnderrailOptions.Save
WindowIcon.Apply
WorldDrawHooks.Add
WorldDrawHooks.AfterPass
WorldDrawHooks.BeforePass
WorldDrawHooks.DrawLayer
WorldDrawHooks.Drawn
WorldDrawHooks.Interactive
WorldDrawHooks.Reverse
WorldDrawHooks.TileExtras
WorldDrawHooks.WorldDone"

EXPECTED64="$(printf '%s\n' "$EXPECTED" "FixMusic.Create" "FixMusic.Dispose" "FixMusic.Play" "FixMusic.SetLoop" "FixMusic.SetPitch" "FixMusic.SetVolume" "FixMusic.Status" "FixMusic.Stop" "FnaMode.SaveTypeName" "FnaMode.SavedName" "FnaMode.WrittenAssembly" "FnaMode.WrittenTypeName" "FnaWindow.Form" "GalaxyBridge..ctor" "FnaWindow.GetBorderStyle" "FnaWindow.GetHeight" "FnaWindow.GetLocation" "FnaWindow.GetWidth" "FnaWindow.Hwnd" "FnaWindow.SetBorderStyle" "FnaWindow.SetLocation" "FnaWindow.SetStartPosition" "SteamBridge.ClearAchievement" "SteamBridge.IncrementStat" "SteamBridge.InitSteam" "SteamBridge.IsOnSteamDeck" "SteamBridge.IsRunningOnWine" "SteamBridge.ResetAllStats" "SteamBridge.SetAchievement" "SteamBridge.ShowKeyboard" "SteamBridge.ShutdownSteam" | LC_ALL=C sort)"
SFML="sfmlnet-audio-2.dll sfmlnet-window-2.dll"
sfml_sums() { (cd "$1" && md5sum $SFML); }
xna_refs() { MONO_PATH="$CECIL_DIR" mono "$WORK/ListRefs.exe" "$1" | grep -c "^Microsoft.Xna" || true; }
cat > "$WORK/ListRefs.cs" <<'CS'
class P { static void Main(string[] a) { foreach (var r in Mono.Cecil.ModuleDefinition.ReadModule(a[0]).AssemblyReferences) System.Console.WriteLine(r.Name); } }
CS
mcs -nologo -out:"$WORK/ListRefs.exe" -r:"$CECIL_DIR/Mono.Cecil.dll" "$WORK/ListRefs.cs"
nomono() { bwrap --ro-bind / / --bind "$WORK" "$WORK" --tmpfs /usr/lib/mono --tmpfs /etc/mono --dev /dev --proc /proc "$@" </dev/null; }
LAUNCHER=$(md5 "$DIST/UnderRailLauncher.exe")
layout() {     # <folder> <what>: an installed folder looks right
    [ "$(md5 "$1/underrail.exe")" = "$LAUNCHER" ] || fail "$2: underrail.exe is not the launcher"
    [ "$(md5 "$1/underrail.exe.backup")" = "$STOCK" ] || fail "$2: the backup is not the original exe"
    [ -f "$1/UnderRail.Game.exe" ] && [ -f "$1/dotnet/dotnet.exe" ] && [ -d "$1/dotnet/shared/Microsoft.WindowsDesktop.App" ] || fail "$2: game or runtime missing"
    [ -f "$1/Mods/UnderRailFixes/UnderRailFixes.dll" ] || fail "$2: the UnderRailFixes mod is missing"
}
uninstalled() {     # <folder> <what>
    [ "$(md5 "$1/underrail.exe")" = "$STOCK" ] || fail "$2: uninstall didn't restore the original exe"
    [ "$(sfml_sums "$1")" = "$SFML_STOCK" ] || fail "$2: uninstall didn't restore the sfmlnet DLLs"
    ls "$1"/*.backup >/dev/null 2>&1 && fail "$2: uninstall left backups"
    [ ! -e "$1/UnderRail.Game.exe" ] && [ ! -e "$1/dotnet" ] && [ ! -e "$1/UnderRailLauncher.exe" ] || fail "$2: uninstall left the game copy, launcher or runtime"
}

echo "== Linux patcher (Mono hidden)"
X="$WORK/linux"; game_copy "$X"
SFML_STOCK="$(sfml_sums "$X")"
nomono "$X/underrailworks-patcher" "$X" >/dev/null || fail "install"
layout "$X" install
diff <(echo "$EXPECTED64") <(hooks "$X/UnderRail.Game.exe") >/dev/null || { diff <(echo "$EXPECTED64") <(hooks "$X/UnderRail.Game.exe") >&2; fail "the patched game's hooks differ from the expected set"; }
[ "$(xna_refs "$X/UnderRail.Game.exe")" = 0 ] || fail "the patched game still references XNA"
[ "$(xna_refs "$X/UnderRailWorks.dll")" = 0 ] || fail "UnderRailWorks.dll references XNA"
for f in $SFML; do [ "$(md5 "$X/$f.backup")" = "$(echo "$SFML_STOCK" | grep " $f" | cut -d' ' -f1)" ] || fail "$f.backup is not the original"; done
FIRST=$(md5 "$X/UnderRail.Game.exe")
nomono "$X/underrailworks-patcher" "$X" >/dev/null || fail "re-run"
[ "$(md5 "$X/UnderRail.Game.exe")" = "$FIRST" ] || fail "re-running the patcher changed the result (patches stacking?)"
layout "$X" re-run
cp "$X/underrail.exe.backup" "$X/underrail.exe"          # a game update / Verify files puts an original back
nomono "$X/underrailworks-patcher" "$X" >/dev/null || fail "patch after game update"
layout "$X" "after a game update"
[ "$(md5 "$X/UnderRail.Game.exe")" = "$FIRST" ] || fail "after a game update: a different patched game"
cp "$X/UnderRail.Game.exe" "$X/underrail.exe"            # an exe patched in place (as UnderRailFixes 1.x did)
nomono "$X/underrailworks-patcher" "$X" >/dev/null || fail "install over a patched exe"
layout "$X" "over a patched exe"
nomono "$X/underrailworks-patcher" "$X" --uninstall >/dev/null || fail "uninstall"
uninstalled "$X" Linux
echo "   ok: install, re-run, game update, over a patched exe, uninstall; $(echo "$EXPECTED64" | wc -l) hooks, no XNA references"

echo "== Windows patcher (run with mono)"
Y="$WORK/windows"; game_copy "$Y"
(cd "$Y" && mono UnderRailWorks-Patcher.exe "$Y" </dev/null >/dev/null) || fail "Windows patcher install"
layout "$Y" "Windows patcher"
[ "$(md5 "$Y/UnderRail.Game.exe")" = "$FIRST" ] || fail "the Windows and Linux patchers produce different games"
(cd "$Y" && mono UnderRailWorks-Patcher.exe "$Y" --uninstall </dev/null >/dev/null) || fail "Windows patcher uninstall"
uninstalled "$Y" Windows
echo "   ok: same patched game as Linux, uninstall restores the originals"

echo "== Linux patcher: baseline x86-64 (no AVX/AVX2/AVX-512, which not every CPU has)"
for f in "$DIST"/linux/*.bin "$DIST"/linux/*.so; do
    n=$(objdump -d --no-show-raw-insn "$f" | grep -c -E '%[yz]mm[0-9]' || true)
    [ "$n" = 0 ] || fail "$(basename "$f") uses AVX/AVX-512 instructions ($n): it was built from a CPU-specific Mono"
done
echo "   ok"
echo "All checks passed."
