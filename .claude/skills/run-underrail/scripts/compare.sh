#!/usr/bin/env bash
# compare.sh [launch options] DRIVER...      e.g. compare.sh --saves D3D11 OpenGL Vulkan
# The same scene on several FNA3D drivers at once, for "is driver X broken?" (see SKILL.md, "Checking a rendering
# bug"). One run per driver (RUN=cmp-<driver>), all launched with the same launch.sh options; with --saves / --save
# it clicks Resume Last Saved, otherwise it stays on the main menu. Then full-size shots, a stacked crop and, when
# D3D11 is among the drivers, a pixel diff of each against D3D11. Stops its runs at the end (KEEP=1 keeps them).
#   OUT=dir (default $CACHE/compare)   CROP=WxH+X+Y (default: the whole screen)   WAIT=seconds after loading (30)
# Other environment (TEST_GAME, driver/Mesa variables, experiment switches) passes through to every run.
set -uo pipefail
S="$(cd "$(dirname "$0")" && pwd)"
. "$S/common.sh"
opts=() drivers=() resume=0
while [ $# -gt 0 ]; do
    case "$1" in
        --save) opts+=("$1" "$2"); resume=1; shift 2;;
        --saves) opts+=("$1"); resume=1; shift;;
        --*) opts+=("$1"); shift;;
        *) drivers+=("$1"); shift;;
    esac
done
[ ${#drivers[@]} -gt 0 ] || { echo "usage: compare.sh [--config] [--saves | --save NAME] DRIVER..." >&2; exit 1; }
OUT=${OUT:-$CACHE/compare}; mkdir -p "$OUT"
run_of() { echo "cmp-$(echo "$1" | tr 'A-Z' 'a-z')"; }
for d in "${drivers[@]}"; do FNA3D_FORCE_DRIVER=$d RUN=$(run_of "$d") "$S/launch.sh" "${opts[@]}"; done
timeout 25 tail -f /dev/null
if [ "$resume" = 1 ]; then
    for d in "${drivers[@]}"; do RUN=$(run_of "$d") "$S/inp.sh" focus click 960 395; done   # 1280x720: Resume Last Saved
fi
timeout "${WAIT:-30}" tail -f /dev/null
stack=()
for d in "${drivers[@]}"; do
    r=$(run_of "$d")
    RUN=$r "$S/inp.sh" focus                    # the world stays black without focus (SKILL.md, "Focus matters")
    RUN=$r "$S/shot.sh" "$OUT/$d.png" >/dev/null
    if [ -n "${CROP:-}" ]; then magick "$OUT/$d.png" -crop "$CROP" +repage "$OUT/$d-crop.png"; stack+=("$OUT/$d-crop.png"); else stack+=("$OUT/$d.png"); fi
done
magick "${stack[@]}" -append "$OUT/stacked.png"
echo "shots: $OUT/<driver>.png; top to bottom (${drivers[*]}): $OUT/stacked.png"
if printf '%s\n' "${drivers[@]}" | grep -qx D3D11; then
    python3 -I - "$OUT" "${drivers[@]}" <<'EOF'
import sys, subprocess, numpy as np
out, drivers = sys.argv[1], sys.argv[2:]
def load(d):
    w, h = map(int, subprocess.run(['magick', 'identify', '-format', '%w %h', f'{out}/{d}.png'], capture_output=True, text=True).stdout.split())
    raw = subprocess.run(['magick', f'{out}/{d}.png', 'rgb:-'], capture_output=True).stdout
    return np.frombuffer(raw, np.uint8).reshape(h, w, 3).astype(np.int16)
ref = load('D3D11')
for d in drivers:
    if d == 'D3D11': continue
    diff = np.abs(load(d) - ref).max(axis=2)
    print(f"{d:8} vs D3D11: {int((diff > 40).sum())} px differ by >40, {(diff == 0).mean() * 100:.2f}% identical "
          "(animation and lighting differ a little anyway: look at the stacked shot)")
EOF
fi
if [ "${KEEP:-0}" != 1 ]; then for d in "${drivers[@]}"; do RUN=$(run_of "$d") "$S/stop.sh" >/dev/null; done; echo "runs stopped"; fi
