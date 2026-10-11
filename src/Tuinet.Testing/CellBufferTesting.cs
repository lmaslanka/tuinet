using System.Globalization;
using System.Text;

namespace Tuinet.Testing;

/// <summary>
/// Test assertions and dumps for a <see cref="CellBuffer"/>: a styled text format for snapshot files, ANSI for
/// eyeballing in a terminal, and finding text on screen. Allocates freely: these are for tests, not frames.
/// </summary>
public static class CellBufferTesting
{
    private static readonly string[] ColorNames =
    [
        "black", "red", "green", "yellow", "blue", "magenta", "cyan", "white",
        "bright-black", "bright-red", "bright-green", "bright-yellow", "bright-blue", "bright-magenta", "bright-cyan", "bright-white",
    ];

    /// <summary>
    /// The buffer as <see cref="Markup"/>, one line per row: text in the default style is plain, styled text is
    /// wrapped in a tag, e.g. <c>[fg=#F5A623 b]ITEMS[/]</c>, and a literal <c>[</c> is <c>[[</c>. When a row is mostly
    /// one style with a background (a panel), that style opens the row and tags inside it hold only what differs. Stable, so
    /// it suits snapshot files (see <see cref="Snapshot"/>), and <see cref="Snapshot.Parse"/> reads it back exactly.
    /// Rows are full width, like <see cref="CellBuffer.ToString"/>. Hyperlinks and the cursor aren't included.
    /// </summary>
    public static string ToStyledString(this CellBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var builder = new StringBuilder((buffer.Width + 16) * buffer.Height);
        var counts = new Dictionary<Style, int>();
        for (int y = 0; y < buffer.Height; y++)
        {
            if (y > 0)
            {
                builder.Append('\n');
            }

            ReadOnlySpan<Cell> row = buffer.Row(y);
            Style rowStyle = MostCommon(row, counts);
            if (rowStyle.Bg.IsDefault)
            {
                rowStyle = default;   // only a background is worth hoisting: flat tags read better for text colors
            }

            bool rowOpen = false;
            bool inner = false;
            Style current = default;
            bool first = true;
            foreach (Cell cell in row)
            {
                if (cell.IsContinuation)
                {
                    continue;
                }

                if (first || cell.Style != current)
                {
                    if (inner)
                    {
                        builder.Append("[/]");
                        inner = false;
                    }

                    // Inside the row's tag, a tag can only add attributes: a run without one of them closes the row's tag.
                    bool layers = rowStyle != default && (cell.Style.Attrs & rowStyle.Attrs) == rowStyle.Attrs;
                    if (rowOpen && !layers)
                    {
                        builder.Append("[/]");
                        rowOpen = false;
                    }
                    else if (!rowOpen && layers)
                    {
                        AppendTag(builder, rowStyle, default);
                        rowOpen = true;
                    }

                    Style under = rowOpen ? rowStyle : default;
                    if (cell.Style != under)
                    {
                        AppendTag(builder, cell.Style, under);
                        inner = true;
                    }

                    current = cell.Style;
                    first = false;
                }

                string text = cell.Text;
                builder.Append(text == "[" ? "[[" : text);
            }

            builder.Append(inner ? "[/]" : "").Append(rowOpen ? "[/]" : "");
        }

        return builder.ToString();
    }

    /// <summary>
    /// The buffer as ANSI text (SGR colors and attributes, 24-bit where the cell is), one line per row, each ending
    /// with a reset. Print it to look at a frame in a real terminal: <c>Console.WriteLine(buffer.ToAnsi())</c>.
    /// </summary>
    public static string ToAnsi(this CellBuffer buffer)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        var builder = new StringBuilder((buffer.Width + 32) * buffer.Height);
        for (int y = 0; y < buffer.Height; y++)
        {
            if (y > 0)
            {
                builder.Append('\n');
            }

            Style current = default;
            foreach (Cell cell in buffer.Row(y))
            {
                if (cell.IsContinuation)
                {
                    continue;
                }

                if (cell.Style != current)
                {
                    AppendSgr(builder, cell.Style);
                    current = cell.Style;
                }

                builder.Append(cell.Text);
            }

            builder.Append("\u001b[0m");
        }

