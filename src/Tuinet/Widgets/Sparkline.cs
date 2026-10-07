namespace Tuinet.Widgets;

/// <summary>
/// A value over time in eighth-block columns (▁▂▃▄▅▆▇█), one value per column, newest on the right. With several
/// rows the levels stack, so h rows give 8h levels. Given more values than columns, it shows the last ones.
/// Zero draws nothing; NaN and negative values leave a gap; any other positive value shows at least ▁.
/// </summary>
/// <remarks>
/// The two spans draw an app-owned ring buffer without a copy: pass the older part, then the newer part.
/// Only cells with a block are written, so <see cref="Style"/>'s background shows only behind the bars.
/// </remarks>
public readonly ref struct Sparkline : IWidget
{
    private readonly ReadOnlySpan<double> _older;
    private readonly ReadOnlySpan<double> _newer;

    /// <param name="values">The values, oldest first (or the older part of a ring buffer).</param>
    /// <param name="more">Values after <paramref name="values"/> (the newer part of a ring buffer).</param>
    public Sparkline(ReadOnlySpan<double> values, ReadOnlySpan<double> more = default)
    {
        _older = values;
        _newer = more;
    }

    /// <summary>The value of a full-height column; larger values are clamped. NaN: the largest visible value,
    /// so an old spike stops squashing the scale once it scrolls out.</summary>
    public double Max { get; init; } = double.NaN;

    public Style Style { get; init; }

    /// <summary>Layered over <see cref="Style"/> on the column of the largest visible value (the newest, on a tie).</summary>
    public Style MaxStyle { get; init; }

    public void Render(Rect area, CellBuffer buffer)
    {
        area = area.Intersect(buffer.Area);
        int total = _older.Length + _newer.Length;
        if (area.IsEmpty || total == 0)
        {
            return;
        }

        int count = Math.Min(area.Width, total);
        int first = total - count;
        int peak = -1;
        double peakValue = 0;
        for (int i = 0; i < count; i++)
        {
            double value = At(first + i);
            if (value > 0 && value >= peakValue)
            {
                peak = i;
                peakValue = value;
            }
        }

        double max = double.IsNaN(Max) ? peakValue : Max;
        Style peakStyle = Style.Patch(MaxStyle);
        int x = area.Right - count;
        for (int i = 0; i < count; i++)
        {
            int level = Eighths.Of(At(first + i), max, area.Height);
            Style style = i == peak ? peakStyle : Style;
            for (int y = area.Bottom - 1; level > 0; y--, level -= 8)
            {
                buffer.SetRune(x + i, y, level >= 8 ? Eighths.Full : Eighths.Lower(level), style);
            }
        }
    }

    private double At(int index) => index < _older.Length ? _older[index] : _newer[index - _older.Length];
}
