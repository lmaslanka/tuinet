# Tuinet performance review

Scope: every file in `src/Tuinet` (kernel, text, platform, widgets), `src/Tuinet.Testing`, the benchmark projects and
the allocation tests. All claims below were checked against the code at the cited lines; the numbers in the
findings were measured on this machine (see the Appendix for the exact commands and raw output). Line references
are `path:line` against the current tree.

## Summary

The kernel is in very good shape for its stated goals: cells are 16-byte blittable values diffed with a vectorized
memcmp, text writes are template stores, steady-state frames allocate nothing (verified by tests and re-verified
here), every frame is one `write(2)`, and the renderer already uses scroll regions, DECLRMM column bands, erase
sequences and REP to minimize bytes. The shift-detection worst case I suspected costs under 1 µs per frame. The
findings are therefore mostly about *coverage of the fast paths* (which scripts, which cursor moves, which widgets),
not about the architecture.

### Top 5 priorities

| # | Severity | Finding | Measured cost |
|---|---|---|---|
| 1 | **High** | Any text outside Latin/CJK/box-drawing (Cyrillic, Greek, Hebrew, Arabic, Thai, Devanagari, …) falls off every fast path: `Graphemes.Simple` is a hand-picked range list, so each such code point goes through `StringInfo.GetNextTextElementLength` in `CellBuffer.Write`, in `TextWidth.Of`, and *again per cell in the renderer* (`EmitComplex` → `Graphemes.MayJoin`). | `SetString` 21.5 ns/char vs 1.0 ns/char ASCII (21×); `TextWidth.Of` 25×; a full-screen repaint frame 579 µs vs 95 µs (6×) |
| 2 | **Medium** | `Paragraph` is O(scroll offset + visible rows) per frame, and `Scrollbar = Auto/Always` re-measures the whole text every frame. | 2,000-line paragraph: 28 µs at the top, 655 µs at the bottom (23×); 695 µs at the top with `Scrollbar.Auto` |
| 3 | **Medium** | `Renderer.MoveTo` knows only CUF, CR, CR LF and CUP. Vertical runs (scrollbars, borders), backward moves (caret left) and the forced repositioning after each lone emoji all pay a full CUP. | Column of 60 changed cells: 12.1 B/cell (CUD/LF would be 4–6); caret-left frame 6 B instead of 3; row of 50 lone emoji 17.2 B/glyph vs 3.5 for CJK |
| 4 | **Medium** | Every frame with a visible cursor hides and re-shows it (`CSI ?25l` … `CSI ?25h`) although the frame is already inside a synchronized update. | 12 of the 29 bytes (41%) of a "typed one character" frame in a focused `TextInput` |
| 5 | **Medium** | `TextArea` wrapping/measuring works one cluster at a time (`ClusterLength` + `TextWidth.Of` per char) with no ASCII-run fast path, unlike `CellBuffer.Write`. | ~5–7 ns/char in render vs 1 ns/char for the same text through `SetString`; wrapped frame 60 µs vs unwrapped 39 µs in the project's own benchmark |

Everything else is Low: see the per-area sections.

## Methodology

1. Read `docs/performance.md`, `README.md`, and every file in `src/Tuinet` (whole files), `src/Tuinet.Testing`,
   `bench/Tuinet.Benchmarks`, `bench/Tuinet.Comparisons/README.md`, `tests/Tuinet.Tests/AllocationTests.cs`, and
   grepped the samples for render-time formatting.
2. Built the solution in Release (`dotnet build tuinet.slnx -c Release`), ran the project's `bytes` report and one
   targeted BenchmarkDotNet run (`RenderBenchmarks.FullRepaint*`, `NoChange`, short job).
3. Wrote a throw-away console harness in the session scratchpad (not in the repo) that references
   `src/Tuinet/Tuinet.csproj` and drives the public API (`CellBuffer`, `Terminal` over a counting `ITty`). Absolute
   times from it were taken with `DOTNET_TieredCompilation=0` so that short loops run fully optimized code; treat
   them as relative numbers (ratios), the BenchmarkDotNet figures in `docs/performance.md` remain the canonical
   absolutes. Byte counts are exact.
4. Severity: **High** = a common workload that is an order of magnitude off the fast path, or a design-goal
   violation; **Medium** = a clear, measurable win on a realistic path; **Low** = small, situational or informational.

## Findings

### A. Frame pipeline (CellBuffer, Renderer, VtBuffer, flush)

**A1. High — non-Latin alphabetic scripts take the UAX#29 slow path per code point, in three places.**
`src/Tuinet/Internal/Graphemes.cs:177-186` defines `Simple(c)` as `c < 0x300`, CJK ideographs, Hangul syllables,
kana, and a few symbol blocks. Everything else (Cyrillic U+0400.., Greek U+0370.., Armenian, Hebrew, Arabic, Thai,
all of Indic, Georgian, Latin Extended Additional U+1E00.., fullwidth forms …) is treated as "may join".
Consequences:

- `src/Tuinet/CellBuffer.cs:658-678`: in `Write`, a non-simple char skips the one-code-point path and calls
  `Graphemes.Next` → `Graphemes.Length` (`Graphemes.cs:50-68`) → `StringInfo.GetNextTextElementLength` per
  character, then sets `_mayJoin[y]` for the row (`CellBuffer.cs:669, 722-725`).
- `src/Tuinet/TextWidth.cs:25` only fast-paths `c < 0x80`; everything else goes through `Graphemes.Next` too.
  `src/Tuinet/Widgets/EditableText.cs:538-539` (`ClusterLength`) has the same `< 0x80` test.
