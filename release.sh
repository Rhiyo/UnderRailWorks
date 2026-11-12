#!/usr/bin/env bash
# Makes a GitHub release of UnderRailWorks from this machine, the same way every time:
#   ./release.sh 1.0.0 "What changed, in a sentence or a paragraph."
#   ./release.sh 1.0.0 -F notes.md          (description from a file)
#   ./release.sh 1.0.0-preview.1 -F notes.md   (a version with a "-" is published as a pre-release)
# It refuses to run unless the tree is clean, on main or a release/* branch, in sync with origin, and the version
# is new. Then it stamps
# the version, builds from scratch, runs tools/check-release.sh and tools/savecheck/check-saves.sh --play (every save
# written by the build must load in the stock game), packages the zip with the licence notices, writes
# the release notes (your description + game version read from the exe + install steps + checksum), tags, pushes
# and publishes with gh. Any failure stops it before anything is pushed.
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE"
GAME="${GAME:-$HOME/.local/share/Steam/steamapps/common/Underrail}"
CECIL_DIR=/usr/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756
REPO=Rhiyo/UnderRailWorks

VERSION="${1:-}"; shift || true
case "${1:-}" in
    -F) DESCRIPTION="$(cat "${2:?-F needs a file}")";;
    "") echo "usage: ./release.sh X.Y.Z \"description\"  |  ./release.sh X.Y.Z -F notes.md" >&2; exit 1;;
    *) DESCRIPTION="$1";;
esac
die() { echo "release: $*" >&2; exit 1; }

