# 14 · Repeat-character sequence for long runs

**Type:** performance · **Effort:** S · **Priority:** 7

## Problem
Borders and rules send each glyph: a 198-cell `─` rule is 594 bytes. `REP` (`CSI n b`) repeats the last
graphic character: the same rule becomes about 8 bytes.

## Design
- In `RenderRow`, when ≥ N identical cells (same rune, style, width 1) follow an emitted cell, emit `CSI n b`.
- Support varies (xterm, foot, kitty, WezTerm, Ghostty yes; the Linux console and some others no), so it's
  enabled only after detection: primary device attributes (`CSI c`) reply containing parameter 22 isn't a
  guarantee — use DECRQM/XTVERSION allow-list, plus `TerminalOptions.RepeatSequences` override.
- Never across a grapheme cell (plan 01) or a wide glyph.

## Tests
Exact bytes; fuzz emulator implements REP; bytes report shows border-heavy frames shrinking.
