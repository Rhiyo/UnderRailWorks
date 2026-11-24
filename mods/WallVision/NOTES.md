# Wall Vision: notes for whoever works on it next (person or AI)

What the code doesn't say. The long history (the keyhole, its walking-floor bug and how it was found) is in
UnderRailFixes' AGENTS.md ("World drawing", "The keyhole walking-floor bug"); read it before touching the keyhole.

- **Draw nothing for a character the game doesn't draw.** Cutscenes hide the player by setting its body's opacity to 0
  (`Character.Opacity`, the game's `c5x.ae` with the "hidePlayer" flag); the effects are off for opacity <= 0.01
  (measured in a new game's intro: an outline was drawn on the desk the hidden player stood behind).
- **Measure, don't guess.** Rendering bugs: log draw order, sample the pixels, compare with the switch off on the same
  build; trust the player's description ("only while moving" means draw order, not lighting).
- **Cut whole pieces, not pixels**: per-piece pixel cuts split walls and looked worse; keep the cut set coherent.
- Follows the game's Character Outlines option: nothing with "Never".
