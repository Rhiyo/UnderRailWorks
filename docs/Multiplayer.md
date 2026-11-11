# Multiplayer (co-op) mod: design notes and open questions

Started 2026-11-11. The final test of the framework: a co-op mod. Nothing is built yet. This file records what the
player wants, what is known about the game, and the questions to work through one at a time.

## What it should do (the player's design)

- An option in game (on the HUD, or its own menu) makes the running game a multiplayer one (the host).
- Someone joining sees a character list: the host's character first (not choosable), then the co-op characters saved
  with this game. They pick one (it is then taken: nobody else can pick it) or make a new one, and enter the world.
- A new character starts at the game's start or near the host's character. A saved one starts where it left, or near
  the host's character.
- The co-op characters are saved with the save (the mod's SaveData next to it, never inside the game's save: the
  save must stay loadable by the stock game).

## What is known about the game (measured in the decompile, Nov 2026)

- "The player" is one entity: the game state's `v().b()` (`cnk.aqu().v().b()`), referenced directly about 1,166
  times in 540 files (experience, inventory and character windows, dialogs, quests, the camera, triggers...).
  (`aqu().ac()` is the faction table, not the player: an earlier note here said otherwise.) `c5x.IsPlayer` is a flag
  on the entity (`e2b.d`, about 175 uses). Making the game accept a second "player" is out of reach; joiners'
  characters stay ordinary characters the mod controls (CharacterControl), in the player's faction.
- Combat is turn-based through `crp` (turn start `crp.f()`, end `crp.ahq`); Works already has TurnStarted /
  TurnEnding / ModifyTurnPoints and AI hooks (`Ai.ShouldFlee`, weapon switching), and followers (placed characters
  that follow the player, keep dialog/store across zones).
- The simulation is not deterministic (its own random numbers, real-time movement and AI outside combat), so
  "lockstep" (both games run the same inputs) won't stay in sync.
- Only the zone the player is in is live; other zones are stored states.
- Steam: Works only bridges the game's SteamHelper (9 functions); Steam networking isn't reachable without
  redistributing Steamworks files, so connections would be direct (LAN, port forwarding, or a VPN like Tailscale).

## Likely architecture (to confirm with a spike)

Host-authoritative: the host's game is the only real simulation. Each joiner runs their own copy of UnderRail
(they must own the game) as a client: it loads the host's world and shows it, receives what changed (characters'
positions and animations, health, doors, containers, items, combat turns, dialog lines) and sends only its
character's commands (move here, attack that, use this, say this answer). The joiner's character is, on the host, a
character placed by the mod and controlled by those commands instead of by the AI.

## Where things go (decided 2026-11-11)

- **The multiplayer mod** owns the networking (plain .NET sockets: host/join, messages, who is connected, its
  protocol and versioning), the character list, what to send when, and its menu/HUD option. Nothing in it needs the
  game's internals. Other mods that want to work in co-op get the mod's API through `Mods.Provide` (an optional
  dependency), not through Works. If a second networking mod ever needs the same layer, move it into Works then.
