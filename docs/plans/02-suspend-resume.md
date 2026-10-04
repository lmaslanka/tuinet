# 02 · Suspend and resume (Ctrl+Z)

**Type:** hole · **Effort:** S · **Priority:** 4

## Problem
Raw mode turns `ISIG` off (`Platform/UnixTty.cs`), so Ctrl+Z arrives as a key and the app can't be
backgrounded. There is no API to leave the screen, stop, and come back with a full repaint.

## Design
- `Terminal.Suspend()` (Unix; no-op on Windows): write the leave sequence (same bytes as `Dispose`'s restore),
  restore termios, `raise(SIGTSTP)` via `LibC`. When the process continues (`raise` returns after SIGCONT),
  re-apply raw mode, re-send the enter sequence, `Invalidate()` so the next `Present` repaints everything,
  and queue a `Resize` event in case the window changed while stopped.
- Apps opt in: `if (key.IsCtrl('z')) terminal.Suspend();`. Optionally `TerminalOptions.SuspendOnCtrlZ`
  to do it inside `Poll` (default off, so Ctrl+Z stays a normal key unless asked).
- Also handle an external `SIGTSTP`/`SIGCONT` (e.g. `kill -TSTP`): register `PosixSignalRegistration`
  for SIGCONT to re-enter and invalidate.

## Files
`src/Tuinet/Terminal.cs`, `src/Tuinet/Platform/UnixTty.cs`, `src/Tuinet/Platform/LibC.cs` (`raise`),
`src/Tuinet/TerminalOptions.cs`, `src/Tuinet/ITty.cs` (a `Suspend` hook so `TestTty` can record it), README.

## Tests
- `TestTty` records suspend/resume; `TerminalTests`: suspend writes the leave bytes, resume writes enter
  bytes and the next frame is a full repaint.
- `UnixTermiosTests`: termios restored before stop and raw again after.
- Manual/e2e (plan 03): run a sample in tmux, send `C-z`, check the shell prompt, `fg`, check the screen.
