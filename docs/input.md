# Input and the terminal

Events come from `Terminal.Poll` (or `Terminal.Run`, which polls for you). This page covers keys, mouse and paste,
results from background threads, and the terminal features beyond drawing: the title, the clipboard, cursor shapes,
hyperlinks, suspend and inline mode.

## Keys, mouse and paste

```csharp
using var term = Terminal.Open(new TerminalOptions { Mouse = true, BracketedPaste = true, FocusEvents = true });
```

```csharp
switch (ev.Kind)
{
    case EventKind.Key when ev.Key.Is(KeyCode.Up, Modifiers.Ctrl): /* Ctrl+↑ */ break;
    case EventKind.Key when ev.Key.IsCtrl('s'): /* Ctrl+S */ break;
    case EventKind.Mouse when ev.Mouse.IsClickIn(saveButton): Save(); break;
    case EventKind.Paste: input.Insert(ev.Paste); break;   // one event, not a key per char
    case EventKind.Resize: break;                         // the next BeginFrame already has the new size
}
```

**Mouse.** There is no retained widget tree to route clicks, so hit-testing uses where things were
drawn on the last frame. Stateful widgets record it in their state. For stateless ones (buttons,
checkboxes), keep the `Rect` you rendered into and test it with `ev.Mouse.IsClickIn(rect)`. Every
`HandleMouse` returns whether it used the event, so you can try one control after another.

| Call | Does |
|---|---|
| `ListState.HandleMouse(ev, count)` | Click selects the row under the pointer, wheel scrolls, click or drag on the scrollbar scrolls there (lists and tables) |
| `ListState.RowAt(x, y, count)` | Item drawn at a cell, or -1 |
| `table.HeaderColumnAt(x, y, state)` | Column whose header is at a cell, or -1 (e.g. click to sort) |
| `Scrollbar.PositionAt(area, x, y, content, viewport)` | Scroll position for a click or drag on a scrollbar you drew |
| `DropdownState.HandleMouse(ev, count)` | Click opens; click an item to pick it; wheel scrolls the list; click elsewhere cancels |
| `tabs.HandleMouse(ev, ref state)` | Click selects a tab; click on `‹` `›` or the wheel scrolls the bar |
| `tabs.TabAt(x, y, state)` | Tab drawn at a cell, or -1 |
| `popup.Frame(area)` | The box without its shadow, for click-outside tests |
| `TextInputState.HandleMouse(ev)` | Click puts the caret on the clicked character (wide glyphs and clusters included); Shift+click or drag selects |
| `TextAreaState.HandleMouse(ev)` | The same on wrapped rows, plus the wheel scrolls and a drag past the edge scrolls |
| `MouseEvent.IsClick` / `IsWheel` / `WheelDelta` / `IsIn(rect)` / `IsClickIn(rect)` | Conveniences |

Widgets act on the press (`MouseKind.Down`), as most terminal apps do. The showcase uses all of these:
click rows, double-click to edit, wheel-scroll the table, drag its scrollbar, click a header to sort, click a tab,
click every control in the edit dialog, and click outside the progress dialog to close it.

| `TerminalOptions` | Default | |
|---|---|---|
| `Mouse` / `MouseMotion` | off | Clicks, drags and wheel (SGR 1006) / also motion with no button held |
| `BracketedPaste` | off | Pastes arrive as one `Paste` event |
| `FocusEvents` | off | `FocusGained` / `FocusLost` |
| `ColorMode` | detected | `TrueColor`, `Indexed256`, `Basic16` or `None` |
| `EscapeTimeoutMs` | 20 | How long a lone ESC waits before it counts as the Escape key |
| `ScrollRegions` | on | Let the terminal move rows that scrolled, instead of repainting them: full-width bands, and narrower ones where the terminal reports left/right margins (`term.LeftRightMarginsActive`) |
| `EraseSequences` | on | Clear runs of blanks with EL/ECH instead of spaces (needs background color erase, which modern terminals have) |
| `SuspendOnCtrlZ` | off | Ctrl+Z suspends the app inside `Poll` (see below) instead of arriving as a key |
| `KittyKeyboard` | off | Kitty keyboard protocol where the terminal has it (see below) |
| `KeyReleaseEvents` | off | With `KittyKeyboard`: also key repeats and releases (`KeyEvent.Kind`) |
| `Inline` | null | A band of `InlineOptions.Height` rows under the prompt instead of the alternate screen (see below) |

Every mode is switched off again on exit, on a crash, or on a signal.

