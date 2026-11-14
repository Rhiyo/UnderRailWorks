#!/bin/bash
# Keeps this run's game silent: mutes every PipeWire/Pulse stream of a process running in this run's prefix, as soon as
# it appears (checked every 0.1 s), for as long as the run lives. Only this run's streams: the player's own games,
# devices and default output are never touched. (The game needs an audio device, so sound can't be switched off in
# Wine or SDL: without one it stops with "Could not find a suitable audio device". A null sink per run made the desktop
# switch the player's output to it, Nov 14 2026.) Started by launch.sh, ended by stop.sh or when the game is gone.
# Usage: mute.sh <prefix>/pfx
pfx="$1"; seen=0; gone=0
while :; do
    any=0
    pactl list sink-inputs 2>/dev/null | awk '/^Sink Input #/{id=substr($3,2)} /Mute:/{m=$2} /application.process.id/{gsub(/"/,"",$3); print id, m, $3}' |
    while read -r id muted pid; do
        tr '\0' '\n' < "/proc/$pid/environ" 2>/dev/null | grep -Eqx "WINEPREFIX=$pfx/?" || continue
        [ "$muted" = yes ] || pactl set-sink-input-mute "$id" 1
    done
    # Alive while a process of the run's prefix exists; once one was seen, stop 20 s after the last is gone.
    for p in $(pgrep -f 'underrail\.exe|wineserver' 2>/dev/null); do tr '\0' '\n' < "/proc/$p/environ" 2>/dev/null | grep -Eqx "WINEPREFIX=$pfx/?" && { any=1; break; }; done
    if [ $any = 1 ]; then seen=1; gone=0; elif [ $seen = 1 ]; then gone=$((gone+1)); [ $gone -gt 200 ] && exit 0; fi
    timeout 0.1 tail -f /dev/null
done
