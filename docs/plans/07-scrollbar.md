# 07 · Scrollbar

**Type:** missing · **Effort:** S · **Priority:** 5

## Problem
`Paragraph.LineCount` is documented as being "for scrollbars" but there is no scrollbar. Long lists,
tables and paragraphs give no sense of position.

## Design
- `readonly ref struct Scrollbar : IWidget` — `Content` (total), `Viewport` (visible), `Position` (offset),
  `Orientation` (vertical/horizontal), track/thumb glyphs and styles. Eighth-block thumb ends for
  sub-cell precision (same technique as `ProgressBar`), plain `█`/`│` fallback.
- Built-in option on `ListView`/`Table`/`Paragraph`: `Scrollbar = ScrollbarMode.Auto` (shown only when
  content exceeds the viewport) drawing in the rightmost column (content area shrinks by one).
- Mouse (plan 05): click/drag on the track maps to a position via a static helper.

## Files
`Widgets/Scrollbar.cs` (new), `Widgets/ListView.cs`, `Widgets/Table.cs`, `Widgets/Paragraph.cs`, Showcase, README.

## Tests
Thumb size/position at start/middle/end, tiny viewports, content ≤ viewport (hidden in Auto), eighths rendering.
