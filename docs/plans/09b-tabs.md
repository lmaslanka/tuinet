# 09b · Tabs

**Type:** missing · **Effort:** S–M · **Priority:** 5 · **Depends on:** — · **Used by:** 09c (Stats page), 09e (Groups page)

## Problem
There's no way to switch between screens or panes. The Showcase has one screen, so any more content would mean
more dialogs.

## Design
```csharp
public struct TabsState
{
    public int Selected { get; set; }
    public int Offset { get; set; }        // first visible tab when the titles overflow
    public Rect Area { get; private set; } // last render, for hit-testing
    public void Next(int count);           // wraps
    public void Previous(int count);       // wraps
    public void Select(int index, int count);
    public bool Handle(KeyEvent key, int count);  // Left/Right, h/l, Home/End: for when the bar has focus
}

public readonly ref struct Tabs : IStatefulWidget<TabsState>
{
    public Tabs(ReadOnlySpan<string> titles);
    public Style Style { get; init; }
    public Style SelectedStyle { get; init; }      // layered over Style
    public ReadOnlySpan<char> Divider { get; init; } = "│";
    public Style DividerStyle { get; init; }
    public int Padding { get; init; } = 1;         // cells each side of a title
    public Style ArrowStyle { get; init; }         // ‹ › overflow markers

    public int TabAt(int x, int y, in TabsState state);          // -1, or a tab index
    public bool HandleMouse(MouseEvent ev, ref TabsState state);  // click selects, arrows/wheel scroll
}
```
- **One row.** Titles are measured with `TextWidth`, so wide glyphs and clusters work, and clipped with
  `SetString`. To get an underline look, put `Attr.Underline` in `SelectedStyle`. No second indicator row.
- **Overflow.** When the titles don't fit, show `‹`/`›` at the edges and keep `Selected` visible by moving
  `Offset`, using the same follow-on-change rule as `ListState.Follow`. A click on an arrow scrolls by one tab,
  and the wheel over the bar scrolls too.
- **Hit-testing without stored rects.** As with `Table.HeaderColumnAt`, the widget recomputes the layout from
  (titles, `state.Area`, `state.Offset`, `Padding`, `Divider`), so the state holds no arrays and render
  allocates nothing.
- **Global shortcuts are up to the app.** Ctrl+Tab isn't reported without the kitty keyboard protocol, so the
  library doesn't claim one. `Handle` covers a focused tab bar only.
- **Works inside a block border.** Render the tabs into the top border row of a `Block`, between its corners,
  to get `╭─ Items │ Stats ──╮`.

## Showcase
- The list panel's block border carries the tabs **List · Stats** (09e adds **Groups**). `[`/`]` and
  Alt+1…3 switch tabs, and a click on a title switches too. Each page keeps its own state, so the list
  selection survives a tab switch.
- The **Stats** page starts as plain numbers: item counts per kind and per priority, plus bytes and time of
  the last frame, fed by `Program.cs` through `app.RecordFrame(terminal.LastFrameBytes, elapsed)`. Plan 09c
  turns them into charts.
- The key hints row shows the tab keys.

## Files
`Widgets/Tabs.cs` (new), Showcase `ShowcaseApp.cs`/`Program.cs`, `PublicAPI.Unshipped.txt`, README, CHANGELOG.

## Tests
- Layout: positions and widths with padding and divider, selected style on only the selected title, divider style.
- Overflow: arrows appear only when needed, selecting the last tab scrolls it into view, resizing back to
  full width resets `Offset`, a CJK/emoji title is cut at a cell boundary.
- `TabAt` with an offset, on a divider (−1), and on an arrow; `HandleMouse` click/arrow/wheel.
- `Next`/`Previous` wrap; count 0 gives `Selected` −1; `Handle` keys.
- Showcase: switching tabs keeps the list selection; a click on a tab title switches. `AllocationTests` with a tab bar allocates 0 B.

## Non-goals
Closable or reorderable tabs, and vertical tabs. A vertical list of pages is just a `ListView`.
