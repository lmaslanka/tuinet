# 12 · Erase with escape codes instead of spaces

**Type:** performance · **Effort:** S · **Priority:** 4

## Problem
When cells become blank (a dialog closes, a panel is cleared, a line shortens) the renderer writes one
space per cell, plus SGR if needed. Erasing is much cheaper with VT100 sequences every terminal supports.

## Design (`Internal/Renderer.cs`, `RenderRow`)
- **EL (`CSI K`)**: when the dirty tail of a row from x to the end is all blanks with the same background,
  set the pen background and emit `CSI K` (3 bytes) instead of N spaces. BCE fills with the current background.
- **ECH (`CSI n X`)**: for a run of ≥ ~6 blanks inside a row, emit `ECH n` then jump past the run (CUF);
  doesn't move the cursor, so cursor tracking stays exact.
- Pen: blank cells' fg/attrs don't matter visually except underline/reverse — only use EL/ECH when the
  blank's style has no attributes that make blanks visible (reverse, underline, strike).
- Keep "re-emit vs jump" logic for short gaps; pick the cheapest of spaces / ECH / EL by byte count.

## Tests
Exact-byte tests (closing a dialog over a blank screen, trailing blanks with a colored background, reverse
blanks keep spaces); fuzz emulator gains EL/ECH with BCE; bytes report adds a "dialog closed" scenario.

## Verification
`-- bytes` before/after; render benchmarks must not regress.
