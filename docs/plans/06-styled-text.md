# 06 · Mixed-style text

**Type:** missing · **Effort:** M · **Priority:** 2

## Problem
`Paragraph` takes one style. Mixed styles on one line (a key hint, a highlighted word, a status with a
colored value) are built by chaining `SetString` calls by hand — the Showcase subtitle and key bar do this.
Table cells, list items and block titles also take a single style.

## Design (zero allocation)
- `readonly record struct StyledRun(int Length, Style Style)` and `readonly ref struct StyledText`
  = text span + runs span (`ReadOnlySpan<char>` + `ReadOnlySpan<StyledRun>`); runs are typically a
  collection expression on the stack.
- Builder for convenience without allocation: `StyledTextBuilder` over caller `Span<char>`/`Span<StyledRun>`
  (`b.Append("j/k", key).Append(" move", dim)`).
- Optional tiny markup parser into caller spans: `"[b]bold[/] and [fg=#F5A623]amber[/]"` → `StyledText`.
- `CellBuffer.SetText(x, y, StyledText, maxWidth, overflow)` — one pass, styles layered per run.
- `Paragraph` gains a `StyledText` constructor; wrapping walks runs (cluster-aware, plan 01).
- `TextWidth.Of(StyledText)`; `Block.Title` overload; `ITableSource` may return styled cells via an
  optional `IStyledTableSource`; `TextItems` stays.

## Files
`src/Tuinet/StyledText.cs` (new), `CellBuffer.cs`, `Widgets/Paragraph.cs`, `Widgets/Block.cs`, `Widgets/Table.cs`,
Showcase (replace hand-chained strings), README.

## Tests
Run boundaries, ellipsis across runs, wrapping across runs, markup parser (escapes, unknown tags), AllocationTests
with styled text, benchmark vs chained `SetString`.
