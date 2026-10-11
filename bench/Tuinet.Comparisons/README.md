# Tuinet.Comparisons

One scenario drawn by three .NET terminal UI libraries: Tuinet, Spectre.Console (live display) and
Terminal.Gui v2. For each one it measures time per frame, managed bytes allocated per frame, and bytes and write
calls sent to the terminal per frame. This project is not part of CI. Its numbers only mean something when they
are measured on one quiet machine.

## Scenario

- A 200×60 terminal with true color output.
- A rounded border titled `items` around a 5,000-row list, with a status line (`j/k move  q quit`) under it.
  The list shows 57 rows.
- Every row is preformatted text such as `row 00042  alpha-service     service   platform`, built once and
  shared by all three libraries.
- The same RGB colors in all three. Text is `#cccccc`, the border and title `#6e6e82`, the status line
  `#8c8c8c`, all on the terminal's default background. The selected row is `#101010` on `#61afef`, highlighted
  across the full inner width.
- Each frame moves the selection down one row. Measurement starts once the selection is past the bottom of the
  viewport, so every frame also scrolls the list by one row and the content of every list row changes.

`Scenario.cs` holds all of this. Captured output was replayed through a VT emulator (pyte), and the three
libraries draw the same screen. The only visible difference is the title: Terminal.Gui draws it as
`╭┤items├──`, while the other two draw `╭─items──`.

## What each library does per frame

| Library | Per frame | Output captured by |
|---|---|---|
| **Tuinet** (this repo) | Sets `ListState.Selected`, then `BeginFrame`, `Layout.Vertical`, `Block`, `ListView<TextItems>` and the status line, then `Present`, which diffs against the previous frame and writes the result in a single write. | An `ITty` that counts bytes and `Write` calls, like `NullTty` in Tuinet.Benchmarks. |
| **Spectre.Console 0.57.2** | The app slices out the 57 visible rows, because Spectre has no virtualized list. It builds `Rows(Panel(Rows(Text…)), Text)` and calls `LiveDisplayContext.UpdateTarget`. Spectre moves the cursor back to the top of the live region and renders and writes the whole renderable again. | `AnsiConsole.Create` with `Ansi = Yes`, `ColorSystem = TrueColor`, `Unicode` on, a fixed 200×60 `IAnsiConsoleOutput`, and a `TextWriter` that counts UTF-8 bytes and write calls. |
| **Terminal.Gui 2.5.0** | Sets `ListView.SelectedItem`, which scrolls the selection into view, then calls `IApplication.LayoutAndDraw()`. That is the main loop's layout, draw and `Driver.Refresh` step, and `Refresh` turns dirty cells into ANSI. The views are a `Runnable` containing a rounded `FrameView` around a `ListView`, plus a `Label`, started with `IApplication.Begin`. | The stock `DriverImpl` with a custom `IOutput` (`CountingOutput`, an `OutputBase`). It mirrors `AnsiOutput`'s Unix path, including the cursor moves and the UTF-8 encoding of each chunk, but counts the bytes instead of making the write syscall. |

For every library, the measurement covers everything up to the write syscall and nothing after it. The time
the terminal emulator spends parsing and drawing is not measured.

`Tuinet, no scroll regions` is the same Tuinet code with `TerminalOptions.ScrollRegions = false`. It shows how
much of Tuinet's byte count comes from the plain cell diff and how much from moving rows with a scroll region.

## How to run

```sh
# Time and allocations (BenchmarkDotNet). These are the commands used for the results below.
dotnet run -c Release --project bench/Tuinet.Comparisons -- --filter '*' --job short

# Bytes and write calls per frame over 1,000 steady-state frames after 100 warm-up frames.
dotnet run -c Release --project bench/Tuinet.Comparisons -- bytes

# Same, and also save each library's whole output to <dir>/<library>.ansi.
# `cat` a file in a 200x60 terminal to watch that library's run.
dotnet run -c Release --project bench/Tuinet.Comparisons -- bytes /tmp/captures
```

## Results

Measured on 2026-10-10:

- Machine: AMD Ryzen 9 7950X (16 cores/32 threads), Linux 7.2.5 (Omarchy)
- .NET: SDK 10.0.300, runtime 10.0.8, X64 RyuJIT x86-64-v4
- Packages: BenchmarkDotNet 0.15.8, Spectre.Console 0.57.2, Terminal.Gui 2.5.0

The BenchmarkDotNet job was `ShortRun`: 1 launch, 3 warm-up iterations and 3 measured iterations. Error bars
are therefore wider than with the default job, but the gaps between the libraries are orders of magnitude.