- Because `_mayJoin[y]` is set, the renderer takes `RenderRow<JoinChecks, …>` (`src/Tuinet/Internal/Renderer.cs:733-754`)
  and, for every dirty cell whose rune is not `Simple` **and for the cell after it** (`previousComplex`,
  `Renderer.cs:842-849`), calls `EmitComplex` (`Renderer.cs:1028-1072`), which calls `Graphemes.MayJoin`
  (`Graphemes.cs:158-169`): a `stackalloc`, two `EncodeToUtf16` and another `StringInfo.GetNextTextElementLength`
  per cell.

Measured (200×60, 60 rows of text): `SetString` 1.0 ns/char ASCII, 6.8 ns/char CJK, **21.5 ns/char Cyrillic,
21.3 ns/char Greek**; `TextWidth.Of` 8.0 µs vs **199.5 µs** for 60 rows; a full-repaint frame through `Terminal`
(clear + 60 `SetString` + diff + encode) **95 µs ASCII vs 579 µs Cyrillic / 566 µs Greek**. The renderer's share
of the Cyrillic slowdown is ~240 µs for 12,000 cells, i.e. ~20 ns per cell, matching one `StringInfo` call per
cell. Users whose UI language is Russian, Greek, Hebrew, Arabic, Thai or Hindi get a renderer that is ~6× slower
than the one benchmarked in `docs/performance.md`.

Fix: replace the hand-written range list with a generated per-code-point bit ("can never start or extend a
cluster": GraphemeBreakProperty = Other, not Extended_Pictographic, not Hangul L/V/T, not Regional_Indicator,
not ZWJ/Extend/SpacingMark/Prepend) produced by `tools/gen-width/gen.py` as a second two-stage table
(~1 bit/code point: roughly 4 KB stage-1 shared with the width table + ~3 KB of 32-byte stage-2 blocks). Then make
`TextWidth.Of`, `EditableText.ClusterLength` and `Paragraph.Fit` use the same `Simple(c) && Simple(next)` test the
`Write` cluster path uses, instead of `< 0x80`. With that, Cyrillic/Greek/Hebrew/Arabic letters (not their combining
marks) take the 1 ns/char path and their rows no longer set `_mayJoin`, so the renderer's `NoJoinChecks` loop
applies. The width table's unused value 3 cannot encode this (three widths × joinable needs 3 bits), hence a
separate bitmap.

**A2. Medium — `MoveTo` has no CUU/CUD/CUB/CHA shortcuts.** `src/Tuinet/Internal/Renderer.cs:1297-1345` emits CUF
for forward moves on the same row, `\r\n` for (0, y+1), `\r` for (0, y), and a full `CSI row;col H` for everything
else. Cases that are common and pay the full CUP:

- Same column, next row (a scrollbar track, a vertical border, a column of status glyphs): measured **12.1 B/cell**
  for 60 cells in column 199 (`ESC[nn;200H` + glyph). With `OPOST` off (`Platform/UnixTty.cs:357`) and
  `DISABLE_NEWLINE_AUTO_RETURN` on Windows (`Platform/WindowsTty.cs:64`), a bare `\n` is a 1-byte CUD within the
  band (never on the last screen row, which the band never crosses); `CSI B` is a safe 3-byte alternative.
- Same row, backward (Backspace/Left in an editor): the caret-only frame is `ESC[1;8H` = 6 bytes where `CSI D`
  is 3 and `CSI 8 G` (CHA) is 5.
- Same row, unknown column: after a cluster or an emoji-plane code point `_cx` is set to -1
  (`Renderer.cs:1041-1048, 1182, 1191`) and the next cell gets `CSI y;x H`; CHA `CSI x G` saves 2–4 bytes each
  time. A row of 50 lone emoji measured **17.2 B/glyph (858 bytes)** vs 3.5 B/glyph for CJK, and the dump shows
  `ESC[6;3H🔥ESC[6;5H🔥…`.

Fix: in `MoveTo`, when `y == _cy`: emit `CSI nD` for `x < _cx` and `CSI xG` when `_cx < 0`; when `x == _cx` and
`_cx >= 0`: emit `\n`×n (n ≤ 2, else `CSI nB`) for `y > _cy` and `CSI nA` for `y < _cy`; pick the shortest of the
candidates by byte count (the costs are static). `ReemitIsCheaper` (`Renderer.cs:1106-1125`) should then use the
same cost function.

**A3. Medium — cursor hidden and re-shown around every frame that shows it.** `Begin()`
(`Renderer.cs:290-306`) writes `CSI ?25l` whenever `_cursorShown`, and `PlaceCursor` (`Renderer.cs:1194-1232`)
writes `CSI ?25h` again at the end: 12 bytes on every keystroke frame of a focused `TextInput`/`TextArea`. Measured:
a typed character is `ESC[?2026hESC[?25lxESC[?25hESC[?2026l` = **29 bytes, 12 of them hide/show**; a one-cell
change with a stationary cursor elsewhere is 38 bytes vs 18 without a cursor (12 hide/show + 8 to move back). The
hide exists so the cursor does not flicker across the paint on terminals without DEC 2026; on terminals that do
support 2026 it is redundant. `Enter()` already queries DECRQM 69 and XTVERSION (`src/Tuinet/Terminal.cs:635-643`)
and parses the reply (`Internal/VtParser.cs:294-303`). Fix: also send `CSI ?2026$p`; when the terminal reports the
mode as set/settable, skip the hide in `Begin` and the show in `PlaceCursor` when the cursor stays visible.

