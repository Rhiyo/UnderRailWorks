#!/usr/bin/env bash
# Automated checks on a built dist/UnderrailFix (one patcher: 64-bit mode by default, --32bit), run by release.sh (and fine to run by
# hand after ./build.sh).
# Works on a temporary copy of the game folder; the real install is only read.
#   - Linux patcher (--32bit), with Mono hidden (it must be self-contained): install, re-run, simulated game update, uninstall
#   - the original exe is restored byte-identical and the backup is removed
#   - the patched exe calls exactly the expected set of hooks
#   - the Windows patcher (run with mono) installs and uninstalls too
#   - the 64-bit mode (the default) likewise, plus: no XNA reference left, the game's sfmlnet DLLs backed up and restored
#     byte for byte, and switching between the 64-bit and 32-bit modes
set -euo pipefail
HERE="$(cd "$(dirname "$0")/.." && pwd)"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
DIST="$HERE/dist/UnderrailFix"
DIST64="$DIST"     # one download: the 64-bit mode by default, the 32-bit mode with --32bit
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
    for f in "$GAME"/*.dll; do case "$(basename "$f")" in UnderrailFix*.dll|Mono.Cecil.dll|FNA.dll|NVorbis.dll) ;; *) cp "$f" "$1/";; esac; done
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

echo "== Linux patcher (Mono hidden)"
L="$WORK/linux"; game_copy "$L"
nomono() { bwrap --ro-bind / / --bind "$WORK" "$WORK" --tmpfs /usr/lib/mono --tmpfs /etc/mono --dev /dev --proc /proc "$@" </dev/null; }
nomono "$L/underrailfix-patcher" "$L" --32bit >/dev/null || fail "install"
[ "$(md5 "$L/underrail.exe.backup")" = "$STOCK" ] || fail "backup is not the original exe"
diff <(echo "$EXPECTED") <(hooks "$L/underrail.exe") >/dev/null || { diff <(echo "$EXPECTED") <(hooks "$L/underrail.exe") >&2; fail "patched exe hooks differ from the expected set"; }
FIRST=$(md5 "$L/underrail.exe")
nomono "$L/underrailfix-patcher" "$L" --32bit >/dev/null || fail "re-run"
[ "$(md5 "$L/underrail.exe")" = "$FIRST" ] || fail "re-running the patcher changed the result (patches stacking?)"
cp "$L/underrail.exe.backup" "$L/underrail.exe"          # a game update / Verify files puts an original back
nomono "$L/underrailfix-patcher" "$L" --32bit >/dev/null || fail "patch after game update"
[ "$(md5 "$L/underrail.exe.backup")" = "$STOCK" ] || fail "backup after game update is not the original"
nomono "$L/underrailfix-patcher" "$L" --32bit --uninstall >/dev/null || fail "uninstall"
[ "$(md5 "$L/underrail.exe")" = "$STOCK" ] || fail "uninstall didn't restore the original exe"
[ ! -e "$L/underrail.exe.backup" ] || fail "uninstall left the backup"
echo "   ok: install, re-run, game update, uninstall; $(echo "$EXPECTED" | wc -l) hooks"

echo "== Windows patcher (run with mono)"
W="$WORK/windows"; game_copy "$W"
(cd "$W" && mono UnderrailFix-Patcher.exe "$W" --32bit </dev/null >/dev/null) || fail "Windows patcher install"
diff <(echo "$EXPECTED") <(hooks "$W/underrail.exe") >/dev/null || fail "Windows patcher: hooks differ"
[ "$(md5 "$W/underrail.exe")" = "$FIRST" ] || fail "Windows and Linux patchers produce different exes"
(cd "$W" && mono UnderrailFix-Patcher.exe "$W" --32bit --uninstall </dev/null >/dev/null) || fail "Windows patcher uninstall"
[ "$(md5 "$W/underrail.exe")" = "$STOCK" ] || fail "Windows patcher uninstall didn't restore the original"
echo "   ok: same patched exe as Linux, uninstall restores the original"
EXPECTED64="$(printf '%s\n' "$EXPECTED" "FixMusic.Create" "FixMusic.Dispose" "FixMusic.Play" "FixMusic.SetLoop" "FixMusic.SetPitch" "FixMusic.SetVolume" "FixMusic.Status" "FixMusic.Stop" "FnaMode.SaveTypeName" "FnaMode.SavedName" "FnaMode.WrittenAssembly" "FnaMode.WrittenTypeName" "FnaWindow.Form" "GalaxyBridge..ctor" "FnaWindow.GetBorderStyle" "FnaWindow.GetHeight" "FnaWindow.GetLocation" "FnaWindow.GetWidth" "FnaWindow.Hwnd" "FnaWindow.SetBorderStyle" "FnaWindow.SetLocation" "FnaWindow.SetStartPosition" "SteamBridge.ClearAchievement" "SteamBridge.IncrementStat" "SteamBridge.InitSteam" "SteamBridge.IsOnSteamDeck" "SteamBridge.IsRunningOnWine" "SteamBridge.ResetAllStats" "SteamBridge.SetAchievement" "SteamBridge.ShowKeyboard" "SteamBridge.ShutdownSteam" | LC_ALL=C sort)"
SFML="sfmlnet-audio-2.dll sfmlnet-window-2.dll"
sfml_sums() { (cd "$1" && md5sum $SFML); }
xna_refs() { MONO_PATH="$CECIL_DIR" mono "$WORK/ListRefs.exe" "$1" | grep -c "^Microsoft.Xna" || true; }
cat > "$WORK/ListRefs.cs" <<'CS'
class P { static void Main(string[] a) { foreach (var r in Mono.Cecil.ModuleDefinition.ReadModule(a[0]).AssemblyReferences) System.Console.WriteLine(r.Name); } }
CS
mcs -nologo -out:"$WORK/ListRefs.exe" -r:"$CECIL_DIR/Mono.Cecil.dll" "$WORK/ListRefs.cs"

echo "== 64-bit Linux patcher (Mono hidden)"
X="$WORK/linux64"; game_copy "$X" "$DIST64"
SFML_STOCK="$(sfml_sums "$X")"
nomono "$X/underrailfix-patcher" "$X" >/dev/null || fail "64-bit install"
[ "$(md5 "$X/underrail.exe.backup")" = "$STOCK" ] || fail "64-bit: backup is not the original exe"
diff <(echo "$EXPECTED64") <(hooks "$X/underrail.exe") >/dev/null || { diff <(echo "$EXPECTED64") <(hooks "$X/underrail.exe") >&2; fail "64-bit: hooks differ from the expected set"; }
[ "$(xna_refs "$X/underrail.exe")" = 0 ] || fail "64-bit: the exe still references XNA"
[ "$(xna_refs "$X/UnderrailFix64.dll")" = 0 ] || fail "64-bit: UnderrailFix64.dll still references XNA"
for f in $SFML; do [ "$(md5 "$X/$f.backup")" = "$(md5 "$L/$f")" ] || fail "64-bit: $f.backup is not the original"; done
FIRST64=$(md5 "$X/underrail.exe")
nomono "$X/underrailfix-patcher" "$X" >/dev/null || fail "64-bit re-run"
[ "$(md5 "$X/underrail.exe")" = "$FIRST64" ] || fail "64-bit: re-running changed the result"
cp -r "$DIST"/. "$X/"                                       # switching back: the standard download over it
nomono "$X/underrailfix-patcher" "$X" --32bit >/dev/null || fail "32-bit over a 64-bit install"
[ "$(md5 "$X/underrail.exe")" = "$FIRST" ] || fail "standard over 64-bit: not the standard patched exe"
[ "$(sfml_sums "$X")" = "$SFML_STOCK" ] || fail "standard over 64-bit: sfmlnet DLLs not restored"
nomono "$X/underrailfix-patcher" "$X" >/dev/null || fail "64-bit over a standard install"
[ "$(md5 "$X/underrail.exe")" = "$FIRST64" ] || fail "64-bit over standard: different exe"
nomono "$X/underrailfix-patcher" "$X" --uninstall >/dev/null || fail "64-bit uninstall"
[ "$(md5 "$X/underrail.exe")" = "$STOCK" ] || fail "64-bit uninstall didn't restore the original exe"
[ "$(sfml_sums "$X")" = "$SFML_STOCK" ] || fail "64-bit uninstall didn't restore the sfmlnet DLLs"
ls "$X"/*.backup >/dev/null 2>&1 && fail "64-bit uninstall left backups"
echo "   ok: install, re-run, standard <-> 64-bit, uninstall; $(echo "$EXPECTED64" | wc -l) hooks, no XNA references"

echo "== 64-bit Windows patcher (run with mono)"
Y="$WORK/windows64"; game_copy "$Y" "$DIST64"
(cd "$Y" && mono UnderrailFix-Patcher.exe "$Y" </dev/null >/dev/null) || fail "64-bit Windows patcher install"
[ "$(md5 "$Y/underrail.exe")" = "$FIRST64" ] || fail "64-bit Windows and Linux patchers produce different exes"
(cd "$Y" && mono UnderrailFix-Patcher.exe "$Y" --uninstall </dev/null >/dev/null) || fail "64-bit Windows patcher uninstall"
[ "$(md5 "$Y/underrail.exe")" = "$STOCK" ] || fail "64-bit Windows patcher uninstall didn't restore the original"
[ "$(sfml_sums "$Y")" = "$SFML_STOCK" ] || fail "64-bit Windows patcher uninstall didn't restore the sfmlnet DLLs"
echo "   ok: same patched exe as Linux, uninstall restores the originals"
echo "== Linux patchers: baseline x86-64 (no AVX/AVX2/AVX-512, which not every CPU has)"
for f in "$DIST"/linux/*.bin "$DIST"/linux/*.so; do
    n=$(objdump -d --no-show-raw-insn "$f" | grep -c -E '%[yz]mm[0-9]' || true)
    [ "$n" = 0 ] || fail "$(basename "$f") uses AVX/AVX-512 instructions ($n): it was built from a CPU-specific Mono"
done
echo "   ok"
echo "All checks passed."
