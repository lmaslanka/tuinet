# Layout and styling

Split the screen into rectangles, then draw text and widgets into them with styles. Everything here is a plain
value: a `Rect`, a `Style`, a `StyledText` over memory you own.

## Layout

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

## Styles and colors

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

### Mixed-style text

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

## Custom widgets

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

---

[Getting started](getting-started.md) · [Layout and styling](layout.md) · [Widgets](widgets.md) ·
[Input and the terminal](input.md) · [Testing](testing.md) · [Performance](performance.md) · [API reference](api/index.md)
