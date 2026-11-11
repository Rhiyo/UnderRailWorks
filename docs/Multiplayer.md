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

- "The player" is mostly one object: the game state's `cyt.ac()` (`cnk.aqu().ac()`), used in about 168 places
  (experience, the inventory and character windows, dialogs, quests, the camera...). `c5x.IsPlayer` is a flag on the
  entity (`e2b.d`, used in about 175 places), so a second entity could carry it, but what each of those places does
  with two players is unknown.
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

## Plan

1. Spike, no network: in the host's game, a second character controlled by console commands (walk, attack on its
   combat turn instead of the AI, use, talk). Answers question 5 and most of the "second controllable character" API.
2. Networking basics (in the mod): host and join between two copies (two test copies on this machine), a character list, and
   the joiner's commands moving their character on the host.
3. The client view (replication), the hardest part.
4. Then the open questions above, one at a time.
