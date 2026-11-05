# 19c · Date input and calendar

**Type:** missing · **Effort:** M–L · **Priority:** 4 · **Depends on:** 19b (validation pattern) · **Used by:** —

## Problem
Dates (due dates, report ranges, schedules) are common in business forms and have to be typed as free text
and parsed by the app, with no picker and no validation.

## Design
```csharp
public struct CalendarState
{
    public CalendarState(DateOnly selected);
    public DateOnly Selected { get; set; }   // setting it moves Month to show it
    public DateOnly Month { get; set; }      // the month on screen (day ignored)
    public Rect Area { get; private set; }
    public bool Handle(KeyEvent key);        // ←/→ day, ↑/↓ week, PgUp/PgDn month, Shift+PgUp/PgDn year, Home/End month start/end
    public bool HandleMouse(MouseEvent ev);  // click a day; click ‹ › in the header; wheel changes month
}

public readonly ref struct Calendar : IStatefulWidget<CalendarState>
{
    public DayOfWeek FirstDayOfWeek { get; init; } = DayOfWeek.Monday;
    public DateOnly Today { get; init; }                 // highlighted if set; the app passes it (testable, no clock)
    public DateOnly MinDate { get; init; } public DateOnly MaxDate { get; init; }   // days outside are dimmed, unselectable
    public DateTimeFormatInfo Format { get; init; }      // month and day names; default InvariantInfo
    public Style Style, HeaderStyle, SelectedStyle, TodayStyle, OutsideStyle { get; init; }
    public static Size Size { get; }                     // 20 × 8: header, weekdays, 6 weeks
}

public struct DateInputState
{
    public DateInputState(DateOnly? value);
    public DateOnly? Value { get; }
    public bool IsOpen { get; }
    public bool IsValid { get; }
    public bool Handle(KeyEvent key);        // typing edits the text; Alt+↓ or Enter on a valid date opens the calendar;
                                             // in the calendar, Enter picks and Esc cancels
    public bool HandleMouse(MouseEvent ev);  // click on the field's ▾ opens; a click outside the popup closes
}

public readonly ref struct DateInput : IStatefulWidget<DateInputState>
{
    public string Pattern { get; init; } = "yyyy-MM-dd";   // parse and display
    public bool Focused { get; init; }
    public Style Style, FocusedStyle, InvalidStyle { get; init; }
    public bool PopupShadow { get; init; }
    // + the Calendar's options, passed through to the popup
}
```
- **The calendar is usable on its own** (a sidebar date filter) and as `DateInput`'s popup, placed with
  `Rect.PlaceNear` and drawn with `Popup`, exactly like `Dropdown`.
- **Allocation-free frames:** day numbers come from a static table of "1"…"31", and month and day names are read
  from `Format` once and cached per `DateTimeFormatInfo` (its array properties clone on every call). Typed text
  is parsed with `DateOnly.TryParseExact(ReadOnlySpan<char>, …)`.
- **No clock inside the library:** `Today` is a property, so tests and snapshots are deterministic.
- `DateInputState` is built on `TextInputState`, like `NumberInputState` (19b): invalid text shows
  `InvalidStyle`, and `Value` keeps the last valid date.

## Showcase
`Item` gains an optional **due** date. The edit dialog gets a `DateInput`. The table gets a **due** column that
is dropped first on narrow screens, with overdue dates in the theme's red. The details panel shows a small
`Calendar` of the selected item's due month.

## Files
`Widgets/Calendar.cs`, `Widgets/DateInput.cs` (new), Showcase `Item.cs`/`EditDialog.cs`/`ShowcaseApp.cs`,
`PublicAPI.Unshipped.txt`, `docs/widgets.md`, README, CHANGELOG.

## Tests
- Calendar grid for a month starting on each weekday, with Monday and Sunday first; leap February; 6-week
  months; selected, today and outside styles; Min/Max dimming and refusal; a non-invariant culture's names.
- Keys across month and year boundaries (Jan 31 + 1 month → Feb 28/29); clicks on days, arrows and wheel.
- DateInput: typed text valid and invalid, open/pick/cancel, a click outside closes, popup placed above when
  there's no room below.
- Showcase snapshot with the date column; set a due date with the picker and save.
- `AllocationTests`: a frame with an open DateInput allocates 0 B.

## Non-goals
Date ranges, times of day, and time zones. Add them as a separate plan if a real form asks.
