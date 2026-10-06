using System.Buffers;
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

/// <summary>Terminal cursor shapes (DECSCUSR). <see cref="Default"/> is whatever the user's terminal is set to.</summary>
public enum CursorShape : byte
{
    Default,
    BlinkingBlock,
    Block,
    BlinkingUnderline,
    Underline,
    BlinkingBar,
    Bar,
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

    /// <summary>
    /// Per row: holds a glyph that could join a neighbouring cell into one cluster on the terminal (an emoji,
    /// a mark, a regional indicator, a cluster…). Rows of plain Latin, CJK and box drawing never do, and the
    /// renderer skips its join checks for them. Set on write, reset by <see cref="Clear"/>.
    /// </summary>
    private bool[] _mayJoin;

    public CellBuffer(int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfLessThan(width, 1);
        ArgumentOutOfRangeException.ThrowIfLessThan(height, 1);
        Width = width;
        Height = height;
        _cells = new Cell[width * height];
        _mayJoin = new bool[height];
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

    /// <summary>Shape of the terminal cursor after this frame is presented.</summary>
    public CursorShape CursorShape { get; private set; }

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

    /// <summary>All cells, for direct writes (tests). Every row is then assumed to hold joinable glyphs.</summary>
    internal Span<Cell> Cells
    {
        get
        {
            _mayJoin.AsSpan(0, Height).Fill(true);
            return _cells.AsSpan(0, Width * Height);
        }
    }

    internal bool RowMayJoin(int y) => _mayJoin[y];

    /// <summary>Could this glyph join a neighbouring cell on the terminal? (See <see cref="_mayJoin"/>.)</summary>
    private static bool Joinable(Rune rune) => !Graphemes.Simple(rune.Value);

    /// <summary>
    /// Show the terminal cursor at (x, y) after this frame, in <paramref name="shape"/> (e.g. a bar for a text
    /// caret). Out-of-range positions hide it. The renderer sends the shape only when it changes, and the
    /// terminal's own shape comes back on exit.
    /// </summary>
    public void SetCursor(int x, int y, CursorShape shape = CursorShape.Default)
    {
        if ((uint)x >= (uint)Width || (uint)y >= (uint)Height)
        {
            HideCursor();
            return;
        }

        CursorX = x;
        CursorY = y;
        CursorShape = shape;
    }

    public void HideCursor()
    {
        CursorX = -1;
        CursorY = -1;
        CursorShape = CursorShape.Default;
    }

    /// <summary>
    /// Make the cells in <paramref name="area"/> a hyperlink to <paramref name="url"/> (OSC 8): terminals that
    /// support it open the URL on click (often Ctrl+click), others show the text as is. Draw the text first: text
    /// written over a cell later replaces its link, as it does its attributes; <see cref="SetStyle"/> keeps it.
    /// An empty <paramref name="url"/> removes links. Returns false, removing links instead, for a URL that can't
    /// be linked: one with control characters, longer than 2048 chars once encoded, or past the 4095th distinct URL
    /// of the process. Spaces and non-ASCII characters are percent-encoded.
    /// <code>
    /// int end = buffer.SetString(x, y, "docs", linkStyle);
    /// buffer.SetLink(new Rect(x, y, end - x, 1), "https://example.com/docs");
    /// </code>
    /// </summary>
    public bool SetLink(Rect area, ReadOnlySpan<char> url)
    {
        int id = url.IsEmpty ? 0 : Links.Intern(url);
        Rect r = area.Intersect(Area);
        for (int y = r.Y; y < r.Bottom; y++)
        {
            // Whole glyphs only: a wide glyph cut by the area's edge is linked as a whole.
            Span<Cell> row = RowSpan(y);
            int start = r.X > 0 && row[r.X].IsContinuation ? r.X - 1 : r.X;
            int end = r.Right < Width && row[r.Right].IsContinuation ? r.Right + 1 : r.Right;
            for (int x = start; x < end; x++)
            {
                Cell.SetLink(ref row[x], id);
            }
        }

        return id != 0 || url.IsEmpty;
    }

    /// <summary>Reset every cell to <see cref="Cell.Empty"/> and hide the cursor.</summary>
    public void Clear()
    {
        _cells.AsSpan(0, Width * Height).Fill(Cell.Empty);
        _mayJoin.AsSpan(0, Height).Clear();
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

        bool joinable = cell.IsGrapheme || Joinable(cell.RawRune);
        for (int y = r.Y; y < r.Bottom; y++)
        {
            Span<Cell> row = RowSpan(y);
            FixLeft(row, r.X);
            row.Slice(r.X, r.Width).Fill(cell);
            FixRight(row, r.Right);
            _mayJoin[y] |= joinable;
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
        bool joinable = Joinable(rune);
        for (int y = r.Y; y < r.Bottom; y++)
        {
            Span<Cell> row = RowSpan(y);
            _mayJoin[y] |= joinable;
            FixLeft(row, r.X);
            Span<Vector128<byte>> cells = MemoryMarshal.Cast<Cell, Vector128<byte>>(row.Slice(r.X, r.Width));
            for (int i = 0; i < cells.Length; i++)
            {
                cells[i] = (cells[i] & keep) | set;
            }

            FixRight(row, r.Right);
        }
    }

    /// <summary>' ' to '~'. (IndexOfAnyExceptInRange allocates on this path; SearchValues doesn't.)</summary>
    private static readonly SearchValues<char> PrintableAscii =
        SearchValues.Create(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~");

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
            int end = Write(row, x, limit - 1, text, style, ref _mayJoin[y], out _);
            return Write(row, end, limit, "…", style, ref _mayJoin[y], out _);
        }

        return Write(row, x, limit, text, style, ref _mayJoin[y], out _);
    }

    /// <summary>
    /// Write styled text, like <see cref="SetString"/> but with a style per run (layered over the text's base
    /// style, and over the cells underneath like any text write). Stops at the first glyph that doesn't fit.
    /// A run boundary inside a grapheme cluster moves to the cluster's end. With
    /// <see cref="Overflow.Ellipsis"/>, the '…' takes the style of the text it replaces.
    /// </summary>
    public int SetText(int x, int y, StyledText text, int maxWidth = int.MaxValue, Overflow overflow = Overflow.Clip)
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
        ref bool mayJoin = ref _mayJoin[y];
        ReadOnlySpan<char> chars = text.Text;
        ReadOnlySpan<StyledRun> runs = text.Runs;
        bool layer = text.Style != default;
        bool ellipsis = overflow == Overflow.Ellipsis && 2 * chars.Length > limit - x && TextWidth.Of(chars) > limit - x;
        int end = ellipsis ? limit - 1 : limit;
        int skip = text.RunOffset;   // chars of runs[r] already written
        int pos = 0;
        int col = x;
        Style style = text.Style;
        for (int r = 0; pos < chars.Length; r++)
        {
            int length;
            if (r < runs.Length)
            {
                length = runs[r].Length - skip;
                if (length <= 0)
                {
                    skip = -length;   // this run was used up by a cluster from the run before
                    continue;
                }

                style = layer ? text.Style.Patch(runs[r].Style) : runs[r].Style;
                skip = 0;
            }
            else
            {
                length = chars.Length - pos;
                style = text.Style;
            }

            int stop = Math.Min(pos + length, chars.Length);
            if (stop < chars.Length && chars[stop] >= 0x300)
            {
                // The boundary may fall inside a cluster: the cluster goes with the run it starts in.
                int boundary = pos;
                while (boundary < stop)
                {
                    boundary += Graphemes.Length(chars[boundary..]);
                }

                skip = boundary - stop;
                stop = boundary;
            }

            col = Write(row, col, end, chars[pos..stop], style, ref mayJoin, out bool complete);
            pos = stop;
            if (!complete)
            {
                break;
            }
        }

        return ellipsis ? Write(row, col, limit, "…", style, ref mayJoin, out _) : col;
    }