**A4. Low — no multi-row erase.** Trailing blank rows (a dialog closing, a list emptying) are erased row by row
with `\r\n` + `CSI K` (~5 bytes/row, `Renderer.cs:933-938`). When every remaining row from `y` to the bottom is
blank in the same background, one `CSI J` (ED, 3 bytes) after the last non-blank row would replace them.
`EraseBelow` (`Renderer.cs:222-230`) already exists for inline mode. Saves ~5 bytes × rows; only matters for
tall erases.

**A5. Low — REP probe costs ~10% on text-heavy full repaints where nothing repeats.** The project's own benchmark
(short job, this machine): `full repaint, styled` 35.5 µs with `Repeat=false` vs **39.1 µs** with `Repeat=true`;
`no change` is unaffected. The per-cell test at `Renderer.cs:867-871` is one 8-byte compare plus the `advance ==
1 && positioned` guards, inside the `Repeats` instantiation. A cheaper guard (probe only when the glyph is a
space, a box-drawing/block character, or when `cur[x+1]` equals `cur[x]` *and* `cur[x+2]` does too, since REP
needs 3+ ASCII cells to pay off) would keep the saving on rules/bars and skip the probe on prose.

**A6. Low — `BeginFrame` always clears the back buffer.** `Terminal.cs:224-232` → `CellBuffer.Clear`
(`CellBuffer.cs:176-181`) is a vectorized 16-byte fill: measured **1.58 µs for 200×60 (192 KB)**, about 30% of a
no-change frame (5.2 µs). Apps that paint their whole area anyway (a `Fill` or a full-screen `Block` with a
`Style`) pay for two passes. Optional: `BeginFrame(clear: false)` for apps that guarantee full coverage, or clear
only rows the previous frame touched (not possible today since there is no per-row dirty tracking in the writer).
Not urgent.

**A7. Low — per-row output reservation is pessimistic.** `Renderer.cs:195` reserves `(last - first + 2) * 96 + 64`
bytes per dirty row (19.5 KB for a 200-column row). `VtBuffer.Reserve` (`Internal/VtBuffer.cs:23-29`) grows to
`max(2×, length + extra)` when `length + extra > capacity`, so a frame that would fit can still trigger a doubling
when it is within 20 KB of the end of the buffer. One-time growth only (the buffer never shrinks); the initial
capacity `W*H*8 + 4096` (`Terminal.cs:77`) covers normal frames. No action needed beyond noting it.

**A8. Low — `Frame` heap-allocates for terminals taller than 256 rows.** `Renderer.cs:159-160` stackallocs
`source`/`prefix` for `height <= 256` and allocates two `int[height]` per frame above that. A 4K monitor in a
small font can exceed 256 rows. Keep two `int[]` fields sized on resize instead.

**A9. Informational — scroll/column-shift detection worst case is cheap.** `FindShift` (`Renderer.cs:368-415`)
scans up to 3 probes × (H−3) distances × 2 row compares, and `FindColumnShift`/`ColumnsFor`
(`Renderer.cs:441-600`) adds seed matching, a 10-row sample, a sort and the margin-narrowing loop. On a full
repaint with every row dirty and no shift to find, the measured overhead is **36.2 µs vs 35.5 µs** without
DECLRMM, i.e. under 1 µs, because `Same` is a memcmp that exits at the first differing cell. Nothing to change.

**A10. Informational — flush and syscalls.** `Present` → `Flush` → `UnixTty.Write` (`Platform/UnixTty.cs:120-146`)
is one `write(2)` per frame (looping only on short writes / `EAGAIN`). Confirmed by the `bytes` report and
`TestTty.WriteCount` tests. Good.

### B. Allocations on hot paths

Re-verified: `tests/Tuinet.Tests/AllocationTests.cs` covers widget frames, REP frames, inline frames + `PrintAbove`,
text area frames, menu frames with mouse, fuzzy scoring and polling. I found no per-frame or per-key allocation
in `src/Tuinet` outside the documented ones. Items worth knowing:

**B1. Low — `Links.Intern` re-encodes and re-hashes the URL on every `SetLink` call.** `Internal/Links.cs:33-50`
`stackalloc char[2048]` (4 KB of stack per call, not zeroed since `SkipLocalsInit` is on, `Properties.cs:4`),
percent-encodes, then hashes the span for the `AlternateLookup`. Measured **63 ns per call** for a 40-char URL; an app rendering 100
links per frame pays ~6 µs. Fine as is; if it ever matters, expose a `LinkId` handle the app can cache.

**B2. Low — `EditableText.Slice` moves the gap on read.** `Widgets/EditableText.cs:372-381` relocates the gap when
a read spans it. `TextInput.Render` reads `Slice(0, Length)` every frame (`Widgets/TextInput.cs:190`), so with the
caret mid-text each keystroke costs two memmoves of up to `Length/2` chars (render moves it to an end, the next
`Splice` moves it back). Measured: key + render + backspace **6.4 µs at 200 chars, 9.9 µs at 200,000 chars**: the
O(n) is real but the constant is tiny. `TextArea` only reads the caret's line (`LineSpan`), so its cost is bounded
by one line. Documented behaviour; no change needed unless very long single-line inputs are a target.

