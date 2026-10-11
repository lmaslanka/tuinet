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

## Status: implemented

- Detection: XTVERSION (`CSI > 0 q`) at startup when `RepeatSequences` is on, beside the DECRQM query (on the alternate
  screen before the clear; in inline mode in the band, followed by `\r CSI K`). The parser reads the reply
  `DCS > | name(version) ST`; `ESC P` not followed by `>` is still Alt+Shift+P. The name, up to a space or parenthesis,
  must be on an allow-list: xterm, kitty, foot, WezTerm, Ghostty. `Terminal.RepeatSequencesActive` reports it. Not on
  the list: tmux (it answers for the terminal inside it, and repeats only ASCII), xterm.js, iTerm2, VTE, Konsole and
  anything else not checked; DA1 isn't used, since its 22 means ANSI color, not REP.
- Emission: right after a narrow glyph, with nothing in between, `CSI n b` covers the cells up to the last one of the
  run that must be written, clean cells inside it included, when `3 + digits(n)` is fewer bytes than `n` glyphs: 5+
  more ASCII cells, 3+ for 2-byte UTF-8, 2+ for box drawing and blocks. Only single code points that never join a
  neighbour (`Graphemes.Simple`) and unlinked cells; never clusters, wide glyphs or emoji. A run ending in the last
  column leaves the cursor column unknown, as a glyph there does. A blank run inside a row weighs one space + REP
  against ECH + jump, only when both need the same SGR (ECH needs only the background); EL to the row end always wins.
- The row loop takes REP as a compile-time policy, like the join checks: without it the code is what it was.
- Bytes (200×60, `-- bytes`): rules every third row 14,885 → 3,065; bar chart 19,754 → 3,071; dialog opening
  2,037 → 1,524; list first frame 3,721 → 2,573; Showcase edit dialog 5,478 → 4,144. Text-only frames don't change.
  CPU: rules and bars render in ~17 µs instead of 33–39 µs; a text-only full repaint costs ~13% more with REP on
  (34.5 → 39 µs) and the same with it off.
- Not verified on a real terminal with REP (only tmux is installed here): the fuzz emulator implements REP strictly (only
  right after a narrow glyph, never past the right edge, a cluster never repeated).