    /// <summary>
    /// Parse <see cref="Markup"/> (e.g. <c>"[b fg=#F5A623]q[/] quit"</c>) and write it with <see cref="SetText"/>.
    /// Parses on every call, without allocating for markup up to 1024 chars.
    /// </summary>
    public int SetMarkup(int x, int y, ReadOnlySpan<char> markup, Style style = default,
        int maxWidth = int.MaxValue, Overflow overflow = Overflow.Clip)
    {
        const int StackLimit = 1024;
        int maxRuns = markup.Length / 3 + 2;   // a run per text piece; pieces are separated by tags of 3+ chars
        Span<char> chars = markup.Length <= StackLimit ? stackalloc char[markup.Length] : new char[markup.Length];
        Span<StyledRun> runs = markup.Length <= StackLimit ? stackalloc StyledRun[maxRuns] : new StyledRun[maxRuns];
        return SetText(x, y, Markup.Parse(markup, chars, runs, style), maxWidth, overflow);
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

        if (_mayJoin.Length < height)
        {
            _mayJoin = new bool[height];
        }

        Clear();
    }

    /// <summary>The glyphs of one row as a string (for tests and debugging).</summary>
    public string RowText(int y)
    {
        var builder = new StringBuilder(Width);
        foreach (Cell cell in Row(y))
        {
            if (cell.IsGrapheme)
            {
                builder.Append(cell.Text);
            }
            else if (!cell.IsContinuation)
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
        _mayJoin[y] |= cell.IsGrapheme || Joinable(cell.RawRune);
        return width;
    }

    /// <summary>
    /// Write text into a row; sets <paramref name="mayJoin"/> when a joinable glyph is written.
    /// <paramref name="complete"/> is false when a glyph didn't fit before <paramref name="limit"/>.
    /// </summary>
    private static int Write(Span<Cell> row, int x, int limit, ReadOnlySpan<char> text, Style style, ref bool mayJoin, out bool complete)
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
                    return Finish(row, col, wrote, out complete, done: false);
                }