**B3. Low — paste allocates two strings.** `Internal/VtParser.cs:612-613` `Encoding.UTF8.GetString` then
`ReplaceLineEndings` (returns the same instance when nothing changes). Documented ("Allocation-free except for
paste text"). `PasteChunk` also compares the last 6 bytes per byte (`VtParser.cs:610`); `data.IndexOf(0x1B)` to
copy whole chunks would make a 1 MB paste ~10× cheaper, but pastes are rare.

**B4. Informational — no closures, LINQ, boxing or `params` on hot paths.** `Terminal.Run` (`App.cs:50-94`),
`Poll`, `Present`, all `Render` methods and the parser are free of them. `BarChart` formats with
`double.TryFormat(scratch, out _, ValueFormat)` into a `stackalloc` (`Widgets/BarChart.cs:177-178`); `TextArea` line
numbers with `int.TryFormat` (`Widgets/TextArea.cs:614`). The samples use `TryWrite` into spans and their own
`SpanText` helper to avoid the boxing interpolation handler. Good.

### C. Input

**C1. Low — two syscalls per read, plus one `poll(0)` per burst.** `UnixTty.Read` (`Platform/UnixTty.cs:148-204`)
always `poll`s both fds, then `read`s. With `VMIN=0, VTIME=0` (`UnixTty.cs:361-362`) a `read` on the tty returns
immediately with 0 when nothing is pending, so for `timeoutMs == 0` (the drain loop in `Run`, `App.cs:76`) the
`poll` could be skipped: try `read` first and only `poll` when the caller wants to wait. The wake pipe does not
need draining on that path (`_wakePending` stays set and the pending byte makes the next blocking `poll` return
at once). Saves one syscall per key burst; per keystroke frame the loop is `poll` + `read` + `poll(0)` + `write`.

**C2. Informational — parser.** `VtParser.Feed` (`Internal/VtParser.cs:91-124`) is a byte-at-a-time state
machine with a 64-entry `Queue<Event>` (about 3 KB; `Event` is ~48 bytes because it carries a `KeyEvent`, a
`MouseEvent`, a `Size`, an `object?` payload and the kind). 13 ns per event in the project benchmark. `DispatchCsi`
uses a `stackalloc int[4]`; mouse decode is branch-only. Fine.

**C3. Informational — Windows.** `WindowsTty.Read` (`Platform/WindowsTty.cs:116-158`) waits on two handles, then
`GetNumberOfConsoleInputEvents` + `ReadConsoleInputW` into a fixed 128-record array and re-encodes to UTF-8 in place
(`Translate`, lines 198-245): three kernel calls per wake, no allocation. `Translate` silently drops records that
do not fit the 4 KB buffer (line 238) — a correctness edge for very long pastes, not a perf issue.

**C4. Informational — polling overhead per iteration.** `TryNext` (`Terminal.cs:438-478`) does a volatile read
(`TakeSignals`), a `Queue.TryDequeue`, a cached `Size` read and a `ConcurrentQueue.TryDequeue` on the message
queue. All O(1) and lock-free. Good.

### D. Text (graphemes, width, StyledText, Fuzzy)

**D1. High** — see A1 (the `Simple` predicate).

**D2. Low — `TextWidth.Of` ASCII fast path only.** `TextWidth.cs:25` (`c < 0x80 && next < 0x300`). Covered by
A1's fix: use the generated predicate so CJK, Cyrillic etc. skip `Graphemes.Next` (which costs a `DecodeFromUtf16`
even on its fast branch).

**D3. Informational — width tables.** `Internal/UnicodeWidth.g.cs`: stage 1 is 4,352 bytes (one byte per 256
code points), stage 2 is 109 × 64-byte blocks (6,976 bytes), ~11.3 KB in total as `ReadOnlySpan<byte>` RVA data
(no startup cost, shared pages). Lookup is two dependent loads and a shift (`UnicodeWidth.cs:19-29`) after the
printable-ASCII early-out. Good; a 1-bit "simple" table alongside it (A1) would reuse the stage-1 layout.

**D4. Informational — grapheme interning.** `Graphemes.Intern` (`Graphemes.cs:103-145`) is a lock-free
`ConcurrentDictionary` span lookup on the hot path and locks only on first sight; cells stay 16 bytes. The cap
`MaxEntries = 1 << 20` means an app that shows untrusted text with many distinct combining sequences (log viewers,
chat) can intern up to a million strings process-wide and never release them (~100–200 MB worst case). Not a hot-path
issue, but consider a lower cap (e.g. 64 K; the fallback already degrades gracefully to the first code point).

**D5. Low — `StyledText.Slice` is O(runs) per call.** `StyledText.cs:54-66` walks runs from the start;
`Paragraph.Render` calls it per visual line (`Widgets/Paragraph.cs:115-116`), so a styled paragraph is O(lines ×
runs). Carry the run cursor across lines in `LineEnumerator` instead. Only matters for long styled paragraphs.

**D6. Low — `Fuzzy.Score` forward scan is scalar.** `Fuzzy.cs:63-70` compares char by char with
`char.ToLowerInvariant` per char. Most candidates in a 10 K list do not match; a vectorized
`IndexOfAny(lower, upper)` for the *first* pattern char would reject them in a few ns. Already 28 ns per name,
so Low.

### E. Widgets

