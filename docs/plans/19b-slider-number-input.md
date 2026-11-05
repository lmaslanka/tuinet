# 19b · Slider and number input

**Type:** missing · **Effort:** M · **Priority:** 3 · **Depends on:** — · **Used by:** —

## Problem
Numbers are typed into a plain `TextInput` and parsed by the app, with no range, no stepping and no feedback
while typing. There's no way to drag a value along a range (a percentage, a limit, a volume).

## Design
```csharp
public struct SliderState
{
    public SliderState(int value, int min, int max, int step = 1);
    public int Value { get; set; }                       // always clamped to [Min, Max]
    public int Min { get; } public int Max { get; }
    public int Step { get; } public int LargeStep { get; init; }   // default (Max − Min) / 10
    public Rect Area { get; private set; }
    public bool Handle(KeyEvent key);        // ←/→, h/l: Step; PgUp/PgDn: LargeStep; Home/End: Min/Max
    public bool HandleMouse(MouseEvent ev);  // click or drag on the track sets the value by position
}

public readonly ref struct Slider : IStatefulWidget<SliderState>
{
    public bool Focused { get; init; }
    public Style TrackStyle { get; init; }  public Style FillStyle { get; init; }
    public Style ThumbStyle { get; init; }  public Style FocusedStyle { get; init; }
    public bool ShowValue { get; init; }     // the value right of the track, formatted on the stack
    public ReadOnlySpan<char> Suffix { get; init; }   // e.g. "%"
}

public struct NumberInputState
{
    public NumberInputState(int value, int min = int.MinValue, int max = int.MaxValue, int step = 1);
    public int Value { get; }                // the last valid value
    public bool IsValid { get; }             // false while the text doesn't parse or is out of range
    public bool Handle(KeyEvent key);        // digits and '-' edit; ↑/↓ step; anything else is refused
    public bool HandleMouse(MouseEvent ev);  // caret placement, wheel steps
    public void Commit();                    // on blur: text reset to Value if invalid
}

public readonly ref struct NumberInput : IStatefulWidget<NumberInputState>
{
    public bool Focused { get; init; }
    public Style Style { get; init; } public Style FocusedStyle { get; init; }
    public Style InvalidStyle { get; init; }  // layered over the text while !IsValid
    public Alignment Alignment { get; init; } = Alignment.Right;
}
```
- **Slider fill uses `Eighths.Left`** like `ProgressBar`, so a 20-cell track has 160 positions. The thumb is a
  one-cell glyph over the fill; a click maps x to a value rounded to `Step`. A drag keeps following the pointer
  even outside the track (`MouseKind.Drag` until release), as `ListState`'s scrollbar drag does.
- **NumberInput is a `TextInputState` underneath** (`EditableText`, so selection, undo and paste work). It
  filters keys before editing and parses with `int.TryParse(ReadOnlySpan<char>)`, so nothing is allocated per
  keystroke or frame. Invalid text stays editable and shows `InvalidStyle`; `Value` keeps the last good number.
- `int` only for now. A `decimal` variant would add format and culture questions, and nothing needs it yet.

## Showcase
`Item` gains **progress** (0–100 %) and **estimate** (hours, 0–999). The edit dialog gets a `Slider` with
`ShowValue` and `Suffix = "%"`, and a `NumberInput`, both in the `FocusRing`. The details panel shows both, and
the progress also appears as a `ProgressBar`.

## Files
`Widgets/Slider.cs`, `Widgets/NumberInput.cs` (new), Showcase `Item.cs`/`EditDialog.cs`/`ShowcaseApp.cs`,
`PublicAPI.Unshipped.txt`, `docs/widgets.md`, README, CHANGELOG.

## Tests
- Slider: fill at 0, 50, 100 % and eighths in between; keys and clamping; click and drag map positions to
  values (including dragging past the ends); `ShowValue` formatting; a min > max range is rejected.
- NumberInput: letters refused, '-' only at the start and only when Min < 0, ↑/↓ steps clamp, out-of-range text
  is invalid until `Commit`, paste of "12abc" is refused whole, undo restores.
- Showcase: drag the slider and type an estimate, save, and the details panel shows both.
- `AllocationTests`: frames with a slider and a number input, and typing into the number input, allocate 0 B.

## Non-goals
Vertical sliders, two-thumb range sliders, decimal values.