                // A combining mark (or other extender) after the last letter belongs to that letter's
                // cluster: leave the letter to the cluster path below.
                int stop = text.Slice(i, Math.Min(text.Length - i, run + 1)).IndexOfAnyExcept(PrintableAscii);
                if (stop > 0 && stop <= run && text[i + stop] >= 0x300)
                {
                    run = stop - 1;
                }

                if (run == 0)
                {
                    goto Cluster;
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

            Cluster:
            int length;
            Rune rune;
            int width;
            int id = -1;
            bool joinable = false;
            if (Graphemes.Simple(c) && (i + 1 == text.Length || Graphemes.Simple(text[i + 1])))
            {
                // One code point that can't join its neighbour (most CJK and symbol text). Simple chars
                // are never surrogates, so the rune needs no validation.
                length = 1;
                rune = Unsafe.BitCast<int, Rune>(c);
                width = UnicodeWidth.Of(c);
            }
            else
            {
                length = Graphemes.Next(text[i..], out rune, out width, out bool multi);
                joinable = multi || Joinable(rune);
                if (multi && width > 0)
                {
                    id = Graphemes.Intern(text.Slice(i, length));
                    if (id < 0)
                    {
                        width = UnicodeWidth.Of(rune.Value);   // store full: keep just the first code point
                    }
                }
            }

            i += length;
            if (width == 0)
            {
                continue;
            }

            if (col + width > limit)
            {
                return Finish(row, col, wrote, out complete, done: false);
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
            if (joinable)
            {
                mayJoin = true;
            }

            if (id >= 0)
            {
                Cell.PatchGrapheme(ref lead, id, width);
            }
            else
            {
                Cell.Patch(ref lead, rune, width);
            }
            if (width == 2)
            {
                row[col + 1] = continuation;
            }

            col += width;
        }

        return Finish(row, col, wrote, out complete, done: true);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static int Finish(Span<Cell> row, int col, bool wrote, out bool complete, bool done)
    {
        if (wrote)
        {
            FixRight(row, col);
        }

        complete = done;
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
