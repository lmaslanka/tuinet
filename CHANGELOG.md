# Changelog

All notable changes to this project are documented here. The format follows
[Keep a Changelog](https://keepachangelog.com/en/1.1.0/), and the project uses
[Semantic Versioning](https://semver.org/spec/v2.0.0.html). Before 1.0, minor versions may break the API;
every public API change is recorded in `src/Tuinet/PublicAPI.*.txt`.

## [Unreleased]

The first release.

### Added
- Immediate-mode kernel: draw into a `CellBuffer` every frame; the renderer diffs it against the screen
  and sends only what changed, in one synchronized write per frame.
- Renderer optimizations: vectorized skipping of unchanged rows, relative cursor jumps over gaps inside a
  row, scroll detection with scroll margins + insert/delete line, and minimal style deltas.
- Zero allocations per steady-state frame and per input poll, enforced by tests.
- Truecolor with automatic fallback to 256 or 16 colors.
- Input: keys with Ctrl/Alt/Shift, F1–F12, PgUp/PgDn; SGR 1006 mouse; bracketed paste; focus events;
  thread-safe `Post` for background results.
- Constraint layout.
- Widgets: `Block`, `Paragraph`, `ListView<T>`, `Table<T>`, `TextInput`, `Dropdown<T>`, `Checkbox`,
  `Button`, `ProgressBar`, `Spinner` and `Clear`.
- Mixed-style text (`StyledText`) in paragraphs and block titles.
- Unicode 17 width tables for wide CJK and emoji glyphs; grapheme clusters (combining marks, Indic and
  Thai syllables, emoji sequences, flags) occupy one cell, with rows aligned on terminals with or
  without grapheme support.
- Safety: control characters in text never reach the terminal; the terminal is restored on exit, on an
  unhandled exception and on SIGINT/SIGTERM/SIGHUP.
- Backends: Linux and macOS (termios, `poll(2)`, `SIGWINCH`) and Windows 10+ (console VT mode).
- Fewer bytes for blanks: runs of blank cells are cleared with EL (to the end of the row) or ECH (n cells)
  instead of spaces when that is shorter; erased cells keep their background (BCE). A dialog closing over a
  themed screen goes from 2.2 KB to 313 bytes, and list and table scroll frames shrink by 53–71%.
  `TerminalOptions.EraseSequences` turns it off.
- Inline mode: `TerminalOptions.Inline = new InlineOptions(height)` draws in a band under the shell prompt
  instead of on the alternate screen; the band stays in the scrollback after exit. `Terminal.PrintAbove`
  prints log lines above the band. The band is placed with a cursor position query and follows resizes.
- Mouse in widgets: `ListState.HandleMouse` (click selects, wheel scrolls lists and tables),
  `ListState.RowAt`, `Table.HeaderColumnAt` (click a header to sort), `DropdownState.HandleMouse` and
  `TextInputState.HandleMouse` (click places the caret), plus `MouseEvent.IsClick`, `IsWheel`,
  `WheelDelta`, `IsIn` and `IsClickIn`. Hit-testing uses where each widget was drawn on the last frame.
- Scrollbars: `ListView`, `Table` and `Paragraph` take `Scrollbar = ScrollbarMode.Auto` (or `Always`) and
  draw one in their rightmost column, with eighth-block thumb ends. `ListState.HandleMouse` scrolls on a click
  or drag on it. The standalone `Scrollbar` widget (vertical or horizontal) and `Scrollbar.PositionAt` cover
  everything else.
- Terminal extras: `Terminal.SetTitle` (OSC 2, the shell's title restored on exit), `Terminal.CopyToClipboard`
  (OSC 52, works over SSH), cursor shapes (`CellBuffer.SetCursor(x, y, CursorShape)`, DECSCUSR, sent on change and
  reset on exit; `TextInput` shows a blinking bar) and hyperlinks (`CellBuffer.SetLink`, OSC 8, `Cell.Link`).
  The title and shape resets are added to the exit sequence only once used.
- Kitty keyboard protocol (opt-in, `TerminalOptions.KittyKeyboard`): Ctrl+I, Ctrl+M, Ctrl+[ and Ctrl+Shift+letter
  arrive distinct from Tab, Enter, Esc and Ctrl+letter, and Esc needs no timeout. `KeyReleaseEvents` adds key
  repeats and releases (`KeyEvent.Kind`); keypad keys map to their keys. `Terminal.KittyKeyboardActive` reports
  whether the terminal confirmed it.
- Suspend and resume (Unix): `Terminal.Suspend()` and `TerminalOptions.SuspendOnCtrlZ` stop the app
  the way Ctrl+Z stops a shell command; `fg` resumes with a full repaint. `kill -TSTP` suspends cleanly
  and the app recovers from `kill -STOP`.
- `TestTty` for driving a `Terminal` in tests.
- Native AOT and trimming compatible.

[Unreleased]: https://github.com/lmaslanka/tuinet/commits/main
