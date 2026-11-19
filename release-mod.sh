#!/usr/bin/env bash
# Makes a GitHub release of one mod from mods/<name>/ (each mod is its own release; Works itself: release.sh):
#   ./release-mod.sh WallVision -F notes.md          (the version in mods/WallVision/mod.json)
#   ./release-mod.sh WallVision 0.0.2 -F notes.md    (sets mod.json's version first, committed with the release)
#   ./release-mod.sh Coop 0.1.0-preview.1 "notes"    (a version with a "-" is published as a pre-release)
# Same rules as release.sh: clean main or release/* in sync with origin, a new version (tag <name>-v<version>),
# UnderRail closed. It builds from scratch, runs the mod's own tests (MOD_TESTS below; every mod also gets the smoke
# test), packages <name>-<version>.zip (the <name> folder players drop into the game's Mods folder, with LICENSE.txt),
# writes the notes (your description + the Works API version the mod needs + install steps + checksum), tags, pushes
# and publishes with gh, never as the repo's "latest" release (that is Works). Any failure stops it before anything
# is pushed.
#   DRY_RUN=1 ./release-mod.sh WallVision "notes"   builds and packages into dist/release and prints the notes; no
#   checks of git, no commit, tag, push or release (SKIP_TESTS=1 also skips the tests).
set -euo pipefail
HERE="$(cd "$(dirname "$0")" && pwd)"
cd "$HERE"
REPO=Rhiyo/UnderRailWorks
die() { echo "release-mod: $*" >&2; exit 1; }

MOD="${1:-}"; shift || true
[ -n "$MOD" ] || die "usage: ./release-mod.sh <mod folder in mods/> [version] (\"description\" | -F notes.md)"
SRC="mods/$MOD"; JSON="$SRC/mod.json"
[ -f "$JSON" ] || die "no $JSON (released mods live in mods/; examples/ aren't released)"
VERSION=""
case "${1:-}" in -F|"") ;; *) [[ "$1" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]] && { VERSION="$1"; shift; };; esac
case "${1:-}" in
    -F) DESCRIPTION="$(cat "${2:?-F needs a file}")";;
    "") die "a description is needed: \"text\" or -F notes.md";;
    *) DESCRIPTION="$1";;
esac
field() { grep -oP "\"$1\"\s*:\s*\"\K[^\"]*" "$JSON" | head -1; }
[ -n "$VERSION" ] || VERSION="$(field version)"
[[ "$VERSION" =~ ^[0-9]+\.[0-9]+\.[0-9]+(-[0-9A-Za-z.]+)?$ ]] || die "version must look like 1.0.0 or 1.0.0-preview.1 (got \"$VERSION\")"
TITLE="$(field name)"; [ -n "$TITLE" ] || TITLE="$MOD"
API="$(field works)"
TAG="$MOD-v$VERSION"

# Each mod's own tests, beside the smoke test every release runs.
declare -A MOD_TESTS=(
    [WallVision]="tools/smoketest/drawing.sh"
    [Coop]="tools/smoketest/coop.sh tools/smoketest/coop-midfight.sh"
)

# 1. Preconditions.
DRY="${DRY_RUN:-0}"
BRANCH="$(git branch --show-current)"
if [ "$DRY" != 1 ]; then
case "$BRANCH" in main|release/*) ;; *) die "not on main or a release/* branch";; esac
[ -z "$(git status --porcelain)" ] || die "working tree not clean (commit or stash first)"
git fetch -q origin
git rev-parse -q --verify "origin/$BRANCH" >/dev/null || die "$BRANCH isn't on GitHub yet (git push -u origin $BRANCH)"
[ "$(git rev-parse HEAD)" = "$(git rev-parse "origin/$BRANCH")" ] || die "$BRANCH is not in sync with origin/$BRANCH (pull/push first)"
git rev-parse -q --verify "refs/tags/$TAG" >/dev/null && die "tag $TAG already exists"
git ls-remote --exit-code --tags origin "refs/tags/$TAG" >/dev/null && die "tag $TAG already exists on GitHub"
gh auth status >/dev/null 2>&1 || die "gh is not logged in"
fi
if pgrep -ix 'underrail.exe' >/dev/null; then die "close UnderRail first"; fi

# 2. The version into mod.json (committed only once everything passed).
WORK="$(mktemp -d)"
committed=0
cp "$JSON" "$WORK/mod.json.before"     # put back as it was (not from git: a dry run may have local changes)
cleanup() { [ "$committed" = 1 ] || cp "$WORK/mod.json.before" "$JSON"; rm -rf "$WORK"; }
trap cleanup EXIT
sed -i -E "0,/\"version\"[[:space:]]*:[[:space:]]*\"[^\"]*\"/s//\"version\": \"$VERSION\"/" "$JSON"
[ "$(field version)" = "$VERSION" ] || die "couldn't set the version in $JSON"

# 3. Build from scratch and test.
./build.sh
OUT="dist/mods/$MOD"
[ -f "$OUT/$MOD.dll" ] || [ -n "$(ls "$OUT"/*.json 2>/dev/null)" ] || die "the build made nothing in $OUT"
if [ "${SKIP_TESTS:-0}" != 1 ]; then
    tools/smoketest/smoke.sh
    for t in ${MOD_TESTS[$MOD]:-}; do "$t"; done
fi

# 4. Package: the mod's folder as players drop it into Mods.
NAME="$MOD-$VERSION"
mkdir -p dist/release "$WORK/pkg"
cp -r "$OUT" "$WORK/pkg/$MOD"
cp LICENSE "$WORK/pkg/$MOD/LICENSE.txt"
rm -f "dist/release/$NAME.zip"
(cd "$WORK/pkg" && zip -qr -y "$HERE/dist/release/$NAME.zip" "$MOD")
ZIP="dist/release/$NAME.zip"
SHA="$(sha256sum "$ZIP" | cut -d' ' -f1)"

# 5. Notes.
NOTES="$WORK/notes.md"
cat > "$NOTES" <<EOF
$DESCRIPTION

---

**Needs UnderRailWorks** with API ${API:-?} or newer ([Works releases](https://github.com/$REPO/releases?q=UnderRailWorks)).

### Install
1. Install [UnderRailWorks](https://github.com/$REPO#install) first.
2. Close the game and extract \`$NAME.zip\` into the game's \`Mods\` folder (you get \`Mods/$MOD/\`). Replacing an older version keeps your settings (\`settings.ini\`).
3. In game: the main menu's **Mods** button lists it and switches it on or off; its settings are under **Options -> Works**.

SHA-256 \`$NAME.zip\`: \`$SHA\`
EOF

if [ "$DRY" = 1 ]; then echo "dry run: $ZIP"; echo "----- notes -----"; cat "$NOTES"; exit 0; fi

# 6. Commit the version (if it changed), tag, push, publish.
if [ -n "$(git status --porcelain -- "$JSON")" ]; then git commit -q -m "Release $TITLE $VERSION" -- "$JSON"; fi
committed=1
git tag -a "$TAG" -m "$TITLE $VERSION (UnderRailWorks mod)"
git push -q origin "$BRANCH" "$TAG"
PRE=""; case "$VERSION" in *-*) PRE=--prerelease;; esac
gh release create "$TAG" "$ZIP" --repo "$REPO" --title "$TITLE $VERSION" --notes-file "$NOTES" $PRE --latest=false
echo "Released $TAG: https://github.com/$REPO/releases/tag/$TAG"
