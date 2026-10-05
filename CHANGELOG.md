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
- Suspend and resume (Unix): `Terminal.Suspend()` and `TerminalOptions.SuspendOnCtrlZ` stop the app
  the way Ctrl+Z stops a shell command; `fg` resumes with a full repaint. `kill -TSTP` suspends cleanly
  and the app recovers from `kill -STOP`.
- `TestTty` for driving a `Terminal` in tests.
- Native AOT and trimming compatible.

[Unreleased]: https://github.com/lmaslanka/tuinet/commits/main
