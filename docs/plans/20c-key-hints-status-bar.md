# 20c · Key hints and status bar

**Type:** missing · **Effort:** S · **Priority:** 1 · **Depends on:** — · **Used by:** 20a, 20b (Showcase hints)

## Problem
Nearly every TUI ends in a row of key hints, often with a status line. The Showcase builds its own from a
`(string Key, string Action)[]` with a `StyledTextBuilder` (`ShowcaseApp.RenderKeys`). When the row is too
narrow it cuts the last hint mid-word, and the hints can't be clicked.

## Design
```csharp
public readonly record struct KeyHint(string Key, string Action, int Priority = 0);

public readonly ref struct KeyHints
{
    public KeyHints(ReadOnlySpan<KeyHint> hints);
    public Style KeyStyle { get; init; } public Style ActionStyle { get; init; }
    public ReadOnlySpan<char> Separator { get; init; } = "  ·  ";
    public Style SeparatorStyle { get; init; }
    public void Render(Rect area, CellBuffer buffer);
    public int HintAt(int x, int y, Rect area);   // −1, or the index of the hint under the pointer
}

public readonly ref struct StatusBar
{
    public StyledText Left { get; init; }  public StyledText Center { get; init; }  public StyledText Right { get; init; }
    public Style Style { get; init; }      // fills the row
    public void Render(Rect area, CellBuffer buffer);
}
```
- **Drop whole hints, never cut one:** when the hints don't fit, the ones with the lowest `Priority` are dropped
  first (ties: from the end), and the order of the rest is kept. "q quit" with a high priority survives a
  narrow screen.
- **Clickable:** `HintAt` recomputes the same fit (no stored rects), so a click on "ctrl+p commands" can run the
  same action as the key.
- **StatusBar** places three `StyledText` segments in one row: right first, then left, then center in the gap
  that's left, cut with an ellipsis. It's a small convenience over `Layout`, but every app writes it.
- Both measure with `TextWidth` and allocate nothing; hint arrays are static in the app.

## Showcase
`RenderKeys` and its tuple arrays become `KeyHints` with priorities (move/edit/quit highest). The bottom row
becomes a `StatusBar`: hints on the left, and "page · n items · frame µs" on the right. A click on a hint runs
its action. A Showcase test narrows the terminal and checks that "q quit" is still shown.

## Files
`Widgets/KeyHints.cs`, `Widgets/StatusBar.cs` (new), Showcase `ShowcaseApp.cs`, `PublicAPI.Unshipped.txt`,
`docs/widgets.md`, README, CHANGELOG.

## Tests
- Fit: everything fits; drop by priority, then from the end; one hint wider than the area; empty spans.
- `HintAt` agrees with the drawn layout at every x, and returns −1 on separators.
- StatusBar: segment precedence when they collide; wide glyphs at the cut point.
- Showcase snapshot of the new bottom row; a narrow-terminal test; a click on "ctrl+p" opens the palette.
- `AllocationTests`: 0 B.

## Non-goals
Reading the hints from a key-binding table (Tuinet has no binding system), and multi-row hint panels
(a `Table` does that).
