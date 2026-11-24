# Movable Windows: notes for whoever works on it next (person or AI)

What the code doesn't say.

- The game closes the windows on one side when another opens there; a window the player moved is out of the way, so it
  stays open until closed (`OpenWindow.StaysOpen`). Right-click on the title bar puts it back (and back under the
  game's rule).
- Built from Works' API only; gaps go into Works.
