# Testing

The `Tuinet.Testing` namespace ships in the library: an in-memory tty, a driver for whole apps, styled snapshots
and screen assertions.

## Rendering into a buffer

UI code is just a function from state to cells, so you can test it without a terminal. Render
into a `CellBuffer` and assert on its text and styles:

```csharp
[Fact]
public void Shows_the_title()
{
    var buffer = new CellBuffer(80, 24);
    new ShowcaseApp().Render(buffer, nowMs: 0);
    Assert.Contains("ITEMS · 20", buffer.ToString());
    Assert.Equal(Color.Hex(0xF5A623), buffer[2, 4].Style.Fg);   // the list's amber border
}
```

## Driving an app

To drive a whole app, `Tuinet.Testing.TestTerminal` runs an `IApp` the way `Terminal.Run` does: a real
`Terminal` over the in-memory `TestTty`, a frame after each input, and a clock the test sets. Calls chain,
and `Click("Save")` clicks wherever that text is on screen:

```csharp
[Fact]
public void Enter_edits_the_selected_item()
{
    using var term = new TestTerminal(new ShowcaseApp(), 110, 34);
    term.Press("jj").Key(KeyCode.Enter);
    Assert.Contains("EDIT ITEM · 03", term.Text);
    term.Click("Close");
    Assert.DoesNotContain("EDIT ITEM", term.Text);
    Assert.Equal(Color.Hex(0xF5A623), term.Screen.StyleOf("ITEMS · 20").Fg);
}
```

| `TestTerminal` | Does |
|---|---|
| `Press("jj")`, `Press('s', Modifiers.Ctrl)`, `Key(KeyCode.Tab, Modifiers.Shift)` | Key presses, a frame after each (`\n` is Enter, `\t` Tab, `\u001b` Esc) |
| `Click(x, y)`, `Click("Save")`, `Wheel(x, y, notches)`, `Mouse(kind, x, y, button)` | Mouse reports |
| `Paste(text)`, `Resize(w, h)`, `Post(message)` | Paste, resize and message events |
| `Input("\u001b[1;5C")` | Raw bytes through the terminal's input parser |
| `NowMs`, `Advance(ms)` | The clock; `Advance` sends a tick only when `Run` would wake (animating, or `NextDueMs` due) |
| `Screen`, `Text`, `IsRunning`, `Terminal`, `Tty` | The last frame (a copy), its text, whether `Handle` returned false, and frame and byte counters |

## Assertions and snapshots

On a `CellBuffer` (`using Tuinet.Testing;`): `FindText("Save")` returns the cells the text covers
(and shows the screen when it isn't there), `StyleAt(x, y)` and `StyleOf("Save")` read styles,
`ToAnsi()` prints a frame in color in your terminal, and `ToStyledString()` writes it as
[markup](layout.md#mixed-style-text), one line per row:

```
[fg=#E2E8F0 bg=#0D1117]  [fg=#F5A623]│[/]   [b]parse & validate intent[/]   [/]
```

`Snapshot.Verify(buffer, "edit-form")` compares that with `__snapshots__/edit-form.txt` next to the
test file (`styled: false` compares the plain text). Trailing spaces and blank lines don't count. A
missing or different snapshot fails the test and writes `edit-form.received.txt` beside it. To accept
the new output, rename that file, or run `TUINET_ACCEPT_SNAPSHOTS=1 dotnet test`, then review the diff.
`Snapshot.Parse` reads a snapshot back into a `CellBuffer`.

The snapshot folder is found from the test's source path. CI builds that map source paths
(`ContinuousIntegrationBuild`, which turns `/home/you/repo/tests/…` into `/_/tests/…`) are handled by
looking for the mapped folder above the test assembly; pass `directory:` to put snapshots elsewhere.
Add `*.received.txt` to your `.gitignore`.

A snapshot is plain [markup](layout.md#mixed-style-text). When most of a row has one style with a background
(a themed panel), that style opens the row and the tags inside it hold only what differs, so a dark theme
doesn't repeat its background on every run. Hyperlinks and the cursor aren't part of the snapshot; assert
those with `buffer[x, y].Link` and `buffer.CursorX`/`CursorY`.

The allocation guarantee is easy to test in your own app too: render into one `CellBuffer` in a loop and
compare `GC.GetAllocatedBytesForCurrentThread()` before and after. Call the app directly for that, not through
`TestTerminal`, which copies every frame.

## Bytes on the wire

To test the bytes themselves, use a `Terminal` over a `TestTty` directly:

```csharp
[Fact]
public void Key_in_frame_out()
{
    var tty = new TestTty(80, 24);
    using var term = new Terminal(tty);
    tty.Enqueue("j");

    Assert.True(term.Poll(out Event ev, 0));
    Assert.True(ev.Key.IsChar('j'));

    term.BeginFrame().SetString(0, 0, "moved");
    term.Present();
    Assert.Contains("moved", tty.WrittenText);
    Assert.Equal(1, tty.WriteCount);
}
```

---

[Getting started](getting-started.md) · [Layout and styling](layout.md) · [Widgets](widgets.md) ·
[Input and the terminal](input.md) · [Testing](testing.md) · [Performance](performance.md) · [API reference](api/index.md)
