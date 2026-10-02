<div align="center">

# ▐█▌ TUI.NET

**Immediate-mode terminal UIs for .NET, built for speed.**

Redraw everything every frame and let TUI.NET send only what changed.<br>
Zero allocations per frame · one `write` per frame · Native AOT

[![CI](https://github.com/lmaslanka/tuinet/actions/workflows/ci.yml/badge.svg)](https://github.com/lmaslanka/tuinet/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Native AOT](https://img.shields.io/badge/Native_AOT-ready-34D399)
![Allocations](https://img.shields.io/badge/allocations-0_B%2Fframe-38BDF8)
![License](https://img.shields.io/badge/license-Apache--2.0-F5A623)

<img src="docs/images/showcase-main.png" width="860" alt="TUI.NET showcase: a themed list with a details panel">

</div>

---

## ✨ Highlights

- **Immediate mode.** There is no widget tree to keep in sync. Each frame, your app draws its state into a cell buffer. TUI.NET diffs that buffer against the screen and writes the difference.
- **Fast by design.**
  - Unchanged rows are skipped with a vectorized memcmp.
  - Gaps inside a row are jumped with relative cursor moves.
  - Style changes are sent as minimal deltas.
  - Each frame goes out in a single synchronized write.
- **Zero allocations.** Steady-state rendering and input polling allocate nothing. Tests enforce this, so there are no GC pauses between a key press and the frame it produces.
- **Batteries included.**
  - Widgets: blocks, paragraphs, virtualized lists and tables, text inputs, dropdowns, checkboxes, buttons, progress bars and spinners.
  - A constraint layout.
  - Truecolor with automatic fallback to 256 or 16 colors.
- **Full input.**
  - Keys with Ctrl/Alt/Shift, F1–F12, PgUp/PgDn.
  - Mouse (SGR 1006), bracketed paste and focus events.
  - Thread-safe `Post` for background results.
- **Safe by default.**
  - Control characters in your text can never reach the terminal as escape sequences.
  - The terminal is restored on exit, on an unhandled exception, and on SIGINT/SIGTERM/SIGHUP.
  - Wide CJK and emoji glyphs are handled from Unicode 17 width tables.
- **Native AOT.** No reflection. The stress sample publishes to a 1.3 MB native binary.

<table>
  <tr>
    <td><img src="docs/images/showcase-edit.png" alt="Edit dialog with text inputs, dropdown and checkboxes"></td>
    <td><img src="docs/images/showcase-progress.png" alt="Progress dialog with animated bars and spinners"></td>
  </tr>
  <tr>
    <td align="center"><sub>Text inputs, dropdowns, checkboxes and buttons</sub></td>
    <td align="center"><sub>Progress bars, segmented meters and spinners</sub></td>
  </tr>
</table>

## 🚀 Quick start

**Requirements:** the .NET 10 SDK and a terminal with VT support. That covers essentially every modern terminal on Linux and macOS, plus Windows Terminal and conhost on Windows 10+.

TUI.NET isn't on NuGet yet. Reference the project directly, or pack it locally (the package and namespace are `Tuinet`):

```sh
git clone https://github.com/lmaslanka/tuinet.git
dotnet pack tuinet/src/Tuinet -c Release -o ./packages
dotnet add package Tuinet --source ./packages
```

A complete program:

```csharp
using Tuinet;
using Tuinet.Widgets;

using var term = Terminal.Open();
while (true)
{
    CellBuffer frame = term.BeginFrame();
    var block = new Block { Title = " hello ", BorderType = BorderType.Rounded };
    frame.Render(block, frame.Area);
    frame.SetString(2, 2, "Press q to quit", new Style(Color.Cyan, default, Attr.Bold));
    term.Present();

    if (term.Poll(out Event ev, Timeout.Infinite) && ev.Key.IsChar('q'))
    {
        break;
    }
}
```

To see what the library can do, run the samples:

```sh
./run           # the showcase in the screenshots: list, edit form, progress dialog
./run stress    # latency harness: hold j on 100k items, or press n for full-screen noise
```

## 🧠 How it works

```
  Poll ──▶ Handle ──▶ BeginFrame ──▶ Render ──▶ Present
   ▲      (update       (cleared       (draw the     (diff vs screen,
   │       your state)   back buffer)   whole UI)     one write, swap)
   └──────────────────────────────────────────────────────┘
```

Your app owns its state. Each frame it renders that state into a `CellBuffer`, the same way a game
engine draws a frame. `Present()` compares the new buffer with what's on screen, encodes the
difference as VT bytes, and sends it in a single `write`, wrapped in synchronized-update markers so
the terminal never shows half a frame.

The recommended loop handles every input event that's already waiting before it draws. That way a
burst of key repeats costs one frame instead of dozens, and the UI never builds a backlog:

```csharp
bool running = true;
while (running)
{
    Render(term.BeginFrame());   // draw everything, every frame
    term.Present();              // diff + one write

    if (!term.Poll(out Event ev, Timeout.Infinite))
    {
        continue;
    }

    do
    {
        running = Handle(ev);
    }
    while (running && term.Poll(out ev, 0));   // drain the burst, then draw once
}
```

## 📚 Guide

### Layout

`Layout` splits a `Rect` along one axis. The constraints are a collection expression and the results
go into a `stackalloc`'d span, so nothing reaches the heap.

```csharp
Span<Rect> rows = stackalloc Rect[3];
Layout.Vertical(frame.Area, [Constraint.Length(1), Constraint.Fill(), Constraint.Length(1)], rows);

Span<Rect> cols = stackalloc Rect[2];
Layout.Horizontal(rows[1], [Constraint.Percent(30), Constraint.Fill()], cols, spacing: 1);
// rows[0] = header, cols[0] = sidebar, cols[1] = content, rows[2] = status bar
```

| Constraint | Meaning |
|---|---|
| `Length(n)` | Exactly `n` cells |
| `Percent(p)` | `p`% of the available space |
| `Fill(weight)` | A share of what's left, in proportion to `weight` |
| `Min(n)` / `Max(n)` | At least / at most `n`; they grow into leftover space when there's no `Fill` |

`Rect` also has `Inset`, `Intersect`, `Centered(w, h)` (handy for dialogs) and `Row(i)`.

### Styles and colors

A `Style` is a foreground color, a background color and attributes (`Bold`, `Dim`, `Italic`,
`Underline`, `Blink`, `Reverse`, `Hidden`, `Strike`). A `Color` is the terminal default,
a palette index (`Color.Red`, `Color.Indexed(208)`), or RGB (`Color.Rgb(…)`, `Color.Hex(0x34D399)`).

Text **layers**: a default color in the style keeps whatever color the cell already has. Fill a
panel once, and every piece of text on it keeps the panel's background without repeating it.

```csharp
var panel = new Style(Color.Hex(0xE2E8F0), Color.Hex(0x111722));
var accent = new Style(Color.Hex(0x34D399), default, Attr.Bold);

frame.Fill(area, panel);                                       // background
int x = frame.SetString(area.X, area.Y, "build ", accent);     // default bg keeps the panel color
x = frame.SetString(x, area.Y, "passing", new Style(Color.Green, default));
frame.SetString(x + 1, area.Y, "· 14/14", new Style(Color.BrightBlack, default, Attr.Italic));
```

`SetString` returns the column after the text it wrote, so styled segments chain naturally. Text is
clipped to the buffer, or to `maxWidth`, optionally ending in `…` (`Overflow.Ellipsis`).

TUI.NET detects the terminal's color depth from `COLORTERM`, `TERM`, `TERM_PROGRAM`, `WT_SESSION` and
`NO_COLOR`. It maps RGB down to 256 or 16 colors as needed, so a truecolor theme still looks right
on a basic terminal.

### Lists

`ListView` is virtualized: it asks your `IListSource` to draw only the rows that are visible. It
handles selection, highlighting and keeping the selected row in view. The selection lives in a
`ListState` that you own.

```csharp
readonly struct Files(FileInfo[] files) : IListSource
{
    public int Count => files.Length;

    public void RenderItem(int index, Rect area, CellBuffer buffer, bool selected)
    {
        Span<char> size = stackalloc char[20];
        files[index].Length.TryFormat(size, out int n);
        buffer.SetString(area.X, area.Y, files[index].Name, default, area.Width - n - 1, Overflow.Ellipsis);
        buffer.SetString(area.Right - n, area.Y, size[..n], new Style(Color.BrightBlack, default));
    }
}
```

```csharp
var state = new ListState();   // keep this across frames

// render
frame.Render(new ListView<Files>(new Files(files))
{
    SelectedStyle = new Style(default, Color.Hex(0x1C2433), Attr.Bold),
    HighlightSymbol = "▌",
    HighlightSymbolStyle = new Style(Color.Cyan, default),
}, area, ref state);

// input
if (ev.Key.IsChar('j')) state.Next(files.Length);
if (ev.Key.IsChar('k')) state.Previous(files.Length);
if (ev.Key.Is(KeyCode.PageDown)) state.PageDown(files.Length);
```

For plain strings, use the built-in `TextItems` source: `new ListView<TextItems>(new TextItems(names))`.

### Tables

`Table` is a virtualized list with a header and columns. Column widths are layout constraints.
Each column aligns its header and cells left, center or right. Your `ITableSource` returns the text
of each visible cell: either a string you already hold, or text formatted into the `scratch` span
it is given. The table measures, aligns and clips the text, so formatting numbers stays allocation-free.
Selection uses the same `ListState` as `ListView`.

```csharp
static readonly TableColumn[] Columns =
[
    new("name", Constraint.Fill()),
    new("size", Constraint.Length(10), Alignment.Right),
    new("state", Constraint.Length(7), Alignment.Center),
];

readonly struct Files(FileInfo[] files) : ITableSource
{
    public int RowCount => files.Length;

    public ReadOnlySpan<char> Cell(int row, int column, Span<char> scratch, out Style style)
    {
        style = default;
        switch (column)
        {
            case 0:
                return files[row].Name;
            case 1:
                files[row].Length.TryFormat(scratch, out int n);
                return scratch[..n];
            default:
                style = new Style(files[row].IsReadOnly ? Color.Yellow : Color.Green, default);
                return files[row].IsReadOnly ? "ro" : "rw";
        }
    }
}

frame.Render(new Table<Files>(new Files(files), Columns)
{
    HeaderStyle = new Style(Color.BrightBlack, default, Attr.Bold),
    HeaderSeparator = true,                 // ─── under the header
    ColumnSeparator = '│',                  // drawn in the gap, joined with ┼ on the rule
    AlternateRowStyle = new Style(default, Color.Hex(0x111722)),   // zebra stripes
    SortColumn = 1, SortDescending = true,  // ▼ on "size"; the sorting itself is yours
    SelectedStyle = new Style(default, Color.Hex(0x1C2433), Attr.Bold),
    HighlightSymbol = "▌",
}, area, ref state);
```

`Table.Measure(source, column, header)` returns the widest cell in a column. Call it when the data
changes and cache the result as `Constraint.Length(width)` to size a column to its content. For rows
of strings, use the built-in `TextRows` source: `new Table<TextRows>(new TextRows(rows), columns)`.

### Forms

Interactive widgets keep their state in objects you own, so focus is just an `int` in your app.
There's no focus manager to fight.

```csharp
// state, kept across frames
var name = new TextInputState("TUI.NET");
var priority = new DropdownState(selected: 1);
string[] priorities = ["low", "medium", "high"];
bool notify = true;

// render
var box = new Block { Title = " name ", BorderType = BorderType.Rounded };
frame.Render(box, nameArea);
frame.Render(new TextInput { Focused = focus == 0 }, box.Inner(nameArea), ref name);   // places the real cursor

var dropdown = new Dropdown<TextItems>(new TextItems(priorities))
{
    SelectedStyle = new Style(Color.Black, Color.Cyan),
    PopupBordered = true,
};
frame.Render(dropdown, pickArea, ref priority);
frame.Render(new Checkbox("notify me", notify) { Focused = focus == 2 }, checkArea);
frame.Render(new Button("Save") { Focused = focus == 3, FocusedStyle = new Style(Color.Black, Color.Green) }, buttonArea);
dropdown.RenderPopup(pickArea, frame, ref priority);   // last, so it draws on top

// input
switch (focus)
{
    case 0: name.Handle(ev.Key); break;                          // editing, Ctrl+W, Alt+B/F, …
    case 1: priority.Handle(ev.Key, priorities.Length); break;   // Enter opens, ↑/↓ moves, Enter picks
    case 2 when ev.Key.IsChar(' '): notify = !notify; break;
}
```

`TextInputState` supports the usual editing keys: Home/End, Ctrl+A/E, Ctrl+U/K, Ctrl+W, Ctrl/Alt+←/→,
Alt+B/F/D and Delete. It also does masking (`mask: '•'` for passwords) and paste (`Insert(string)`).
For a complete form with validation, see [`EditDialog.cs`](samples/Tuinet.Samples.Showcase/EditDialog.cs).

### Progress and animation

```csharp
var clock = Stopwatch.StartNew();

frame.Render(new ProgressBar(done / (double)total)
{
    FilledStyle = new Style(Color.Green, default),
    EmptyStyle = new Style(Color.BrightBlack, default),   // ░ remainder; eighth-blocks at the edge
}, barArea);

char spinner = Spinner.Frame(Spinner.Dots, clock.ElapsedMilliseconds);
frame.SetRune(barArea.Right + 1, barArea.Y, new Rune(spinner));

// Wake ~30×/s only while something animates; otherwise sleep until input.
bool animating = done < total;
term.Poll(out Event ev, animating ? 33 : Timeout.Infinite);
```

Base animation on elapsed time, not frame count, so it runs at the same speed whatever the frame
rate. For a segmented meter like `■■□□`, use a `ProgressBar` with `FilledChar = '■'` and `EmptyChar = '□'`.

### Background work

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

### Keys, mouse and paste

```csharp
using var term = Terminal.Open(new TerminalOptions { Mouse = true, BracketedPaste = true, FocusEvents = true });
```

```csharp
switch (ev.Kind)
{
    case EventKind.Key when ev.Key.Is(KeyCode.Up, Modifiers.Ctrl): /* Ctrl+↑ */ break;
    case EventKind.Key when ev.Key.IsCtrl('s'): /* Ctrl+S */ break;
    case EventKind.Mouse when ev.Mouse.Kind == MouseKind.Down: Click(ev.Mouse.X, ev.Mouse.Y); break;
    case EventKind.Mouse when ev.Mouse.Kind == MouseKind.ScrollDown: list.Next(count); break;
    case EventKind.Paste: input.Insert(ev.Paste); break;   // one event, not a key per char
    case EventKind.Resize: break;                         // the next BeginFrame already has the new size
}
```

| `TerminalOptions` | Default | |
|---|---|---|
| `Mouse` / `MouseMotion` | off | Clicks, drags and wheel (SGR 1006) / also motion with no button held |
| `BracketedPaste` | off | Pastes arrive as one `Paste` event |
| `FocusEvents` | off | `FocusGained` / `FocusLost` |
| `ColorMode` | detected | `TrueColor`, `Indexed256`, `Basic16` or `None` |
| `EscapeTimeoutMs` | 20 | How long a lone ESC waits before it counts as the Escape key |

Every mode is switched off again on exit, on a crash, or on a signal.

### Custom widgets

A widget is any type that implements `IWidget` (or `IStatefulWidget<TState>`). Make it a
`readonly ref struct` and it can hold spans, such as text formatted on the stack, at zero cost.

```csharp
readonly ref struct Badge : IWidget
{
    private readonly ReadOnlySpan<char> _text;
    private readonly Color _color;

    public Badge(ReadOnlySpan<char> text, Color color)
    {
        _text = text;
        _color = color;
    }

    public void Render(Rect area, CellBuffer buffer)
    {
        int x = buffer.SetString(area.X, area.Y, "▐", new Style(_color, default));
        x = buffer.SetString(x, area.Y, _text, new Style(Color.Black, _color, Attr.Bold));
        buffer.SetString(x, area.Y, "▌", new Style(_color, default));
    }
}
```

```csharp
Span<char> text = stackalloc char[16];
text.TryWrite($"{count} new", out int length);           // formatted on the stack: no string
frame.Render(new Badge(text[..length], Color.Red), area);
```

### Testing

UI code is just a function from state to cells, so you can test it without a terminal. Render
into a `CellBuffer` and assert on its text and styles. To test end to end, drive a real `Terminal`
over the in-memory `TestTty`.

```csharp
[Fact]
public void Shows_the_title()
{
    var buffer = new CellBuffer(80, 24);
    new ShowcaseApp().Render(buffer, nowMs: 0);
    Assert.Contains("ITEMS · 20", buffer.ToString());
    Assert.Equal(Color.Hex(0xF5A623), buffer[2, 4].Style.Fg);   // the list's amber border
}

[Fact]
public void Key_in_frame_out()
{
    var tty = new TestTty(80, 24);
    using var term = new Terminal(tty);
    tty.Enqueue("j");

    Assert.True(term.Poll(out Event ev, 0));
    Assert.True(ev.Key.IsChar('j'));

    term.BeginFrame().SetString(0, 0, "moved");
    term.Present();
    Assert.Contains("moved", tty.WrittenText);
    Assert.Equal(1, tty.WriteCount);
}
```

## 🧩 Widgets

| Widget | State | What it does |
|---|---|---|
| `Block` | | Borders (plain, rounded, double, thick, dashed), title and footer with alignment, background, `Inner(area)` |
| `Paragraph` | | Multi-line text with word/char wrapping, alignment and scroll; `LineCount` for scrollbars |
| `ListView<T>` | `ListState` | Virtualized, selectable, scrolls to follow the selection, highlight symbol |
| `Table<T>` | `ListState` | Header and columns with constraint widths, left/center/right alignment, per-cell styles, separators, zebra stripes, sort arrow |
| `TextInput` | `TextInputState` | Single-line editing, emacs keys, masking, horizontal scroll, real terminal cursor |
| `Dropdown<T>` | `DropdownState` | Select box with a popup list that flips above when there's no room below |
| `Checkbox` | your `bool` | One-row symbol + label, or a large `Boxed` square |
| `Button` | | Padded label with idle and focused styles |
| `ProgressBar` | | Eighth-block precision, custom fill/empty glyphs (segmented meters) |
| `Spinner` | | Time-based frames: `Line`, `Dots`, `Arc` |
| `Clear` | | Blanks an area, for drawing popups over content |

## ⚡ Performance

200×60 screen, .NET 10, Ryzen 9 7950X. Every scenario allocates **0 bytes**.

| Scenario | Time | Bytes written |
|---|---:|---:|
| Frame with no changes | 4.5 µs | 0 |
| One cell changed | 4.7 µs | 59 |
| Full repaint, a different truecolor style on every row | 34 µs | 13.3 KB |
| App frame: layout + block + 5,000-item list + diff + write | 20 µs | 457 |
| App frame: layout + block + 5,000-row, 4-column table + diff + write | 33 µs | 469 |
| `SetString`, 60 rows: ASCII / ASCII with explicit colors / CJK | 6.4 / 4.1 / 9.5 µs | |
| Parse 9,000 input events (keys, CSI, mouse, UTF-8) | 121 µs | |

Compared with the original kernel this library grew out of, unchanged and sparse frames are about **9× faster**, and full repaints are **3.6× faster**.

```sh
dotnet run -c Release --project bench/Tuinet.Benchmarks -- --filter '*'   # BenchmarkDotNet suite
dotnet run -c Release --project bench/Tuinet.Benchmarks -- bytes          # bytes on the wire per scenario
```

Tests guard these properties too:

| Test | Guards |
|---|---|
| `AllocationTests` | 1,000 frames with widgets and 10,000 key polls allocate 0 bytes |
| `RendererFuzzTests` | Random frames, replayed through a VT emulator, must reproduce the buffer exactly: every glyph, color and attribute, plus the cursor |
| `TerminalTests` | Exact bytes for a one-cell change; control characters in text never reach the terminal |

> **Tip:** publish with Native AOT (`-p:PublishAot=true`) for instant startup and the steadiest
> latency. Under the JIT, code that hasn't been optimized yet can box values while formatting.

## 🖥️ Platforms

| Platform | Backend | Status |
|---|---|---|
| Linux | termios raw mode, `poll(2)` + self-pipe wake, `SIGWINCH` | ✅ tested (x64), JIT and Native AOT |
| macOS | termios raw mode (macOS struct layout), arm64 variadic `ioctl` handled | ⚠️ implemented, not yet tested |
| Windows 10+ | Console VT mode, `ReadConsoleInputW`, UTF-8 code page | ⚠️ implemented, not yet tested |

All platforms share the same VT encoder and input parser.

## 🛠️ Development

```sh
dotnet build                     # warnings are errors
dotnet test                      # library + sample tests
./run                            # showcase sample
dotnet publish samples/Tuinet.Samples.Showcase -c Release -p:PublishAot=true
tools/gen-width/gen.py [17.0.0]  # regenerate the Unicode width table from the UCD
```

CI ([`.github/workflows/ci.yml`](.github/workflows/ci.yml)) builds with warnings as errors and runs
the tests on Linux, macOS and Windows. It also checks that both samples still publish as Native AOT
with no trim or AOT warnings.

```
src/Tuinet/                      the library
  Widgets/                       Block, Paragraph, ListView, Table, TextInput, Dropdown, Checkbox, Button, ProgressBar
  Internal/                      renderer, VT parser, width tables, crash guard
  Platform/                      Unix and Windows backends
  Testing/                       TestTty
samples/Tuinet.Samples.Showcase  list, edit form and progress dialog (the screenshots)
samples/Tuinet.Samples.Stress    latency and throughput harness
bench/Tuinet.Benchmarks          BenchmarkDotNet suite
tests/                           unit, widget, allocation, renderer fuzz and sample tests
tools/gen-width/                 Unicode width table generator
docs/images/                     README screenshots
```

## 📄 License

[Apache 2.0](LICENSE)
