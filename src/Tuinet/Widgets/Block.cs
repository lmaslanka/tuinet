using System.Text;

namespace Tuinet.Widgets;

public enum Alignment : byte
{
    Left,
    Center,
    Right,
}

[Flags]
public enum Borders : byte
{
    None = 0,
    Top = 1,
    Right = 2,
    Bottom = 4,
    Left = 8,
    All = Top | Right | Bottom | Left,
}

public enum BorderType : byte
{
    /// <summary>┌─┐</summary>
    Plain,

    /// <summary>╭─╮</summary>
    Rounded,

    /// <summary>╔═╗</summary>
    Double,

    /// <summary>┏━┓</summary>
    Thick,
}

/// <summary>A frame with optional title and footer on its top and bottom edges.</summary>
public readonly ref struct Block : IWidget
{
    public Block()
    {
    }

    public Borders Borders { get; init; } = Borders.All;
    public BorderType BorderType { get; init; }
    public Style BorderStyle { get; init; }

    /// <summary>Layered over the whole area before the borders are drawn (keeps existing glyphs).</summary>
    public Style Style { get; init; }

    public ReadOnlySpan<char> Title { get; init; }
    public Alignment TitleAlignment { get; init; }

    /// <summary>Title style, layered over <see cref="BorderStyle"/>.</summary>
    public Style TitleStyle { get; init; }

    public ReadOnlySpan<char> Footer { get; init; }
    public Alignment FooterAlignment { get; init; }

    /// <summary>The area inside the borders.</summary>
    public Rect Inner(Rect area)
    {
        int left = (Borders & Borders.Left) != 0 ? 1 : 0;
        int top = (Borders & Borders.Top) != 0 ? 1 : 0;
        int right = (Borders & Borders.Right) != 0 ? 1 : 0;
        int bottom = (Borders & Borders.Bottom) != 0 ? 1 : 0;
        return new Rect(area.X + left, area.Y + top, area.Width - left - right, area.Height - top - bottom);
    }

    public void Render(Rect area, CellBuffer buffer)
    {
        area = area.Intersect(buffer.Area);
        if (area.IsEmpty)
        {
            return;
        }

        if (!Style.Equals(default))
        {
            buffer.SetStyle(area, Style);
        }

        Style style = Style.Patch(BorderStyle);
        (char h, char v, char tl, char tr, char bl, char br) = BorderType switch
        {
            BorderType.Rounded => ('─', '│', '╭', '╮', '╰', '╯'),
            BorderType.Double => ('═', '║', '╔', '╗', '╚', '╝'),
            BorderType.Thick => ('━', '┃', '┏', '┓', '┗', '┛'),
            _ => ('─', '│', '┌', '┐', '└', '┘'),
        };

        bool top = (Borders & Borders.Top) != 0;
        bool bottom = (Borders & Borders.Bottom) != 0 && area.Height > 1;
        bool left = (Borders & Borders.Left) != 0;
        bool right = (Borders & Borders.Right) != 0 && area.Width > 1;
        int x0 = area.X;
        int y0 = area.Y;
        int x1 = area.Right - 1;
        int y1 = area.Bottom - 1;

        if (top)
        {
            HLine(buffer, x0, x1, y0, h, style);
        }

        if (bottom)
        {
            HLine(buffer, x0, x1, y1, h, style);
        }

        if (left)
        {
            VLine(buffer, x0, y0, y1, v, style);
        }

        if (right)
        {
            VLine(buffer, x1, y0, y1, v, style);
        }

        if (top && left) buffer.SetRune(x0, y0, new Rune(tl), style);
        if (top && right) buffer.SetRune(x1, y0, new Rune(tr), style);
        if (bottom && left) buffer.SetRune(x0, y1, new Rune(bl), style);
        if (bottom && right) buffer.SetRune(x1, y1, new Rune(br), style);

        Style titleStyle = style.Patch(TitleStyle);
        int start = x0 + (left ? 1 : 0);
        int end = x1 - (right ? 1 : 0) + 1;
        if (!Title.IsEmpty)
        {
            Label(buffer, Title, TitleAlignment, start, end, y0, titleStyle);
        }

        if (!Footer.IsEmpty && area.Height > 1)
        {
            Label(buffer, Footer, FooterAlignment, start, end, y1, titleStyle);
        }
    }

    private static void Label(CellBuffer buffer, ReadOnlySpan<char> text, Alignment alignment, int start, int end, int y, Style style)
    {
        int available = end - start;
        if (available <= 0)
        {
            return;
        }

        int width = Math.Min(TextWidth.Of(text), available);
        int slack = available - width;
        int x = alignment switch
        {
            Alignment.Center => start + slack / 2,
            Alignment.Right => end - width - Math.Min(1, slack),
            _ => start + Math.Min(1, slack),
        };

        buffer.SetString(x, y, text, style, end - x);
    }

    private static void HLine(CellBuffer buffer, int x0, int x1, int y, char c, Style style)
    {
        var rune = new Rune(c);
        for (int x = x0; x <= x1; x++)
        {
            buffer.SetRune(x, y, rune, style);
        }
    }

    private static void VLine(CellBuffer buffer, int x, int y0, int y1, char c, Style style)
    {
        var rune = new Rune(c);
        for (int y = y0; y <= y1; y++)
        {
            buffer.SetRune(x, y, rune, style);
        }
    }
}

/// <summary>Blanks an area with a style. Render it first to draw a popup over existing content.</summary>
public readonly struct Clear(Style style) : IWidget
{
    public void Render(Rect area, CellBuffer buffer) => buffer.Fill(area, style);
}