**E1. Medium — `Paragraph` is O(scroll + visible) and `Scrollbar.Auto/Always` measures everything every frame.**
`Widgets/Paragraph.cs:95-121`: the render loop starts at `row = -Scroll` and still calls `MoveNext()` (which runs
`Fit`, an O(line) wrap, `Paragraph.cs:185-212`) for every skipped visual line. `Paragraph.cs:79-88`: with
`Scrollbar.Always`, or `Auto` once the text overflows, `LineCount(Text, width, Wrap)` walks the whole text on every
frame. Measured on a 2,000-line / ~130-char-per-line text at 200×60: **28 µs at `Scroll = 0`, 655 µs at
`Scroll = 3900`, 695 µs at `Scroll = 0` with `Scrollbar.Auto`**; the same lines in a `TextArea` near its end:
57 µs. `Fit` also has no ASCII fast path: it calls `Graphemes.Next` per cluster (`DecodeFromUtf16` + width) even
for plain prose. Fixes: (a) an ASCII-run fast path in `Fit` (scan printable ASCII with `IndexOfAnyExcept`, count
columns directly, remember the last space); (b) let the caller cache the line count, e.g. a
`ParagraphState { LineCount, TextHash/Version, Width }` the widget fills on first render and reuses while
`Text`/`Width`/`Wrap` are unchanged; (c) for large texts point users to `TextArea` (read-only) which is already
O(visible). Document the complexity on `Paragraph.Scroll`.

**E2. Medium — `TextArea` measures one cluster at a time.** `Widgets/TextArea.cs:418-457` (`RowEnd`),
`697-713` (`Fits`), `509-528` (`OffsetAtColumn`) and `674-694` (`Draw`'s skip loop) call `ClusterLength` and
`TextWidth.Of(line.Slice(i, n))` per character; both calls are cheap but together cost ~5–7 ns/char versus
1 ns/char for the same ASCII through `CellBuffer.Write`. A visible 200×60 screen is ~12 K chars, so wrapping alone
is on the order of 30–50 µs of the 60 µs wrapped frame in `TextAreaBenchmarks`. Fix: in `RowEnd`, consume runs of
printable ASCII in one step (`IndexOfAnyExcept(PrintableAscii)`, width = run length, last space via
`LastIndexOf(' ')` within the run) and fall back to the cluster path only at the first non-ASCII char; share the
helper with `Fits` and `OffsetAtColumn`.

**E3. Low — `TreeView.RenderRow` recomputes per-row constants.** `Widgets/TreeView.cs:418-421` computes
`TextWidth.Of(ExpandedSymbol)`, `TextWidth.Of(CollapsedSymbol)`, `levels` and calls `state.SetLayout` for every
visible row; `TextWidth.Of(expander)` again at line 458. Hoist into `Render` (or the `Rows` adapter). ~60 × 4
tiny calls per frame; cosmetic.

**E4. Low — `Tabs` recomputes `Width` several times per frame and follows in O(n²).** `Widgets/Tabs.cs:111-123`
sums every title's width; `Render` calls it once (line 145) but `HandleMouse`/`Hit` call it again per event
(lines 235, 248), and the follow loop `while (offset < Selected && !Fits(offset, Selected, …)) offset++`
(lines 155-158) is O(n²) in tab count since `Fits` is O(n). Fine for ≤ 50 tabs; compute a prefix-sum once per
render if tab bars get long.

**E5. Low — `Popup.Render` paints the box twice.** `Widgets/Popup.cs:67-68` `Fill(box, Block.Style)` then
`Render(Block, box)`, and `Block.Render` (`Widgets/Block.cs:88-91`) does `SetStyle(area, Style)` over the same
box. The second pass is a no-op per cell (`(cell & keep) | set`) but still touches every cell. Pass a `Block` with
`Style = default` to the inner render, or skip the `Fill` and let the block's `SetStyle` layer over an `Erase`.
Dialog-sized areas only (80×24 ≈ 2 K cells ≈ 1 µs).

**E6. Low — `Table` cell callback per cell per frame.** `Widgets/Table.cs:166-170` calls `source.Cell(index, i,
scratch, out style)` then `DrawAligned`; for non-left alignment `DrawAligned` measures with `TextWidth.Of` then
`SetString` measures again only when the ellipsis path is taken (lines 319-322, already guarded). With A1 the
measurement becomes 1 ns/char for all scripts. `LayoutColumns` (lines 236-247) runs `Layout.Horizontal` once per
frame: O(columns). Good.

**E7. Informational — virtualization.** `ListView` (`Widgets/ListView.cs:182-201`), `Table` (`Table.cs:155-171`),
`TreeView` (rows flattened once per shape change, `TreeView.cs:250-309`, 4.3 ns/row on rebuild) and `TextArea`
(`TextArea.cs:608-660`, only lines in view) are O(visible). `TextAreaState.Reveal` / `ScrollBy` are bounded by
the viewport height (`TextArea.cs:244-248, 357-361`). `Menu` and `Dropdown` are O(visible) too. Good.

**E8. Informational — `Scrollbar`, `ProgressBar`, `Sparkline`, `BarChart`.** Per-cell `SetRune(x, y, …)` calls
(`Widgets/Scrollbar.cs:68-91`, `ProgressBar.cs:40-56`, `Sparkline.cs:60-68`); each is ~5–10 ns (width lookup,
layer, place, fix-ups). At most a few hundred cells per frame. `BarChart.RenderHorizontal` formats each value twice
(measure + draw, `BarChart.cs:139-143, 167`) with `double.TryFormat`; ~100 ns per bar. Fine.

### F. Layout

**F1. Low — `Layout.Distribute` is O(n²) in the worst case.** `Layout.cs:113-161` loops `round <= n` times over
`n` constraints while space remains; with Min/Max segments each round can hand out one cell. `n` is tiny in
practice (≤ 10) and the stackalloc covers `n <= 64`; above that it allocates `int[n]` (`Layout.cs:74`). Nothing to
do unless someone lays out hundreds of segments per frame.

### G. Platform

**G1. Informational — P/Invoke.** `LibraryImport` with blittable spans, no `SafeHandle`, no marshalling stubs on
the hot path (`Platform/LibC.cs:54-61`); `SetLastError` only where errno is read. `termios`/`ioctl` are called only
on enter/leave/suspend and after `SIGWINCH` (`UnixTty.cs:106-118, 344-350`): zero per frame. Signals go through
`PosixSignalRegistration` with `Cancel = true` to avoid the runtime's own `SIGCONT` termios handling. Windows uses
VT mode on both handles and a single `WriteFile` per frame. Good.

**G2. Low — see C1** (poll-before-read).

### H. Memory, resize, startup

**H1. Informational — steady-state footprint.** Two `CellBuffer`s of `W×H×16` bytes (384 KB at 200×60, 1.25 MB
at 400×100), `_mayJoin` per row, a `VtBuffer` of `W×H×8 + 4 KB` that grows by doubling and never shrinks, the
renderer's `_blank` row and, once a DECLRMM column shift has happened, `_shown` (`Renderer.cs:660-664`, another
`W×H×16`). Inline mode adds `_print` and `_blank` buffers (`Terminal.cs:303, 327`). `Resize`
(`CellBuffer.cs:464-486`) reallocates only when growing and clears; the front buffer is cleared and a full redraw
scheduled. Reasonable; no pooling needed.