**Kitty keyboard.** The keyboard encoding terminals inherited from the 1970s sends some keys as the same
bytes: Ctrl+I is Tab, Ctrl+M is Enter, Ctrl+[ is Esc, and Ctrl+Shift+A is Ctrl+A. Esc is also the first byte
of every escape sequence, so a lone Esc waits `EscapeTimeoutMs` in case more is coming. With
`KittyKeyboard = true`, terminals that support the [kitty keyboard protocol](https://sw.kovidgoyal.net/kitty/keyboard-protocol/)
(kitty, Ghostty, foot, WezTerm, recent Alacritty and iTerm2) send those keys unambiguously, and Esc arrives at
once. Other terminals ignore the request and nothing changes. `term.KittyKeyboardActive` turns true once the
terminal confirms it, during the first polls. `KeyReleaseEvents` adds key repeats and releases
(`KeyEvent.Kind`), e.g. for hold-to-move. Releases never match `Is`, `IsChar` or `IsCtrl`, so existing key
bindings don't fire twice.

## Background work

`Post` is thread-safe and wakes a blocked `Poll`. The message comes back as `EventKind.Message`
on the UI thread.

```csharp
record Loaded(string[] Items);
```

```csharp
_ = Task.Run(() => term.Post(new Loaded(LoadItems())));   // any thread; wakes Poll

// in the loop
if (ev.Message is Loaded loaded)
{
    items = loaded.Items;
}
```

## Title, clipboard, cursor and links

```csharp
term.SetTitle(file.Name);                       // OSC 2; only changes are sent, so call it every frame
term.CopyToClipboard(selection);                // OSC 52: the user's clipboard, even over SSH

frame.SetCursor(x, y, CursorShape.Bar);         // DECSCUSR, sent only when the shape changes

int end = frame.SetString(x, y, "docs", new Style(Color.Blue, default, Attr.Underline));
frame.SetLink(new Rect(x, y, end - x, 1), "https://example.com/docs");   // OSC 8: Ctrl+click opens it
```

- **Title.** Control characters are dropped. On exit the terminal's own title comes back (title stack,
  `CSI 22 t` / `CSI 23 t`).
- **Clipboard.** Up to `Terminal.MaxClipboardBytes` of UTF-8. Terminals may ignore OSC 52 or ask the user
  first, and in tmux it needs `set-clipboard on`. There is no reply saying whether it worked. Paste already
  arrives as a `Paste` event with `BracketedPaste`.
- **Cursor shape.** Block, underline or bar, blinking or steady. A focused `TextInput` shows a blinking bar
  (its `CursorShape` property). The user's shape comes back on exit, but only if the app changed it.
- **Links.** Terminals without OSC 8 show plain text. Text drawn over a linked cell later replaces the link,
  as it does attributes. `SetStyle` keeps it. URLs with control characters are refused; spaces and non-ASCII
  are percent-encoded. A process can link up to 4095 distinct URLs, and after that new links show as plain
  text.

The showcase uses all four: the window title names the selected item, `y` copies its name, the subtitle has a
link to this repository, and text fields show a bar caret.

## Suspend (Ctrl+Z)

Raw mode turns Ctrl+Z into an ordinary key. To let users background the app as they would a shell
command, set `SuspendOnCtrlZ`, or call `Suspend` yourself:

```csharp
case EventKind.Key when ev.Key.IsCtrl('z'): term.Suspend(); break;
```

`Suspend` leaves the screen, restores the terminal and stops the process. When the user types `fg`, it
returns with the alternate screen back, and the next `Poll` returns a `Resize` event so the app draws a
frame, which repaints everything. A `kill -TSTP` from outside suspends the same way on the next `Poll`,
and the app also recovers from `kill -STOP`. On Windows, `Suspend` returns false and does nothing.

## Inline mode

CLI tools often want a live area under the prompt, not a full screen: progress bars, a spinner, a prompt, a
live table. With `Inline`, the terminal draws the frame in a band of that many rows under the cursor, and the
band stays in the terminal (and its scrollback) after exit. Everything else is the same: `BeginFrame`
returns a buffer the size of the band, and only changes are written.

```csharp
using var term = Terminal.Open(new TerminalOptions { Inline = new InlineOptions(height: 4) });

term.PrintAbove($"✓ {file.Name}");    // a log line above the band; it stays in the scrollback
CellBuffer frame = term.BeginFrame();  // 4 rows, full width
frame.Render(new ProgressBar(done / (double)total), new Rect(0, 0, frame.Width, 1));
term.Present();
```

- **Start.** The band starts on the cursor's line (the next one if that line has text). Near the bottom of
  the screen, the screen scrolls up to make room. To find the cursor, the terminal is asked for its
  position (`CSI 6n`); with no reply within `CursorReportTimeoutMs`, the band goes at the bottom.
- **`PrintAbove`** prints wrapped, sanitized text (plain or `StyledText`) where the band was, moves the band
  down (scrolling once it reaches the bottom) and repaints it, in one write.
- **Exit** keeps the last frame and continues on the line after its last non-blank row, so a final
  one-line summary frame reads like ordinary output.
- **Resize** reports a `Resize` with the band's size, asks the terminal where the band went and repaints
  it there. Taller, shorter and wider are seamless. When a terminal that reflows long lines gets
  narrower, the band's old rows can be rewrapped in ways that leave a stale copy above it (tmux does
  this).

The `Tuinet.Samples.Inline` sample is a complete example.

---

[Getting started](getting-started.md) · [Layout and styling](layout.md) · [Widgets](widgets.md) ·
[Input and the terminal](input.md) · [Testing](testing.md) · [Performance](performance.md) · [API reference](api/index.md)
