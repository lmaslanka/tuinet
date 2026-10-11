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
- Optional main loop: `Terminal.Run(IApp)` draws a frame, sleeps until input, handles every event already waiting
  and draws once. While `IApp.IsAnimating` it draws about 30 frames a second (`frameMs`), with an `EventKind.Tick`
  for each frame no input caused, and `IApp.NextDueMs` wakes an idle app at a given time. In inline mode the last
  frame is drawn again after the app stops. It allocates nothing per frame. `FocusRing` is a value type for the
  focused control: Tab/Shift+Tab wrap around, a click focuses the control under it, and `Is(i)` feeds `Focused`.
  `Alarm` is a one-shot "at this time" check for timed changes, with no timer or callback.
  `Terminal.LastFrameTime` is the time from `BeginFrame` to the end of `Present`. All three samples run on `Run`,
  the edit dialog's focus is a `FocusRing`, and the Showcase's "saved"/"copied" message hides itself after 3 s.
- Tree view: `TreeView<TSource>` over an `ITreeSource` (integer node ids, children asked for only once a node is
  expanded) with `TreeState`: `[+]`/`[-]` expanders, `├─`/`└─`/`│` guides, cached visible rows (a frame draws only
  the rows on screen; rows are rebuilt only when the tree's shape changes), a selection that follows its node,
  `Select` to open the path to a node, `Invalidate` for changed data. Keys (arrows, h/j/k/l, PageUp/PageDown,
  Home/End, Space), a click on the expander, wheel and scrollbar drag. A frame takes ~7 µs at any scroll position in
  a 109,000-row tree. The Showcase has a Groups tab: the items by kind and priority, in sync with the list.
- Split scrolling: on terminals with left/right margins (DECLRMM/DECSLRM, asked with DECRQM at startup), a band
  narrower than the screen that scrolls, such as a list beside a panel, is moved by the terminal too. A 150-column
  list scrolling beside a 50-column panel goes from 10.3 KB to 459 bytes per frame. `Terminal.LeftRightMarginsActive`
  reports whether the terminal has them; elsewhere nothing changes. `TerminalOptions.ScrollRegions` gates it.
- Repeated glyphs: on terminals that name themselves in reply to XTVERSION (asked at startup) as xterm, kitty, foot,
  WezTerm or Ghostty, a glyph followed by more of the same cell (a border, a rule, a bar, a highlighted blank row) is
  drawn once and repeated with REP (`CSI n b`) when that is shorter: from 5 more ASCII cells, 3 more 2-byte or 2 more
  box-drawing ones. At 200×60 a screen with a rule every third row goes from 14.9 KB to 3.1 KB, a bar chart from
  19.8 KB to 3.1 KB, a dialog opening from 2.0 KB to 1.5 KB and a list's first frame from 3.7 KB to 2.6 KB; in the
  Showcase the edit dialog opens in 4.1 KB instead of 5.5 KB. Clusters, wide glyphs and links are never repeated.
  `Terminal.RepeatSequencesActive` reports it; elsewhere the output is unchanged. `TerminalOptions.RepeatSequences`
  turns it off.
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
- Multi-line text editing: `TextArea` / `TextAreaState` with soft wrap at word boundaries (or sideways scroll),
  line numbers, a placeholder, ↑/↓ and PageUp/PageDown by screen row keeping the column, and click, drag and
  wheel. The text is a gap buffer with a lazily shifted line index, and frames only measure visible lines: a
  100,000-line text renders in 60 µs at 200×60 with zero allocations.
- Selection and undo/redo in both text editors, from a shared core (`EditableText`): Shift+movement and mouse
  drags select, `Selection` feeds `Terminal.CopyToClipboard`, typing replaces the selection, and Ctrl+Z / Ctrl+Y
  (also Ctrl+/ and Ctrl+Shift+Z) undo and redo a word of typing at a time, restoring replaced selections.
  `TextInputState` offsets (`Caret`, `Scroll`, `Length`) are now UTF-16 chars, not runes; `Runes` and
  `DisplayRune` are gone (use `Text`).
- `CellBuffer.Erase` is vectorized: erasing a 200×60 area went from 156 µs to 21 µs, which every widget that
  erases its area (`TextInput`, `Button`, `TextArea`) benefits from.
- `Popup`: a drop shadow (restyles the cells underneath, keeping their glyphs), a fill and a `Block` in one call,
  with `Frame`/`Inner`/`Outer` to size and hit-test it. `Rect.PlaceNear(anchor, size)` places a popup by an anchor
  (below, else above, else the roomier side; a 0×0 anchor is a point) and `Rect.Centered(Size)`. `Dropdown` uses
  both and takes `PopupShadow`.
- `Tabs` / `TabsState`: a one-row tab bar with dividers that scrolls to keep the selected tab visible, with `‹` `›`
  when the titles don't fit. It draws only the tabs, so it fits in a block's border. `Next`/`Previous` wrap;
  `HandleMouse` selects on click and scrolls on the arrows and wheel; `TabAt` hit-tests.
- `Sparkline` and `BarChart`: a value over time in eighth-block columns (several rows stack into 8 levels each,
  newest on the right, scaled to the largest visible value, and drawn from a ring buffer's two halves without a
  copy), and vertical or horizontal bars with eighth-block ends, labels and values formatted on the stack. Both
  allocate nothing per frame. The showcase's stats page charts items per kind and priority and the bytes and time
  of recent frames; the stress sample's status bar ends in a frame-time sparkline.
- `Menu` / `MenuState` / `MenuItem`: a popup action list with `&` mnemonics, right-aligned shortcuts, separators
  joined to the border (with tees that match it) and disabled items. Up/Down and j/k skip separators and disabled
  items and wrap; Enter, Space, a click or a mnemonic activates; Esc or a press outside cancels, and the caller can
  still act on that click. With `MouseMotion`, the item under the pointer is highlighted. `Menu.Measure` sizes it
  for `Rect.PlaceNear`; a menu taller than its area scrolls.
- `Fuzzy.Score`: fzf-style fuzzy matching (word starts, camelCase, runs; smart case), optionally with the matched
  positions for highlighting. 28 ns per name, 0 B. `TextInputState.CopyTo` / `TextAreaState.CopyTo` read the text
  without allocating.
- The showcase gets a context menu on the rows (right-click or `m`) and a command palette (Ctrl+P or `:`) over
  every action, filtered as you type with the matches highlighted.
- `CellBuffer.SetStyle` and `SetLink` no longer throw for an area entirely to the right of the buffer.
- `TestTty` for driving a `Terminal` in tests.
- `Tuinet.Testing`, a test-helper package for apps (any test framework; failures throw `ScreenAssertionException`):
  `TestTerminal` runs an `IApp` over a real `Terminal` on a `TestTty` with a test clock (`Press("jj")`, `Key`,
  `Click(x, y)` or `Click("Save")`, `Wheel`, `Mouse`, `Paste`, `Resize`, `Post`, raw `Input`, `Advance`), a frame
  after each input and `Screen` as a stable copy. `buffer.ToStyledString()` writes a frame as markup
  (`[fg=#F5A623 b]ITEMS[/]`) that `Snapshot.Parse` reads back; `ToAnsi()` prints it in color.
  `Snapshot.Verify(buffer, name)` compares with `__snapshots__/name.txt` next to the test, writes
  `name.received.txt` on a mismatch, and `TUINET_ACCEPT_SNAPSHOTS=1` accepts. `FindText`, `StyleAt`, `StyleOf` and
  `Allocations.AssertNone` (the steady-state zero-allocation check). The sample tests use it, with styled snapshots
  of the Showcase screens and every Gallery page.
- `CellBuffer.CopyTo`: an exact copy of the cells, links and cursor, allocating only when the destination grows.
- Docs site: topic pages and the API reference for `Tuinet` and `Tuinet.Testing`, built with docfx (pinned as a local
  dotnet tool), checked in CI with warnings as errors and published to GitHub Pages.
- Gallery sample: every widget live beside the code that draws it. `tools/screenshots` regenerates the README and docs
  images from the Showcase and Gallery, and CI checks they are deterministic and up to date.
- Comparison benchmarks against Spectre.Console and Terminal.Gui (`bench/Tuinet.Comparisons`), with the
  methodology and results in the README.
- Native AOT and trimming compatible.

[Unreleased]: https://github.com/lmaslanka/tuinet/commits/main
