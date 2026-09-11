using System.Text;

namespace Tuinet;

public sealed class CellBuffer
{
    private Cell[] _cells;

    public CellBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;

        _cells = new Cell[width * height];

        Clear();
    }

    public int Width { get; private set; }
    public int Height { get; private set; }

    public Cell this[int x, int y]
    {
        get => _cells[Index(x, y)];
        set => _cells[Index(x, y)] = value;
    }

    public void Clear() => Array.Fill(_cells, Cell.Empty);

    public void Fill(Rect rect, Style style)
    {
        int x0 = Math.Max(rect.X, 0);
        int y0 = Math.Max(rect.Y, 0);
        int x1 = Math.Min(rect.X + rect.Width, Width);
        int y1 = Math.Min(rect.Y + rect.Height, Height);
        var cell = new Cell(new Rune(' '), style);

        for (int y = y0; y < y1; y++)
        {
            int row = y * Width;
            for (int x = x0; x < x1; x++)
                _cells[row + x] = cell;
        }
    }

    public void DrawBox(Rect rect, ReadOnlySpan<char> title = default, Style style = default)
    {
        if (rect.Width < 2 || rect.Height < 2)
        {
            return;
        }

        int x0 = rect.X;
        int y0 = rect.Y;
        int x1 = rect.X + rect.Width - 1;
        int y1 = rect.Y + rect.Height - 1;

        Put(x0, y0, new Rune('┌'), style);
        Put(x1, y0, new Rune('┐'), style);
        Put(x0, y1, new Rune('└'), style);
        Put(x1, y1, new Rune('┘'), style);

        for (int x = x0 + 1; x < x1; x++)
        {
            Put(x, y0, new Rune('─'), style);
            Put(x, y1, new Rune('─'), style);
        }

        for (int y = y0 + 1; y < y1; y++)
        {
            Put(x0, y, new Rune('│'), style);
            Put(x1, y, new Rune('│'), style);
        }

        if (title.IsEmpty)
        {
            return;
        }

        int max = rect.Width - 4;
        if (max < 1)
        {
            return;
        }

        int col = x0 + 2;
        int remaining = max;
        foreach (Rune rune in title.EnumerateRunes())
        {
            int width = Cell.WidthOf(rune);
            if (width <= Cell.ZeroWidth)
            {
                continue;
            }

            if (width > remaining)
            {
                break;
            }

            Put(col, y0, rune, style);
            
            col += width;
            remaining -= width;
        }
    }

    public void Put(Rect clip, int x, int y, Rune rune, Style style = default)
    {
        int absX = clip.X + x;
        int absY = clip.Y + y;

        int width = Cell.WidthOf(rune);
        if (width <= Cell.ZeroWidth)
        {
            return;
        }

        if (!clip.Contains(absX, absY) || !clip.Contains(absX + width - 1, absY))
        {
            return;
        }

        Put(absX, absY, rune, style);
    }

    public void Put(Rect clip, int x, int y, ReadOnlySpan<char> text, Style style = default)
    {
        int col = x;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int width = Cell.WidthOf(rune);
            if (width <= Cell.ZeroWidth)
            {
                continue;
            }

            Put(clip, col, y, rune, style);
            col += width;
        }
    }

    public void Put(int x, int y, Rune rune, Style style = default)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            return;
        }

        int width = Cell.WidthOf(rune);
        if (width <= Cell.ZeroWidth)
        {
            return;
        }

        if (x + width > Width)
        {
            return;
        }

        if (x > 0 && _cells[Index(x, y)].IsContinuation)
        {
            _cells[Index(x - 1, y)] = Cell.Empty;
        }

        if (x + 1 < Width && _cells[Index(x + 1, y)].IsContinuation)
        {
            _cells[Index(x + 1, y)] = Cell.Empty;
        }

        if (width == Cell.WideWidth && x + 2 < Width && _cells[Index(x + 2, y)].IsContinuation)
        {
            _cells[Index(x + 2, y)] = Cell.Empty;
        }

        _cells[Index(x, y)] = new Cell(rune, style);
        if (width == Cell.WideWidth)
        {
            _cells[Index(x + 1, y)] = new Cell(rune, style, isContinuation: true);
        }
    }

    public void Put(int x, int y, ReadOnlySpan<char> text, Style style = default)
    {
        int col = x;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int width = Cell.WidthOf(rune);
            if (width <= Cell.ZeroWidth)
            {
                continue;
            }

            Put(col, y, rune, style);

            col += width;
        }
    }

    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);

        Width = width;
        Height = height;

        _cells = new Cell[width * height];

        Clear();
    }

    public void CopyFrom(CellBuffer other)
    {
        if (other.Width != Width || other.Height != Height)
        {
            Resize(other.Width, other.Height);
        }

        other._cells.AsSpan().CopyTo(_cells);
    }

    private int Index(int x, int y) => y * Width + x;
}
