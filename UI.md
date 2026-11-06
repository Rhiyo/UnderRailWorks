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
- **Reuse the game's own windows and popups before building one.** Many exist and are already right for every size:
  - `e4u`: the save-name popup (text box, Save/Cancel, screen dimmed, Escape cancels). `Ui.Ask` is built on it.
  - `tg`: the split-stack popup (a number).
  - `bnr`: the message / confirmation box with buttons and a countdown (UnderRailFixes 1.x's keep/revert dialog).
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

- `Ui.Window` (mod windows: Merchant Ledger, Loadouts, Vendor Directory...) draws its own title bar (22 px) and close
  box (16 px) at fixed sizes, and mods place their contents at fixed offsets. Fine at the test size; check at the
  small and huge interface sizes and on 5120x1440 before relying on it, and move it onto `ec9` + theme sizes.
- `Ui.Label` is 22 px tall whatever the font; size labels from the font instead.
