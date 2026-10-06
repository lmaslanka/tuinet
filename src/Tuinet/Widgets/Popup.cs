namespace Tuinet.Widgets;

/// <summary>
/// A box drawn over existing content: an optional drop shadow, a fill and a <see cref="Widgets.Block"/>, in one call.
/// The area includes the shadow (the last two columns and the last row), so the box itself is
/// <see cref="Frame"/>. Stateless: keep the rect you rendered into for hit-testing, e.g. click outside to close.
/// </summary>
/// <example><code>
/// var popup = new Popup { Block = new Block { Title = " confirm ", Style = dialog }, Shadow = true, Padding = 1 };
/// Rect area = frame.Area.Centered(popup.Outer(40, 3));
/// frame.Render(popup, area);
/// Rect inner = popup.Inner(area);
/// </code></example>
public readonly ref struct Popup : IWidget
{
    public Popup()
    {
    }

    /// <summary>Borders, title and footer; <see cref="Block.Style"/> is the fill. <c>Borders = None</c> for a plain box.</summary>
    public Block Block { get; init; } = new();

    /// <summary>Darken the cells below and to the right of the box, keeping their glyphs.</summary>
    public bool Shadow { get; init; }

    /// <summary>Layered over the cells under the shadow.</summary>
    public Style ShadowStyle { get; init; } = new(Color.Default, Color.Black, Attr.Dim);

    /// <summary>Blank rows inside the border on each side, and twice as many blank columns (cells are tall).</summary>
    public int Padding { get; init; }

    private int ShadowWidth => Shadow ? 2 : 0;
    private int ShadowHeight => Shadow ? 1 : 0;

    /// <summary>The box without the shadow.</summary>
    public Rect Frame(Rect area) => new(area.X, area.Y, area.Width - ShadowWidth, area.Height - ShadowHeight);

    /// <summary>The area inside the border and padding.</summary>
    public Rect Inner(Rect area) => Block.Inner(Frame(area)).Inset(2 * Padding, Padding);

    /// <summary>The area a popup needs for <paramref name="contentWidth"/> × <paramref name="contentHeight"/> of content.</summary>
    public Size Outer(int contentWidth, int contentHeight)
    {
        Borders b = Block.Borders;
        int borderWidth = ((b & Borders.Left) != 0 ? 1 : 0) + ((b & Borders.Right) != 0 ? 1 : 0);
        int borderHeight = ((b & Borders.Top) != 0 ? 1 : 0) + ((b & Borders.Bottom) != 0 ? 1 : 0);
        return new Size(
            contentWidth + borderWidth + 4 * Padding + ShadowWidth,
            contentHeight + borderHeight + 2 * Padding + ShadowHeight);
    }

    public void Render(Rect area, CellBuffer buffer)
    {
        Rect box = Frame(area);
        if (box.IsEmpty)
        {
            return;
        }

        if (Shadow)
        {
            // Offset by one row and two columns, so it looks as deep as it is tall.
            buffer.SetStyle(new Rect(box.Right, box.Y + 1, ShadowWidth, box.Height), ShadowStyle);
            buffer.SetStyle(new Rect(box.X + ShadowWidth, box.Bottom, box.Width, ShadowHeight), ShadowStyle);
        }

        buffer.Fill(box, Block.Style);
        buffer.Render(Block, box);
    }
}