        return builder.ToString();
    }

    /// <summary>
    /// Where <paramref name="text"/> first appears, reading rows top to bottom: the cells it covers (one row; wide
    /// glyphs count two columns). Throws <see cref="ScreenAssertionException"/>, showing the screen, when it isn't there.
    /// </summary>
    public static Rect FindText(this CellBuffer buffer, string text) =>
        buffer.TryFindText(text, out Rect area)
            ? area
            : throw new ScreenAssertionException($"'{text}' is not on screen:\n{buffer}");

    /// <summary>Where <paramref name="text"/> first appears, as in <see cref="FindText"/>; false when it isn't there.</summary>
    public static bool TryFindText(this CellBuffer buffer, string text, out Rect area)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        ArgumentException.ThrowIfNullOrEmpty(text);
        var row = new StringBuilder(buffer.Width);
        var columns = new List<int>(buffer.Width + 1);
        for (int y = 0; y < buffer.Height; y++)
        {
            row.Clear();
            columns.Clear();
            ReadOnlySpan<Cell> cells = buffer.Row(y);
            for (int x = 0; x < cells.Length; x++)
            {
                if (cells[x].IsContinuation)
                {
                    continue;
                }

                string glyph = cells[x].Text;
                row.Append(glyph);
                for (int i = 0; i < glyph.Length; i++)
                {
                    columns.Add(x);
                }
            }

            int at = row.ToString().IndexOf(text, StringComparison.Ordinal);
            if (at >= 0)
            {
                int end = at + text.Length;
                int right = end < columns.Count ? columns[end] : buffer.Width;
                area = new Rect(columns[at], y, right - columns[at], 1);
                return true;
            }
        }

        area = default;
        return false;
    }

    /// <summary>The style of cell (<paramref name="x"/>, <paramref name="y"/>); the right half of a wide glyph has the glyph's style.</summary>
    public static Style StyleAt(this CellBuffer buffer, int x, int y)
    {
        ArgumentNullException.ThrowIfNull(buffer);
        return buffer[x, y].Style;
    }

    /// <summary>The style of the first cell of <paramref name="text"/> (see <see cref="FindText"/>).</summary>
    public static Style StyleOf(this CellBuffer buffer, string text)
    {
        Rect area = buffer.FindText(text);
        return buffer[area.X, area.Y].Style;
    }

    /// <summary>The style that covers the most cells of <paramref name="row"/> (the first one on a tie).</summary>
    private static Style MostCommon(ReadOnlySpan<Cell> row, Dictionary<Style, int> counts)
    {
        counts.Clear();
        Style best = default;
        int bestCount = 0;
        foreach (Cell cell in row)
        {
            int count = counts[cell.Style] = counts.GetValueOrDefault(cell.Style) + 1;
            if (count > bestCount)
            {
                (best, bestCount) = (cell.Style, count);
            }
        }

        return best;
    }

    /// <summary>A tag that turns <paramref name="under"/> (the enclosing tag's style) into <paramref name="style"/>.</summary>
    private static void AppendTag(StringBuilder builder, Style style, Style under)
    {
        builder.Append('[');
        int start = builder.Length;
        if (style.Fg != under.Fg)
        {
            builder.Append("fg=");
            AppendColorName(builder, style.Fg);
        }

        if (style.Bg != under.Bg)
        {
            Space(builder, start).Append("bg=");
            AppendColorName(builder, style.Bg);
        }

        foreach ((Attr attr, string word) in AttrWords)
        {
            if ((style.Attrs & ~under.Attrs & attr) != 0)
            {
                Space(builder, start).Append(word);
            }
        }

        builder.Append(']');
    }

    private static readonly (Attr Attr, string Word)[] AttrWords =
    [
        (Attr.Bold, "b"), (Attr.Dim, "dim"), (Attr.Italic, "i"), (Attr.Underline, "u"),
        (Attr.Blink, "blink"), (Attr.Reverse, "reverse"), (Attr.Hidden, "hidden"), (Attr.Strike, "s"),
    ];

    private static StringBuilder Space(StringBuilder builder, int start) =>
        builder.Length > start ? builder.Append(' ') : builder;

    private static void AppendColorName(StringBuilder builder, Color color)
    {
        if (color.IsDefault)
        {
            builder.Append("default");
        }
        else if (color.Kind == ColorKind.Rgb)
        {
            builder.Append(CultureInfo.InvariantCulture, $"#{color.R:X2}{color.G:X2}{color.B:X2}");
        }
        else if (color.Index < ColorNames.Length)
        {
            builder.Append(ColorNames[color.Index]);
        }
        else
        {
            builder.Append(color.Index.ToString(CultureInfo.InvariantCulture));
        }
    }

    private static void AppendSgr(StringBuilder builder, Style style)
    {
        builder.Append("\u001b[0");
        ReadOnlySpan<int> codes = [1, 2, 3, 4, 5, 7, 8, 9];   // Bold … Strike, in Attr bit order
        for (int bit = 0; bit < codes.Length; bit++)
        {
            if (((int)style.Attrs & (1 << bit)) != 0)
            {
                builder.Append(';').Append(codes[bit]);
            }
        }

        AppendSgrColor(builder, style.Fg, 30, 90, 38);
        AppendSgrColor(builder, style.Bg, 40, 100, 48);
        builder.Append('m');
    }

    private static void AppendSgrColor(StringBuilder builder, Color color, int basic, int bright, int extended)
    {
        switch (color.Kind)
        {
            case ColorKind.Rgb:
                builder.Append(CultureInfo.InvariantCulture, $";{extended};2;{color.R};{color.G};{color.B}");
                break;
            case ColorKind.Indexed when color.Index < 8:
                builder.Append(';').Append(basic + color.Index);
                break;
            case ColorKind.Indexed when color.Index < 16:
                builder.Append(';').Append(bright + color.Index - 8);
                break;
            case ColorKind.Indexed:
                builder.Append(CultureInfo.InvariantCulture, $";{extended};5;{color.Index}");
                break;
        }
    }
}