| Library | Time / frame | Allocated / frame | Bytes / frame | Writes / frame |
|---|---:|---:|---:|---:|
| Tuinet | 14.1 µs | 0 B | 254 | 1 |
| Tuinet, no scroll regions | 24.8 µs | 0 B | 2,548 | 1 |
| Spectre.Console live display | 5,194 µs | 3,203,172 B (3.1 MB) | 17,098 | 639 |
| Terminal.Gui v2 | 9,707 µs | 26,991,144 B (25.7 MB) | 15,992 | 118 |

Raw BenchmarkDotNet output:

```
| Method                      | Mean        | Error      | StdDev    | Ratio  | Gen0      | Gen1     | Allocated  |
|---------------------------- |------------:|-----------:|----------:|-------:|----------:|---------:|-----------:|
| Tuinet                      |    14.13 us |   0.165 us |  0.009 us |   1.00 |         - |        - |          - |
| 'Tuinet, no scroll regions' |    24.77 us |   0.410 us |  0.022 us |   1.75 |         - |        - |          - |
| 'Spectre.Console live'      | 5,194.32 us | 302.801 us | 16.598 us | 367.65 |  187.5000 |  46.8750 |  3203172 B |
| 'Terminal.Gui v2'           | 9,706.71 us | 290.710 us | 15.935 us | 687.04 | 1609.3750 | 593.7500 | 26991144 B |
```

## Reading the numbers, and caveats

- **Tuinet's 254 bytes come mostly from scroll regions.** The border columns are the same on every row, so
  rows 2–58 shift up as whole lines. Tuinet moves them with one scroll-region delete-line
  (`CSI 2;58 r`, `CSI M`, `CSI r`) and then redraws only the two rows that really changed: the row that loses
  the highlight and the newly selected row. With scroll regions off it falls back to the cell diff, 2,548
  bytes, which is still small because neighboring rows share most of their characters. Both variants send the
  frame in one write wrapped in synchronized output (`CSI ?2026 h/l`), and those bytes are counted.
- **Spectre.Console has no diffing or cursor addressing.** The live display is inline: it uses `\r`, cursor-up
  and newlines, not the alternate screen. Every refresh writes the whole renderable again, including the
  border and the padding out to 200 columns. The app also rebuilds the renderable every frame, which is the
  usual Spectre pattern, and that cost is part of Spectre's time and allocations. `LiveDisplay.Start` blocks
  for the life of the display, so it runs on a parked thread. Frames are pushed from the benchmark thread and
  rendered there, and the context lock is never contended.
- **Spectre's writes/frame counts calls on its `TextWriter`.** Spectre writes each style change and each text
  run as a separate call. On the default `Console.Out`, which flushes automatically, each call becomes at least
  one write syscall. The counting writer adds no buffering and allocates nothing.
- **Terminal.Gui redraws the whole list and border every frame.** Its output stage skips clean cells, but
  `ListView` marks every row dirty when the selection or viewport moves, so every row is re-sent. That is
  about two writes per row: a cursor move, then the row. Terminal.Gui does not use scroll regions.
- **Terminal.Gui's allocations were checked separately.** Drawing the `ListView` alone allocates about 6.7 MB
  per frame. A full `LayoutAndDraw` allocates about 27 MB, while `Refresh` with nothing dirty allocates about
  200 B. The counting output reproduces `AnsiOutput`'s own per-write `ToString()` and `GetBytes` allocations,
  which come to roughly 16 KB per frame, so they are a negligible share. The NuGet build compiles out
  Terminal.Gui's tracing (`[Conditional("DEBUG")]`).
- **The Terminal.Gui driver is built by reflection.** `DriverImpl` is internal, and the public way to get a
  driver (`IApplication.Init`) cannot take a custom output. The driver is therefore created through its public
  constructor via reflection, wired the same way as in Terminal.Gui's own `TestDriverBase`, and assigned to
  `IApplication.Driver`. Stock `AnsiOutput` cannot be measured directly. When it is not attached to a tty it
  runs in a degraded mode that skips cursor-positioning sequences, which would under-count bytes. When it is
  attached, it writes to the real terminal. Only `LayoutAndDraw` is measured, not the rest of a main-loop
  iteration (input polling, timers, restoring the cursor).
- **One frame in 5,000 is a jump.** The selection wraps from row 4,999 back to 0 once every 5,000 frames, which
  makes that frame a jump instead of a scroll. BenchmarkDotNet's averages include it. The `bytes` mode does not
  reach it.
- **Not measured:** the cost of the write syscalls themselves (see writes/frame), how fast terminal emulators
  process each stream, input handling, and startup.
