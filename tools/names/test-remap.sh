#!/bin/bash
# Tests Remap: scrambles a copy of the game (every scrambled name replaced by a random one, as a new game build would),
# runs Remap on it and checks every match against the scrambler's record. Usage: tools/names/test-remap.sh [seed...]
set -e
HERE="$(cd "$(dirname "$0")/../.." && pwd)"
CECIL=${CECIL:-/usr/lib/mono/gac/Mono.Cecil/0.11.1.0__0738eb9f132ed756/Mono.Cecil.dll}
EXE=${GAME_EXE:-$HOME/.local/share/Steam/steamapps/common/Underrail/underrail.exe.backup}
WORKS=$HERE/build/xna/UnderRailWorks.dll
T=$(mktemp -d); trap 'rm -rf "$T"' EXIT
export MONO_PATH="$(dirname "$CECIL")"
mcs -nologo -out:"$T/Remap.exe" -r:"$CECIL" "$HERE/tools/names/Remap.cs" "$HERE/tools/names/GameMap.cs" -main:Remap 2>&1 | grep -v warning || true
fail=0
for seed in ${@:-1 2 3}; do
  mono "$T/Remap.exe" --scramble "$EXE" "$T/s.exe" "$seed"
  mono "$T/Remap.exe" "$EXE" "$T/s.exe" "$WORKS" > "$T/out.txt" 2>"$T/err.txt" || true
  r=$(python3 - "$T/out.txt" "$T/s.exe.truth" <<'PY'
import sys
truth = {}
for l in open(sys.argv[2]):
    kind, rest = l.rstrip("\n").split(" ", 1); old, new = rest.rsplit(" ", 1)
    truth[(kind, old)] = new
ok = wrong = un = 0
for l in open(sys.argv[1]):
    kind, rest = l.rstrip("\n").split(" ", 1)
    old, new = rest.strip().split(" -> ")
    if new == "UNMATCHED": un += 1; print("  unmatched", kind, old, file=sys.stderr); continue
    exp = truth.get((kind, old))
    got = new if kind == "type" else new.split(".")[-1] if not new.endswith(".ctor") else ".ctor"
    if kind == "type" and exp is None: exp = old     # a kept name
    if kind != "type" and exp is None: exp = old.split("(")[0].split(".")[-1]     # not renamed (virtual, kept)
    if kind == "method" and ".ctor" in old: exp = ".ctor"
    if got == exp: ok += 1
    else: wrong += 1; print("  WRONG", kind, old, "->", new, "expected", exp, file=sys.stderr)
print(ok, wrong, un)
PY
)
  set -- $r; echo "seed $seed: $1 right, $2 wrong, $3 unmatched"; [ "$2" = 0 ] && [ "$3" = 0 ] || fail=1
done
exit $fail
