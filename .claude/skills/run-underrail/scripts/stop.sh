#!/usr/bin/env bash
# stop.sh: ends THIS run only (RUN = this Claude session, see common.sh): the process group launch.sh started
# (gamescope, its reaper, Proton) and the Wine processes in this run's prefix, then removes this run's silent sink and
# its folder with the prefix. Another session's game, gamescope or sink is never touched, even if it runs the same
# test copy. KEEP_PREFIX=1 stop.sh keeps the folder (to look at what the game wrote).
. "$(dirname "$0")/common.sh"
[ -d "$RUN_DIR" ] || { echo "no run $RUN (nothing to stop)"; exit 0; }
pgid=$(cat "$RUN_DIR/pgid" 2>/dev/null || true)
pid=$(game_pid) && kill "$pid"
for i in $(seq 15); do game_pid >/dev/null || break; timeout 1 tail -f /dev/null; done
pid=$(game_pid) && kill -9 "$pid"
if [ -n "$pgid" ]; then
    kill -TERM -- "-$pgid" 2>/dev/null
    for i in $(seq 5); do kill -0 -- "-$pgid" 2>/dev/null || break; timeout 1 tail -f /dev/null; done
    kill -KILL -- "-$pgid" 2>/dev/null    # headless gamescope can ignore SIGTERM
fi
# Wine in our prefix (the wineserver lingers a few seconds after the game).
for i in $(seq 20); do [ -z "$(prefix_pids)" ] && break; timeout 1 tail -f /dev/null; done
for p in $(prefix_pids); do kill -9 "$p" 2>/dev/null; done
m=$(cat "$RUN_DIR/sink_module" 2>/dev/null || true)
[ -n "$m" ] && pactl unload-module "$m" 2>/dev/null
if [ "${KEEP_PREFIX:-0}" = 1 ]; then echo "stopped run $RUN; kept $RUN_DIR"; else rm -rf "$RUN_DIR"; echo "stopped run $RUN"; fi
others=$(live_runs | grep -vx "$RUN" || true)
[ -n "$others" ] && echo "(other runs still going, not ours: $(echo $others))"
exit 0
