# gamecopy.sh <folder>: a game copy for test runs (sourced by smoke.sh and coop.sh): the player's original exe and
# DLLs, data linked, then this build (dist/) installed with the test mods (build/Mods). Log=1.
gamecopy() {
    local dir="$1" f
    local orig="$GAME/underrail.exe"; [ -f "$GAME/underrail.exe.backup" ] && orig="$GAME/underrail.exe.backup"
    rm -rf "$dir"; mkdir -p "$dir"; cp "$orig" "$dir/underrail.exe"
    for f in "$GAME"/*.dll; do case "$(basename "$f")" in UnderrailFix*.dll|UnderRailWorks.dll|UnderRailLauncher.dll|0Harmony.dll|System.Runtime.Serialization.Formatters.dll|Mono.Cecil.dll|FNA.dll|NVorbis.dll) ;; *) cp "$f" "$dir/";; esac; done
    for f in "$GAME"/sfmlnet-*.dll.backup; do [ -f "$f" ] && cp "$f" "$dir/$(basename "$f" .backup)"; done
    ln -s "$GAME/data" "$dir/data"
    cp -r "$DIST"/. "$dir/"
    "$dir/underrailworks-patcher" "$dir" >/dev/null || { echo "patcher failed in $dir" >&2; return 1; }
    for f in WorksTest HalfBroken NeedsNewer HelloWorks; do rm -rf "$dir/Mods/$f"; cp -r "$MODS/$f" "$dir/Mods/"; done
    printf '[UnderRailWorks]\nSettingsVersion=3\nLog=1\n' > "$dir/UnderRailWorks.ini"
}
win() { echo "Z:$(echo "$1" | tr / '\\')"; }
