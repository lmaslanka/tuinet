# 09a · Popup and placement

**Type:** missing · **Effort:** S · **Priority:** 5 · **Depends on:** — · **Used by:** 09d (menu, palette)

## Problem
Every overlay repeats the same three steps: `Rect.Centered` → `new Clear(style)` → `new Block { … }` →
`block.Inner(...)` (`EditDialog.Render`, `ProgressDialog.Render`, `Dropdown.RenderPopup`). Nothing gives depth
(no shadow), and the "below the anchor, flip above if there is no room" logic lives privately in `Dropdown`, so
a context menu or command palette would have to copy it.

## Design
### `Popup` widget
```csharp
public readonly ref struct Popup : IWidget
{
    public Popup() { }
    public Block Block { get; init; } = new();   // borders, title, footer; Block.Style is the fill
    public bool Shadow { get; init; }
    public Style ShadowStyle { get; init; } = new(Color.Default, Color.Black, Attr.Dim);
    public int Padding { get; init; }            // cells inside the border, horizontal ×2 like the dialogs

    public void Render(Rect area, CellBuffer buffer);
    public Rect Frame(Rect area);                 // the box without the shadow (for hit-testing)
    public Rect Inner(Rect area);                 // inside border and padding
    public Size Outer(int contentWidth, int contentHeight);  // content size → area size
}
```
- **The area includes the shadow.** A widget draws only inside its area, so the shadow uses the last
  2 columns and the last row, and the box is `area` minus (2, 1). The shadow is 2 cells wide so it looks the
  same depth as it is tall. `Outer()` lets callers center by content size:
  `buffer.Area.Centered(popup.Outer(60, 20))`.
- **The shadow restyles the cells already there and keeps their glyphs**, the classic dialog look. It uses
  `CellBuffer.SetStyle`, the 128-bit patch, so it's cheap. Cells off the screen are clipped.
- Render order: shadow strips (right and bottom, offset by one) → `Clear` with `Block.Style` → `Block`.
  When `Block.Borders == None` it's just a filled box.
- Stateless like `Button`: the app keeps the `Rect` it rendered into, and checks `ev.IsIn(popup.Frame(r))`
  for click-outside-to-close.

### Placement helpers on `Rect`
```csharp
public Rect Centered(Size size);                         // overload of the existing Centered(int, int)
public Rect PlaceNear(Rect anchor, Size size);           // called on the bounds (usually buffer.Area)
```
- `PlaceNear`: below `anchor` if it fits, otherwise above it if there's more room above, otherwise on
  whichever side has more room, cut to fit. Horizontally it starts at `anchor.X` and moves left to stay inside
  the bounds. A 0×0 anchor is a point (a context menu at the pointer opens at the pointer's row).
- `Dropdown.RenderPopup` uses `PlaceNear` and `Popup`. Its current placement rule becomes the shared one,
  and its existing tests must stay green. Add `PopupShadow` to `Dropdown`.

## Showcase
- `EditDialog`, `ProgressDialog` and the dropdown lists switch to `Popup` with `Shadow = true`. Each dialog's
  `Render` loses its Clear/Block/Inner block.
- A click outside a dialog's `Frame` closes the progress dialog. It's ignored in the edit dialog, so unsaved
  input isn't lost.

## Files
`Widgets/Popup.cs` (new), `Rect.cs`, `Widgets/Dropdown.cs`, Showcase `EditDialog.cs`/`ProgressDialog.cs`,
`PublicAPI.Unshipped.txt`, README widget list, CHANGELOG.

## Tests
- `Inner`/`Frame`/`Outer` with and without borders, padding and shadow, where `Outer` → `Inner` gives back the content size.
- Shadow keeps the glyphs and patches the style; the shadow is clipped at the right and bottom edges; a wide glyph cut by the shadow edge.
- Tiny areas (0×0, 1×1, 3×2) don't throw and draw nothing outside the area.
- `PlaceNear`: fits below, flips above, neither side fits (cut to the bigger side), pushed left at the right edge, point anchor, bigger than the bounds.
- `Dropdown` tests are unchanged. An `AllocationTests` frame with a shadowed popup over a list allocates 0 B.

## Non-goals
Modal input routing or a stack of popups: the app decides which overlay gets events, as it does today.