**H2. Low — process-wide interning never shrinks** (D4 for graphemes; `Links` is capped at 4,095 × ≤ 2 KB ≈ 8 MB).

**H3. Informational — startup.** No large managed tables are built at startup: width tables are RVA data,
`VtBuffer.DecimalTable` is 768 bytes, `SearchValues` for printable ASCII is built once. `Terminal.Open` sends the
enter sequence and three queries in one write and does not wait for replies on the alternate screen
(`Terminal.cs:623-680`); inline mode waits up to `CursorReportTimeoutMs` for `CSI 6n`. Under the JIT the first
frame is dominated by compilation (≈5 ms measured for ctor + first frame in a warm process with tiering off);
the docs already recommend Native AOT.

**H4. Informational — `TerminalOptions.DetectColorMode`** (`TerminalOptions.cs:155-158`) maps a bare
`TERM=xterm-256color` without `COLORTERM` to `Indexed256`, so on such terminals every `SetPen` runs `Nearest256`
(`Internal/ColorMapping.cs:48-61`, a dozen integer ops) per RGB color change. Negligible, but it is a different
code path from the one benchmarked (`TrueColor` identity) and has no benchmark (see coverage gaps).

## Benchmark coverage gaps

1. **No non-Latin alphabetic script anywhere.** `TextBenchmarks` has ASCII, CJK and cluster strings
   (`bench/Tuinet.Benchmarks/InputBenchmarks.cs:43-45`); `RenderBenchmarks` is all ASCII. A Cyrillic/Greek row
   in `SetString` and in `FullRepaint` would have exposed A1 (21× / 6×). Add one of each, and an Arabic/Devanagari
   string with real combining marks to keep the cluster path honest.
2. **No scrolled `Paragraph`.** `TextAreaBenchmarks.Paragraph` renders 60 lines at `Scroll = 0`
   (`TextAreaBenchmarks.cs:36-41`). Add a large text at a large `Scroll`, and one with `Scrollbar.Auto` (E1).
3. **No editor keystroke in the `bytes` report.** `Program.cs` "bytes" mode covers renderer scenarios, list/table
   frames and the Showcase, but not a focused `TextInput`/`TextArea` typing frame, which is where A3 (12 bytes
   of cursor hide/show) and the caret-move case of A2 show up.
4. **No vertical-run scenario.** Nothing in `RenderBenchmarks` changes a single column across many rows (scrollbar
   thumb, vertical border recolor on focus change), so A2's 12 B/cell is unmeasured.
5. **No end-to-end input latency.** `InputBenchmarks.Parse` measures the parser only. A benchmark that feeds a key
   through `Terminal.Poll` on a scripted `ITty` and renders the frame (the key → pixel path the README sells) would
   catch regressions in `TryNext`/`Poll`.
6. **No `Indexed256`/`Basic16` renderer run.** All renderer benchmarks use `ColorMode.TrueColor`; the mapping path
   (`ColorMapping.Nearest256/Nearest16`) is the default on plain `xterm-256color` and is unmeasured (H4).
7. **No `TextInput` benchmark** (B2) and no `Menu`/`Dropdown` benchmark; they are covered by allocation tests only.
8. **No resize benchmark** (`Resize` + full redraw) and no inline-mode `PrintAbove` benchmark; both are allocation-tested only.
9. `RenderBenchmarks.Render` calls `renderer.AfterClear()` before every frame (`RenderBenchmarks.cs:125-128`), so
   every measured frame starts with the cursor position unknown and the pen known-default. That is the right
   steady state for a diff, but it means the `one cell` number (59 bytes) always includes a full CUP; a variant
   where the cursor is already on the row would show the incremental cost.
