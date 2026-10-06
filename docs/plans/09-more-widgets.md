# 09 · Common widgets (overview)

Split into one plan per widget. All of them follow the house style: `readonly ref struct` widgets,
app-owned state, visible-only rendering, 0 B per steady-state frame, and hit-testing recomputed from the last
render instead of a retained tree.

| Plan | Widget | Effort | Depends on |
|---|---|---|---|
| [09a](09a-popup.md) | Popup + `Rect.PlaceNear` placement | S | — |
| [09b](09b-tabs.md) | Tabs | S–M | — |
| [09c](09c-sparkline-barchart.md) | Sparkline and bar chart | M | 09b (Stats page) |
| [09d](09d-menu-fuzzy-palette.md) | Menu, `Fuzzy` scorer, command palette pattern | M | 09a, 09b |
| [09e](09e-tree-view.md) | Tree view | M–L | 09b (Groups page) |

Order: 09a → 09b → 09c → 09d → 09e. The first two are the building blocks the others use.

## How the Showcase grows
Every control and feature must be visible somewhere in the Showcase:

- **09a:** the edit and progress dialogs and the dropdown lists get shadowed `Popup`s.
- **09b:** a tab bar in the list panel's border, with **List · Stats** (numbers only at first).
- **09c:** the Stats page gets bar charts per kind and priority, and live sparklines of bytes and time per
  frame. The Stress sample gets a frame-time sparkline.
- **09d:** a right-click or `m` opens a context menu on a row; Ctrl+P opens a command palette over every action.
- **09e:** a **Groups** tab with a kind → priority → item tree, kept in sync with the List selection.

Plan 17's gallery sample is still separate and can reuse these pages.
