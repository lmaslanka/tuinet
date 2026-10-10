<div align="center">

# ▐█▌ TUI.NET

**Immediate-mode terminal UIs for .NET, built for speed.**

Redraw everything every frame and let TUI.NET send only what changed.<br>
Zero allocations per frame · one `write` per frame · Native AOT

[![CI](https://github.com/lmaslanka/tuinet/actions/workflows/ci.yml/badge.svg)](https://github.com/lmaslanka/tuinet/actions/workflows/ci.yml)
![.NET 10](https://img.shields.io/badge/.NET-10-512BD4?logo=dotnet&logoColor=white)
![Native AOT](https://img.shields.io/badge/Native_AOT-ready-34D399)
![Allocations](https://img.shields.io/badge/allocations-0_B%2Fframe-38BDF8)
![License](https://img.shields.io/badge/license-Apache--2.0-F5A623)

<img src="https://raw.githubusercontent.com/lmaslanka/tuinet/main/docs/images/showcase-main.png" width="860" alt="TUI.NET showcase: a themed list with a details panel">

</div>

---

## ✨ Highlights

- **Immediate mode.** There is no widget tree to keep in sync. Each frame, your app draws its state into a cell buffer. TUI.NET diffs that buffer against the screen and writes the difference.
- **Fast by design.**
  - Unchanged rows are skipped with a vectorized memcmp.
  - Gaps inside a row are jumped with relative cursor moves.
  - Runs of blanks are erased (to the end of the row, or n cells) instead of written as spaces.
  - When a band of rows scrolls, the terminal moves it (scroll margins + insert/delete line) and only the new rows are painted.
    On terminals with left/right margins (DECLRMM) this works for a band narrower than the screen too, such as a list beside a panel.
  - Style changes are sent as minimal deltas.
  - Each frame goes out in a single synchronized write.
- **Zero allocations.** Steady-state rendering and input polling allocate nothing. Tests enforce this, so there are no GC pauses between a key press and the frame it produces.
- **Batteries included.**
  - Widgets: blocks, paragraphs, virtualized lists, tables and trees, scrollbars, text inputs, multi-line text areas, dropdowns, checkboxes, buttons, progress bars and spinners.
  - A constraint layout.
  - Truecolor with automatic fallback to 256 or 16 colors.
  - Inline mode: a live band under the shell prompt (progress, spinners, prompts) with logs printed above
    it, instead of the alternate screen.
  - Window title, clipboard copy over SSH (OSC 52), cursor shapes and clickable hyperlinks (OSC 8).
  - An optional main loop (`Terminal.Run`) that sleeps when idle and coalesces input bursts, plus `FocusRing`
    for Tab-order focus and `Alarm` for timed changes. All of them are plain values, with no widget tree.
- **Full input.**
  - Keys with Ctrl/Alt/Shift, F1–F12, PgUp/PgDn.
  - Opt-in kitty keyboard protocol: Ctrl+I apart from Tab, Esc with no delay, key releases.
  - Mouse (SGR 1006), bracketed paste and focus events.
  - Thread-safe `Post` for background results.
- **Safe by default.**
  - Control characters in your text can never reach the terminal as escape sequences.
  - The terminal is restored on exit, on an unhandled exception, and on SIGINT/SIGTERM/SIGHUP.
  - Ctrl+Z suspends and `fg` resumes with a full repaint (opt-in, Unix).
  - Wide CJK and emoji glyphs are handled from Unicode 17 width tables.
  - Grapheme clusters are one cell: accented letters written with combining marks, Indic and Thai
    syllables, emoji sequences (👨‍👩‍👧, 👍🏽, ❤️) and flags (🇵🇱). Rows stay aligned on terminals with
    or without grapheme support.
- **Native AOT.** No reflection. The stress sample publishes to a 1.3 MB native binary.

<table>
  <tr>
    <td><img src="https://raw.githubusercontent.com/lmaslanka/tuinet/main/docs/images/showcase-edit.png" alt="Edit dialog with text inputs, dropdown and checkboxes"></td>
    <td><img src="https://raw.githubusercontent.com/lmaslanka/tuinet/main/docs/images/showcase-progress.png" alt="Progress dialog with animated bars and spinners"></td>
  </tr>
  <tr>
    <td align="center"><sub>Text inputs, dropdowns, checkboxes and buttons</sub></td>
    <td align="center"><sub>Progress bars, segmented meters and spinners</sub></td>
  </tr>
</table>

## 🚀 Quick start

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

To see what the library can do, run the samples:

```sh
./run           # the showcase in the screenshots: list, stats charts, menu, palette, edit form, progress dialog
./run inline    # inline mode: a download with live progress bars under the prompt
./run stress    # latency harness: hold j on 100k items, or press n for full-screen noise
```

## 🧠 How it works

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

## 📚 Guide

### Layout

`Layout` splits a `Rect` along one axis. The constraints are a collection expression and the results
go into a `stackalloc`'d span, so nothing reaches the heap.

```csharp
Span<Rect> rows = stackalloc Rect[3];
Layout.Vertical(frame.Area, [Constraint.Length(1), Constraint.Fill(), Constraint.Length(1)], rows);

Span<Rect> cols = stackalloc Rect[2];
Layout.Horizontal(rows[1], [Constraint.Percent(30), Constraint.Fill()], cols, spacing: 1);
// rows[0] = header, cols[0] = sidebar, cols[1] = content, rows[2] = status bar
```

| Constraint | Meaning |
|---|---|
| `Length(n)` | Exactly `n` cells |
| `Percent(p)` | `p`% of the available space |
| `Fill(weight)` | A share of what's left, in proportion to `weight` |
| `Min(n)` / `Max(n)` | At least / at most `n`; they grow into leftover space when there's no `Fill` |

`Rect` also has `Inset`, `Intersect`, `Centered(w, h)` (handy for dialogs) and `Row(i)`.

### Styles and colors

A `Style` is a foreground color, a background color and attributes (`Bold`, `Dim`, `Italic`,
`Underline`, `Blink`, `Reverse`, `Hidden`, `Strike`). A `Color` is the terminal default,
a palette index (`Color.Red`, `Color.Indexed(208)`), or RGB (`Color.Rgb(…)`, `Color.Hex(0x34D399)`).

Text **layers**: a default color in the style keeps whatever color the cell already has. Fill a
panel once, and every piece of text on it keeps the panel's background without repeating it.

```csharp
var panel = new Style(Color.Hex(0xE2E8F0), Color.Hex(0x111722));
var accent = new Style(Color.Hex(0x34D399), default, Attr.Bold);

frame.Fill(area, panel);                                       // background
int x = frame.SetString(area.X, area.Y, "build ", accent);     // default bg keeps the panel color
x = frame.SetString(x, area.Y, "passing", new Style(Color.Green, default));
frame.SetString(x + 1, area.Y, "· 14/14", new Style(Color.BrightBlack, default, Attr.Italic));
```

`SetString` returns the column after the text it wrote, so styled segments chain naturally. Text is
clipped to the buffer, or to `maxWidth`, optionally ending in `…` (`Overflow.Ellipsis`).

#### Mixed-style text

`StyledText` is text plus style runs, as one value: `SetText` writes it, and `Paragraph` and block
titles (`StyledTitle`, `StyledFooter`) accept it. It points at your memory, so it never allocates. Build
one from runs, with `StyledTextBuilder`, or from markup:

```csharp
// Runs: lengths in chars, each style layered over the base style (and over the cells underneath).
frame.SetText(x, y, new StyledText("j/k move", [new(3, key), new(5, dim)]));

// A builder over stack memory: merges equal styles, formats numbers in place, never throws when full.
var status = new StyledTextBuilder(stackalloc char[64], stackalloc StyledRun[8]);
status.Append("build ", accent);
status.Append(passed, ok);
status.Append("/", dim);
status.Append(total, dim);
frame.SetText(area.X, area.Y, status.Build(), area.Width, Overflow.Ellipsis);

// Markup: [b] [dim] [i] [u] [s] [reverse], fg=/bg= with a name, #RRGGBB or 0-255; [/] closes the latest tag.
frame.SetMarkup(x, y, "[b fg=#38BDF8]q[/] quit  [u]?[/] help");
frame.Render(new Paragraph(Markup.Parse(help, chars, runs)) { Wrap = TextWrap.Word }, area);
```

A run boundary never splits a character, clipping stops at the first glyph that doesn't fit, and an
ellipsis takes the style of the text it replaces. Unknown tags stay as text, so `[1/3]` needs no
escaping (`[[` is a literal `[`). `SetMarkup` parses on every call: for text drawn every frame in a
hot path, parse once with `Markup.Parse` into arrays you keep, or use the builder.

TUI.NET detects the terminal's color depth from `COLORTERM`, `TERM`, `TERM_PROGRAM`, `WT_SESSION` and
`NO_COLOR`. It maps RGB down to 256 or 16 colors as needed, so a truecolor theme still looks right
on a basic terminal.

### Lists

`ListView` is virtualized: it asks your `IListSource` to draw only the rows that are visible. It
handles selection, highlighting and keeping the selected row in view. The selection lives in a
`ListState` that you own.

```csharp
readonly struct Files(FileInfo[] files) : IListSource
{
    public int Count => files.Length;

    public void RenderItem(int index, Rect area, CellBuffer buffer, bool selected)
    {
        Span<char> size = stackalloc char[20];
        files[index].Length.TryFormat(size, out int n);
        buffer.SetString(area.X, area.Y, files[index].Name, default, area.Width - n - 1, Overflow.Ellipsis);
        buffer.SetString(area.Right - n, area.Y, size[..n], new Style(Color.BrightBlack, default));
    }
}
```

```csharp
var state = new ListState();   // keep this across frames

// render
frame.Render(new ListView<Files>(new Files(files))
{
    SelectedStyle = new Style(default, Color.Hex(0x1C2433), Attr.Bold),
    HighlightSymbol = "▌",
    HighlightSymbolStyle = new Style(Color.Cyan, default),
}, area, ref state);

// input
if (ev.Key.IsChar('j')) state.Next(files.Length);
if (ev.Key.IsChar('k')) state.Previous(files.Length);
if (ev.Key.Is(KeyCode.PageDown)) state.PageDown(files.Length);
if (ev.Kind == EventKind.Mouse) state.HandleMouse(ev.Mouse, files.Length);   // click selects, wheel scrolls
```

The view scrolls to the selected row whenever the selection changes. In between, it can scroll on its
own (the mouse wheel, or `state.Scroll(rows, count)`) and leave the selection off screen.

For plain strings, use the built-in `TextItems` source: `new ListView<TextItems>(new TextItems(names))`.

### Scrollbars

`ListView`, `Table` and `Paragraph` draw a scrollbar in their rightmost column with
`Scrollbar = ScrollbarMode.Auto` (only while the content is longer than the view) or `Always`. The
content gets one column less while it shows. The thumb's ends use eighth-blocks, so it moves in
eighths of a cell.

```csharp
frame.Render(new ListView<Files>(new Files(files))
{
    Scrollbar = ScrollbarMode.Auto,
    ScrollbarThumbStyle = new Style(Color.BrightBlack, default),
    ScrollbarTrackStyle = new Style(Color.Hex(0x3A4252), default),   // '│' track
}, area, ref state);
```

For lists and tables, `state.HandleMouse` also scrolls when you click or drag the scrollbar, and the
drag keeps going when the pointer leaves it. For anything else, render a `Scrollbar` yourself and map
the pointer back with `Scrollbar.PositionAt`:

```csharp
var bar = new Rect(area.Right - 1, area.Y, 1, area.Height);
frame.Render(new Scrollbar(content: lines, viewport: area.Height, position: scroll), bar);

// input: a press on the bar starts a drag that lasts until the button comes up
if (ev.Mouse.IsClickIn(bar)) dragging = true;
if (ev.Mouse.Kind == MouseKind.Up) dragging = false;
if (dragging) scroll = Scrollbar.PositionAt(bar, ev.Mouse.X, ev.Mouse.Y, lines, area.Height);
```

`Orientation = Direction.Horizontal` draws it along a row instead. A custom `ThumbChar` (or
`Smooth = false`) moves the thumb in whole cells.

### Tables

`Table` is a virtualized list with a header and columns. Column widths are layout constraints.
Each column aligns its header and cells left, center or right. Your `ITableSource` returns the text
of each visible cell: either a string you already hold, or text formatted into the `scratch` span
it is given. The table measures, aligns and clips the text, so formatting numbers stays allocation-free.
Selection uses the same `ListState` as `ListView`.

```csharp
static readonly TableColumn[] Columns =
[
    new("name", Constraint.Fill()),
    new("size", Constraint.Length(10), Alignment.Right),
    new("state", Constraint.Length(7), Alignment.Center),
];

readonly struct Files(FileInfo[] files) : ITableSource
{
    public int RowCount => files.Length;

    public ReadOnlySpan<char> Cell(int row, int column, Span<char> scratch, out Style style)
    {
        style = default;
        switch (column)
        {
            case 0:
                return files[row].Name;
            case 1:
                files[row].Length.TryFormat(scratch, out int n);
                return scratch[..n];
            default:
                style = new Style(files[row].IsReadOnly ? Color.Yellow : Color.Green, default);
                return files[row].IsReadOnly ? "ro" : "rw";
        }
    }
}

frame.Render(new Table<Files>(new Files(files), Columns)
{
    HeaderStyle = new Style(Color.BrightBlack, default, Attr.Bold),
    HeaderSeparator = true,                 // ─── under the header
    ColumnSeparator = '│',                  // drawn in the gap, joined with ┼ on the rule
    AlternateRowStyle = new Style(default, Color.Hex(0x111722)),   // zebra stripes
    SortColumn = 1, SortDescending = true,  // ▼ on "size"; the sorting itself is yours
    SelectedStyle = new Style(default, Color.Hex(0x1C2433), Attr.Bold),
    HighlightSymbol = "▌",
}, area, ref state);
```

`Table.Measure(source, column, header)` returns the widest cell in a column. Call it when the data
changes and cache the result as `Constraint.Length(width)` to size a column to its content. For rows
of strings, use the built-in `TextRows` source: `new Table<TextRows>(new TextRows(rows), columns)`.

### Trees

`TreeView` shows a hierarchy: a file tree, grouped items, an outline. Your `ITreeSource` addresses nodes by
integer ids you choose, and is asked for a node's children only once it's expanded, so a file tree can read a
directory the first time it opens. `TreeState` (a class you keep across frames) holds the expanded nodes and
caches the visible rows. They're rebuilt only when the tree's shape changes, so a frame draws only the rows on
screen, however far down a 100,000-row tree it's scrolled.

```csharp
readonly struct Folders(Folder[] folders) : ITreeSource   // ids: folder i is i, file j of folder i is 1000 * (i + 1) + j
{
    public int ChildCount(int node) => node == TreeState.Root ? folders.Length : node < 1000 ? folders[node].Files.Length : 0;
    public int Child(int node, int index) => node == TreeState.Root ? index : 1000 * (node + 1) + index;
    public bool HasChildren(int node) => node >= 0 && node < 1000;
    public int Parent(int node) => node < 1000 ? TreeState.Root : node / 1000 - 1;

    public void RenderLabel(int node, Rect area, CellBuffer buffer, bool selected) =>
        buffer.SetString(area.X, area.Y, node < 1000 ? folders[node].Name : folders[node / 1000 - 1].Files[node % 1000],
            default, area.Width, Overflow.Ellipsis);
}
```

```csharp
var tree = new TreeState();   // keep this across frames
var source = new Folders(folders);

// render
frame.Render(new TreeView<Folders>(source)
{
    GuideStyle = new Style(Color.BrightBlack, default),
    SelectedStyle = new Style(default, Color.Hex(0x1C2433), Attr.Bold),
}, area, ref tree);

// input: arrows or j/k move, → opens (then steps in), ← closes (then steps out), space toggles
if (ev.Kind == EventKind.Key && !tree.Handle(ev.Key, source) && ev.Key.Is(KeyCode.Enter)) Open(tree.SelectedNode);
if (ev.Kind == EventKind.Mouse) tree.HandleMouse(ev.Mouse, source);   // click [+]/[-] to fold, click selects
```

```
[-] src
 ├─ [+] Internal
 ├─ [-] Widgets
 │   ├─ ListView.cs
 │   └─ TreeView.cs
 └─ Terminal.cs
[+] tests
```

Nodes with children get `[+]` when collapsed and `[-]` when expanded (`CollapsedSymbol`, `ExpandedSymbol`; e.g.
`"▸ "` and `"▾ "`). `Guides = false` drops the lines. The selection follows its node when rows above it open or
close; if its parent closes, the parent is selected. `tree.Select(node, source)` opens the path to a node (with
`Parent`) and selects it, and `tree.Invalidate()` picks up data that changed or arrived later. Highlight symbol,
scrollbar, wheel and scrollbar drag work as in `ListView`, through `tree.List`.

### Forms

Interactive widgets keep their state in objects you own, so focus is just a value in your app.
There's no focus manager to fight. `FocusRing` is that value with the usual keys: Tab and Shift+Tab move it
(wrapping around), a click focuses the control under it, and `Is(i)` feeds each widget's `Focused`.

```csharp
// state, kept across frames
var focus = new FocusRing(5);                 // name, priority, notify, Save, notes (below)
var targets = new Rect[5];                    // where each control was drawn, for clicks
var name = new TextInputState("TUI.NET");
var priority = new DropdownState(selected: 1);
string[] priorities = ["low", "medium", "high"];
bool notify = true;

// render
var box = new Block { Title = " name ", BorderType = BorderType.Rounded };
frame.Render(box, nameArea);
frame.Render(new TextInput { Focused = focus.Is(0) }, box.Inner(nameArea), ref name);   // places the real cursor

var dropdown = new Dropdown<TextItems>(new TextItems(priorities))
{
    SelectedStyle = new Style(Color.Black, Color.Cyan),
    PopupBordered = true,
};
frame.Render(dropdown, pickArea, ref priority);
frame.Render(new Checkbox("notify me", notify) { Focused = focus.Is(2) }, checkArea);
frame.Render(new Button("Save") { Focused = focus.Is(3), FocusedStyle = new Style(Color.Black, Color.Green) }, buttonArea);
dropdown.RenderPopup(pickArea, frame, ref priority);   // last, so it draws on top
(targets[0], targets[1], targets[2], targets[3]) = (nameArea, pickArea, checkArea, buttonArea);

// input
if (ev.Kind == EventKind.Mouse && focus.HandleMouse(ev.Mouse, targets)) { /* clicked: focused, now use it */ }
if (ev.Kind == EventKind.Key && focus.Handle(ev.Key)) return;   // Tab, Shift+Tab
switch (focus.Current)
{
    case 0: name.Handle(ev.Key); break;                          // editing, Ctrl+W, Alt+B/F, …
    case 1: priority.Handle(ev.Key, priorities.Length); break;   // Enter opens, ↑/↓ moves, Enter picks
    case 2 when ev.Key.IsChar(' '): notify = !notify; break;
}
```

`TextInputState` supports the usual editing keys: Home/End, Ctrl+A/E, Ctrl+U/K, Ctrl+W, Ctrl/Alt+←/→,
Alt+B/F/D and Delete. It also does masking (`mask: '•'` for passwords) and paste (`Insert(string)`).
For a complete form with validation, see [`EditDialog.cs`](https://github.com/lmaslanka/tuinet/blob/main/samples/Tuinet.Samples.Showcase/EditDialog.cs).

#### Multi-line text

`TextArea` edits multi-line text: descriptions, commit messages, notes. Long lines wrap at word boundaries
(or scroll sideways with `SoftWrap = false`), and it can show line numbers.

```csharp
var notes = new TextAreaState("first line\nsecond line");    // state, kept across frames

frame.Render(new TextArea { Focused = focus.Is(4), LineNumbers = true, Placeholder = "notes…" }, notesArea, ref notes);

// input
switch (ev.Kind)
{
    case EventKind.Key when ev.Key.IsCtrl('c') && notes.HasSelection: term.CopyToClipboard(notes.Selection); break;
    case EventKind.Key when focus.Is(4): notes.Handle(ev.Key); break;   // Enter is a line break; Tab is yours
    case EventKind.Mouse: notes.HandleMouse(ev.Mouse); break;           // click, drag to select, wheel
    case EventKind.Paste: notes.Insert(ev.Paste); break;                // keeps line breaks
}
```

Both editors share one editing core (`EditableText`), so both have:

- **Selection.** Shift with any movement key selects. `Selection` is the selected text, ready for
  `CopyToClipboard`; `DeleteSelection()` cuts. Typing or pasting replaces it. Dragging the mouse selects.
- **Undo and redo.** Ctrl+Z (or Ctrl+/, which also works when `SuspendOnCtrlZ` takes Ctrl+Z) and Ctrl+Y
  (or Ctrl+Shift+Z). Runs of typing undo a word at a time; undo restores the selection an edit replaced.
  The last 1000 steps are kept. `Set` and `Clear` start a new history.
- **Grapheme clusters.** The caret moves and deletes by cluster, and by word with Ctrl or Alt
  (←/→, Backspace, Delete). Offsets (`Caret`, `SelectionStart`, …) are UTF-16 indexes into `Text`.

On top of that, the text area has ↑/↓ and PageUp/PageDown by screen row (keeping the column), Home/End for the
line (Ctrl+Home/End for the text) and Ctrl+A to select all. The text lives
in a gap buffer with a line index, and a frame only measures the lines on screen, so a 100,000-line text draws
as fast as a short one. It allocates only when the text or the undo history outgrows its buffers.

### Popups and dialogs

`Popup` draws a box over whatever is already on screen: an optional drop shadow, a fill and a `Block`, in one
call. The area you give it includes the shadow, and `Outer` turns a content size into that area, so centering
on the content works:

```csharp
var popup = new Popup
{
    Block = new Block { Title = " confirm ", BorderType = BorderType.Rounded, Style = new Style(Color.Default, Color.Hex(0x111722)) },
    Shadow = true,       // darkens the cells below and right of the box, keeping their glyphs
    Padding = 1,         // one blank row and two blank columns inside the border
};
Rect dialog = frame.Area.Centered(popup.Outer(40, 3));   // 40×3 of content
frame.Render(popup, dialog);
frame.SetString(popup.Inner(dialog).X, popup.Inner(dialog).Y, "Delete 3 files?");

// input: click outside to close
if (ev.Mouse.IsClick && !ev.Mouse.IsIn(popup.Frame(dialog))) open = false;
```

`frame.Area.PlaceNear(anchor, size)` places a popup next to something instead: below it if it fits, else
above, else on the side with more room, moved left to stay on screen. Pass a 0×0 anchor for a point, such
as the mouse pointer for a context menu. `Dropdown` lists use the same rule and take `PopupShadow = true`.

### Tabs

```csharp
string[] pages = ["Files", "Search", "Settings"];
var tabs = new TabsState();                                  // state, kept across frames

var bar = new Tabs(pages)
{
    SelectedStyle = new Style(Color.Black, Color.Cyan, Attr.Bold),
    DividerStyle = new Style(Color.BrightBlack, default),   // '│' between titles
};
frame.Render(bar, new Rect(area.X + 2, area.Y, area.Width - 4, 1), ref tabs);   // e.g. in a block's top border

// input
if (ev.Kind == EventKind.Key && ev.Key.IsChar(']')) tabs.Next(pages.Length);   // wraps
if (ev.Kind == EventKind.Mouse) bar.HandleMouse(ev.Mouse, ref tabs);           // click a title, ‹ ›, wheel
```

The bar draws only the tabs and leaves the rest of the row alone, so it can sit in a `Block`'s border.
When the titles don't fit, it scrolls to keep the selected one visible and shows `‹` `›` where tabs are hidden.
`TabsState.Handle` takes ←/→ and Home/End for when the bar itself has focus. Shortcuts that switch tabs from
anywhere are yours: the showcase uses `[`/`]` and Alt+1…9, because terminals don't report Ctrl+Tab without the
kitty keyboard protocol.

### Menus and the command palette

```csharp
// Built once; '&' marks the mnemonic letter. Swap an item with `with` to change it: a struct copy, no allocation.
MenuItem[] items = [new("&Edit", "enter"), new("&Copy", "y"), MenuItem.Separator, new("&Delete", "del", Enabled: false)];
var menu = new MenuState();

// open: at the pointer (a 0×0 point) or under a row
menu.Reset(items);
Rect box = frame.Area.PlaceNear(new Rect(mouse.X, mouse.Y, 0, 0), Menu.Measure(items));

// render last, so it sits on top
frame.Render(new Menu(items) { Popup = new Popup { Shadow = true } }, box, ref menu);

// input
MenuResult result = ev.Kind == EventKind.Key ? menu.Handle(ev.Key, items) : menu.HandleMouse(ev.Mouse, items);
if (result == MenuResult.Activated) Run(menu.Selected);   // Enter, Space, a click or a mnemonic
// Cancelled: Esc, or a press outside the menu; the caller can still act on that click
```

Up/Down (and j/k) skip separators and disabled items and wrap. With `TerminalOptions.MouseMotion`, the item under
the pointer is highlighted. A menu taller than its area scrolls.

`Fuzzy.Score(pattern, text)` filters a list by what was typed: the pattern's chars must appear in order. It's -1
for no match, and higher for matches at word starts, camelCase humps and in runs. Case is ignored unless the
pattern has an uppercase letter. An overload writes the matched positions, for highlighting. Scoring 10,000 command
names takes about 0.3 ms and allocates nothing, so filter on every keystroke, only when the query changed
(`TextInputState.CopyTo` reads it without allocating). The showcase's
[`CommandPalette.cs`](https://github.com/lmaslanka/tuinet/blob/main/samples/Tuinet.Samples.Showcase/CommandPalette.cs)
puts this together with a `Popup`, a `TextInput` and a `ListView` in about 250 lines.

### Progress and animation

```csharp
var clock = Stopwatch.StartNew();

frame.Render(new ProgressBar(done / (double)total)
{
    FilledStyle = new Style(Color.Green, default),
    EmptyStyle = new Style(Color.BrightBlack, default),   // ░ remainder; eighth-blocks at the edge
}, barArea);

char spinner = Spinner.Frame(Spinner.Dots, clock.ElapsedMilliseconds);
frame.SetRune(barArea.Right + 1, barArea.Y, new Rune(spinner));

// Wake ~30×/s only while something animates; otherwise sleep until input.
bool animating = done < total;
term.Poll(out Event ev, animating ? 33 : Timeout.Infinite);
```

Base animation on elapsed time, not frame count, so it runs at the same speed whatever the frame
rate. With `Terminal.Run`, return `animating` from `IApp.IsAnimating` instead of choosing the timeout.

For something that happens once, later (hide a "saved" message after 3 seconds, run a search once typing
pauses), keep an `Alarm` in your state. It's a plain value: no timer thread, no callback.

```csharp
flash = "saved";
flashAlarm.Start(nowMs, 3000);

// IApp
public bool Handle(Event ev, long nowMs)
{
    if (flashAlarm.Fire(nowMs)) flash = "";   // true once, at the first event at or after the time
    …
}

public long NextDueMs(long nowMs) => flashAlarm.DueMs;   // Run wakes then with a Tick; long.MaxValue when not set
```
 For a segmented meter like `■■□□`, use a `ProgressBar` with `FilledChar = '■'` and `EmptyChar = '□'`.

### Charts

```csharp
// A value over time: one value per column, newest on the right. The two spans draw your own ring
// buffer without a copy (older part, then newer part); h rows give 8h levels.
frame.Render(new Sparkline(times.AsSpan(head), times.AsSpan(0, head))
{
    Style = new Style(Color.Blue, default),
    MaxStyle = new Style(Color.Yellow, default),   // the peak column
}, new Rect(x, y, 40, 3));

// Bars that compare values: vertical (labels under, values above) or horizontal (label column, values after).
Bar[] bars = [new(12, "feature", new Style(Color.Green, default)), new(7, "bugfix"), new(3, "docs")];
frame.Render(new BarChart(bars) { BarWidth = 7, ValueFormat = "0" }, chartArea);
frame.Render(new BarChart(bars) { Direction = Direction.Horizontal, Gap = 0, Max = 20 }, listArea);
```

With `Max` left at NaN, a sparkline scales to the largest *visible* value, so an old spike stops squashing
the chart once it scrolls out. Zero draws nothing, NaN and negative values leave a gap, and any other positive
value shows at least `▁`. Bar charts drop bars that don't fit instead of squashing them; values are formatted
on the stack with `ValueFormat` (`null` hides them). Keep the `Bar[]` in a field and refill it each frame:
a frame with both charts allocates nothing.

### Background work

`Post` is thread-safe and wakes a blocked `Poll`. The message comes back as `EventKind.Message`
on the UI thread.

```csharp
record Loaded(string[] Items);
```

```csharp
_ = Task.Run(() => term.Post(new Loaded(LoadItems())));   // any thread; wakes Poll

// in the loop
if (ev.Message is Loaded loaded)
{
    items = loaded.Items;
}
```

### Keys, mouse and paste

```csharp
using var term = Terminal.Open(new TerminalOptions { Mouse = true, BracketedPaste = true, FocusEvents = true });
```

```csharp
switch (ev.Kind)
{
    case EventKind.Key when ev.Key.Is(KeyCode.Up, Modifiers.Ctrl): /* Ctrl+↑ */ break;
    case EventKind.Key when ev.Key.IsCtrl('s'): /* Ctrl+S */ break;
    case EventKind.Mouse when ev.Mouse.IsClickIn(saveButton): Save(); break;
    case EventKind.Paste: input.Insert(ev.Paste); break;   // one event, not a key per char
    case EventKind.Resize: break;                         // the next BeginFrame already has the new size
}
```

**Mouse.** There is no retained widget tree to route clicks, so hit-testing uses where things were
drawn on the last frame. Stateful widgets record it in their state. For stateless ones (buttons,
checkboxes), keep the `Rect` you rendered into and test it with `ev.Mouse.IsClickIn(rect)`. Every
`HandleMouse` returns whether it used the event, so you can try one control after another.

| Call | Does |
|---|---|
| `ListState.HandleMouse(ev, count)` | Click selects the row under the pointer, wheel scrolls, click or drag on the scrollbar scrolls there (lists and tables) |
| `ListState.RowAt(x, y, count)` | Item drawn at a cell, or -1 |
| `table.HeaderColumnAt(x, y, state)` | Column whose header is at a cell, or -1 (e.g. click to sort) |
| `Scrollbar.PositionAt(area, x, y, content, viewport)` | Scroll position for a click or drag on a scrollbar you drew |
| `DropdownState.HandleMouse(ev, count)` | Click opens; click an item to pick it; wheel scrolls the list; click elsewhere cancels |
| `tabs.HandleMouse(ev, ref state)` | Click selects a tab; click on `‹` `›` or the wheel scrolls the bar |
| `tabs.TabAt(x, y, state)` | Tab drawn at a cell, or -1 |
| `popup.Frame(area)` | The box without its shadow, for click-outside tests |
| `TextInputState.HandleMouse(ev)` | Click puts the caret on the clicked character (wide glyphs and clusters included); Shift+click or drag selects |
| `TextAreaState.HandleMouse(ev)` | The same on wrapped rows, plus the wheel scrolls and a drag past the edge scrolls |
| `MouseEvent.IsClick` / `IsWheel` / `WheelDelta` / `IsIn(rect)` / `IsClickIn(rect)` | Conveniences |

Widgets act on the press (`MouseKind.Down`), as most terminal apps do. The showcase uses all of these:
click rows, double-click to edit, wheel-scroll the table, drag its scrollbar, click a header to sort, click a tab,
click every control in the edit dialog, and click outside the progress dialog to close it.

| `TerminalOptions` | Default | |
|---|---|---|
| `Mouse` / `MouseMotion` | off | Clicks, drags and wheel (SGR 1006) / also motion with no button held |
| `BracketedPaste` | off | Pastes arrive as one `Paste` event |
| `FocusEvents` | off | `FocusGained` / `FocusLost` |
| `ColorMode` | detected | `TrueColor`, `Indexed256`, `Basic16` or `None` |
| `EscapeTimeoutMs` | 20 | How long a lone ESC waits before it counts as the Escape key |
| `ScrollRegions` | on | Let the terminal move rows that scrolled, instead of repainting them: full-width bands, and narrower ones where the terminal reports left/right margins (`term.LeftRightMarginsActive`) |
| `EraseSequences` | on | Clear runs of blanks with EL/ECH instead of spaces (needs background color erase, which modern terminals have) |
| `SuspendOnCtrlZ` | off | Ctrl+Z suspends the app inside `Poll` (see below) instead of arriving as a key |
| `KittyKeyboard` | off | Kitty keyboard protocol where the terminal has it (see below) |
| `KeyReleaseEvents` | off | With `KittyKeyboard`: also key repeats and releases (`KeyEvent.Kind`) |
| `Inline` | null | A band of `InlineOptions.Height` rows under the prompt instead of the alternate screen (see below) |

Every mode is switched off again on exit, on a crash, or on a signal.

**Kitty keyboard.** The keyboard encoding terminals inherited from the 1970s sends some keys as the same
bytes: Ctrl+I is Tab, Ctrl+M is Enter, Ctrl+[ is Esc, and Ctrl+Shift+A is Ctrl+A. Esc is also the first byte
of every escape sequence, so a lone Esc waits `EscapeTimeoutMs` in case more is coming. With
`KittyKeyboard = true`, terminals that support the [kitty keyboard protocol](https://sw.kovidgoyal.net/kitty/keyboard-protocol/)
(kitty, Ghostty, foot, WezTerm, recent Alacritty and iTerm2) send those keys unambiguously, and Esc arrives at
once. Other terminals ignore the request and nothing changes. `term.KittyKeyboardActive` turns true once the
terminal confirms it, during the first polls. `KeyReleaseEvents` adds key repeats and releases
(`KeyEvent.Kind`), e.g. for hold-to-move. Releases never match `Is`, `IsChar` or `IsCtrl`, so existing key
bindings don't fire twice.

### Title, clipboard, cursor and links

```csharp
term.SetTitle(file.Name);                       // OSC 2; only changes are sent, so call it every frame
term.CopyToClipboard(selection);                // OSC 52: the user's clipboard, even over SSH

frame.SetCursor(x, y, CursorShape.Bar);         // DECSCUSR, sent only when the shape changes

int end = frame.SetString(x, y, "docs", new Style(Color.Blue, default, Attr.Underline));
frame.SetLink(new Rect(x, y, end - x, 1), "https://example.com/docs");   // OSC 8: Ctrl+click opens it
```

- **Title.** Control characters are dropped. On exit the terminal's own title comes back (title stack,
  `CSI 22 t` / `CSI 23 t`).
- **Clipboard.** Up to `Terminal.MaxClipboardBytes` of UTF-8. Terminals may ignore OSC 52 or ask the user
  first, and in tmux it needs `set-clipboard on`. There is no reply saying whether it worked. Paste already
  arrives as a `Paste` event with `BracketedPaste`.
- **Cursor shape.** Block, underline or bar, blinking or steady. A focused `TextInput` shows a blinking bar
  (its `CursorShape` property). The user's shape comes back on exit, but only if the app changed it.
- **Links.** Terminals without OSC 8 show plain text. Text drawn over a linked cell later replaces the link,
  as it does attributes. `SetStyle` keeps it. URLs with control characters are refused; spaces and non-ASCII
  are percent-encoded. A process can link up to 4095 distinct URLs, and after that new links show as plain
  text.

The showcase uses all four: the window title names the selected item, `y` copies its name, the subtitle has a
link to this repository, and text fields show a bar caret.

### Suspend (Ctrl+Z)

Raw mode turns Ctrl+Z into an ordinary key. To let users background the app as they would a shell
command, set `SuspendOnCtrlZ`, or call `Suspend` yourself:

```csharp
case EventKind.Key when ev.Key.IsCtrl('z'): term.Suspend(); break;
```

`Suspend` leaves the screen, restores the terminal and stops the process. When the user types `fg`, it
returns with the alternate screen back, and the next `Poll` returns a `Resize` event so the app draws a
frame, which repaints everything. A `kill -TSTP` from outside suspends the same way on the next `Poll`,
and the app also recovers from `kill -STOP`. On Windows, `Suspend` returns false and does nothing.

### Inline mode

CLI tools often want a live area under the prompt, not a full screen: progress bars, a spinner, a prompt, a
live table. With `Inline`, the terminal draws the frame in a band of that many rows under the cursor, and the
band stays in the terminal (and its scrollback) after exit. Everything else is the same: `BeginFrame`
returns a buffer the size of the band, and only changes are written.

```csharp
using var term = Terminal.Open(new TerminalOptions { Inline = new InlineOptions(height: 4) });

term.PrintAbove($"✓ {file.Name}");    // a log line above the band; it stays in the scrollback
CellBuffer frame = term.BeginFrame();  // 4 rows, full width
frame.Render(new ProgressBar(done / (double)total), new Rect(0, 0, frame.Width, 1));
term.Present();
```

- **Start.** The band starts on the cursor's line (the next one if that line has text). Near the bottom of
  the screen, the screen scrolls up to make room. To find the cursor, the terminal is asked for its
  position (`CSI 6n`); with no reply within `CursorReportTimeoutMs`, the band goes at the bottom.
- **`PrintAbove`** prints wrapped, sanitized text (plain or `StyledText`) where the band was, moves the band
  down (scrolling once it reaches the bottom) and repaints it, in one write.
- **Exit** keeps the last frame and continues on the line after its last non-blank row, so a final
  one-line summary frame reads like ordinary output.
- **Resize** reports a `Resize` with the band's size, asks the terminal where the band went and repaints
  it there. Taller, shorter and wider are seamless. When a terminal that reflows long lines gets
  narrower, the band's old rows can be rewrapped in ways that leave a stale copy above it (tmux does
  this).

The `Tuinet.Samples.Inline` sample is a complete example.

### Custom widgets

A widget is any type that implements `IWidget` (or `IStatefulWidget<TState>`). Make it a
`readonly ref struct` and it can hold spans, such as text formatted on the stack, at zero cost.

```csharp
readonly ref struct Badge : IWidget
{
    private readonly ReadOnlySpan<char> _text;
    private readonly Color _color;

    public Badge(ReadOnlySpan<char> text, Color color)
    {
        _text = text;
        _color = color;
    }

    public void Render(Rect area, CellBuffer buffer)
    {
        int x = buffer.SetString(area.X, area.Y, "▐", new Style(_color, default));
        x = buffer.SetString(x, area.Y, _text, new Style(Color.Black, _color, Attr.Bold));
        buffer.SetString(x, area.Y, "▌", new Style(_color, default));
    }
}
```

```csharp
Span<char> text = stackalloc char[16];
text.TryWrite($"{count} new", out int length);           // formatted on the stack: no string
frame.Render(new Badge(text[..length], Color.Red), area);
```

### Testing

UI code is just a function from state to cells, so you can test it without a terminal. Render
into a `CellBuffer` and assert on its text and styles. To test end to end, drive a real `Terminal`
over the in-memory `TestTty`.

```csharp
[Fact]
public void Shows_the_title()
{
    var buffer = new CellBuffer(80, 24);
    new ShowcaseApp().Render(buffer, nowMs: 0);
    Assert.Contains("ITEMS · 20", buffer.ToString());
    Assert.Equal(Color.Hex(0xF5A623), buffer[2, 4].Style.Fg);   // the list's amber border
}

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

## 🧩 Widgets

| Widget | State | What it does |
|---|---|---|
| `Block` | | Borders (plain, rounded, double, thick, dashed), plain or styled title and footer with alignment, background, `Inner(area)` |
| `Paragraph` | | Multi-line plain or styled text with word/char wrapping, alignment and scroll; optional scrollbar; `LineCount` |
| `ListView<T>` | `ListState` | Virtualized, selectable, scrolls to follow the selection, highlight symbol, optional scrollbar; click, wheel and scrollbar drag |
| `TreeView<T>` | `TreeState` | Hierarchy with `[+]`/`[-]` expanders and `├─`/`└─` guides, lazy children, cached rows (a frame draws only what's visible), selection that follows its node; keys, click on the expander, wheel and scrollbar drag |
| `Table<T>` | `ListState` | Header and columns with constraint widths, left/center/right alignment, per-cell styles, separators, zebra stripes, sort arrow, optional scrollbar; click, wheel, scrollbar drag and header hit-testing |
| `Scrollbar` | | Vertical or horizontal, eighth-block thumb ends, `PositionAt` for clicks and drags |
| `TextInput` | `TextInputState` | Single-line editing, emacs keys, selection, undo/redo, masking, horizontal scroll, real terminal cursor; click or drag |
| `TextArea` | `TextAreaState` | Multi-line editing: soft wrap or sideways scroll, line numbers, selection, undo/redo, placeholder; click, drag and wheel; only visible lines are measured |
| `Dropdown<T>` | `DropdownState` | Select box with a popup list that flips above when there's no room below, optional shadow; click to open and pick |
| `Tabs` | `TabsState` | One-row tab bar, dividers, scrolls with `‹` `›` when the titles don't fit, fits in a block border; click and wheel |
| `Popup` | | Shadow + fill + `Block` over existing content; `Outer` sizes it from its content, `Rect.PlaceNear` places it by an anchor |
| `Menu` | `MenuState` | Popup action list: mnemonics, right-aligned shortcuts, separators joined to the border, disabled items; keys, click and hover |
| `Checkbox` | your `bool` | One-row symbol + label, or a large `Boxed` square |
| `Button` | | Padded label with idle and focused styles |
| `ProgressBar` | | Eighth-block precision, custom fill/empty glyphs (segmented meters) |
| `Sparkline` | | Values over time in eighth-block columns, several rows tall, newest on the right; draws a ring buffer's two halves without a copy |
| `BarChart` | | Vertical or horizontal bars with eighth-block ends, labels and formatted values; per-bar styles |
| `Spinner` | | Time-based frames: `Line`, `Dots`, `Arc` |
| `Clear` | | Blanks an area (`Popup` does this for you) |

## ⚡ Performance

200×60 screen, .NET 10, Ryzen 9 7950X. Every scenario allocates **0 bytes**.

| Scenario | Time | Bytes written |
|---|---:|---:|
| Frame with no changes | 4.5 µs | 0 |
| One cell changed | 4.7 µs | 59 |
| Full repaint, a different truecolor style on every row | 35 µs | 13.3 KB |
| Scroll by one row, every row different | 7.7 µs | 271 |
| Scroll by one row beside a 50-column panel, terminal without left/right margins | 31 µs | 10.3 KB |
| The same, terminal with left/right margins (DECLRMM) | 13 µs | 459 |
| An 80×24 dialog closes over a themed background | 8.6 µs | 313 |
| Every row gets shorter (text, then blanks to the right edge) | 12 µs | 698 |
| App frame, scrolling: layout + block + 5,000-item list + diff + write | 14 µs | 165 |
| App frame, scrolling: layout + block + 5,000-row, 4-column table + diff + write | 21 µs | 233 |
| `SetString`, 60 rows: ASCII / ASCII with explicit colors / CJK | 6.4 / 4.1 / 9.5 µs | |
| `TextArea` over a 100,000-line text, wrapped / unwrapped, with line numbers | 60 / 39 µs | |
| `TreeView` frame, 109,000 rows open in a 1,000,000-node tree, at row 0 / 50,000 / 99,000 | 6.8 / 7.0 / 6.6 µs | |
| Rebuilding those 109,000 rows after a node opens or closes | 0.47 ms | |
| Caret down / a keystroke in that text area, then its frame | 61 / 78 µs | |
| Parse 9,000 input events (keys, CSI, mouse, UTF-8) | 121 µs | |
| `Fuzzy.Score`, a 3-char pattern against one command name (10,000 names: 0.28 ms) | 28 ns | |

Compared with the original kernel this library grew out of, unchanged and sparse frames are about **9× faster**, and full repaints are **3.3× faster**.

```sh
dotnet run -c Release --project bench/Tuinet.Benchmarks -- --filter '*'   # BenchmarkDotNet suite
dotnet run -c Release --project bench/Tuinet.Benchmarks -- bytes          # bytes on the wire per scenario
```

Tests guard these properties too:

| Test | Guards |
|---|---|
| `AllocationTests` | 1,000 frames with widgets and charts, 1,000 text area frames with caret keys, 1,000 open-menu frames with keys and pointer moves, fuzzy scoring, and 10,000 key polls allocate 0 bytes |
| `RendererFuzzTests` | Random frames, replayed through a VT emulator, must reproduce the buffer exactly: every glyph, color and attribute, plus the cursor |
| `TerminalTests` | Exact bytes for a one-cell change; control characters in text never reach the terminal |

> **Tip:** publish with Native AOT (`-p:PublishAot=true`) for instant startup and the steadiest
> latency. Under the JIT, code that hasn't been optimized yet can box values while formatting.

## 🖥️ Platforms

| Platform | Backend | Status |
|---|---|---|
| Linux | termios raw mode, `poll(2)` + self-pipe wake, `SIGWINCH`, job control (`SIGTSTP`/`SIGCONT`) | ✅ tested (x64), JIT and Native AOT |
| macOS | termios raw mode (macOS struct layout), arm64 variadic `ioctl` handled | ⚠️ implemented, not yet tested |
| Windows 10+ | Console VT mode, `ReadConsoleInputW`, UTF-8 code page | ⚠️ implemented, not yet tested |

All platforms share the same VT encoder and input parser.

## 🛠️ Development

```sh
dotnet build                     # warnings are errors
dotnet test                      # library + sample tests
./run                            # showcase sample
./run e2e                        # end-to-end checks in tmux (needs tmux, python3)
dotnet publish samples/Tuinet.Samples.Showcase -c Release -p:PublishAot=true
tools/gen-width/gen.py [17.0.0]  # regenerate the Unicode width table from the UCD
```

CI ([`.github/workflows/ci.yml`](https://github.com/lmaslanka/tuinet/blob/main/.github/workflows/ci.yml)) builds with warnings as errors and runs
the tests on Linux, macOS and Windows. It also checks that both samples still publish as Native AOT
with no trim or AOT warnings, then runs the end-to-end checks, and packs the library and inspects the
package ([`tools/pack/verify.sh`](https://github.com/lmaslanka/tuinet/blob/main/tools/pack/verify.sh)).

Every public API is listed in `src/Tuinet/PublicAPI.Shipped.txt` and `PublicAPI.Unshipped.txt`. Adding,
changing or removing a public member fails the build (RS0016/RS0017) until the change is recorded in
`PublicAPI.Unshipped.txt`, so API changes show up in review. The IDE code fix adds the entries for you;
otherwise copy the symbol text from each RS0016 error into the file.

The end-to-end checks ([`tools/e2e`](https://github.com/lmaslanka/tuinet/tree/main/tools/e2e)) run the AOT binaries in tmux, a real terminal
implementation, and read the screen back:

- **showcase**, at 110×34 and 80×24: navigation, edit and save, the animated progress dialog, and real
  SGR mouse reports (click a row, wheel-scroll, click a header to sort).
- **inline**: the inline sample under an interactive bash, at 90×20, at 80×8 (the logs scroll), with Ctrl+C,
  and resized taller, shorter and wider mid-run. The history must read: the command, the 8 log lines once
  each, one summary line, then the shell, with no leftover progress bars. Terminal modes are restored.
- **suspend**: the showcase under an interactive bash. Ctrl+Z, `kill -TSTP` and `kill -STOP` each stop it,
  the shell takes commands (with the terminal modes restored, where the app could restore them), and
  `fg` brings back the whole screen with keys working.
- **stress**: bursts of scrolling; every visible row must have the right content in the right order, and
  the terminal must have received scroll-region sequences.
- **clusters**: the grapheme cluster cases in [`clusters.txt`](https://github.com/lmaslanka/tuinet/blob/main/tools/e2e/clusters.txt) must produce the
  same screen as a reference that places every cell with an explicit cursor move.

Every app must also exit with code 0, restore the terminal modes, and write nothing under `$HOME`.

### Releasing

Pushing a `v*` tag runs [`release.yml`](https://github.com/lmaslanka/tuinet/blob/main/.github/workflows/release.yml):
build, test, pack, verify, push to NuGet (with symbols), and a GitHub release with notes from
[`CHANGELOG.md`](https://github.com/lmaslanka/tuinet/blob/main/CHANGELOG.md). It publishes with nuget.org
Trusted Publishing, so no API key is stored. One-time setup:

- On nuget.org, under your name → *Trusted Publishing*, add a policy: owner `lmaslanka`, repository
  `tuinet`, workflow file `release.yml`, environment `release`.
- In GitHub, add a `NUGET_USER` secret holding your nuget.org profile name (not your email).

1. Set `<VersionPrefix>` in `Directory.Build.props` to the new version. The tag must match it.
2. In `CHANGELOG.md`, rename `## [Unreleased]` to `## [x.y.z] - YYYY-MM-DD` and add a fresh, empty
   `## [Unreleased]` above it.
3. Move the entries from `src/Tuinet/PublicAPI.Unshipped.txt` into `PublicAPI.Shipped.txt`.
4. Commit, then `git tag vx.y.z && git push origin vx.y.z`.

For a pre-release, tag `vx.y.z-rc.1` and skip steps 2 and 3. Its notes come from `## [Unreleased]`.

```
src/Tuinet/                      the library
  Widgets/                       Block, Popup, Menu, Tabs, Paragraph, ListView, Table, TreeView, Scrollbar, TextInput, TextArea, Dropdown, Checkbox, Button, ProgressBar, Sparkline, BarChart
  Internal/                      renderer, VT parser, width tables, crash guard
  Platform/                      Unix and Windows backends
  Testing/                       TestTty
samples/Tuinet.Samples.Showcase  list, stats charts, context menu, command palette, edit form and progress dialog (the screenshots)
samples/Tuinet.Samples.Inline    inline mode: download progress under the prompt, logs above it
samples/Tuinet.Samples.Stress    latency and throughput harness
bench/Tuinet.Benchmarks          BenchmarkDotNet suite
tests/                           unit, widget, allocation, renderer fuzz and sample tests
tools/e2e/                       end-to-end checks in tmux
tools/gen-width/                 Unicode width table generator
tools/pack/                      package check (CI and release)
docs/images/                     README screenshots
```

## 📄 License

[Apache 2.0](https://github.com/lmaslanka/tuinet/blob/main/LICENSE)
