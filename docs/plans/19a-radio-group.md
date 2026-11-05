# 19a · Radio group

**Type:** missing · **Effort:** S · **Priority:** 2 · **Depends on:** — · **Used by:** 19b, 19c (same focus/mouse pattern)

## Problem
A form can't offer "exactly one of a few options, all visible". `Dropdown` hides the choices behind a popup,
and a row of `Checkbox`es doesn't enforce that only one is chosen. Business forms need both: radio buttons for
2–5 choices (priority, environment, yes/no/ask), dropdowns for long lists.

## Design
```csharp
public struct RadioState
{
    public RadioState(int selected = 0);
    public int Selected { get; set; }
    public Rect Area { get; private set; }              // last render, for hit-testing
    public bool Handle(KeyEvent key, int count);        // arrows along the group's direction, Home/End, a digit 1–9
    public bool HandleMouse(MouseEvent ev, int count);  // a click on an option selects it
}

public readonly ref struct RadioGroup : IStatefulWidget<RadioState>
{
    public RadioGroup(ReadOnlySpan<string> options);
    public Direction Direction { get; init; } = Direction.Vertical;
    public int Spacing { get; init; } = 2;               // horizontal: cells between options
    public bool Focused { get; init; }
    public Style Style { get; init; }
    public Style FocusedStyle { get; init; }             // layered over the selected option when focused
    public Style SelectedStyle { get; init; }            // layered over the selected option's symbol
    public ReadOnlySpan<char> SelectedSymbol { get; init; } = "(•)";
    public ReadOnlySpan<char> UnselectedSymbol { get; init; } = "( )";

    public int OptionAt(int x, int y, in RadioState state);   // -1, or an option index
}
```
- **Arrows change the selection**, as native radio groups do (no separate "focused option" to confirm with
  Space). Up/Down for a vertical group, Left/Right (and h/l, j/k) for a horizontal one; they don't wrap, so
  arrowing past the end leaves the key for the app's `FocusRing`.
- **Hit-testing without stored rects:** as in `Tabs.TabAt`, the option positions are recomputed from the
  labels, `state.Area`, `Direction` and `Spacing`.
- Labels are measured with `TextWidth` and cut with `Overflow.Ellipsis` like `Checkbox`. A horizontal group
  that doesn't fit drops trailing options rather than cutting a label mid-word; `OptionAt` agrees.
- Matches `Checkbox`'s style properties so the two sit side by side in a form without restyling.

## Showcase
The edit dialog's **priority** field (low/medium/high/critical) becomes a horizontal `RadioGroup` in the
`FocusRing`; **kind** stays a `Dropdown`, so the dialog shows when to pick which.

## Files
`Widgets/RadioGroup.cs` (new), Showcase `EditDialog.cs`, `PublicAPI.Unshipped.txt`, `docs/widgets.md`, README, CHANGELOG.

## Tests
- Vertical and horizontal layout; symbols and styles on the selected option only; focused style.
- Keys per direction, no wrap at the ends (returns false), digits, Home/End; count 0 leaves `Selected` −1.
- `OptionAt`/`HandleMouse` on labels, symbols and the gaps between options; a horizontal group too narrow for
  all options.
- Snapshot of the edit dialog; Showcase test: selecting "high" with the mouse and saving updates the item.
- `AllocationTests`: a frame with a radio group allocates 0 B.

## Non-goals
Disabled options and option descriptions (a second line). Add them when a real form asks.
