# HUD Layout: notes for whoever works on it next (person or AI)

What the code doesn't say.

- A layout must survive resolution and interface-size changes: every part is kept relative to an anchor (an edge, a
  corner, the centre), never as absolute pixels.
- Built from Works' API only (Hud handles, Ui, Settings); a gap in what Works offers goes into Works, not into
  scrambled game names here.
