#!/usr/bin/env bash
# playsave.sh <save name> <out folder> (RUN=<run name>, TEST_GAME=<game copy with Works>, SKILL=<skill scripts>): the
# game loads the save, plays 30 s, saves from the menu as "played" (1280x720 positions), and the save is copied to
# <out folder>/<save name>.
S="$SKILL"; export TEST_GAME
name="$1"; out="$2"
cd ~ && $S/launch.sh --save "$name" >/dev/null 2>&1
# Resume Last Saved: the main menu is centred, so its buttons move with the number of rows (Co-op's Join a Game
# added one: 395 -> 335). If the menu gains or loses a row, this moves by half a row (30 px at 720p).
sleep 35; $S/inp.sh focus click 960 335 >/dev/null 2>&1
sleep 40; $S/inp.sh focus click 1245 33 sleep 1500 click 640 338 sleep 2000 click 900 666 sleep 1500 click 640 336 type played sleep 300 click 688 381 >/dev/null 2>&1
D="$HOME/.cache/underrail-test/runs/${RUN:0:8}/prefix/pfx/drive_c/users/steamuser/Documents/My Games/Underrail/Saves/played"
for i in $(seq 30); do [ -f "$D/global.dat" ] && break; sleep 2; done; sleep 5
if [ -f "$D/global.dat" ]; then rm -rf "$out/$name"; mkdir -p "$out"; cp -r "$D" "$out/$name"; echo "$name: saved"; else echo "$name: NOT saved"; fi
$S/stop.sh >/dev/null 2>&1
