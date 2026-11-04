#!/usr/bin/env bash
# Fetches FNA for the experimental 64-bit mode into lib/fna/ (not committed, like NVorbis):
#   FNA.dll   built here with mcs (against the .NET 4.8 reference assemblies) from the FNA 26.08 release (Ms-PL; the version Wine Mono 11.3 ships), hash-checked
#   x64/      FNA3D.dll, FAudio.dll, SDL3.dll (zlib) from FNA's official fnalibs build (github.com/FNA-XNA/fnalibs-dailies,
#             CI artifact FNALIBS_ARTIFACT, the build tested with UnderRail; needs gh). Hashes go to lib/fna/SOURCES.txt.
#             CI artifacts expire after a while: then pick the newest (gh api repos/FNA-XNA/fnalibs-dailies/actions/
#             artifacts?name=fnalibs), set it here and test the 64-bit mode in game again.
#   licenses/ the licence texts for everything above, for THIRD-PARTY-NOTICES in the 64-bit release.
set -euo pipefail
HERE="$(cd "$(dirname "$0")/.." && pwd)"
DEST="$HERE/lib/fna"
FNA_TAG=26.08
FNALIBS_ARTIFACT="${FNALIBS_ARTIFACT:-13069851379}"
FNA_SHA=217e26bf3215b629c39734954e4f3f9041efd833c4821ba49abee9c3619f10ce
WORK="$(mktemp -d)"; trap 'rm -rf "$WORK"' EXIT

gh release download "$FNA_TAG" --repo FNA-XNA/FNA --pattern "FNA-${FNA_TAG/./}.zip" --dir "$WORK"
echo "$FNA_SHA  $WORK/FNA-${FNA_TAG/./}.zip" | sha256sum -c --quiet -
mkdir -p "$WORK/src" && unzip -q "$WORK/FNA-${FNA_TAG/./}.zip" -d "$WORK/src"
# Against the .NET Framework 4.8 reference assemblies: Mono's own mscorlib has types that .NET keeps elsewhere
# (Queue<T> is in System.dll), and a plain mcs build references them in mscorlib, which fails on .NET and Wine Mono.
mkdir -p "$WORK/bin"
printf '#!/bin/sh\nexec %s -noconfig -nostdlib -lib:/usr/lib/mono/4.8-api -r:mscorlib.dll -r:System.dll -r:System.Core.dll -r:System.Xml.dll -r:System.Drawing.dll "$@"\n' "$(command -v mcs)" > "$WORK/bin/mcs"
chmod +x "$WORK/bin/mcs"
PATH="$WORK/bin:$PATH" make -s -C "$WORK/src/FNA" release >/dev/null 2>&1 || { echo "building FNA failed" >&2; exit 1; }

ARTIFACT="$FNALIBS_ARTIFACT $(gh api "repos/FNA-XNA/fnalibs-dailies/actions/artifacts/$FNALIBS_ARTIFACT" --jq '.created_at')"
gh api "repos/FNA-XNA/fnalibs-dailies/actions/artifacts/$FNALIBS_ARTIFACT/zip" > "$WORK/fnalibs.zip"
mkdir -p "$WORK/libs" && unzip -q "$WORK/fnalibs.zip" -d "$WORK/libs"

rm -rf "$DEST"; mkdir -p "$DEST/x64"
cp "$WORK/src/FNA/bin/Release/FNA.dll" "$DEST/"
cp "$WORK/libs/x64/"{FNA3D,FAudio,SDL3}.dll "$DEST/x64/"
L="$WORK/src/FNA"; mkdir -p "$DEST/licenses"
cp "$L/licenses/LICENSE" "$DEST/licenses/FNA.txt"
cp "$L/licenses/lzxdecoder.LICENSE" "$DEST/licenses/FNA-lzxdecoder.txt"
cp "$L/licenses/monoxna.LICENSE" "$DEST/licenses/FNA-monoxna.txt"
cp "$L/lib/FNA3D/LICENSE" "$DEST/licenses/FNA3D.txt"
cp "$L/lib/FNA3D/MojoShader/LICENSE.txt" "$DEST/licenses/MojoShader.txt"
cp "$L/lib/FAudio/LICENSE" "$DEST/licenses/FAudio.txt"
cp "$L/lib/SDL3-CS/LICENSE" "$DEST/licenses/SDL3-CS.txt"
cp "$L/lib/SDL2-CS/LICENSE" "$DEST/licenses/SDL2-CS.txt"
cp "$L/lib/Theorafile/csharp/LICENSE" "$DEST/licenses/Theorafile-CS.txt"
cp "$L/lib/dav1dfile/LICENSE" "$DEST/licenses/dav1dfile.txt"
gh api repos/libsdl-org/SDL/contents/LICENSE.txt -H "Accept: application/vnd.github.raw" > "$DEST/licenses/SDL3.txt"
{
    echo "FNA $FNA_TAG (built from FNA-${FNA_TAG/./}.zip, sha256 $FNA_SHA)"
    echo "fnalibs artifact $ARTIFACT"
    (cd "$DEST" && sha256sum FNA.dll x64/*.dll)
} > "$DEST/SOURCES.txt"
cat "$DEST/SOURCES.txt"
