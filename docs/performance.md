# Performance

Performance is the first feature. Three numbers are treated as product metrics and guarded by tests and
benchmarks: **bytes allocated per frame** (zero in steady state), **bytes written to the terminal per frame**, and
**writes (syscalls) per frame** (one).

## What a frame costs

<!-- The headline table is also in README.md: keep both in sync. -->
200×60 screen, .NET 10, Ryzen 9 7950X. Every scenario allocates **0 bytes**.

| Scenario | Time | Bytes written |
|---|---:|---:|
| Frame with no changes | 4.5 µs | 0 |
| One cell changed | 4.7 µs | 59 |
| Full repaint, a different truecolor style on every row | 35 µs | 13.3 KB |
| Scroll by one row, every row different | 7.7 µs | 271 |
| Scroll by one row beside a 50-column panel, terminal without left/right margins | 31 µs | 10.3 KB |
| The same, terminal with left/right margins (DECLRMM) | 13 µs | 459 |
| An 80×24 dialog closes over a themed background | 8.6 µs | 313 |
| Every row gets shorter (text, then blanks to the right edge) | 12 µs | 698 |
| App frame, scrolling: layout + block + 5,000-item list + diff + write | 14 µs | 165 |
| App frame, scrolling: layout + block + 5,000-row, 4-column table + diff + write | 21 µs | 233 |
| `SetString`, 60 rows: ASCII / ASCII with explicit colors / CJK | 6.4 / 4.1 / 9.5 µs | |
| `TextArea` over a 100,000-line text, wrapped / unwrapped, with line numbers | 60 / 39 µs | |
| `TreeView` frame, 109,000 rows open in a 1,000,000-node tree, at row 0 / 50,000 / 99,000 | 6.8 / 7.0 / 6.6 µs | |
| Rebuilding those 109,000 rows after a node opens or closes | 0.47 ms | |
| Caret down / a keystroke in that text area, then its frame | 61 / 78 µs | |
| Parse 9,000 input events (keys, CSI, mouse, UTF-8) | 121 µs | |
| `Fuzzy.Score`, a 3-char pattern against one command name (10,000 names: 0.28 ms) | 28 ns | |

Compared with the original kernel this library grew out of, unchanged and sparse frames are about **9× faster**, and full repaints are **3.3× faster**.

```sh
dotnet run -c Release --project bench/Tuinet.Benchmarks -- --filter '*'   # BenchmarkDotNet suite
dotnet run -c Release --project bench/Tuinet.Benchmarks -- bytes          # bytes on the wire per scenario
```

Tests guard these properties too:

| Test | Guards |
|---|---|
| `AllocationTests` | 1,000 frames with widgets and charts, 1,000 text area frames with caret keys, 1,000 open-menu frames with keys and pointer moves, fuzzy scoring, and 10,000 key polls allocate 0 bytes |
| `RendererFuzzTests` | Random frames, replayed through a VT emulator, must reproduce the buffer exactly: every glyph, color and attribute, plus the cursor |
| `TerminalTests` | Exact bytes for a one-cell change; control characters in text never reach the terminal |

> **Tip:** publish with Native AOT (`-p:PublishAot=true`) for instant startup and the steadiest
> latency. Under the JIT, code that hasn't been optimized yet can box values while formatting.

## Where the bytes go

Drawing everything every frame is cheap because the renderer sends only the difference:

- Unchanged rows are skipped with a vectorized memcmp.
- Gaps inside a row are jumped with relative cursor moves.
- Runs of blanks are erased (to the end of the row, or n cells) instead of written as spaces.
- When a band of rows scrolls, the terminal moves it (scroll margins + insert/delete line) and only the new rows are painted.
  On terminals with left/right margins (DECLRMM) this works for a band narrower than the screen too, such as a list beside a panel.
- Style changes are sent as minimal deltas.
- Each frame goes out in a single synchronized write.

## Allocation rules

- Widgets are `readonly ref struct`s built on the stack each frame; their state is a value your app owns.
- Text is written from spans: format into `stackalloc` buffers with `TryWrite` instead of building strings.
- `Markup.Parse` and `StyledTextBuilder` write into memory you pass in; `SetMarkup` parses on the stack.
- `Terminal.Poll` and `Terminal.Run` allocate nothing per event or per frame.
- Test it in your own app: see [Testing](testing.md#no-allocations-per-frame).

## Compared with other .NET libraries

The same scenario in each library: a 200×60 screen with a bordered 5,000-row list, true color, the selection moving
down one row per frame so the list scrolls. Ryzen 9 7950X, .NET 10, BenchmarkDotNet short job; bytes and writes over
1,000 steady-state frames, counted up to the write call.

| Library | Time / frame | Allocated / frame | Bytes written / frame | Writes / frame |
|---|---:|---:|---:|---:|
| TUI.NET | 14.1 µs | 0 B | 254 | 1 |
| TUI.NET, scroll regions off | 24.8 µs | 0 B | 2,548 | 1 |
| Spectre.Console 0.57.2, live display | 5,194 µs | 3.1 MB | 17,098 | 639 |
| Terminal.Gui 2.5.0 | 9,707 µs | 25.7 MB | 15,992 | 118 |

Most of TUI.NET's lead in bytes comes from moving the scrolled rows with a scroll region; with that turned off it
is a plain cell diff. Spectre.Console's live display has no diffing and rewrites the whole region each refresh, and
Terminal.Gui marks every list row dirty, so both re-send the whole list every frame. The methodology, how each
library's output is captured, and the caveats (Terminal.Gui needs reflection to swap in a counting output) are in
[`bench/Tuinet.Comparisons`](https://github.com/lmaslanka/tuinet/tree/main/bench/Tuinet.Comparisons). It isn't run in
CI; the numbers come from one machine.

---

[Getting started](getting-started.md) · [Layout and styling](layout.md) · [Widgets](widgets.md) ·
[Input and the terminal](input.md) · [Testing](testing.md) · [Performance](performance.md) · [API reference](api/index.md)
