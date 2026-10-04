# 08 · Multi-line text editing

**Type:** missing · **Effort:** L · **Priority:** 5

## Problem
`TextInput` is single-line and has no selection, undo or clipboard. Descriptions, commit messages and
notes need multi-line editing (the Showcase edit dialog's "description" is single-line today).

## Design
- `TextAreaState` (class, app-owned): gap buffer of chars, caret (line, cluster column), selection anchor,
  vertical scroll, desired column for up/down, undo/redo stack of edit records (bounded), soft wrap on/off.
- `TextArea` widget (`IStatefulWidget<TextAreaState>`): renders visible lines only, real cursor, selection
  style, optional line numbers, placeholder.
- Keys: arrows/Home/End/PageUp/PageDown, Ctrl+arrows by word, Shift+movement selects, Ctrl+A,
  Backspace/Delete by cluster (plan 01), Enter inserts a newline (Ctrl+Enter/Tab left to the app),
  Ctrl+Z/Ctrl+Y undo/redo, paste inserts multi-line text.
- Shared editing core with `TextInput` (selection + undo there too).
- Clipboard copy via OSC 52 when plan 11 lands.

## Files
`Widgets/TextArea.cs` (new), `Widgets/TextInput.cs` (shared core), Showcase description field, README.

## Tests
Editing/caret/selection/undo unit tests, wrap with wide glyphs and clusters, large text (100k lines) render
benchmark (visible lines only), AllocationTests for steady-state frames.