# 1. Preconditions.
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]] || die "version must look like 1.0.0 or 1.0.0-preview.1"
# main, or a release/* branch (fixes for an older release line without what's on main since)
BRANCH="$(git branch --show-current)"
case "$BRANCH" in main|release/*) ;; *) die "not on main or a release/* branch";; esac
[ -z "$(git status --porcelain)" ] || die "working tree not clean (commit or stash first)"
git fetch -q origin
git rev-parse -q --verify "origin/$BRANCH" >/dev/null || die "$BRANCH isn't on GitHub yet (git push -u origin $BRANCH)"
[ "$(git rev-parse HEAD)" = "$(git rev-parse "origin/$BRANCH")" ] || die "$BRANCH is not in sync with origin/$BRANCH (pull/push first)"
git rev-parse -q --verify "refs/tags/v$VERSION" >/dev/null && die "tag v$VERSION already exists"
git ls-remote --exit-code --tags origin "refs/tags/v$VERSION" >/dev/null && die "tag v$VERSION already exists on GitHub"
gh auth status >/dev/null 2>&1 || die "gh is not logged in"
if pgrep -ix 'underrail.exe' >/dev/null; then die "close UnderRail first"; fi

# 2. Version stamp (the patcher writes it into the exe; DLL/exe mismatches are caught at launch). It is committed
# only once everything has passed; on any failure before that the file is put back.
VFILE=src/Runtime/Version.cs
WORK="$(mktemp -d)"
committed=0
cleanup() { [ "$committed" = 1 ] || git checkout -q -- "$VFILE"; rm -rf "$WORK"; }
trap cleanup EXIT
sed -i -E "s/(public const string Value = \")[^\"]*(\";)/\1$VERSION\2/" "$VFILE"
grep -q "Value = \"$VERSION\"" "$VFILE" || die "couldn't set the version in $VFILE"

# 3-4. Build from scratch and check (the API too: changes since the last release must be covered by WorksApi's
# version, docs/Versioning.md).
API_RELEASE=1 ./build.sh
tools/check-release.sh
tools/savecheck/check-saves.sh --play

# 5. Package.
ORIG="$GAME/underrail.exe"; [ -f "$GAME/underrail.exe.backup" ] && ORIG="$GAME/underrail.exe.backup"
mcs -nologo -out:"$WORK/GameVersion.exe" -r:"$CECIL_DIR/Mono.Cecil.dll" -r:System.Core.dll src/Build/GameVersion.cs
GAME_VERSION="$(MONO_PATH="$CECIL_DIR" mono "$WORK/GameVersion.exe" "$ORIG")" || die "couldn't read the game version"
# Works says which game version it was made for (players are told when theirs differs): it must be this one. Changing
# it is a decision (Works checked on that game: GameNames, tools/names/Remap), not something to stamp automatically.
grep -q "ForGame = \"$GAME_VERSION\"" "$VFILE" || die "FixVersion.ForGame in $VFILE isn't the game's version $GAME_VERSION (check Works on it, then update it)"
STEAM_BUILD="$(grep -oP '"buildid"\s+"\K[0-9]+' "$GAME/../../appmanifest_250520.acf" 2>/dev/null || echo unknown)"

NAME="UnderRailWorks-$VERSION"
notice() {     # <title> <url> <licence file>
    echo "=== $1 ==="; echo "$2"; echo; cat "$3"; echo
}
notices() {
    notice "Mono.Cecil (Mono.Cecil.dll, and inside the Linux patcher)" https://github.com/jbevain/cecil src/Release/cecil.txt
    notice "Mono runtime and class libraries (inside the Linux patcher: linux/*.bin, linux/libmono-native.so)" https://github.com/mono/mono src/Release/mono.txt
    notice "NVorbis 0.8.6 (NVorbis.dll)" https://github.com/NVorbis/NVorbis src/Release/nvorbis.txt
    notice "Harmony 2.4.3 (0Harmony.dll)" https://github.com/pardeike/Harmony src/Release/harmony.txt
    notice ".NET 10 runtime (dotnet/, the launcher's apphost and System.Runtime.Serialization.Formatters.dll; its own third-party notices are in dotnet/ThirdPartyNotices.txt)" https://github.com/dotnet/runtime dist/UnderRailWorks/dotnet/LICENSE.txt
    notice "FNA (FNA.dll)" https://github.com/FNA-XNA/FNA lib/fna/licenses/FNA.txt
    notice "FNA: LzxDecoder (inside FNA.dll)" https://github.com/FNA-XNA/FNA lib/fna/licenses/FNA-lzxdecoder.txt
    notice "FNA: MonoXNA code (inside FNA.dll)" https://github.com/FNA-XNA/FNA lib/fna/licenses/FNA-monoxna.txt
    notice "SDL3-CS (inside FNA.dll)" https://github.com/flibitijibibo/SDL3-CS lib/fna/licenses/SDL3-CS.txt
    notice "SDL2-CS (inside FNA.dll)" https://github.com/flibitijibibo/SDL2-CS lib/fna/licenses/SDL2-CS.txt
    notice "Theorafile C# bindings (inside FNA.dll)" https://github.com/FNA-XNA/Theorafile lib/fna/licenses/Theorafile-CS.txt
    notice "dav1dfile C# bindings (inside FNA.dll)" https://github.com/FNA-XNA/dav1dfile lib/fna/licenses/dav1dfile.txt
    notice "FNA3D (x64/FNA3D.dll)" https://github.com/FNA-XNA/FNA3D lib/fna/licenses/FNA3D.txt
    notice "MojoShader (inside x64/FNA3D.dll)" https://github.com/icculus/mojoshader lib/fna/licenses/MojoShader.txt
    notice "FAudio (x64/FAudio.dll)" https://github.com/FNA-XNA/FAudio lib/fna/licenses/FAudio.txt
    notice "SDL3 (x64/SDL3.dll)" https://github.com/libsdl-org/SDL lib/fna/licenses/SDL3.txt
    echo "=== Versions of the above ==="; cat lib/fna/SOURCES.txt
}
mkdir -p dist/release
package() {     # <dist folder> <name> <notices function>
    local pkg="$WORK/$2"
    cp -r "$1" "$pkg"
    cp LICENSE "$pkg/LICENSE.txt"
    { echo "UnderRailWorks includes the following third-party software."; echo; "$3"; } > "$pkg/THIRD-PARTY-NOTICES.txt"
    rm -f "dist/release/$2.zip"
    (cd "$WORK" && zip -qr -y "$HERE/dist/release/$2.zip" "$2")
}
package dist/UnderRailWorks "$NAME" notices
ZIP="dist/release/$NAME.zip"
SHA="$(sha256sum "$ZIP" | cut -d' ' -f1)"

# 6. Release notes.
NOTES="$WORK/notes.md"
cat > "$NOTES" <<EOF
$DESCRIPTION

---

**Built against UnderRail $GAME_VERSION** (Steam build $STEAM_BUILD). On another game version the patcher checks every place it changes first, and stops without changing anything if the game's code moved.

### Install
1. Close the game, extract \`$NAME.zip\` and copy the contents of \`$NAME\` into the UnderRail folder.
2. Windows: run \`UnderRailWorks-Patcher.exe\`. Linux / Steam Deck: run \`./underrailworks-patcher\`.
3. Start the game from Steam as usual. Settings: **Options -> Mods** and \`UnderRailWorks.ini\`; mods go in the \`Mods\` folder.

The game then runs 64-bit on .NET 10 (included, in \`dotnet\`) and FNA. It replaces an UnderRailFixes 1.x install and takes over its settings. Saves stay compatible with the unmodded game. After a game update, run the patcher again. To uninstall, run it with \`--uninstall\` (or Steam's "Verify integrity of game files"). Details in the [README](https://github.com/$REPO#readme).

SHA-256 \`$NAME.zip\`: \`$SHA\`
EOF

# 7. Commit the version (if it changed), tag, push, publish.
# This release's API becomes the baseline the next builds compare with.
MONO_PATH="$(dirname /usr/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll)" mono build/ApiSurface.exe dist/UnderRailWorks/UnderRailWorks.dll api/UnderRailWorks.api.txt >/dev/null
if [ -n "$(git status --porcelain -- "$VFILE" api/UnderRailWorks.api.txt)" ]; then git add api/UnderRailWorks.api.txt; git commit -q -m "Release v$VERSION" -- "$VFILE" api/UnderRailWorks.api.txt; fi
committed=1
git tag -a "v$VERSION" -m "UnderRailWorks $VERSION (UnderRail $GAME_VERSION)"
git push -q origin "$BRANCH" "v$VERSION"
PRE=""; case "$VERSION" in *-*) PRE=--prerelease;; esac
gh release create "v$VERSION" "$ZIP" --repo "$REPO" --title "UnderRailWorks $VERSION" --notes-file "$NOTES" $PRE
echo "Released v$VERSION: https://github.com/$REPO/releases/tag/v$VERSION"
