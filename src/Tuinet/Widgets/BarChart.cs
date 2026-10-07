namespace Tuinet.Widgets;

/// <summary>One bar of a <see cref="BarChart"/>. <paramref name="Style"/> is layered over <see cref="BarChart.BarStyle"/>.</summary>
public readonly record struct Bar(double Value, string Label = "", Style Style = default);

/// <summary>
/// Bars that compare values, with eighth-block ends. <see cref="Direction.Vertical"/> bars grow up from the bottom,
/// <see cref="BarWidth"/> wide, with the label under each bar and the value above it (inside its top when the bar is
/// full height). <see cref="Direction.Horizontal"/> bars grow right, one row each, after a label column as wide as the
/// widest label (at most a third of the width), with the value after the bar. Bars that don't fit are dropped from
/// the end. Zero, negative and NaN values draw no bar; values over <see cref="Max"/> are clamped.
/// </summary>
public readonly ref struct BarChart : IWidget
{
    private readonly ReadOnlySpan<Bar> _bars;

    public BarChart(ReadOnlySpan<Bar> bars) => _bars = bars;

    /// <summary>Vertical: bars grow up. Horizontal: bars grow right.</summary>
    public Direction Direction { get; init; }

    /// <summary>The value of a full-length bar. NaN: the largest value drawn.</summary>
    public double Max { get; init; } = double.NaN;

    /// <summary>Columns per bar (vertical only; horizontal bars are one row).</summary>
    public int BarWidth { get; init; } = 3;

    /// <summary>Columns (vertical) or rows (horizontal) between bars.</summary>
    public int Gap { get; init; } = 1;

    /// <summary>The style under each <see cref="Bar.Style"/>.</summary>
    public Style BarStyle { get; init; }

    public Style LabelStyle { get; init; }
    public Style ValueStyle { get; init; }

    /// <summary>A numeric format for the values (<c>"0"</c>, <c>"F1"</c>, <c>"N0"</c>…), in the current culture. Null hides them.</summary>
    public string? ValueFormat { get; init; } = "0";

    /// <summary>Bars that fit in <paramref name="area"/> at full <see cref="BarWidth"/>.</summary>
    private int FittingBars(Rect area)
    {
        int gap = Math.Max(0, Gap);
        int length = Direction == Direction.Vertical ? area.Width : area.Height;
        int size = Direction == Direction.Vertical ? Math.Max(1, BarWidth) : 1;
        return length < size ? 0 : Math.Min(_bars.Length, (length + gap) / (size + gap));
    }

    public void Render(Rect area, CellBuffer buffer)
    {
        area = area.Intersect(buffer.Area);
        int count = FittingBars(area);
        if (count == 0)
        {
            return;
        }

        ReadOnlySpan<Bar> bars = _bars[..count];
        double max = Max;
        if (double.IsNaN(max))
        {
            max = 0;
            foreach (Bar bar in bars)
            {
                max = bar.Value > max ? bar.Value : max;
            }
        }

        if (Direction == Direction.Vertical)
        {
            RenderVertical(area, buffer, bars, max);
        }
        else
        {
            RenderHorizontal(area, buffer, bars, max);
        }
    }

    private void RenderVertical(Rect area, CellBuffer buffer, ReadOnlySpan<Bar> bars, double max)
    {
        int barWidth = Math.Max(1, BarWidth);
        int step = barWidth + Math.Max(0, Gap);
        bool labels = false;
        foreach (Bar bar in bars)
        {
            labels |= bar.Label?.Length > 0;
        }

        int rows = area.Height - (labels ? 1 : 0);
        Span<char> scratch = stackalloc char[32];
        for (int i = 0; i < bars.Length; i++)
        {
            Bar bar = bars[i];
            int x = area.X + i * step;
            if (labels)
            {
                Centered(buffer, x, area.Bottom - 1, barWidth, bar.Label, LabelStyle);
            }

            if (rows <= 0)
            {
                continue;
            }

            Style style = BarStyle.Patch(bar.Style);
            int level = Eighths.Of(bar.Value, max, rows);
            int y = area.Y + rows - 1;
            for (int left = level; left > 0; left -= 8, y--)
            {
                buffer.SetRune(new Rect(x, y, barWidth, 1), left >= 8 ? Eighths.Full : Eighths.Lower(left), style);
            }

            ReadOnlySpan<char> value = Format(bar.Value, scratch);
            if (value.IsEmpty)
            {
                continue;
            }

            if (y >= area.Y)
            {
                Centered(buffer, x, y, barWidth, value, ValueStyle, Overflow.Ellipsis);
            }
            else
            {
                // Full height: no row above, so the value goes inside the top, in the bar's colors swapped.
                Style inside = ValueStyle.Fg.IsDefault
                    ? style.With(Attr.Reverse | ValueStyle.Attrs)
                    : new Style(ValueStyle.Fg, style.Fg, ValueStyle.Attrs);
                Centered(buffer, x, area.Y, barWidth, value, inside, Overflow.Ellipsis);
            }
        }
    }

    private void RenderHorizontal(Rect area, CellBuffer buffer, ReadOnlySpan<Bar> bars, double max)
    {
        int labelWidth = 0;
        int valueWidth = 0;
        Span<char> scratch = stackalloc char[32];
        foreach (Bar bar in bars)
        {
            labelWidth = Math.Max(labelWidth, TextWidth.Of(bar.Label));
            valueWidth = Math.Max(valueWidth, TextWidth.Of(Format(bar.Value, scratch)));
        }

        labelWidth = Math.Min(labelWidth, area.Width / 3);
        int barX = area.X + (labelWidth > 0 ? labelWidth + 1 : 0);
        int barCells = Math.Max(0, area.Right - barX - (valueWidth > 0 ? valueWidth + 1 : 0));
        int step = 1 + Math.Max(0, Gap);
        for (int i = 0; i < bars.Length; i++)
        {
            Bar bar = bars[i];
            int y = area.Y + i * step;
            if (labelWidth > 0)
            {
                buffer.SetString(area.X, y, bar.Label, LabelStyle, labelWidth);
            }

            int level = Eighths.Of(bar.Value, max, barCells);
            int full = level / 8;
            Style style = BarStyle.Patch(bar.Style);
            buffer.SetRune(new Rect(barX, y, full, 1), Eighths.Full, style);
            if (level % 8 > 0)
            {
                buffer.SetRune(barX + full, y, Eighths.Left(level % 8), style);
            }

            ReadOnlySpan<char> value = Format(bar.Value, scratch);
            if (!value.IsEmpty)
            {
                int x = barX + (level + 7) / 8 + (level > 0 ? 1 : 0);
                buffer.SetString(x, y, value, ValueStyle, area.Right - x);
            }
        }
    }

    /// <summary>The value formatted with <see cref="ValueFormat"/> into <paramref name="scratch"/>; empty when hidden, NaN or too long.</summary>
    private ReadOnlySpan<char> Format(double value, Span<char> scratch) =>
        ValueFormat is not null && !double.IsNaN(value) && value.TryFormat(scratch, out int written, ValueFormat) ? scratch[..written] : default;

    /// <summary>Text centered in <paramref name="width"/> columns; text that's too wide is cut (labels) or ends in '…' (values).</summary>
    private static void Centered(CellBuffer buffer, int x, int y, int width, ReadOnlySpan<char> text, Style style, Overflow overflow = Overflow.Clip)
    {
        int textWidth = TextWidth.Of(text);
        int offset = textWidth < width ? (width - textWidth) / 2 : 0;
        buffer.SetString(x + offset, y, text, style, width - offset, overflow);
    }
}
