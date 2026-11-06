# UI.md: rules for interface work (Works' UI API and mods' UI)

Read this before adding or changing anything the player sees in the interface: a Works UI helper, a mod window, a
HUD part, a popup.

## The rule: any resolution, any aspect ratio, any interface size

Players run UnderRail from 1280x720 to 8K, on 16:9, 21:9, 32:9 (5120x1440) and portrait screens, with the game's
three interface sizes (small, large, huge: separate hand-made layouts, atlases and bitmap fonts per tier, `wj`) and
with Works' interface scale and insets on top. A layout that looks right in the test size (2560x720, interface 1792
wide after insets) can be clipped, overlapping or tiny elsewhere.

- **Never hard-code pixel offsets or sizes for game controls.** Sizes come from the game's theme (`wj.a()`): its
  popup size (`h7`), button size (`h8`), text field height (`h8.d`), fonts (`gm`, text offset `gn`), frame textures
  (`f`, `b8`, `b9`). Place parts from measured sizes (`control.Size`, the font's `LineSpacing`/`MeasureString`), from
  the edges they belong to (e.g. buttons from the bottom-right corner, as the game does), never at fixed numbers.
- **Asking the player something: use `Ui.Ask` (text), `Ui.AskNumber` (a number), `Ui.Confirm` (yes/no) or
  `Ui.Choose` (buttons)** (UiPopups.cs). They are the game's own popups, tested at 2560x720 and 5120x1440. Never
  build a window with a text box or buttons for a question. The table at the top of docs/API.md says what to use when.
- **Reuse the game's own windows and popups before building one.** Many exist and are already right for every size:
  - `e4u`: the save-name popup (text box, Save/Cancel, screen dimmed, Escape cancels). `Ui.Ask` is built on it.
  - `tg`: the split-stack popup (slider + number; no frame, plain-text buttons, as the game draws it). `Ui.AskNumber`.
  - `bnr`: the message box (title, word-wrapped text, a full-width row per button; size given in base units and
    scaled by the interface size, `wj.g4`). `Ui.Confirm` / `Ui.Choose` size it from the text with its own fonts.
  - `ec9`: the game's framed pane (base of its popups); `amm` the closable-with-result base.
  Find more with `grep -l 'new c68('` (text boxes) or by their button texts in the decompile.
- **Centre and clamp against the live screen size** (`bjn.b().aau().Size`, or the HUD root's), at the moment of
  showing; never assume 1920x1080 or the test size.
- **Test at more than one size before calling it done**: at least the default test size and the player's own setup
  (`launch.sh --config 5120 1440`: their config.dat, so their interface size and scale), and look at a crop of the
  screenshot at full size. Small or huge interface: switch it in Options.
- Text: one line of the theme's font is `font.LineSpacing` tall; long text must wrap or the box must grow (measure).
- Keyboard: a popup with a text box gives the box the keyboard when it opens, and keeps it if something closing in the
  same moment takes it back (a conversation does: see `UiWindows.KeepAskFocus`).

## Known debt

- `Ui.Window` is the game's titled window (`a9t`: frame, title strip, font and close button of the interface size;
  at least as wide as its title, at least as tall as the frame's picture, else the frame draws above it). Mods still
  place their contents at fixed offsets from `Top`; check them at other interface sizes.
- Mod text (`Ui.Label`, `Ui.TextBox`, canvas text, HUD parts) uses one font at every interface size (C13b), so it
  stays small on large and huge interfaces. Switching to the interface size's own text font (`wj.fo`) was tried
  (Nov 2026) and broke mods that assume a line height (Quest Tracker's lines overlapped, "Sec"/"Zap" collided,
  Loadouts' labels were cut): each mod has to measure its text first (`Ui.TextWidth`, a font line height API), then
  the font can follow the interface size.
