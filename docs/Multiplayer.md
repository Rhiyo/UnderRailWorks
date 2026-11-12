# Multiplayer (co-op) mod: design notes and open questions

Started 2026-11-11. The final test of the framework: a co-op mod (mods/Coop, 0.7). This file records what the
player wants, what is known about the game, what was built and measured, and the questions still open.

**Where it stands (2026-11-12):** a joiner joins from the main menu, makes a character in the game's own creation
(or picks one saved with the host's game), and plays it in the host's world: their copy loads the host's save and
mirrors it (positions, walks with the game's animation, health, deaths, doors), their HUD and inventory are their own
character's, and their clicks walk, attack, use doors/levers/containers (loot goes to their own bag), take exits (the
group travels), and talk (the conversation runs on the host for their character, relayed to their window). They join
fights like anyone: in sight of an enemy or by fighting. Everyone is in the host's zone (see "Players in different
zones" below).

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

### Where the open questions stand (2026-11-12)

1. Leaving mid-combat: the character stays and passes its turns (orphan); someone can pick it up again.
2. Leaving mid-conversation: not handled yet (the conversation stays open on the host; TalkAs.Close would end it).
3. Conversations: the one who clicked talks, with their own sheet answering the checks; only they see it; one at a
   time (the game's limit).
4. Experience: not shared yet; kills give XP to the host's player only (the game's rule). Undecided.
5. The game's machinery: turns, targeting, doors, walking (after the collision fix), using objects, exits, looting,
   conversations all work for a co-op character; stealth not checked.
6. Quests and story: the host's (one world); a joiner's character keeps only its sheet (stats, items, XP).
7. Zone changes: anyone taking an exit takes the group (unless Party.NeedTogether says everyone must be near).
   Separate zones: see below.
8. Real time: a joiner's conversation doesn't pause the host; the host's own menus do pause the host's world.
9. Death: not handled beyond the mirror (the body dies on both sides).
10. Loot: each takes into their own bag; trading between players not built.
11. Saving: only the host; joined characters' sheets are saved with the host's save data.
12. Versions: Co-op's version must match exactly.
13-14. As planned: friends only, direct connections.

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
- Co-op 0.3.1: a "Join a Game" button on the main menu (Works' MainMenu.AddButton) asks the address and opens the
  Co-op window over the menu: joining, choosing and playing work without loading a save (tested against a scripted
  host). The address box didn't accept `.` or `:` before (Works' Ui.Ask now takes extra characters).
- Works fix found on the way: HUD parts the player had placed were only reserved when the stacking reached them, so
  a later-stacked one (the Layout button) could end up on top of another (Hud.Flow).

## Step 3 so far (2026-11-11, Co-op 0.4): the joiner in the host's world

- When a joiner's character enters (and when the host changes zone), the host saves ("coop-host", Works'
  SaveGames.SaveNow: cnk.arg) and sends the save's files; the joiner writes them as "coop-join" and loads it
  (SaveGames.Load). In combat the game doesn't save: the world goes when the fight ends.
- The joiner's world is frozen (Works' Simulation.Frozen: ix.a and crp.aho skipped). The host sends every character's
  position, facing, health and death 10 times a second (only changes), matched by the game's entity id (cae.qe(),
  kept in saves); co-op characters (and characters spawned after the save) are spawned on the joiner from the
  host's SPAWN line. The joiner's camera follows their character (Camera.Follow).
- The joiner's clicks (Works' GameEvents.PlayerOrdering, tf.lj) go to the host: walk -> GO x y, attack -> ATTACKID
  <key>; other orders (talk, use, abilities, exits) are refused for now.
