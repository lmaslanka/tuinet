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