10. `FrameBenchmarks.ListScroll` wraps `_tick % Items.Length`, so one frame in 5,000 is a jump to the top (a full
    repaint) averaged into the mean; harmless, but worth a comment or a modulo that keeps scrolling.
11. The `Allocations.AssertNone` helper (`src/Tuinet.Testing/Allocations.cs`) is a good public guard; consider
    also exposing a *bytes-per-frame* assertion helper (`TestTty.WriteCount`/`Terminal.LastFrameBytes` already
    make it possible) so apps can pin their own byte budgets the way `TerminalTests` does.

## What's done well (keep)

- **Cell layout.** 16-byte, blittable, canonical `Cell` (`Cell.cs:23-30`) with the link id packed into the
  spare bits of width/flags, so a row diff is a plain `SequenceEqual`/`CommonPrefixLength` over bytes
  (`Renderer.cs:308-312`) and `PenMatches` is two masked 8-byte compares (`Renderer.cs:1253-1257`).
- **Text writes as template stores.** `WriteAscii` (`CellBuffer.cs:762-796`) writes two 8-byte words per cell from
  a prebuilt template; layering recomputes the template only when the colors underneath change. 1 ns/char measured.
- **Vectorized fills.** `SetStyle`/`SetRune(Rect)` as `(cell & keep) | set` on `Vector128` (`CellBuffer.cs:217-308`);
  `Clear` is a 16-byte-pattern `Span.Fill` (1.6 µs for 192 KB).
- **Renderer specialization.** Join checks and REP are static-abstract policy structs
  (`Renderer.cs:1074-1103`), so rows without joinable glyphs and terminals without REP run a loop with no
  cluster/REP code at all; `_mayJoin` per row makes that the common case for Latin text.
- **Bytes on the wire.** Scroll regions with IL/DL, DECLRMM column bands, EL/ECH with a per-run cost model
  (`TryErase`, `Renderer.cs:892-952`), REP with an explicit break-even (`EmitRepeat`, `Renderer.cs:980-1021`),
  SGR deltas, CUF-vs-reemit cost comparison, synchronized output, OSC 8 opened/closed only around linked runs. The
  `bytes` report reproduces the documented numbers (271 bytes for a one-row scroll of 60 rows, 131–165 for a list
  scroll frame).
- **One write per frame, no per-frame syscalls for size or termios.** Size is cached and refreshed on `SIGWINCH`;
  wake-ups go through a self-pipe with a one-byte-in-flight guard (`UnixTty.cs:207-214`).
- **Zero allocation is a tested property**, not a hope: `AllocationTests` plus the public `Allocations.AssertNone`
  helper; `SkipLocalsInit` module-wide; `SearchValues` instead of `IndexOfAnyExceptInRange` where the latter
  allocates; `stackalloc` with heap fallbacks above fixed limits; `ArrayPool` for the rare clean-on-paste path.
- **Interning for clusters and links** keeps cells fixed-size and the diff byte-exact across frames; lock-free
  reads, publish-before-id ordering for the lock-free array.
- **Widget complexity.** `ListView`/`Table`/`TreeView`/`TextArea`/`Menu` are O(visible); `TreeState` flattens
  once per shape change with an iterative walk and a cycle guard; `EditableText` is a gap buffer with a lazily
  shifted line index and an undo log that merges typing runs without allocating.
- **Input.** Byte-at-a-time state machine with fixed buffers; events are values in a preallocated queue; escape
  timeout handled without timers; terminal replies (kitty flags, DECRQM, XTVERSION) parsed inline without
  allocation.
- **The run loop** draws once per event burst and sleeps on `poll` with the next due time; an idle app costs
  nothing.
- **Benchmarks measure the right layers**: renderer alone, widget alone, whole frame through `Terminal`, and a
  bytes report, with a null tty so the terminal emulator is excluded; the comparison project documents its
  methodology and caveats honestly.

## Appendix: measurements

All on this machine (the one in `docs/performance.md`), .NET 10, Release. The probe harness lives in the session
scratchpad (`/tmp/claude-1000/-home-lmaslanka-projects-tuinet/12a5fff8-61df-41a4-ace8-74679000ce77/scratchpad/perfprobe`),
is not part of the repo, and references `src/Tuinet/Tuinet.csproj` through the public API only.

### A. Build

```sh
cd /home/lmaslanka/projects/tuinet
dotnet build tuinet.slnx -c Release        # 0 warnings, 0 errors
```

### B. Project `bytes` report

```sh
dotnet run -c Release --project bench/Tuinet.Benchmarks -- bytes
```

```
                       no REP      REP
no change                   0        0 bytes
one cell                   59       59 bytes
full repaint, styled    13263    13263 bytes
scroll by one row         271      271 bytes
split scroll            10265    10265 bytes
split scroll, DECLRMM     459      459 bytes
dialog closed             313      313 bytes
dialog opened            2037     1524 bytes  -26%
rows shortened            698      698 bytes
rules                   14885     3065 bytes  -80%
bars                    19754     3071 bytes  -85%
list first frame         3721     2573 bytes  -31%
list scroll frame         131      131 bytes
table first frame        3144     2578 bytes  -19%
table scroll frame        205      193 bytes  -6%
list, scrolling           165      160 bytes  -4%
table, scrolling          233      223 bytes  -5%
showcase first frame     9535     8924 bytes  -7%
showcase j                863      853 bytes  -2%
showcase edit opens      5478     4144 bytes  -25%
showcase edit closes     4457     4405 bytes  -2%
showcase stats tab       2253     1949 bytes  -14%
showcase groups tab      3305     3301 bytes  -1%
showcase list tab        4505     4292 bytes  -5%
```

