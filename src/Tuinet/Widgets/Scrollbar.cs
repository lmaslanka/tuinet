using System.Text;

namespace Tuinet.Widgets;

/// <summary>When <see cref="ListView{TSource}"/>, <see cref="Table{TSource}"/> and <see cref="Paragraph"/> draw a scrollbar.</summary>
public enum ScrollbarMode : byte
{
    None,

    /// <summary>Only while the content is longer than the viewport.</summary>
    Auto,

    Always,
}

/// <summary>
/// A scroll position indicator: a thumb sized <see cref="Viewport"/> / <see cref="Content"/> of the track, placed at
/// <see cref="Position"/>. Drawn along the first column (vertical) or row (horizontal) of its area. With the default
/// '█' thumb, its ends use eighth-blocks for sub-cell precision.
/// </summary>
public readonly ref struct Scrollbar : IWidget
{
    public Scrollbar(int content, int viewport, int position)
    {
        Content = content;
        Viewport = viewport;
        Position = position;
    }

    /// <summary>Total length of what scrolls (items, lines, columns).</summary>
    public int Content { get; init; }

    /// <summary>How much of it is visible at once.</summary>
    public int Viewport { get; init; }

    /// <summary>Offset of the first visible unit, clamped to 0..<see cref="Content"/> - <see cref="Viewport"/>.</summary>
    public int Position { get; init; }

    public Direction Orientation { get; init; }

    public Style ThumbStyle { get; init; }
    public Style TrackStyle { get; init; }
    public char ThumbChar { get; init; } = '█';

    /// <summary>'\0' for '│' (vertical) or '─' (horizontal).</summary>
    public char TrackChar { get; init; }

    /// <summary>Use eighth-blocks at the thumb ends (only applies when <see cref="ThumbChar"/> is '█').</summary>
    public bool Smooth { get; init; } = true;

    public void Render(Rect area, CellBuffer buffer)
    {
        area = area.Intersect(buffer.Area);
        if (area.IsEmpty)
        {
            return;
        }

        bool vertical = Orientation == Direction.Vertical;
        int length = vertical ? area.Height : area.Width;
        int scale = Smooth && ThumbChar == '█' ? 8 : 1;
        Thumb(length * scale, scale, Content, Viewport, Position, out int start, out int size);
        int end = start + size;

        var thumb = new Rune(ThumbChar);
        var track = new Rune(TrackChar != '\0' ? TrackChar : vertical ? '│' : '─');
        var edge = new Style(ThumbStyle.Fg, TrackStyle.Bg, ThumbStyle.Attrs);
        for (int i = 0; i < length; i++)
        {
            int x = vertical ? area.X : area.X + i;
            int y = vertical ? area.Y + i : area.Y;
            int cellStart = i * scale;
            int covered = Math.Min(end, cellStart + scale) - Math.Max(start, cellStart);
            if (covered >= scale)
            {
                buffer.SetRune(x, y, thumb, ThumbStyle);
            }
            else if (covered <= 0)
            {
                buffer.SetRune(x, y, track, TrackStyle);
            }
            else
            {
                // Eighth-blocks fill a cell from the bottom (vertical) or the left (horizontal). A thumb end on
                // the other side draws the rest of the cell instead, in reverse video: common fonts lack those blocks.
                bool blockSide = vertical == (start > cellStart);
                int k = blockSide ? covered : scale - covered;
                Rune glyph = vertical ? Eighths.Lower(k) : Eighths.Left(k);
                buffer.SetRune(x, y, glyph, blockSide ? edge : edge.With(Attr.Reverse));
            }
        }
    }

    /// <summary>
    /// The position that puts the thumb's middle under cell (<paramref name="x"/>, <paramref name="y"/>) of a scrollbar
    /// drawn in <paramref name="area"/>: for clicks and drags on the track. Clamped to 0..content - viewport.
    /// </summary>
    public static int PositionAt(Rect area, int x, int y, int content, int viewport, Direction orientation = Direction.Vertical)
    {
        bool vertical = orientation == Direction.Vertical;
        int length = vertical ? area.Height : area.Width;
        int max = content - Math.Max(0, viewport);
        if (length <= 0 || max <= 0)
        {
            return 0;
        }

        Thumb(length * 8, 8, content, viewport, 0, out _, out int size);
        int free = length * 8 - size;
        if (free <= 0)
        {
            return 0;
        }

        int cell = Math.Clamp(vertical ? y - area.Y : x - area.X, 0, length - 1);
        int start = cell * 8 + 4 - size / 2;
        return (int)Math.Clamp(((long)start * max * 2 + free) / (2L * free), 0, max);
    }

    /// <summary>Thumb <paramref name="start"/> and <paramref name="size"/> in a track of <paramref name="track"/> units, at least <paramref name="minimum"/> long.</summary>
    private static void Thumb(int track, int minimum, int content, int viewport, int position, out int start, out int size)
    {
        viewport = Math.Max(0, viewport);
        if (content <= viewport)
        {
            start = 0;
            size = track;
            return;
        }

        size = (int)Math.Clamp(((long)track * viewport * 2 + content) / (2L * content), Math.Min(minimum, track), track);
        int max = content - viewport;
        long pos = Math.Clamp(position, 0, max);
        start = (int)(((track - size) * pos * 2 + max) / (2L * max));
    }
}

internal static class ScrollbarModeExtensions
{
    public static bool Shows(this ScrollbarMode mode, int content, int viewport) =>
        mode == ScrollbarMode.Always || (mode == ScrollbarMode.Auto && content > viewport);
}
