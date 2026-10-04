# 11 · Terminal extras: title, clipboard, cursor shape, hyperlinks, kitty keyboard

**Type:** missing · **Effort:** S–M each · **Priority:** 5

## Window title
`Terminal.SetTitle(ReadOnlySpan<char>)` → OSC 2 (sanitised: no control chars). Save/restore the title with
`CSI 22;0t` / `CSI 23;0t` on enter/exit.

## Clipboard (copy) over SSH
`Terminal.CopyToClipboard(ReadOnlySpan<char>)` → OSC 52 with base64 (stack/pooled, no steady-state allocation),
size cap (terminals limit it; e.g. 100 KB). Paste already arrives via bracketed paste.

## Cursor shape
`CellBuffer.SetCursor(x, y, CursorShape)` (block/underline/bar, blinking or steady) → DECSCUSR (`CSI n q`)
emitted only on change by `Renderer`; reset to default on exit. `TextInput` uses a bar caret.

## Hyperlinks
OSC 8 links need per-cell link ids → reuse the interned-store approach from plan 01 (a link id in a side
table keyed per cell range, or a cell flag + id table). `CellBuffer.SetString(..., link: url)`; the renderer
opens/closes OSC 8 around runs with the same link.

## Kitty keyboard protocol
The parser understands `CSI u` keys but never asks for them. Opt-in `TerminalOptions.KittyKeyboard`:
push flags (`CSI > 1 u`, disambiguate) on enter, pop (`CSI < u`) on exit. Gains: Ctrl+I vs Tab, Ctrl+M vs
Enter, Esc without timeout, optional key release/repeat events (`KeyEvent.Kind`).
Detect support with `CSI ? u` query; fall back silently.

## Tests
Exact bytes per feature in `TerminalTests`/`RendererTests`, parser tests for kitty replies and release
events, emulator support for DECSCUSR/OSC 8 in the fuzz test.