- Tested with two test copies: join from the main menu, world sent (6 files, 835 KB) and loaded in ~1 s, both games
  showing the same room and positions; walking out of combat and in combat on its turn; End turn from the joiner;
  turns going round. Not yet: attacking by clicking an enemy (the spawned Rathounds weren't drawn on either side),
  walking animation (positions jump 10 times a second), doors/containers/items, the joiner's HUD (it still shows
  the host's character), zone changes.

## Step 3, round 1 (2026-11-11, Co-op 0.5)

- Walking: the host sends WALK <key> <tile> when a character's walk order changes (Character.WalkTarget: the current
  djk's tile); the joiner's copy walks there itself, so it moves with the game's own animation and speed. The frozen
  world now runs walking only (Simulation.Frozen: ix.a runs em1 / qy / bpr / dnf / bsz, nothing else). Positions
  only snap when the copy is more than 3 tiles off or stands still somewhere else. Measured: Claude's position on
  the joiner went 31.7 -> 32.3 -> 33.4 -> 34 over a second, the walking frame showing.
- Attacking by clicking an enemy: works (hit for 27, both sides told).
- Deaths: World's character lists skip the dead, so the host remembers what it sent and sends DEAD / GONE.
- Doors: the host sends DOOR <id> <0|1> on change; the joiner sets it (Door.IsOpen: b0e.d, passability too).
  Clicking a door, lever, container... sends USE <id>; the host's character walks there and uses it
  (Character.UseObject: ale.b, as the player's click). Locked doors are refused with a message. Containers open
  for the host's NPC without a window; the joiner doesn't see their contents yet.
- Exits: clicking an exit sends EXIT x y; the host's character walks there and takes it (Character.TakeExitAt:
  ale.a(tile, trigger), as the player's click). The game then changes zone for everyone (a0l.a starts ChangeZone
  for any character); joined characters are spawned next to the host and the world is sent again. Tested: cc-res
  -> cc_ph, the joiner reloaded in 2 s.
- Seen: a co-op character walking a long way through corridors as an NPC got stuck on the host (both copies agreed
  on where); its health resets when it's spawned again in a new zone.

## Step 3, round 2 (2026-11-12, Co-op 0.6-0.7)

Decided with the player: co-op characters are made in the game's own creation screen; the host keeps playing while a
joiner makes theirs (they only enter once it's done); conversations don't interrupt the host.
- **Characters:** New character... (on the main menu) starts the game's New Game on the joiner's machine (rules,
  creation; World.StartNewGame); when that game's first zone loads, the player is exported (.char) and sent (NEW name
  sheet); the joiner then loads the host's world. The host builds it as the game builds the player
  (World.SpawnFromCharacterFile), keeps the sheet (base64) in its save data with the record, and exports it again on
  save, on leaving a zone, before taking an exit, and every 5 s out of combat when level, XP or items changed (the
  bytes change constantly: fog of war, clocks). A taken name (an import keeps its name) is numbered. Tested end to
  end (Import used for speed).
- **The joiner's HUD:** the joiner's copy builds the character from the same sheet and makes it its player
  (World.MakePlayer): portrait, health, inventory, quick bar are theirs. A new sheet rebuilds the body there.
- **Conversations:** a click on an NPC sends TALK; the host's character-for-the-joiner walks there and the game runs
  the conversation with the joiner's sheet answering the checks (Works' TalkAs), hidden on the host, the host not
  paused; lines and answers go to the joiner's Conversation window. Tested with Jyles (cc-res): his greeting, five
  answers, an answer and his reply. Barter from a conversation would open on the host: not handled yet.
