# 10 · Inline mode (no alternate screen)

**Type:** missing · **Effort:** M · **Priority:** 4

## Problem
`Terminal` always enters the alternate screen (`?1049h`). CLI tools want a live region drawn under the
prompt — progress bars, spinners, prompts, a live table — that stays in the scrollback afterwards.

## Design
- `TerminalOptions.Inline = new InlineOptions(height)`: no alternate screen; on start, print `height`
  newlines (scrolling the shell output up as needed), query the cursor position (`CSI 6n`, parsed by
  `VtParser`) or compute it from the newlines, and treat that band as the frame (rows offset).
- `Renderer` gets a row offset; scroll regions (already supported) work inside the band.
- Text printed above the live region: `Terminal.PrintAbove(ReadOnlySpan<char>)` scrolls the region down by
  inserting lines above it (IL inside margins) so logs and a progress bar coexist.
- On exit: leave the last frame in place, move the cursor below it, restore modes (no `?1049l`).
- Resize: width change reflows the band; height stays the requested one.

## Files
`Terminal.cs`, `TerminalOptions.cs`, `Internal/Renderer.cs` (row offset), `Internal/VtParser.cs` (CPR reply), a new
`samples/Tuinet.Samples.Inline` (download progress with logs above), README.

## Tests
Exact bytes for start/frame/exit in inline mode, `PrintAbove` with the fuzz emulator, CPR parsing, PTY e2e (plan 03).