### C. Targeted BenchmarkDotNet run (shift-detection worst case, REP probe cost)

```sh
dotnet run -c Release --project bench/Tuinet.Benchmarks -- \
  --filter '*RenderBenchmarks.FullRepaint*' '*RenderBenchmarks.NoChange*' --job short
```

```
| Method                  | Repeat | Mean      | Error     | StdDev    | Allocated |
| 'no change'             | False  |  5.201 us | 1.8386 us | 0.1008 us |         - |
| 'full repaint, styled'  | False  | 35.512 us | 5.2514 us | 0.2878 us |         - |
| 'full repaint, margins' | False  | 36.182 us | 4.0748 us | 0.2234 us |         - |
| 'no change'             | True   |  4.738 us | 0.0925 us | 0.0051 us |         - |
| 'full repaint, styled'  | True   | 39.067 us | 0.4081 us | 0.0224 us |         - |
| 'full repaint, margins' | True   | 40.013 us | 1.7794 us | 0.0975 us |         - |
```

### D. Probe harness, run 1 (scripts, cursor bytes, Paragraph, TextInput)

```sh
cd <scratchpad>/perfprobe
DOTNET_TieredCompilation=0 dotnet run -c Release
```

Each timing is the mean of 400 iterations after 50 warm-ups (200 for the frame scenarios), 200×60 buffer.

```
CellBuffer.Clear 200x60 (192 KB): 1.58 us
SetString 60 rows ascii       :     12.1 us   (1.0 ns/char)
SetString 60 rows cyrillic    :    257.9 us   (21.5 ns/char)
SetString 60 rows greek       :    255.8 us   (21.3 ns/char)
SetString 60 rows cjk(100 ch) :     41.1 us   (6.8 ns/char)
SetString 60 rows box-drawing :     64.6 us   (5.4 ns/char)
TextWidth.Of x60: ascii 8.0 us, cyrillic 199.5 us
full repaint frame ascii    :     94.7 us  12137 bytes      # BeginFrame + 60×SetString + Present, alternating two texts
full repaint frame cyrillic :    578.8 us  22037 bytes
full repaint frame greek    :    566.2 us  23597 bytes
vertical column of 60 cells at x=199: 727 bytes (12.1 B/cell)
vertical column of 60 cells at x=100 (prev column cleared too): 1087 bytes
scrollbar thumb down one (2 cells, same column): 38 bytes: ESC[?2026hESC[11;200H ESC[22;200H█ESC[?2026l
block border recolor (100x60): 1392 bytes
row of 50 lone emoji (U+1F525): 858 bytes (17.2 B/glyph): ESC[?2026hESC[H ... ESC[39m🔥ESC[6;3H🔥ESC[6;5H🔥...
row of 50 CJK: 175 bytes
Paragraph 2000 lines wrapped: scroll=0 28 us, scroll=3900 655 us, scroll=0+Scrollbar.Auto 695 us; TextArea near end 57 us
TextInput len=    200: key + render + backspace: 6.4 us
TextInput len=  20000: key + render + backspace: 6.9 us
TextInput len= 200000: key + render + backspace: 9.9 us
```

(The same harness under default tiered JIT, i.e. mostly tier-0 code for a short run, gave 7.6 ns/char ASCII vs
31.3 ns/char Cyrillic and 434 µs vs 806 µs per full-repaint frame: same conclusions, worse absolutes.)

### E. Probe harness, run 2 (cursor hide/show, SetLink, startup)

```
focused TextInput, one typed char per frame: 29 bytes: ESC[?2026hESC[?25lxESC[?25hESC[?2026l
focused TextInput, caret left only: 6 bytes: ESC[1;8H
SetLink x100 (interned URL, 10 cells): 6.3 us  (63 ns/call)
Terminal ctor + first frame (200x60, warm process, tiering off): 5038 us
one cell, no cursor: 18 bytes: ESC[?2026hbESC[?2026l
one cell, cursor shown at fixed spot: 38 bytes: ESC[?2026hESC[?25lESC[HaESC[6;6HESC[?25hESC[?2026l
```

### F. Scenario definitions used above (for reproduction)

- *Scripts*: 60 rows × 200 chars of `"the quick brown fox jumps over the lazy dog "`, `"съешь же ещё этих мягких
  французских булок да выпей чаю "`, `"αβγδεζηθικλμνξοπρστυφχψω "`, 100 chars of `"你好世界こんにちは안녕하세요"`,
  200 × `'─'`; `SetString(0, y, s)` with the default style.
- *Full repaint frame*: `Terminal` over a counting `ITty` (`ScrollRegions = true`, TrueColor), each frame
  `BeginFrame`, 60 × `SetString` of the lower- or upper-case variant alternately, `Present`.
- *Vertical column*: blank frame, then `SetRune(199, y, '│')` for all 60 rows.
- *Scrollbar thumb*: `'█'` in column 199 rows 10–20, next frame rows 11–21.
- *Block border recolor*: `Block` at (0,0,100,60) with `BorderStyle` fg (100,100,100) then (200,200,0).
- *Paragraph*: 2,000 lines of ~130 chars, `Wrap = Word`, 200×60; `TextArea` with the caret at line 1,950.
- *TextInput*: `new string('a', n)`, caret at `n/2`; per iteration `Handle('x')`, render into (0,0,200,1),
  `Handle(Backspace)`.