- **Works** gets the game-side pieces (they need the game's internals, and are useful without networking too):

## New Works API this needs (first estimate)

- A second controllable character: make a non-player character act on commands (walk to, attack, use, pick up,
  talk), take its combat turns from a remote player instead of the AI, end its turn.
- Character creation for a co-op character (the game's own creation screens, outside a new game) and keeping it in
  SaveData (stats, skills, feats, inventory, position).
- Replication: reading and applying the state a client shows (entity positions/animations, health, statuses,
  doors, containers, items on the floor), and a client mode where the local game doesn't simulate.
- Per-character versions of what is now "the player": experience, inventory window, character window, dialog.

## Open questions (one at a time, later)

1. Leaving mid-combat: does the character stay (AI-controlled, passive, or skipping turns) or vanish? What if it
   was its turn?
2. Leaving mid-conversation: does the conversation end, or does the host take it over?
3. Conversations: who talks? Only the one who started it, with the others watching (and seeing the choices)? Can
   two conversations run at once? Dialog conditions check "the player" (skills, items, quest states): whose?
4. Experience: shared (each gets the full amount), split, or only for whoever earned it? Oddity XP (studying
   items): per character?
5. Does the game's own machinery accept a second human-controlled character in combat turns, stealth detection,
   enemy targeting, traps, doors, zone transitions? (Measure each, don't assume.)
6. Quests and story: the game keeps one quest log and global variables. Is progress shared by everyone (the host's
   world), and what does a joiner's own character keep when it joins another host's game?
7. Zone changes: everyone must be in the host's zone (only one is live). Does a zone exit take everyone along, or
   wait until all are on it?
8. Real time outside combat: the game pauses for menus and dialogs. Does one player's inventory or dialog pause
   everyone?
9. Death: a joiner's character dies: game over for them only, revival, or respawn near the host?
10. Loot and trading: who gets what is picked up, can players hand each other items, merchants' stock shared?
11. Saving: only the host saves? What happens to joiners' characters when the host loads an older save?
12. Joiners' game version and mods must match the host's (check on join).
13. Cheating/trust: friends only (no anti-cheat); say so.
14. Connection: LAN / port forward / Tailscale first; a relay later?

## Step 1 results (2026-11-11, no network; Works CharacterControl + the Coop mod's console commands)

Tested in game (test zone and Station Alpha, the player in god mode):
- A controlled character (spawned, faction "player") walks out of combat and, on its own turn, in combat (spending
  movement points, e.g. 42 -> 12); attacks with its weapon (the game walks it into range, takes AP, rolls hits and
  crits); ends its turn. Several rounds against two Rathound Alphas until they died; combat ended normally.
- Its turn waits for its controller: 35 s and about 2 minutes tested (an AI turn ends after 15 s). The game only
  holds the controlled character's AI step (bm6.r7), keeping its awareness.
- Enemies target it like the player when it is in the player's faction: a Rathound Alpha next to it attacked it, not
  the player further away. In `playerNativeAlly` (the force-field/turret faction) enemies rank it last
  (`bq1.f7`: that faction is a fallback target) and kept attacking the player. The station's own neutrals see it as
  an ally, as they see the player.
- Initiative: it gets its own place in the turn order like anyone.
- Gaps found: MoveTo can't tell "no path" (a closed door): it queues the walk and nothing happens; it now refuses
  tiles that aren't floor. Works' hostile spawns were hostile to the player only (now also to playerNativeAlly).
- Not yet checked: faction "player" side effects (crimes, reputation, who gets blamed), its death, items/inventory,
  using objects and doors, talking, stealth, zone changes (a spawned character stays behind).

## Step 2 so far (2026-11-11, Co-op 0.3)

- Networking in the mod (Net.cs): TCP port 27650, a line per message, words separated by tabs. Joiner: HELLO
  version name, PICK n, NEW name, GO x y, ATTACK [name] (none: the nearest hostile), FOLLOW, END, STATUS, BYE. Host:
  LIST hostName, CHAR n name free|taken, LISTEND, PLAYING name x y, STATE name hp max combat current ap mp x y dead
  (pushed when it changes), TURN ap mp, SAID text, BYE reason.
- The Co-op window (HUD button): host / join (address popup), the host's list of who plays what and its addresses,
  the joiner's character list, the playing view (health, whose turn, messages, End turn / Attack nearest / Follow
  host / Walk to... / Leave). Tested both sides against scripted peers at 2560x720 (Small) and 5120x1440 (Huge).
- Works fix found on the way: HUD parts the player had placed were only reserved when the stacking reached them, so
  a later-stacked one (the Layout button) could end up on top of another (Hud.Flow).

## Plan

1. (done, see above) Spike, no network: in the host's game, a second character controlled by console commands (walk, attack on its
   combat turn instead of the AI, use, talk). Answers question 5 and most of the "second controllable character" API.
2. Networking basics (in the mod): host and join between two copies (two test copies on this machine), a character list, and
   the joiner's commands moving their character on the host.
3. The client view (replication), the hardest part.
4. Then the open questions above, one at a time.
