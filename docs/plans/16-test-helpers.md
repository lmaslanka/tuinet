# 16 · Test helpers for library users

**Type:** nice to have · **Effort:** S · **Priority:** 6

## Problem
`CellBuffer.ToString()` gives plain-text snapshots, but apps can't easily assert styles, and every test
project re-implements key injection and rendering helpers (see the Showcase tests).

## Design (in `Tuinet.Testing`)
- `buffer.ToStyledString()`: text plus a compact style legend (`[fg=#F5A623 b]ITEMS[/]`), stable for
  snapshot files; `buffer.ToAnsi()` for eyeballing in a terminal.
- `Snapshot.Verify(buffer, name)`: compare to `__snapshots__/name.txt`, write `.received.txt` on mismatch,
  env var to accept.
- `TestTerminal` = `Terminal` + `TestTty` + `Press("jj")`, `Key(KeyCode.Enter)`, `Mouse(...)`, `Render()`.
- Assertions: `buffer.FindText("Save")` → position; `buffer.StyleAt(x, y)`.

## Files
`src/Tuinet/Testing/*.cs`, Showcase tests migrated, README testing section.

## Tests
Round-trip of the styled format, snapshot accept/compare flow, `TestTerminal` key and mouse injection.
