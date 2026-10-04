# 09 · Common widgets: tabs, tree, charts, popup, menu

**Type:** missing · **Effort:** M each · **Priority:** 5

All follow the house style: `readonly ref struct`, app-owned state, visible-only rendering, 0 B per frame.

## Tabs
`Tabs` over a span of titles + `TabsState` (selected). Divider, selected/unselected styles, overflow with
scroll arrows, `Next/Previous`, mouse hit-test (plan 05).

## Tree view
`TreeView<TSource>` with `ITreeSource` (child count, label, `IsExpanded` from state) flattened lazily to
visible rows; `TreeState` holds expanded set (app-owned), selection and scroll (reuse `ListState`).
Guide lines (`├─ └─ │`), expand/collapse keys, lazy children for file trees.

## Sparkline and bar chart
`Sparkline` over `ReadOnlySpan<double>` using the eight block heights; `BarChart` with labels, values,
horizontal/vertical, max scaling. Useful for dashboards (the Stress sample's latency).

## Popup / modal helper
`Popup` = `Rect.Centered` + `Clear` + `Block` in one call, plus an optional shadow; returns the inner area.
The Showcase dialogs repeat this pattern.

## Menu / command palette
`Menu` (vertical list with shortcuts and separators) and a `CommandPalette` sample pattern: `TextInput` +
fuzzy-filtered `ListView` (fuzzy scorer allocation-free over spans).

## Tests / verification
Widget tests per component in `WidgetTests`, AllocationTests frame including each, a "gallery" sample (see plan 17).
