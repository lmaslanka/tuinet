# 13 · Scroll regions for split layouts (left/right margins)

**Type:** performance · **Effort:** M · **Priority:** 6

## Problem
Scroll detection only works when whole rows move (DECSTBM has no horizontal margins). A list next to a
fixed side panel (the Showcase layout) still repaints every row when it scrolls.

## Design
- Detection: find column bands as well as row bands — for each dirty row band, compare row *slices*
  [x0, x1) under a vertical shift (try the widest dirty column range first).
- Emission when supported: `CSI ? 69 h` (DECLRMM), `CSI t;b r` + `CSI l;r s` (DECSLRM), CUP, IL/DL,
  reset margins, `CSI ? 69 l`.
- Support detection: DECRQM `CSI ? 69 $ p` at startup (reply parsed by `VtParser`); without a positive
  reply, keep today's behaviour. xterm, foot, WezTerm, Contour, mlterm support it; many others don't.
- `TerminalOptions.ScrollRegions` continues to gate everything.

## Tests
Fuzz with partial-width band shifts in an emulator that implements DECLRMM/DECSLRM, exact-byte tests,
no use of the sequences when the query reply is missing or negative.

## Status: implemented

- Detection runs only where a dirty band has no whole-row shift. The plan said to try "the widest dirty column
  range first", but the panel beside a list usually changes too (it shows the selection), so the union of dirty
  columns spans both. Instead, probe rows give seed columns (their first and last changed cell). The nearest previous
  row matching a few cells around a seed gives a shift. The columns are the median, over a sample of rows, of the
  run of matching cells around the seed. A band is used when 3+ rows and 64+ cells line up. Margins never split a
  wide glyph (the band is narrowed around it).
- Emission: `CSI ? 69 h`, `CSI t;b r`, `CSI l;r s`, CUP, IL/DL, `CSI s`, `CSI r`, `CSI ? 69 l` (~45 bytes). Rows
  of the band are then diffed against what the move left on screen.
- `CSI ? 69 $ p` is sent at startup when `ScrollRegions` is on: on the alternate screen before the clear, and in
  inline mode followed by `\r CSI K`, so a terminal that prints the query's last byte shows nothing.
  `Terminal.LeftRightMarginsActive` reports a reply of 1-3. foot and tmux reply 0, so they keep today's behaviour.
- 200×60, 150-column list beside a 50-column panel: 10.3 KB → 459 B, 31 → 13 µs. Page changes with the
  detection on: 35.3 vs 34.8 µs.
- Not verified on a real terminal that has the margins (none installed here): the fuzz emulator implements
  DECLRMM/DECSLRM strictly (margin-bounded IL/DL, a split wide glyph or printing inside margins fails).
