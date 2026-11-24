# Co-op: notes for whoever works on it next (person or AI)

What the code doesn't say: the rules the player (Sean) set, decisions and why, things measured, traps. The design
history and test results are in `docs/Multiplayer.md`; read that for the long story. Keep this file short and current:
add a rule when the player gives one, fix a line when it stops being true.

## Rules from the player

- **The host is the protagonist.** One world, the host's. Conversations, skill checks, the shared bag and the story go
  through the host's character; joiners suggest (vote), the host decides. Leniency only where it can never block
  content.
- **Host and joiner UI line up where it makes sense.** The same window shape, sections in the same order, buttons in
  the same places (your character's action under the players table, settings beside their heading, the leave/cancel
  button bottom right). Both lobbies are built by one layout (`CoopLobbyView.cs`, `BuildLobbyWindow`): change it there,
  not in one side only. Anything new shown on both sides should come from one builder too.
- **Joiners see what the host sees.** A conversation looks the same for everyone (the game's own window on every
  screen, built from the host's line: `CoopTalk.cs`), the camera in a conversation is the host's, and it opens on
  whoever started the conversation (a joiner's click, or a member who set off its trigger).
- **Nothing moves during a conversation.** The game pauses for one in single player; in co-op joiners' orders are
  refused while it's open (a stray step could start a fight).
- **Windows fit any resolution and interface size** (Works' UI rules, `UI.md`); every window is checked by Works
  ("ui: window ... fits" in the log). Keep a window's width; put buttons where they belong instead of widening it.
- **Players' names:** a store name (Steam), else "Player"; the same name again gets "(2)", "(3)" (`UniqueName`).
- **Co-op saves live in `Saves\Co-op Saves`** (inside Saves: Steam Cloud keeps them, Load Game skips the folder).
- **Importing characters is off in co-op** (another game's or another mod set's items and stats might not exist here).
- **GOG multiplayer is out for now** (Galaxy needs its app, which Linux GOG players lack; untested). GOG players join by
  address.

## Things measured (don't redo)

- **Never save before a zone's first game update.** A save at any step of the arrival workflow, or one frame after,
  stops what the zone starts on entry (a new game's intro never opened). Co-op sends the world on Works'
  `GameEvents.ZoneSettled` (the first game update after arrival). Details: `docs/Multiplayer.md`, "The intro".
- **A joiner's game is a copy** (`Simulation.IsMirror`): it runs no map triggers and starts no conversations of its
  own (Works refuses both); Co-op's own view of the host's conversation goes through `Simulation.Apply`.
- **Making a character the player rebuilds the HUD** (`World.MakePlayer`): windows and the conversation window go
  with the old one. Open them after; reopen what was open.
- **Ask the game whether a conversation window is up** (`TalkAs.IsShown`); a flag of our own went wrong when a
  window's close event came after the next one opened (the joiner's window closed and never came back).
- **Conversation text goes as the dialog has it** (`TalkLine.RawText`, its `\r\n` kept): the game's narration layout
  needs the `\r` (with `\n` alone a line repeated).
- **Test trap:** a test game's mouse parked at the screen edge scrolls its camera (`inp.sh move 640 300` first).

## Testing

`tools/smoketest/coop*.sh` (each runs two or three headless games; run them one at a time: they share ports and
copies). `coop-intro.sh` covers Rules -> lobby -> start -> intro -> votes -> Skip intro -> the next conversation;
`SHOTS=<folder>` saves screenshots of both games on the way. `coop.ui <window>` opens any Co-op window (tests).
