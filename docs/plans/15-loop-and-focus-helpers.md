# 15 · Optional main-loop and focus helpers

**Type:** nice to have · **Effort:** S · **Priority:** 6

## Problem
Every sample rewrites the same loop (poll, coalesce bursts, render, animate on a timer). Forms route focus
by hand: `EditDialog` spends much of its 313 lines on focus across 9 fields. The library deliberately has
no focus manager or retained tree, and these helpers must not add one.

## Design
- `Terminal.Run(IApp app)` or a static helper: `interface IApp { bool Handle(Event ev, long nowMs);
  void Render(CellBuffer frame, long nowMs); bool IsAnimating(long nowMs); }` — poll with infinite timeout
  when idle, frame-rate timeout while animating, coalesce queued events, one `Present` per batch. Exactly
  the loop the Showcase already uses, in one place; apps can keep writing their own.
- `FocusRing` struct: `Count`, `Current`, `Next()/Previous()`, `Handle(KeyEvent)` for Tab/Shift+Tab,
  `bool Is(int index)`. Pure value type; widgets keep taking `Focused = ring.Is(i)`.
- Optional `Timers`: tiny struct for "fire at nowMs" checks to drive animations without allocations.

## Files
`src/Tuinet/App.cs` (new), `src/Tuinet/FocusRing.cs` (new), Showcase/Stress refactored to use them, README.

## Tests
Loop helper with `TestTty` (idle blocks, animation ticks, coalescing), FocusRing wrap-around, Showcase tests unchanged.

## Status: implemented

- `Terminal.Run(IApp app, int frameMs = 33)` is a method on `Terminal` (in `App.cs`, as a partial class).
  `IApp` has `Handle` and `Render`, plus `IsAnimating` and `NextDueMs`, which default to "never". `nowMs` is
  milliseconds since `Run` started. While animating, frames are paced from the start of the last frame, not from
  the last event. `frameMs: 0` draws as fast as the terminal takes them (Stress's noise mode).
- `EventKind.Tick` (`Event.Tick`) is new: `Run` sends it when it wakes with no input, for an animation frame or
  `NextDueMs`. This lets apps that end on their own stop from `Handle`; the Inline download returns false once it
  finishes. `Poll` never returns it.
- In inline mode, `Run` draws one more frame after `Handle` returns false, since the band stays on screen (the
  Inline sample's summary line). On the alternate screen it doesn't.
- "Timers" became `Alarm`: `Timer` would clash with `System.Threading.Timer` under implicit usings. It's one-shot
  (`Start`/`At`, `Fire`, `Cancel`, `DueMs` is `long.MaxValue` when not set, so `Math.Min` combines alarms).
  Without `NextDueMs`, an idle loop would never wake for it. Repeating work is what `IsAnimating` is for.
- `FocusRing` also has `HandleMouse(mouse, areas)` (a click focuses the control drawn there), which replaced
  `EditDialog`'s `Array.FindIndex` (a delegate per click), and `Move(delta)`. `Handle` takes Tab and Shift+Tab
  presses only, not releases or Ctrl+Tab, and does nothing on an empty ring.
- `Terminal.LastFrameTime` (from `BeginFrame` to the end of `Present`) replaces the Stopwatch both samples kept
  around their frames. They record the previous frame at the start of `Render`, which is the same point in time.
- Showcase: `ShowcaseLoop` (in `Program.cs`) connects `ShowcaseApp` to the terminal (title, clipboard, frame
  stats), and `ShowcaseApp` itself stays terminal-free for tests. The Showcase demonstrates `Alarm` with its
  "saved"/"copied" message, which now hides itself after 3 s. Stress and Inline run on `Run` too.
- Tests: `AppTests` covers idle blocking, burst coalescing, stopping mid-burst, ticks at the frame rate and at 0 ms,
  alarm wake-up, resize, the inline final frame, `LastFrameTime`, `Alarm`, and 0 B over 1,900 looped frames.
  `FocusRingTests` covers wrap-around, keys, clicks and ranges. Two Showcase tests cover the message timeout, and
  e2e checks that the message disappears in a real terminal with no input.