- **Looting:** opening a container sends its contents (the host's loot window cancelled); the joiner's Loot window
  takes one or all into their own bag. Tested: SGS Credits x200 from a desk reached the joiner's bag.
- **Combat:** the game brings the player's whole side into a fight; a joined character with no enemy in the fight
  within 14 tiles that hasn't attacked or been attacked is taken out (Combat.Leave) and walks in real time; it joins
  (Combat.Join) when one comes within sight or it fights. Tested: 17 tiles away it stayed out and walked during the
  fight, and joined when walked up to it.
- **Walking fixed:** two bodies' footprints stop a walk for good (bpr.fr); controlled characters now pass the player
  and each other (Works). Before, a co-op character next to the host couldn't walk past them.
- **Party rules (Works):** Party.NeedTogether for entering a zone / a conversation / an object; Exits.Disable. Co-op
  adds joined characters to Party.Members. Tested: an exit off refused; travel refused while a member was far away.

## Other mods in co-op (Co-op 1.1, 2026-11-12)

Goal (the player's): as many mods as possible work in co-op without being written for it, and co-op mods can be built
on top of Co-op.
- **Unaware mods.** Mods run on the host (the real game); joiners see the result through the mirror; on a joiner's
  machine Works refuses world changes (Simulation.IsMirror), so nothing appears that only one machine has.
- **The acting player (Works, `Party.Acting` / `Party.As`).** While a party member does something, `World.Player` is
  that member for mods' handlers: using an object (bim.a, so OnUse, ContainerOpening, ItemTaking...), taking from a
  container, items going into their bag (GameEvents.ItemAdded), XP they gain (GameEvents.ExperienceGained), their
  conversation (TalkAs already swaps the sheet). Tested with handlers written for single player: "Mira opens Desk",
  "Mira got SGS Credits x200", "asd gained 40 XP" then "Mira gained 20 XP".
- **Popups on the right screen (Works' PlayerScreens, Co-op 1.2).** Ui.Ask / AskNumber / Confirm / Choose and
  Notify.Log go to the acting player's screen (or one named with Party.As); a multiplayer mod provides the screens
  (IPlayerScreens: Co-op sends POPUP to that joiner, the answer comes back and the callback runs on the host as that
  player). Tested with an unaware ContainerOpening handler asking "Search the Desk?": shown on Mira's player's screen
  only, "Yes" there ran the callback on the host with World.Player = Mira.
- **Not covered (and why):** a mod's own windows (Ui.Window: drawn live, not a question) opened by a joiner's action
  show on the host; a mod's keys
  and buttons on a joiner's machine run against the copy (refused); level-ups happen on the joiner's screen
  (LevelledUp doesn't fire on the host). World.Player is not changed outside an action: HUD mods, minimaps and windows
  mean "this screen's player".
- **Building on Co-op.** Co-op's rules can each be switched off by a mod doing it its own way (CoopApi):
  TravelTogether, EveryoneListens, ConversationsPause, SharesXp; plus IsMirror, PlayerOf, CharacterOf, SendWorld,
  host options, mod messages. Tested: with TravelTogether off (from WorksTest) the host left alone.

## Co-op 1.0: the bare minimum (decided 2026-11-12)

The player split the work: Co-op is plain multiplayer that keeps the game's own rules, and anything that bends them
becomes a separate mod later, built on APIs (Works' and Co-op's).
- **Conversations:** whoever clicks talks and answers; every other player listens (a read-only Conversation window
  following each line, "<name> is talking"; the host sees a joiner's conversation the same way). The game pauses, as
  for any conversation. Works: GameEvents.ConversationLine (every line of any conversation, with who holds it),
  TalkAs.Talk(..., pause), Character.TalkTo. Tested both ways with Jyles.
- **Travel:** the group travels together: an exit is taken only with every player within 10 tiles of whoever takes
  it (Works' Party.NeedTogether while hosting); refused with a message otherwise. Tested both ways. No separate zones.
- **Combat:** the whole zone, as the game does it: every player takes their turns. (The 8 s auto-pass is gone.)
- **Hosting options:** Host this game... opens a window with Shared XP (the % of what any player earns that each other
  player also gets, 0-100, default 100) and any other mod's options; values remembered. Tested: 50 % gave the joiner
  50 of the host's 100.
- **Co-op's API** (Mods.Api<CoopApi>("Coop") or Mods.Call): IsHosting / IsJoined, Players, PlayerJoined / PlayerLeft,
  XpShare, AddHostOption / AddHostChoice / HostOption, and a mod's own messages (Handle, SendTo, SendToAll,
  SendToHost). Tested from WorksTest by Mods.Call: an option on the Host window, a message to the host and its reply.
- **Left for later mods** (their Works pieces stay: Zones.Export/Import, Variables.Snapshot, WorldMap.Mark,
  Combat.Join/Leave, TalkAs, CharacterLeavingZone, Simulation.IsMirror): players in different zones, fights that
  aren't the whole zone, a campaign mod with separate conversations, other XP rules. The separate-zones code and the
  four XP modes were removed from Co-op (they're in git history: Co-op 0.8-0.11).

## Round 3 (2026-11-12, Co-op 0.8-0.9; separate zones and XP modes since moved out): built from the design below

- **Fights, as the game does them (decided 2026-11-12):** traced in the decompile: nobody "joins" a fight. Every
  character with an AI is registered with the combat manager when it becomes active (amc.mx -> crp.ahx), and a fight
  makes the whole zone turn-based with all of them in the turn order (allies, pets, the player); an ally with nothing
  to do passes its turn (its AI's combat step). A fight ends only when no hostile combatant has detected the player
  (crp.ahs), checked when the player ends a turn. Detection is the vision sensor's (cn8.abj): the other's tile in the
  observer's field of view and within its real SightRange. The field of view itself is wider (an NPC's is at least
  10 tiles, aju.w; the player's SightRange + 2), which is why a Rathound (sight 8) "saw" a tile 10 away.
  So co-op characters are always in the turn order, can't walk in real time during a fight anywhere in the zone
  (nobody can), and a fight doesn't end while an enemy has detected any party member (Works: the game's check extended
  to Party.Members). A joined character not involved (no enemy sees it and it sees none, by the sensor's rule, and it
  hasn't fought) gets its turn but it passes after 8 s unless its player does something; involved, it's an ordinary
  turn. Tested both: far away (21 tiles) the turns passed; next to the host the turns were its player's.
  Earlier versions (taking characters out of the fight so they walk in real time; a distance; the AI's memory, which
  knows of characters far out of sight once a fight starts) are gone.
- **To think through (the player: "potentially has huge ramifications"):** fights that aren't the whole zone (players
  far from a fight playing in real time) would need the game's single turn order split; until then the whole zone
  goes turn-based, as in single player.
- **Trading:** a conversation's trade opens on the joiner's machine (their character, their money, the merchant's
  stock packed and sent); on closing, the merchant's stock and the joiner's sheet go back. Tested: a throwing net
  blueprint bought, 500 -> 458 charons. A mod's spawned merchant gets its store on the joiner too.
- **The joiner's own changes:** equipping, using, dropping on the joiner (level, XP, bag or gear changed) sends their
  sheet; the host rebuilds the character from it (keeping its own health).
- **Experience:** chosen with Host this game...: everyone in full / full if nearby (20 tiles) / split / only who earned
  it (a joined character's kill: the game gives kill XP to the host's player, Co-op moves it to the killer); oddities:
  the same, or only the finder. Each mode tested with 100 XP; kill credit and oddities not seen in game (the saves
  used have no Classic-system kill XP).
- **Conversations:** a joiner leaving or closing the Conversation window ends it on the host.
- **Other mods on joiners:** Works refuses their world changes (Simulation.IsMirror). Tested: a spawn refused.
- **Players in different zones:** as designed below (CoopZones.cs). Tested in cc-res / cc_ph: a joiner taking an exit
  alone ran cc_ph on their machine; variables both ways; walking back in rejoined (placed by the host, a mirror
  again); the host leaving with the joiner far away left them running cc-res (saved and sent first), and coming back
  had it handed over (263 KB). World map: each side's map outlines the other players' zones in blue.
- Not done: more than one joiner apart in the same zone (refused instead), travelling characters (glent) with a
  handed-over zone, scripts on zone entry meant for "the player" on a joiner's machine, a joiner's machine crashing
  while apart (up to 60 s of their zone lost).

## Players in different zones (asked 2026-11-12; designed, then built: round 3)

The player wants players free to be in different zones, the world map showing where *your* character is, and an API
to make some zones (and some conversations or objects) need everyone together (the last part is built: Party.NeedTogether).

**The limit:** a game process has one live zone (c1a); the others are stored. So whoever is alone in a zone must
simulate it on their own machine.

**What the game makes possible (measured in the decompile, 2026-11-12):**
- A stored zone is exactly three compressed blobs in the in-memory bulk storage (`bmg`'s static `ccc`), under
  `lmaps\<zoneId>`: `_L1.uzl` (doq: its layer state), `.uzdm` (l8), `.ap` (az0: pending actions and
  ElapsedZoneTime), written by `bl1.yp(loc, zone, leaving)` (also callable while staying: `d6f.arr(false)`), read back
  when the zone loads (`bl1.a(id, out doq)` over the static map). Raw access: `ccc.d(name)` / `ccc.a(name, bytes,
  ...)` / `ccc.e(name)`. So a zone can be taken out of one game and put into another.
- Characters that travel between zones (`cka`) are stored apart, `glent\<id>.*`, indexed (with their zone) in
  `cyt.b()`: a handoff carries those too.
- Global state lives in the game state cyt: variables `cyt.r()` (a PropertyCollection), quests `cyt.t()` (active,
  completed, failed), notes `cyt.w()`, factions `cyt.ac()`, explored places `cyt.z()` / transitions, times `cyt.p()` /
  `cyt.o()`, delayed events, pending per-zone actions. No built-in diff: compare those collections.
- The world map shows `cyt.c().b()` (the current zone): a joiner simulating their own zone sees themselves there
  naturally. Showing other players' zones too: a postfix on the map drawing (`eg0.a(e5q, d69)`) can outline more nodes.

**The design:**
1. **Zone owners.** The host owns its zone. A joiner who takes an exit alone (no Party rule against it) becomes the
   owner of the zone they enter: the host takes their character out of its zone, sends the target zone's three
   blobs (and its travelling characters) plus the current global state, and the joiner's game unfreezes and travels
   there itself (World.Travel), with their own character as the player (already the case).
2. **Global state flows to the host.** The owner of a zone sends changes to global variables, quests, notes,
   factions and explored places (a diff every few seconds); the host applies them, and forwards them to everyone.
   The host's own changes go out the same way. (Conflicts are unlikely: a quest step happens in one zone.)
3. **Handing a zone back.** When its owner leaves it, or the host (or another player) wants to enter it, the owner
   stores it (`arr(false)`), sends the blobs to the host, which replaces its stored copy (`ccc.a`), and the one
   entering gets it from the host. Two players in one zone: the first one there owns it and the other mirrors it
   (the mirror code, generalised: today only the host can be the source).
4. **Saving.** Only the host saves, so before a save it asks every owner for their zone and the global diff
   (asynchronous: the save waits a moment, or saves what it has and the next save catches up).
5. **The world map.** Each machine shows its own player's zone (as the game does); the host also outlines the
   joiners' zones, and each joiner the others' (the eg0 postfix).
6. **Works APIs this needs:** `Zones.Store(zoneId)` / `Zones.Export(zoneId)` / `Zones.Import(zoneId, blobs)`,
   `GlobalState.Snapshot()` / `Diff(a, b)` / `Apply(diff)`, `WorldMap.Outline(zoneId, colour)`.
- Risks: saves (an imported zone must stay loadable by the stock game: the blobs are the game's own format, so they
  should); scripts that run on zone entry for "the player" running on the joiner's machine; merchants (stored with
  their zone, so they move with it).

## Making other mods work in co-op

What the player asked: Co-op should replace Works' calls so other mods work in multiplayer as long as Co-op loads
first. What that can and can't mean:
- **Mods run on the host.** The host's game is the real one: a mod's handlers there change the real world, and the
  joiners see the result through the mirror (positions, health, doors, items in their bag through the sheet). A
  joiner's copy is frozen and gets overwritten: mods there should only show things, never change the world (a spawn
  there would be a ghost only that joiner sees).
- **"The player" vs "the players".** A mod that gives the player an item, heals the player or checks the player's
  skill means one character. Works now has `Party.Members` (the player + co-op characters): a mod written against it
  works in co-op without knowing about Co-op. Co-op fills it.
- **Next steps (not built):** (1) a Works flag for "this game is a mirror" so Works' world-changing calls (Spawn,
  Place, Inventory.Add, Variables...) do nothing on joiners unless the mirror itself makes them; (2) Works events that
  are about the player (ItemAdded, LevelUp, Equipped...) also raised for party members, marked with the character;
  (3) mod UI (popups, windows) that a joiner's action triggers on the host shown on that joiner instead (a remote
  UI: the hardest; until then it shows on the host). Patching Works itself from Co-op (Harmony on Works' methods)
  would work for (1), but the flag is cleaner and keeps Works the one place that knows.

## Plan

1. (done, see above) Spike, no network: in the host's game, a second character controlled by console commands (walk, attack on its
   combat turn instead of the AI, use, talk). Answers question 5 and most of the "second controllable character" API.
2. Networking basics (in the mod): host and join between two copies (two test copies on this machine), a character list, and
   the joiner's commands moving their character on the host.
3. The client view (replication), the hardest part.
4. Then the open questions above, one at a time.
