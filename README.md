# Tuinet

A performance-first, immediate-mode terminal UI library for .NET.

You redraw the whole UI into a cell buffer every frame. Tuinet diffs that buffer against what is
on screen and writes only the difference, in one `write`. There is no widget tree, no focus
manager and no async. Key in, frame out.

- **Zero allocations per frame**: steady-state rendering and input polling allocate nothing (enforced by tests).
- **Few bytes on the wire**: unchanged rows are skipped with a vectorized memcmp. Gaps are jumped
  with relative cursor moves, style changes are SGR deltas in a single sequence, and output is
  wrapped in synchronized-update markers.
- **Few syscalls**: one write per frame. Terminal size is cached and refreshed only on `SIGWINCH`.
- **Native AOT and trimming**: no reflection, and a full app publishes to a ~1.3 MB native binary.
- **Linux, macOS, Windows**: raw termios, or the Windows console in VT mode. One VT encoder and parser for all.

## Quick start

```csharp
using Tuinet;
using Tuinet.Widgets;

using var term = Terminal.Open();
var list = new ListState();
string[] items = ["alpha", "beta", "gamma"];

bool running = true;
while (running)
{
    CellBuffer frame = term.BeginFrame();
    var block = new Block { Title = "items", BorderType = BorderType.Rounded };
    frame.Render(block, frame.Area);
    frame.Render(new ListView<TextItems>(new TextItems(items)) { SelectedStyle = new Style(Color.Black, Color.Cyan) },
                 block.Inner(frame.Area), ref list);
    term.Present();

    if (!term.Poll(out Event ev, Timeout.Infinite)) continue;
    do   // handle everything already queued, then draw once: a burst of key repeats costs one frame
    {
        if (ev.Key.IsChar('q') || ev.Key.IsCtrl('c')) running = false;
        else if (ev.Key.IsChar('j')) list.Next(items.Length);
        else if (ev.Key.IsChar('k')) list.Previous(items.Length);
    }
    while (running && term.Poll(out ev, 0));
}
```

## The model

| Piece | What it is |
|---|---|
| `Terminal` | Alternate screen, raw input, double buffering. `BeginFrame()` → draw → `Present()`. `Poll` returns keys, mouse, paste, focus, resize, and messages from `Post` (thread-safe, wakes `Poll`). |
| `CellBuffer` | The frame: 16-byte `Cell`s (rune, style, width). `SetString` / `SetRune` / `Fill` / `SetStyle` clip, keep wide glyphs whole and drop control characters, so user text can never inject escape sequences. `SetCursor` places the real terminal cursor. |
| `Style`, `Color`, `Attr` | Foreground, background (default, 256-palette or RGB), bold, dim, italic, underline, blink, reverse, hidden, strike. Colors are downsampled to the terminal's `ColorMode` (detected from `COLORTERM`/`TERM`/…). |
| `Layout`, `Constraint`, `Rect` | `Layout.Vertical(area, [Constraint.Length(1), Constraint.Fill(), Constraint.Length(1)], rows)` writes into a `stackalloc`'d span. |
| Widgets (`Tuinet.Widgets`) | `Block`, `Paragraph`, `ListView<T>` + `ListState`, `TextInput` + `TextInputState`, `Button`, `Clear`. Widgets are `readonly ref struct` values built each frame. Persistent state (selection, scroll, caret) lives in state objects the app owns. |
| `IWidget`, `IStatefulWidget<T>` | Implement these for your own widgets. `ref struct` widgets can hold spans, e.g. text formatted with `stackalloc` + `TryWrite`. |
| `ITty` | The platform seam. `Tuinet.Testing.TestTty` is an in-memory terminal for tests. |

```csharp
// Testing your UI: render into a buffer and assert on text, or drive a Terminal over TestTty.
var buffer = new CellBuffer(40, 10);
app.Render(buffer);
Assert.Contains("Organization", buffer.ToString());
```

Options (`TerminalOptions`): `Mouse` (SGR 1006), `MouseMotion`, `BracketedPaste`, `FocusEvents`,
`ColorMode`, `EscapeTimeoutMs`. Every mode is switched off again on exit, crash (unhandled
exception), or signal (`SIGINT`/`SIGTERM`/`SIGHUP`/`SIGQUIT`).

## Performance

200×60 screen, .NET 10, Ryzen 9 7950X (`dotnet run -c Release --project bench/Tuinet.Benchmarks -- --filter '*'`):

| Scenario | Time | Allocated | Bytes written |
|---|---:|---:|---:|
| Frame with no changes (diff only) | 4.3 µs | 0 | 0 |
| One cell changed | 4.6 µs | 0 | 26 |
| Full repaint, style change on every row | 33 µs | 0 | 13.3 KB |
| App frame: layout + block + 5000-item list + diff + write | 14.5 µs | 0 | 457 (selection moved) |
| `SetString`, 60 rows of ASCII / CJK | 4.2 µs / 7.1 µs | 0 | — |
| Parse 9000 input events | 118 µs | 0 | — |

`dotnet run -c Release --project bench/Tuinet.Benchmarks -- bytes` prints the wire sizes. `./run stress` is an
interactive harness: hold `j` on 100k items or press `n` for full-screen color noise, and watch fps,
bytes per frame and allocations (which stay at 0) in the status bar.

For the lowest latency, publish apps with Native AOT (`-p:PublishAot=true`). Under the JIT, code that
hasn't tiered up yet can box values while formatting.

## Repository

```
src/Tuinet/                   the library (Internal/: renderer, VT parser, width tables; Platform/: Unix, Windows)
samples/Tuinet.Samples.Branches   git branch browser with an Azure DevOps options dialog (`./run`)
samples/Tuinet.Samples.Stress     latency / throughput harness (`./run stress`)
bench/Tuinet.Benchmarks       BenchmarkDotNet suite
tests/                        unit, widget, allocation and renderer property tests
tools/gen-width/              regenerates the Unicode width table from the UCD
```

Notable tests: `AllocationTests` (zero-alloc frames and polling), `RendererFuzzTests` (random frames
replayed through a VT emulator must reproduce the buffer exactly), and `TerminalTests` (exact bytes
for a one-cell change; control characters in text never reach the tty).
