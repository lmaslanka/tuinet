using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics;
using System.Text;

namespace Tuinet;

public enum Overflow
{
    /// <summary>Stop at the last glyph that fits.</summary>
    Clip,

    /// <summary>Replace the tail with '…' when the text does not fit.</summary>
    Ellipsis,
}

/// <summary>
/// A grid of cells plus a cursor position. All writes clip to the buffer and keep wide glyphs
/// consistent: a wide glyph always owns a continuation cell, and overwriting either half blanks the other.
/// <para>
/// Text writes (<see cref="SetString"/>, <see cref="SetRune(int, int, Rune, Style)"/>) layer: a default foreground or
/// background in the style keeps the color already in the cell, so text drawn on a filled panel
/// keeps the panel's background. Attributes are replaced. <see cref="Fill(Rect, Style)"/> and
/// <see cref="SetCell"/> replace whole cells.
/// </para>
/// </summary>
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
    public Rect Area => new(0, 0, Width, Height);
    public Size Size => new(Width, Height);

    /// <summary>Terminal cursor column after this frame is presented, or -1 when hidden.</summary>
    public int CursorX { get; private set; } = -1;

    /// <summary>Terminal cursor row after this frame is presented, or -1 when hidden.</summary>
    public int CursorY { get; private set; } = -1;

    public bool CursorVisible => CursorX >= 0;

    public Cell this[int x, int y]
    {
        get
        {
            if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
            {
                ThrowOutOfRange(x, y);
            }

            return _cells[y * Width + x];
        }
    }

    public ReadOnlySpan<Cell> Row(int y) => RowSpan(y);

    internal Span<Cell> RowSpan(int y)
    {
        if ((uint)y >= (uint)Height)
        {
            ThrowOutOfRange(0, y);
        }

        return _cells.AsSpan(y * Width, Width);
    }

    internal Span<Cell> Cells => _cells.AsSpan(0, Width * Height);

    /// <summary>Show the terminal cursor at (x, y) after this frame. Out-of-range positions hide it.</summary>
    public void SetCursor(int x, int y)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            HideCursor();
            return;
        }

        CursorX = x;
        CursorY = y;
    }

    public void HideCursor()
    {
        CursorX = -1;
        CursorY = -1;
    }

    /// <summary>Reset every cell to <see cref="Cell.Empty"/> and hide the cursor.</summary>
    public void Clear()
    {
        Cells.Fill(Cell.Empty);
        HideCursor();
    }

    public void Fill(Rect area, Style style) => Fill(area, Cell.Blank(style));

    /// <summary>Fill <paramref name="area"/> with a narrow <paramref name="cell"/>.</summary>
    public void Fill(Rect area, Cell cell)
    {
        if (cell.Width != 1)
        {
            throw new ArgumentException("Fill requires a single-column cell.", nameof(cell));
        }

        Rect r = area.Intersect(Area);
        if (r.IsEmpty)
        {
            return;
        }

        for (int y = r.Y; y < r.Bottom; y++)
        {
            Span<Cell> row = RowSpan(y);
            FixLeft(row, r.X);
            row.Slice(r.X, r.Width).Fill(cell);
            FixRight(row, r.Right);
        }
    }

    /// <summary>
    /// Blank the glyphs in <paramref name="area"/>, layering <paramref name="style"/>: default colors keep
    /// the colors already there (so erasing an input row on a panel keeps the panel background).
    /// </summary>
    public void Erase(Rect area, Style style = default)
    {
        Rect r = area.Intersect(Area);
        for (int y = r.Y; y < r.Bottom; y++)
        {
            Span<Cell> row = RowSpan(y);
            FixLeft(row, r.X);
            Span<Cell> cells = row.Slice(r.X, r.Width);
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = Cell.Blank(Layer(style, cells[i].Style));
            }

            FixRight(row, r.Right);
        }
    }

    /// <summary>Layer <paramref name="style"/> over every cell in <paramref name="area"/>, keeping glyphs.</summary>
    public void SetStyle(Rect area, Style style)
    {
        if (style == default)
        {
            return;   // patching with the default style changes nothing
        }

        // Style.Patch on a 16-byte cell is (cell & keep) | set: one 128-bit op per cell. Non-default
        // colors replace their bytes; attributes are OR-ed in; glyph, width and flags are kept.
        Vector128<byte> keep = Vector128<byte>.AllBitsSet;
        if (!style.Fg.IsDefault)
        {
            keep &= ~FgBytes;
        }

        if (!style.Bg.IsDefault)
        {
            keep &= ~BgBytes;
        }

        var set = Unsafe.BitCast<Cell, Vector128<byte>>(new Cell(default, style, 0, CellFlags.None));
        Rect r = area.Intersect(Area);
        for (int y = r.Y; y < r.Bottom; y++)
        {
            Span<Vector128<byte>> row = MemoryMarshal.Cast<Cell, Vector128<byte>>(RowSpan(y).Slice(r.X, r.Width));
            for (int i = 0; i < row.Length; i++)
            {
                row[i] = (row[i] & keep) | set;
            }
        }
    }

    /// <summary>
    /// Fill <paramref name="area"/> with <paramref name="rune"/> (borders, rules, separators). Layers like
    /// <see cref="SetRune(int, int, Rune, Style)"/>: default colors keep the colors already there,
    /// attributes are replaced. Zero-width runes draw nothing; wide runes fill pairs of columns.
    /// </summary>
    public void SetRune(Rect area, Rune rune, Style style = default)
    {
        int width = UnicodeWidth.Of(rune.Value);
        Rect r = area.Intersect(Area);
        if (width == 0 || r.IsEmpty)
        {
            return;
        }

        if (width == 2)
        {
            for (int y = r.Y; y < r.Bottom; y++)
            {
                for (int x = r.X; x + 2 <= r.Right; x += 2)
                {
                    SetRune(x, y, rune, style);
                }
            }

            return;
        }

        // The new cell is (under & keep) | set: default colors keep the bytes underneath.
        Vector128<byte> keep = Vector128<byte>.Zero;
        if (style.Fg.IsDefault)
        {
            keep |= FgBytes;
        }

        if (style.Bg.IsDefault)
        {
            keep |= BgBytes;
        }

        var set = Unsafe.BitCast<Cell, Vector128<byte>>(new Cell(rune, style, 1, CellFlags.None));
        for (int y = r.Y; y < r.Bottom; y++)
        {
            Span<Cell> row = RowSpan(y);
            FixLeft(row, r.X);
            Span<Vector128<byte>> cells = MemoryMarshal.Cast<Cell, Vector128<byte>>(row.Slice(r.X, r.Width));
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = (cells[i] & keep) | set;
            }

            FixRight(row, r.Right);
        }
    }

    /// <summary>Bytes 4-7 of a cell: the foreground color.</summary>
    private static readonly Vector128<byte> FgBytes = Vector128.Create(0, 0, 0, 0, 255, 255, 255, 255, 0, 0, 0, 0, 0, 0, 0, (byte)0);

    /// <summary>Bytes 8-11 of a cell: the background color.</summary>
    private static readonly Vector128<byte> BgBytes = Vector128.Create(0, 0, 0, 0, 0, 0, 0, 0, 255, 255, 255, 255, 0, 0, 0, (byte)0);

    /// <summary>Place one glyph. Returns the columns written: 0 if it does not fit or is zero-width.</summary>
    public int SetRune(int x, int y, Rune rune, Style style = default)
    {
        int width = UnicodeWidth.Of(rune.Value);
        if (width == 0 || (uint)y >= (uint)Height || x < 0 || x + width > Width)
        {
            return 0;
        }

        Style under = _cells[y * Width + x].Style;
        return Place(x, y, new Cell(rune, Layer(style, under), width, CellFlags.None));
    }

    /// <summary>Place one glyph cell. Returns the columns written: 0 if it does not fit.</summary>
    public int SetCell(int x, int y, Cell cell)
    {
        if (cell.IsContinuation || cell.Width == 0)
        {
            return 0;
        }

        return Place(x, y, cell);
    }

    /// <summary>
    /// Write <paramref name="text"/> starting at column <paramref name="x"/>, using at most
    /// <paramref name="maxWidth"/> columns. Controls and combining marks are dropped.
    /// Returns the column after the last glyph written, so styled segments can be chained.
    /// </summary>
    public int SetString(int x, int y, ReadOnlySpan<char> text, Style style = default,
        int maxWidth = int.MaxValue, Overflow overflow = Overflow.Clip)
    {
        if ((uint)y >= (uint)Height || maxWidth <= 0)
        {
            return x;
        }

        int limit = x + Math.Min(maxWidth, Width - x);
        if (x >= limit)
        {
            return x;
        }

        Span<Cell> row = RowSpan(y);
        // A UTF-16 char is at most two columns, so short text can skip measuring.
        if (overflow == Overflow.Ellipsis && 2 * text.Length > limit - x && TextWidth.Of(text) > limit - x)
        {
            int end = Write(row, x, limit - 1, text, style);
            return Write(row, end, limit, "…", style);
        }

        return Write(row, x, limit, text, style);
    }

    public void Resize(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        if (width == Width && height == Height)
        {
            return;
        }

        Width = width;
        Height = height;
        if (_cells.Length < width * height)
        {
            _cells = new Cell[width * height];
        }

        Clear();
    }

    /// <summary>The glyphs of one row as a string (for tests and debugging).</summary>
    public string RowText(int y)
    {
        var builder = new StringBuilder(Width);
        foreach (Cell cell in Row(y))
        {
            if (!cell.IsContinuation)
            {
                builder.Append(cell.Rune);
            }
        }

        return builder.ToString();
    }

    /// <summary>All rows joined with '\n' (for snapshot tests).</summary>
    public override string ToString()
    {
        var builder = new StringBuilder((Width + 1) * Height);
        for (int y = 0; y < Height; y++)
        {
            if (y > 0)
            {
                builder.Append('\n');
            }

            builder.Append(RowText(y));
        }

        return builder.ToString();
    }

    private int Place(int x, int y, Cell cell)
    {
        int width = cell.Width;
        if ((uint)y >= (uint)Height || x < 0 || x + width > Width)
        {
            return 0;
        }

        Span<Cell> row = RowSpan(y);
        FixLeft(row, x);
        row[x] = cell;
        if (width == 2)
        {
            row[x + 1] = Cell.Continuation(cell.Style);
        }

        FixRight(row, x + width);
        return width;
    }

    private static int Write(Span<Cell> row, int x, int limit, ReadOnlySpan<char> text, Style style)
    {
        int col = x;
        bool wrote = false;
        int i = 0;

        // Cells are built by copying a prebuilt template and patching the rune/width: building
        // each cell's 10-byte style field by field is several times slower. With layering, the
        // template is rebuilt only where the colors underneath change.
        bool layered = style.Fg.IsDefault || style.Bg.IsDefault;
        var template = new Cell(Cell.Space, style, 1, CellFlags.None);
        var continuation = Cell.Continuation(style);
        ulong under = 0;
        bool primed = !layered;
        while (i < text.Length)
        {
            char c = text[i];

            // Fast path: a run of printable ASCII, one cell per char, in constant-color segments.
            if (col >= 0 && (uint)(c - 0x20) < 0x5F)
            {
                if (!wrote)
                {
                    FixLeft(row, col);
                    wrote = true;
                }

                int run = Math.Min(text.Length - i, limit - col);
                if (run <= 0)
                {
                    break;
                }

                Span<Cell> dest = row.Slice(col, run);
                ReadOnlySpan<char> chars = text.Slice(i, run);
                int n = 0;
                while (n < run)
                {
                    int segment = run;
                    if (layered)
                    {
                        ulong bits = Cell.ColorBits(ref dest[n]);
                        if (!primed || bits != under)
                        {
                            under = bits;
                            template = new Cell(Cell.Space, Layer(style, dest[n].Style), 1, CellFlags.None);
                            continuation = Cell.Continuation(template.Style);
                            primed = true;
                        }

                        segment = n + 1;
                        while (segment < run && Cell.ColorBits(ref dest[segment]) == bits)
                        {
                            segment++;
                        }
                    }

                    int written = WriteAscii(chars[n..segment], dest[n..segment], in template);
                    n += written;
                    if (n < segment)
                    {
                        break;
                    }
                }

                i += n;
                col += n;
                continue;
            }

            Rune rune;
            int width;
            if (c < 0x80)
            {
                rune = new Rune(c);
                width = (uint)(c - 0x20) < 0x5F ? 1 : 0;
                i++;
            }
            else
            {
                Rune.DecodeFromUtf16(text[i..], out rune, out int consumed);
                width = UnicodeWidth.Of(rune.Value);
                i += consumed;
            }

            if (width == 0)
            {
                continue;
            }

            if (col + width > limit)
            {
                break;
            }

            if (col < 0)
            {
                col += width;
                if (col > 0)
                {
                    row[0] = Cell.Blank(Layer(style, row[0].Style));
                }

                continue;
            }

            if (!wrote)
            {
                FixLeft(row, col);
                wrote = true;
            }

            ref Cell lead = ref row[col];
            if (layered)
            {
                ulong bits = Cell.ColorBits(ref lead);
                if (!primed || bits != under)
                {
                    under = bits;
                    template = new Cell(Cell.Space, Layer(style, lead.Style), 1, CellFlags.None);
                    continuation = Cell.Continuation(template.Style);
                    primed = true;
                }
            }

            lead = template;
            Cell.Patch(ref lead, rune, width);
            if (width == 2)
            {
                row[col + 1] = continuation;
            }

            col += width;
        }

        if (wrote)
        {
            FixRight(row, col);
        }

        return col;
    }

    /// <summary>
    /// Write printable ASCII until the first other char; returns chars written. Each cell is two
    /// 8-byte stores: the template's words with the rune (low 32 bits of the first word) replaced.
    /// </summary>
    private static int WriteAscii(ReadOnlySpan<char> chars, Span<Cell> dest, in Cell template)
    {
        int length = Math.Min(chars.Length, dest.Length);
        if (!BitConverter.IsLittleEndian)
        {
            int i = 0;
            for (; i < length && (uint)(chars[i] - 0x20) < 0x5F; i++)
            {
                dest[i] = template;
                Cell.Patch(ref dest[i], new Rune(chars[i]), 1);
            }

            return i;
        }

        ref ulong source = ref Unsafe.As<Cell, ulong>(ref Unsafe.AsRef(in template));
        ulong low = source & 0xFFFF_FFFF_0000_0000UL;
        ulong high = Unsafe.Add(ref source, 1);
        ref ulong target = ref Unsafe.As<Cell, ulong>(ref MemoryMarshal.GetReference(dest));
        ref char text = ref MemoryMarshal.GetReference(chars);
        int n = 0;
        for (; n < length; n++)
        {
            uint a = Unsafe.Add(ref text, n);
            if (a - 0x20 >= 0x5F)
            {
                break;
            }

            Unsafe.Add(ref target, 2 * n) = low | a;
            Unsafe.Add(ref target, 2 * n + 1) = high;
        }

        return n;
    }

    /// <summary>Default colors in <paramref name="style"/> keep the colors of <paramref name="under"/>; attributes are replaced.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static Style Layer(Style style, Style under) => new(
        style.Fg.IsDefault ? under.Fg : style.Fg,
        style.Bg.IsDefault ? under.Bg : style.Bg,
        style.Attrs);

    /// <summary>Writing at <paramref name="x"/> splits a wide glyph whose right half is there: blank its left half.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FixLeft(Span<Cell> row, int x)
    {
        if (x > 0 && x < row.Length && row[x].IsContinuation)
        {
            row[x - 1] = Cell.Blank(row[x - 1].Style);
        }
    }

    /// <summary>A write ended at <paramref name="x"/>; an orphaned right half there becomes a blank.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void FixRight(Span<Cell> row, int x)
    {
        if (x < row.Length && row[x].IsContinuation)
        {
            row[x] = Cell.Blank(row[x].Style);
        }
    }

    private static void ThrowOutOfRange(int x, int y) =>
        throw new ArgumentOutOfRangeException(null, $"Cell ({x},{y}) is outside the buffer.");
}
