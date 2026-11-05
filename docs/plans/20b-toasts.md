# 20b · Toasts

**Type:** missing · **Effort:** S–M · **Priority:** 2 · **Depends on:** — · **Used by:** —

## Problem
Every app writes its own "saved"/"copied"/"failed" message: the Showcase keeps a `_flash` string and an `Alarm`
to hide it after 3 s (`ShowcaseApp.cs`), and only one message can show at a time. Background results posted
with `Terminal.Post` have nowhere standard to appear.

## Design
```csharp
public enum ToastKind : byte { Info, Success, Warning, Error }

/// <summary>App-owned, fixed-capacity queue of messages that expire. Create once; it doesn't grow.</summary>
public sealed class Toasts
{
    public Toasts(int capacity = 4);
    public void Show(string message, ToastKind kind, long nowMs, int durationMs = 3000);  // the oldest is dropped when full
    public bool Update(long nowMs);          // drops expired toasts; true if any were dropped (redraw)
    public long DueMs { get; }               // the next expiry, for IApp.NextDueMs (combine with Math.Min)
    public int Count { get; }
    public void Dismiss(int index); public void Clear();
    public bool HandleMouse(MouseEvent ev);  // a click on a toast dismisses it
}

public readonly ref struct ToastStack
{
    public ToastStack(Toasts toasts);
    public Corner Corner { get; init; } = Corner.BottomRight;
    public int MaxWidth { get; init; } = 40;
    public Style InfoStyle, SuccessStyle, WarningStyle, ErrorStyle { get; init; }   // border and icon
    public bool Shadow { get; init; } = true;
    public void Render(Rect area, CellBuffer buffer);   // newest nearest the corner; long messages wrap to 3 rows
}
```
- **Expiry is the `Alarm` pattern**, built in: `Update(nowMs)` in `Handle`, `DueMs` from `NextDueMs`, so
  `Terminal.Run` wakes exactly when a toast should disappear. There's no timer or thread.
- **A class, created once, with a fixed-size array** of (message, kind, due) entries: `Show` stores a reference
  to the app's string and never allocates after construction. Rendering wraps with `Paragraph`'s line enumerator
  and draws with `Popup`, so it's 0 B per frame.
- **Errors can be sticky:** `durationMs: Timeout.Infinite` keeps a toast until it's clicked or dismissed.
- Toasts draw over everything, so the app renders the stack last, after any dialogs. `HandleMouse` returns
  true when it consumed the click, so the click doesn't reach the widget underneath.

## Showcase
`_flash`/`_flashAlarm` are replaced by a `Toasts`. Saving shows a Success toast, copying shows an Info toast,
and the progress dialog's simulated job ends with a Success or an Error toast (sticky) posted from its
background thread through `Terminal.Post`.

## Files
`Widgets/Toasts.cs` (new; `Corner` enum next to `Rect.PlaceNear` if it doesn't fit elsewhere), Showcase
`ShowcaseApp.cs`/`ProgressDialog.cs`, `PublicAPI.Unshipped.txt`, `docs/widgets.md`, `docs/input.md` (the loop
pattern), README, CHANGELOG.

## Tests
- Queue: order, capacity drops the oldest, `Update` drops exactly the expired ones, `DueMs` is the earliest
  expiry or `long.MaxValue`, and sticky toasts never expire.
- Render: each corner, stacking, wrapping and the 3-row cap with an ellipsis, kind styles, a stack taller than
  the area drops the oldest.
- `TestTerminal` with a test clock: a toast shown at t=0 is gone at 3000 ms, and the loop's read timeout ends at
  its expiry.
- Mouse: a click dismisses the toast and is consumed; a click beside the toast is not.
- Showcase: save shows "saved · <name>" and it disappears after 3 s. Snapshot updated.
- `AllocationTests`: `Show` after construction and frames with 4 toasts allocate 0 B.

## Non-goals
Action buttons inside toasts, progress inside toasts, and animation (slide-in).
