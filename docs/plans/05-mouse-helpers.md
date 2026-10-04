# 05 · Mouse support in widgets

**Type:** hole · **Effort:** M · **Priority:** 4

## Problem
Mouse events are parsed (`Input.cs`, `TerminalOptions.Mouse`) but no widget or sample uses them, and there
are no helpers to turn a click into a row, cell or button. `Mouse = true` is effectively unsupported.

## Design (immediate mode: hit-testing against the last rendered layout, no retained tree)
- `ListState`: remember the last rendered area (`Area`), add `bool HandleMouse(MouseEvent ev)` — click selects
  the row under the pointer, wheel scrolls (moves `Offset`, clamped), returns whether it changed.
- `Table`: same via `ListState`; plus a static `Table.HitTest(columns area…)`/stored column rects in the
  state so a header click can return the column index (for sorting).
- `Button`/`Checkbox`: `static bool Clicked(Rect area, MouseEvent ev)` helper (press+release inside).
- `DropdownState`/`TextInputState`: click to open/select; click to place the caret (needs the rendered
  scroll offset, already in the state).
- `Rect.Contains` exists; add `MouseEvent.IsClick`/`IsWheel` conveniences.
- Showcase: click rows, wheel-scroll the table, click fields in the edit dialog, click a header to sort.

## Files
`src/Tuinet/Input.cs`, `Widgets/ListView.cs`, `Widgets/Table.cs`, `Widgets/Button.cs`, `Widgets/Checkbox.cs`,
`Widgets/Dropdown.cs`, `Widgets/TextInput.cs`, Showcase, README.

## Tests
`WidgetTests`: click/wheel on list and table (with scroll offset and header rows), header hit-test, caret
placement with wide glyphs. Showcase tests with injected mouse events. AllocationTests unchanged.
