namespace Tuinet.Widgets;

/// <summary>
/// A one-row bar with three segments: <see cref="Left"/>, <see cref="Center"/> and <see cref="Right"/>. The right one
/// is placed first, then the left in the room before it, then the center in the gap between them (centered on the row
/// when it fits there); a segment that doesn't fit is cut with an ellipsis, and neighbors keep a column apart. The
/// segments are drawn over <see cref="Style"/>, which fills the row.
/// <code>
/// buffer.Render(new StatusBar { Left = "NORMAL", Center = fileName, Right = position, Style = bar }, row);
/// </code>
/// For key hints in a segment's place, render a <see cref="KeyHints"/> into the room the bar leaves: it drops whole
/// hints where a segment would be cut.
/// </summary>
public readonly ref struct StatusBar : IWidget
{
    public StyledText Left { get; init; }

    public StyledText Center { get; init; }

    public StyledText Right { get; init; }

    /// <summary>Fills the row, layered over what's there (default colors keep them); the segments are layered over it.</summary>
    public Style Style { get; init; }

    public void Render(Rect area, CellBuffer buffer)
    {
        area = area.Intersect(buffer.Area);
        if (area.IsEmpty)
        {
            return;
        }

        int y = area.Y;
        buffer.Erase(new Rect(area.X, y, area.Width, 1), Style);

        int rightX = area.Right - Math.Min(TextWidth.Of(Right), area.Width);
        buffer.SetText(rightX, y, Right, area.Right - rightX, Overflow.Ellipsis);
        int rightGap = rightX < area.Right ? 1 : 0;   // a blank column beside a segment that shows

        int leftEnd = buffer.SetText(area.X, y, Left, rightX - rightGap - area.X, Overflow.Ellipsis);
        int leftGap = leftEnd > area.X ? 1 : 0;

        if (!Center.IsEmpty)
        {
            int from = leftEnd + leftGap;
            int to = rightX - rightGap;
            int width = TextWidth.Of(Center);
            int x = Math.Clamp(area.X + (area.Width - width) / 2, from, Math.Max(from, to - width));
            buffer.SetText(x, y, Center, to - x, Overflow.Ellipsis);
        }
    }
}
