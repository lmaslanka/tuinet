# TUI kernel (not a framework)

C# rendering kernel that feels like Lazygit: key → cells on screen, no mush.

## Decisions

| Decision | Choice |
|---|---|
| Product | Rendering kernel, not widgets/layout/focus |
| Runtime | C# |
| Platforms v1 | Linux, macOS, Windows |
| Loop | Single-thread, blocking poll. No `Task` in the kernel. Background work posts messages in. |
| Paint API | Immediate frame: app fills a `Cell` buffer every tick; kernel diffs vs last frame |
| Cell | `Rune` + style. Width 1 or 2 via East Asian Width. Continuation cell for wide chars. No grapheme clusters in v1. |
| First ship | Latency harness: hold `j`, counter/cursor moves 1:1 with key repeat |

Paint **on event** (key, resize, posted message). Not a 60fps timer. Timeout exists only for later animation/wakeups.

## Where to start

Do **not** start with widgets, layout, or `System.Console`. Start at the tty and stop when one key equals one frame.

```
Harness  →  Loop  →  Buffer/Diff  →  VT encode/parse  →  Unix termios | Windows VT
```

## Build order

1. **Teardown first** — alt screen, raw mode, restore cursor/mode on `finally`, `SIGTERM`, and crash. If this is wrong, every run bricks the shell.
2. **Platform I/O** — two thin backends, **one** VT dialect.
   - Unix: `termios` raw, `write(1)`, `poll`/`read`, `TIOCGWINSZ`, `SIGWINCH`
   - Windows: `ENABLE_VIRTUAL_TERMINAL_PROCESSING` + `ENABLE_VIRTUAL_TERMINAL_INPUT`, `ReadFile`/`WriteFile` on the console handles — **not** `ReadConsoleInput`, **not** `Console.*`
3. **`struct Cell` + two buffers** — `Rune`, width 1|2 (East Asian Width), continuation cell for wide chars, style (store truecolor RGB; emit `38;2`/`48;2`).
4. **Diff** — walk dirty spans per row, track last SGR/cursor, emit UTF-8 bytes. Wrap frames in synchronized output (`CSI ? 2026`). Zero strings/LINQ/allocs on this path.
5. **VT input parser** — bytes → `Key` (chars, arrows, ctrl). `Ctrl-C` is a key; the loop decides to quit and restore.
6. **Loop** — block/poll → update harness state → fill back buffer → diff → write.
7. **Harness** — hold `j`: counter/cursor moves 1:1 with key repeat, no backlog. That’s the kernel.

## Repo shape

```
src/Tuinet.Kernel/   Cell, Buffer, Diff, Loop, Vt, Platform/{Unix,Windows}
src/Tuinet.Harness/  Program.cs only
```

## Success

- Keydown → tty bytes with no `Console.Write` / `Console.ReadKey`
- Teardown always restores the terminal
- Windows and Unix share encoder + parser

## Anti-goals (not v1)

Widgets, focus, layout, mouse, Kitty keyboard protocol, grapheme clusters, async UI.

A file list with `hjkl` is the first *app* **on top of** this, not instead of it.
