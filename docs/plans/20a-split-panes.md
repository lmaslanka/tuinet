# 20a · Resizable split panes

**Type:** missing · **Effort:** M · **Priority:** 5 · **Depends on:** — · **Used by:** —

## Problem
`Layout` splits are fixed: the Showcase's list and details columns are `Fill` + `Length(38)`, and the user can't
give either one more room. Master–detail apps (logs | details, files | preview) need a divider the user can drag.

## Design
```csharp
public struct SplitState
{
    public SplitState(int permille = 500);           // first pane's share, in 1/1000 of the space
    public int Permille { get; set; }
    public bool IsDragging { get; }
    public Rect Divider { get; private set; }        // last layout, for hit-testing and hover
    public void Nudge(int cells);                    // keyboard resizing; the app picks the keys
    public bool HandleMouse(MouseEvent ev);          // press on the divider starts a drag, drag moves, release ends
    public void Reset();                             // back to the initial share
}

public readonly ref struct SplitPane
{
    public Direction Direction { get; init; } = Direction.Horizontal;   // side by side
    public int MinFirst { get; init; } = 1; public int MinSecond { get; init; } = 1;
    public int DividerWidth { get; init; } = 1;      // 0 = no line, still draggable on the first pane's edge
    public Style DividerStyle { get; init; }
    public Style ActiveStyle { get; init; }          // while dragging or hovered

    /// <summary>Lays out the two panes and draws the divider; the app renders into first and second.</summary>
    public void Render(Rect area, CellBuffer buffer, ref SplitState state, out Rect first, out Rect second);
}
```
- **A layout helper, not a container:** it returns two rects and draws only the divider, so there's no retained
  tree. Nesting is two calls (a vertical split inside the right pane).
- **Proportional state:** the share is kept in per-mille, so a terminal resize keeps the proportions, and
  `MinFirst`/`MinSecond` are applied at layout time without changing the stored share. Dragging stores the
  share the pointer implies.
- The divider is `│`/`─`. While dragging or hovered (with `MouseMotion` on), it uses `ActiveStyle`. It can sit
  in a `Block`'s border: `┬`/`┴` joins are drawn when the pane edges are borders. The tee choice per
  `BorderType` already exists for `Menu`'s separators (`Menu.cs`); move it to a shared internal helper.
- **Scroll bands stay fast:** a pane is an ordinary rect, so the renderer's left/right-margin scrolling (plan 13)
  still moves a scrolling list inside one pane.

## Showcase
The list | details columns become a `SplitPane` (initially 70 %, details at least 30 cells). Drag the divider,
or press `<`/`>` to nudge it and `=` to reset it. The key hints mention it.

## Files
`Widgets/SplitPane.cs` (new), Showcase `ShowcaseApp.cs`, `PublicAPI.Unshipped.txt`, `docs/layout.md`, README, CHANGELOG.

## Tests
- Layout at several shares, both directions, `DividerWidth` 0 and 1; minimums win over the share; a resize keeps
  the share; an area too small for both minimums gives the first pane priority.
- Mouse: press, drag past both ends (clamped), release outside, presses elsewhere ignored; hover style.
- Byte test: dragging the divider by one cell repaints only the columns that changed, and a list scrolling in the
  left pane still uses DECSLRM bands.
- Showcase: dragging the divider widens the table, and a column hidden at 70 % appears.
- `AllocationTests`: a split frame and a drag allocate 0 B.

## Non-goals
More than two panes per split (nest instead), collapsing panes, and persisting the share.
