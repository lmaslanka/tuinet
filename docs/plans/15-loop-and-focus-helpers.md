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
