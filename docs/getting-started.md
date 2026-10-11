# Getting started

**Requirements:** the .NET 10 SDK and a terminal with VT support. That covers essentially every modern terminal on Linux and macOS, plus Windows Terminal and conhost on Windows 10+.

TUI.NET isn't on NuGet yet. Reference the project directly, or pack it locally (the package and namespace are `Tuinet`):

```sh
git clone https://github.com/lmaslanka/tuinet.git
dotnet pack tuinet/src/Tuinet -c Release -o ./packages
dotnet add package Tuinet --source ./packages
```

A complete program:

```csharp
using Tuinet;
using Tuinet.Widgets;

using var term = Terminal.Open();
while (true)
{
    CellBuffer frame = term.BeginFrame();
    var block = new Block { Title = " hello ", BorderType = BorderType.Rounded };
    frame.Render(block, frame.Area);
    frame.SetString(2, 2, "Press q to quit", new Style(Color.Cyan, default, Attr.Bold));
    term.Present();

    if (term.Poll(out Event ev, Timeout.Infinite) && ev.Key.IsChar('q'))
    {
        break;
    }
}
```

## How it works

```
  Poll ──▶ Handle ──▶ BeginFrame ──▶ Render ──▶ Present
   ▲      (update       (cleared       (draw the     (diff vs screen,
   │       your state)   back buffer)   whole UI)     one write, swap)
   └──────────────────────────────────────────────────────┘
```

Your app owns its state. Each frame it renders that state into a `CellBuffer`, the same way a game
engine draws a frame. `Present()` compares the new buffer with what's on screen, encodes the
difference as VT bytes, and sends it in a single `write`, wrapped in synchronized-update markers so
the terminal never shows half a frame.

The recommended loop handles every input event that's already waiting before it draws. That way a
burst of key repeats costs one frame instead of dozens, and the UI never builds a backlog:

```csharp
bool running = true;
while (running)
{
    Render(term.BeginFrame());   // draw everything, every frame
    term.Present();              // diff + one write

    if (!term.Poll(out Event ev, Timeout.Infinite))
    {
        continue;
    }

    do
    {
        running = Handle(ev);
    }
    while (running && term.Poll(out ev, 0));   // drain the burst, then draw once
}
```

`term.Run(app)` is this loop, written once. Your app implements `IApp`: `Handle` returns false to quit, and
`Render` draws the whole UI. While `IsAnimating` returns true, the loop draws about 30 frames a second, with an
`EventKind.Tick` before each frame that no input caused. `NextDueMs` wakes an idle app at a given time. The
`nowMs` arguments are milliseconds since `Run` started. `Run` allocates nothing per frame, and you can still
write your own loop when you need something else.

```csharp
using var term = Terminal.Open();
term.Run(new Counter());

sealed class Counter : IApp
{
    private int _count;

    public bool Handle(Event ev, long nowMs)
    {
        if (ev.Key.IsChar('+')) _count++;
        return !ev.Key.IsChar('q');
    }

    public void Render(CellBuffer frame, long nowMs)
    {
        Span<char> text = stackalloc char[40];
        text.TryWrite($"count: {_count}  (+ adds, q quits)", out int length);   // no string per frame
        frame.SetString(0, 0, text[..length], default);
    }
}
```

In inline mode, `Run` draws the last frame again after the app stops, since that frame stays on screen.
`Terminal.LastFrameBytes` and `LastFrameTime` (from `BeginFrame` to the end of `Present`) give the cost of the
last frame, for a stats overlay.

## Samples

```sh
./run           # the showcase: list, stats charts, menu, palette, edit form, progress dialog
./run gallery   # every widget, one page each, next to the code that draws it
./run inline    # inline mode: a download with live progress bars under the prompt
./run stress    # latency harness: hold j on 100k items, or press n for full-screen noise
```

The samples are small and meant to be read: [`samples/`](https://github.com/lmaslanka/tuinet/tree/main/samples).

## Next steps

- [Layout and styling](layout.md): split the screen, colors, attributes, mixed-style text, your own widgets.
- [Widgets](widgets.md): lists, tables, trees, forms, dialogs, tabs, menus, charts.
- [Input and the terminal](input.md): keys, mouse, paste, background work, clipboard, inline mode.
- [Testing](testing.md): test apps without a terminal, snapshot frames.
- [Performance](performance.md): what a frame costs, and how it is kept that way.

---

[Getting started](getting-started.md) · [Layout and styling](layout.md) · [Widgets](widgets.md) ·
[Input and the terminal](input.md) · [Testing](testing.md) · [Performance](performance.md) · [API reference](api/index.md)
