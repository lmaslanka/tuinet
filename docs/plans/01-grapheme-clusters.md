# 01 · Grapheme clusters

**Type:** hole (correctness) · **Effort:** L · **Priority:** 1 (do before 1.0: it fixes the cell format)

## Problem
A user-perceived character can be several code points. Today every code point is handled on its own:

| Text | Code points | What TUI.NET does now |
|---|---|---|
| `é` written as `e` + U+0301 | 2 | the combining mark is dropped → `e` |
| Devanagari `कि`, Thai `กิ` | base + mark | marks dropped → wrong text |
| `👨‍👩‍👧` (ZWJ family) | 5 | ZWJs dropped → three separate emoji, 6 columns |
| `❤️` (U+2764 U+FE0F) | 2 | VS16 dropped → text-style heart, 1 column |
| `🇵🇱` (two regional indicators) | 2 | counted as 2 + 2 = 4 columns; many terminals draw 2 → the rest of the row can shift |

`TextInput.Insert` also drops every zero-width code point, so typed or pasted emoji sequences are mangled.
`CellFlags.Grapheme` (`Cell.cs`) has been reserved for this but is unused.

## Design

### Segmentation and width (`src/Tuinet/Internal/Graphemes.cs`, new)
- Cluster boundaries come from `StringInfo.GetNextTextElementLength(ReadOnlySpan<char>)` (UAX #29, in CoreLib,
  works with `InvariantGlobalization`, no allocation).
- Fast paths so common text never calls it: printable ASCII followed by ASCII; and pairs of "simple" code
  points (Latin below U+0300, CJK ideographs, kana, Hangul syllables) that can never join.
- **Cluster width** = sum of its code points' widths capped at 2, raised to 2 when it contains VS16 (U+FE0F).
  That gives combining marks the base width (`e`+U+0301 = 1, Thai = 1), Indic spacing marks 2 (as most
  terminals draw them), Hangul jamo sequences 2, every emoji sequence 2, and `❤️`/keycaps 2.
  A cluster whose first code point is zero-width (a stray mark) is dropped, as now.
- **Legacy width** = sum of per-code-point widths (what a terminal without grapheme support advances).
  The renderer uses it to repair the row (below).

### Storage: interned cluster ids in the 16-byte cell
- Multi-code-point clusters are interned in a process-wide store (`GraphemeStore`, lock-protected
  `Dictionary<string,int>` with a `ReadOnlySpan<char>` alternate lookup, so lookups don't allocate).
- The cell's rune field holds the id and `CellFlags.Grapheme` is set. Ids are content-addressed, so equal
  clusters give byte-identical cells in any buffer, and the renderer's memcmp diff stays correct.
- Steady state is allocation-free: only the first sighting of a new cluster allocates its string.
- Cap of ~1M entries; past it a cluster degrades to its first code point (still consistent widths).
- Single-code-point text is unchanged: same cells, same bytes as today.

### Public API
- `Cell.IsGrapheme`; `Cell.Rune` returns the cluster's first code point; new `Cell.Text` returns the full
  cluster (no allocation for clusters; `Rune.ToString()` otherwise).
- `CellBuffer.RowText`/`ToString` print full clusters. `SetString`, `TextWidth.Of(span)` and the ellipsis
  logic become cluster-aware. `SetRune` stays single-code-point.

### Writing (`CellBuffer.Write`)
- ASCII fast path: a run stops one char early when the next char is ≥ U+0300 (it could attach to the last letter).
- Slow path: take one cluster; single code point → existing path; otherwise intern and place a grapheme cell
  (+ continuation when width 2). Clusters are never split at the clip limit.

### Rendering (`Renderer`)
- A grapheme cell emits its whole cluster (UTF-8), reserving enough output space for long clusters.
- **Self-healing cursor:** after a cluster, the cursor column is marked unknown (next glyph uses an
  absolute move) and the next `legacyWidth - width` cells are force-repainted. When the legacy width is
  smaller (VS16), the cluster's cells are blanked first. So the row ends up right whether the terminal
  advances by the cluster width (grapheme-aware) or by the code-point sum (legacy).
- **No accidental joins:** two neighbouring cells can form one cluster on the terminal (two single regional
  indicators, an emoji then a lone skin tone, a Hangul L jamo then a syllable). Before emitting a cell that
  could join the previous one, the renderer moves the cursor explicitly, which ends the cluster. Plain
  Latin/CJK pairs skip the check.
- Enable DEC mode 2027 (grapheme cluster mode) on entry, reset on exit. Terminals that support it
  (Contour, WezTerm, Ghostty, …) then use the same widths; others ignore it.

### Widgets
- `Paragraph` wrapping breaks only between clusters.
- `TextInput` stores text as chars, keeps zero-width code points, and moves/deletes/renders by cluster.

## Tests
- `GraphemeTests` (new): widths for the table above, ASCII + mark, Hangul jamo, fast-path boundaries, stray marks.
- `CellBufferTests`: grapheme cells, continuation halves, overwrite of either half, clip/ellipsis never
  splits a cluster, same cluster → identical cell bytes across buffers, `RowText`.
- `RendererTests`: exact bytes for `éx` and a ZWJ emoji (cluster, then absolute move, forced repaint).
- `RendererFuzzTests`: cluster words in the fuzz corpus; the emulator gains a grapheme-aware mode (strict
  comparison) and a legacy mode (per-code-point advance; checks every non-cluster cell exactly).
- `WidgetTests`: `TextInput` caret/backspace over clusters; `Paragraph` wrap; `Table` right-alignment of emoji.
- `AllocationTests`: frames containing clusters still allocate 0 bytes after warm-up.
- `TerminalTests`: enter/leave sequences include `?2027h` / `?2027l`.

## Verification
- `dotnet test` all green; benchmarks: ASCII and CJK `SetString` must not regress meaningfully.
- tmux run of a sample showing the table above (tmux is grapheme-aware since 3.4; otherwise legacy path).
- AOT publish warning-free.

## Status: implemented (branch `graphemes`)

Built as designed, plus three things the testing turned up:
- **Per-row "may join" flag** in `CellBuffer` (set when a non-simple glyph or a cluster is written, cleared
  by `Clear`). Rows of plain Latin, CJK and box drawing render through a loop specialised by the JIT
  (`RenderRow<NoJoinChecks>`) with no cluster logic, which keeps full repaints within ~4% of before.
- **Emoji-plane code points on their own** (U+1F000 and up: a lone regional indicator or skin tone) also
  end with an absolute move: tmux draws a lone regional indicator 1 column wide and attaches a lone skin
  tone to the emoji before it, which shifted the next cell. Found with a tmux-vs-reference screen comparison.
- `IndexOfAnyExceptInRange` allocated on the ASCII fast path; it uses a static `SearchValues<char>` instead.

Measured cost (same machine, back to back with `main`): ASCII text and frames within run-to-run noise (0–4%),
full repaint +4%, CJK `SetString` +12–24% (~1 ns per character: the "can this join its neighbour" check).
Text full of clusters: 60 rows in ~40 µs, 0 bytes allocated.
