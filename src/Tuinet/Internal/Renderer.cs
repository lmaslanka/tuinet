using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Tuinet;

/// <summary>
/// Diffs two cell buffers into VT bytes. Tracks the terminal's cursor and SGR state so it emits
/// only what changed: clean rows are skipped with a vectorized memcmp, gaps inside a row are
/// jumped (CUF) or re-emitted (whichever is fewer bytes), and style changes are SGR deltas.
/// When a band of rows moved up or down (a scrolling list), the terminal moves them itself
/// (scroll margins + delete/insert line) and only the rows that are really new get painted.
/// </summary>
internal sealed class Renderer
{
    // Worst case per cell: SGR with every attribute + two RGB colors (~64) + cursor move + 4-byte glyph.
    private const int MaxCellBytes = 96;

    private static ReadOnlySpan<byte> SyncStart => "\u001b[?2026h"u8;
    private static ReadOnlySpan<byte> SyncEnd => "\u001b[?2026l"u8;
    private static ReadOnlySpan<byte> ShowCursor => "\u001b[?25h"u8;
    private static ReadOnlySpan<byte> HideCursor => "\u001b[?25l"u8;

    /// <summary>A dirty band must be at least this tall, and this many rows must match after a shift.</summary>
    private const int MinScrollRows = 3;

    private readonly ColorMode _mode;
    private readonly bool _scrollRegions;
    private VtBuffer _out = null!;
    private Cell[] _blank = [];
    private bool _started;
    private int _width;
    private int _cx = -1;
    private int _cy = -1;
    private Style _pen;
    private Style _penSource;
    private bool _penKnown;
    private bool _cursorShown;

    public Renderer(ColorMode mode, bool scrollRegions = true)
    {
        _mode = mode;
        _scrollRegions = scrollRegions;
    }

    public ColorMode Mode => _mode;

    /// <summary>The screen was just cleared with SGR reset: pen is default, cursor position unknown.</summary>
    public void AfterClear()
    {
        _pen = default;
        _penSource = default;
        _penKnown = true;
        _cx = -1;
        _cy = -1;
    }

    /// <summary>Something outside the renderer touched the terminal; assume nothing.</summary>
    public void Forget()
    {
        _penKnown = false;
        _cx = -1;
        _cy = -1;
    }

    public void Render(CellBuffer current, CellBuffer previous, VtBuffer output)
    {
        _out = output;
        _width = current.Width;
        _started = false;
        int height = current.Height;

        // source[y]: the previous row the terminal shows at y (-1: a blank row left by a scroll).
        // prefix[y]: cells row y has in common with that row; the width when clean, -1 when unknown.
        Span<int> source = height <= 256 ? stackalloc int[height] : new int[height];
        Span<int> prefix = height <= 256 ? stackalloc int[height] : new int[height];
        for (int y = 0; y < height; y++)
        {
            source[y] = y;
            prefix[y] = CommonPrefix(current.Row(y), previous.Row(y));
        }

        if (_scrollRegions)
        {
            Scroll(current, previous, source, prefix);
        }

        for (int y = 0; y < height; y++)
        {
            if (prefix[y] == _width)
            {
                continue;   // clean (the common case): don't touch the rows again
            }

            ReadOnlySpan<Cell> cur = current.Row(y);
            ReadOnlySpan<Cell> prev = source[y] >= 0 ? previous.Row(source[y]) : BlankRow();
            int first = prefix[y] >= 0 ? prefix[y] : CommonPrefix(cur, prev);
            if (first == cur.Length)
            {
                continue;
            }

            int last = cur.Length - 1;
            while (last > first && cur[last].Equals(prev[last]))
            {
                last--;
            }

            Begin();
            _out.Reserve((last - first + 2) * MaxCellBytes + 64);
            RenderRow(cur, prev, y, first, last);
        }

        _out.Reserve(64);
        PlaceCursor(current);
        if (_started)
        {
            _out.Bytes(SyncEnd);
        }
    }

    private void Begin()
    {
        if (_started)
        {
            return;
        }

        _out.Reserve(32);
        _out.Bytes(SyncStart);
        if (_cursorShown)
        {
            _out.Bytes(HideCursor);
            _cursorShown = false;
        }

        _started = true;
    }

    private static int CommonPrefix(ReadOnlySpan<Cell> a, ReadOnlySpan<Cell> b) =>
        MemoryMarshal.AsBytes(a).CommonPrefixLength(MemoryMarshal.AsBytes(b)) / Unsafe.SizeOf<Cell>();

    private static bool Same(ReadOnlySpan<Cell> a, ReadOnlySpan<Cell> b) =>
        MemoryMarshal.AsBytes(a).SequenceEqual(MemoryMarshal.AsBytes(b));

    /// <summary>What delete/insert line leaves behind when the pen is reset: default-styled spaces.</summary>
    private ReadOnlySpan<Cell> BlankRow()
    {
        if (_blank.Length != _width)
        {
            _blank = new Cell[_width];
            _blank.AsSpan().Fill(Cell.Empty);
        }

        return _blank;
    }

    /// <summary>For each band of dirty rows, find a vertical shift that lines most rows up with the old screen.</summary>
    private void Scroll(CellBuffer current, CellBuffer previous, Span<int> source, Span<int> prefix)
    {
        int y = 0;
        while (y < source.Length)
        {
            if (prefix[y] == _width)
            {
                y++;
                continue;
            }

            int top = y;
            while (y < source.Length && prefix[y] != _width)
            {
                y++;
            }

            int bottom = y - 1;
            if (bottom - top + 1 >= MinScrollRows)
            {
                int shift = FindShift(current, previous, top, bottom);
                if (shift != 0)
                {
                    ApplyShift(top, bottom, shift, source, prefix);
                }
            }
        }
    }

    /// <summary>
    /// The shift k (current row y shows previous row y + k) that matches the most rows in the band, or 0.
    /// Candidates come from probe rows: the nearest previous row equal to the probe gives a k to score.
    /// </summary>
    private static int FindShift(CellBuffer current, CellBuffer previous, int top, int bottom)
    {
        int length = bottom - top + 1;
        int best = 0;
        int bestMatches = 0;
        Span<int> probes = [top + length / 2, top + length / 4, top + 3 * length / 4];
        foreach (int probe in probes)
        {
            ReadOnlySpan<Cell> row = current.Row(probe);
            for (int distance = 1; distance <= length - MinScrollRows; distance++)
            {
                int found = 0;
                if (probe + distance <= bottom && Same(row, previous.Row(probe + distance)))
                {
                    found = distance;
                }
                else if (probe - distance >= top && Same(row, previous.Row(probe - distance)))
                {
                    found = -distance;
                }

                if (found == 0)
                {
                    continue;
                }

                if (found != best)
                {
                    int matches = CountMatches(current, previous, top, bottom, found);
                    if (matches > bestMatches)
                    {
                        best = found;
                        bestMatches = matches;
                    }
                }

                break;
            }

            if (bestMatches == length - Math.Abs(best))
            {
                break;   // every row that can line up does
            }
        }

        // Worth it only if a good share of the band lines up: the other rows are diffed against shifted content.
        return bestMatches >= MinScrollRows && 2 * bestMatches >= length - Math.Abs(best) ? best : 0;
    }

    private static int CountMatches(CellBuffer current, CellBuffer previous, int top, int bottom, int shift)
    {
        int matches = 0;
        for (int y = Math.Max(top, top - shift); y <= Math.Min(bottom, bottom - shift); y++)
        {
            if (Same(current.Row(y), previous.Row(y + shift)))
            {
                matches++;
            }
        }

        return matches;
    }

    /// <summary>
    /// Move rows [top, bottom] by <paramref name="shift"/> on the terminal: set scroll margins, then delete
    /// lines at the top (content moves up) or insert them (content moves down), then clear the margins.
    /// IL/DL are VT102, so this works more widely than SU/SD (the Linux console has no SU).
    /// </summary>
    private void ApplyShift(int top, int bottom, int shift, Span<int> source, Span<int> prefix)
    {
        Begin();
        _out.Reserve(96);

        // New lines are filled with the current background (BCE): reset the pen so they are plain blanks.
        SetPen(default);
        _out.Bytes("\u001b["u8);
        _out.Int(top + 1);
        _out.Byte((byte)';');
        _out.Int(bottom + 1);
        _out.Bytes("r\u001b["u8);
        _out.Int(top + 1);
        _out.Byte((byte)'H');
        _out.Bytes("\u001b["u8);
        int count = Math.Abs(shift);
        if (count > 1)
        {
            _out.Int(count);
        }

        _out.Byte(shift > 0 ? (byte)'M' : (byte)'L');

        // Clear the margins before painting: a line feed at a bottom margin would scroll the band again.
        _out.Bytes("\u001b[r"u8);
        _cx = -1;
        _cy = -1;

        for (int y = top; y <= bottom; y++)
        {
            int from = y + shift;
            source[y] = from >= top && from <= bottom ? from : -1;
            prefix[y] = -1;
        }
    }

    private void RenderRow(ReadOnlySpan<Cell> cur, ReadOnlySpan<Cell> prev, int y, int first, int last)
    {
        int width = cur.Length;
        int x = first;

        // Defensive: CellBuffer's writes keep wide glyphs whole (splitting one blanks the other
        // half), so a dirty run never starts on a right half. Should that invariant ever break,
        // back up to the left half rather than emit half a glyph.
        if (x > 0 && (cur[x].IsContinuation || prev[x].IsContinuation))
        {
            x--;
        }

        int forceUntil = -1;
        bool positioned = false;
        while (x < width)
        {
            bool dirty = x <= forceUntil || (x <= last && !cur[x].Equals(prev[x]));
            if (!dirty)
            {
                if (x > last)
                {
                    break;
                }

                int next = x + 1;
                while (next <= last && cur[next].Equals(prev[next]))
                {
                    next++;
                }

                if (next > last)
                {
                    break;
                }

                if (positioned && ReemitIsCheaper(cur, x, next))
                {
                    forceUntil = next - 1;
                }
                else
                {
                    x = next;
                    positioned = false;
                }

                continue;
            }

            // Landing on the right half of a wide glyph: emit from its left half instead.
            if (cur[x].IsContinuation && x > 0 && cur[x - 1].Width == 2 && !positioned)
            {
                x--;
            }

            if (!positioned)
            {
                MoveTo(x, y);
                positioned = true;
            }

            int advance = Emit(in cur[x], x);

            // Overwriting the left half of a wide glyph with a narrow one: the terminal blanks the
            // right half, so the cell after it must be repainted even if it compares equal.
            if (advance == 1 && prev[x].Width == 2)
            {
                forceUntil = Math.Max(forceUntil, x + 1);
            }

            x += advance;
        }
    }

    /// <summary>Is writing the clean cells in [from, to) shorter than a CUF jump over them?</summary>
    private bool ReemitIsCheaper(ReadOnlySpan<Cell> cur, int from, int to)
    {
        int gap = to - from;
        int jumpCost = gap == 1 ? 3 : gap < 10 ? 4 : gap < 100 ? 5 : 6;
        if (gap >= jumpCost || !_penKnown)
        {
            return false;
        }

        for (int i = from; i < to; i++)
        {
            Cell c = cur[i];
            if (c.Width != 1 || c.Rune.Value >= 0x80 || !c.Style.Equals(_penSource))
            {
                return false;
            }
        }

        return true;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Emit(in Cell cell, int x)
    {
        int advance = cell.Width;
        Rune rune = cell.Rune;
        if (advance == 0 || (advance == 2 && x + 1 >= _width))
        {
            // Orphaned continuation, uninitialized cell, or a wide glyph with no room: draw a blank.
            rune = Cell.Space;
            advance = 1;
        }

        if (!_penKnown || !cell.Style.Equals(_penSource))
        {
            SetPen(cell.Style);
        }

        int v = rune.Value;
        if (v < 0x80)
        {
            _out.Byte((byte)v);
        }
        else
        {
            _out.Rune(rune);
        }

        _cx += advance;
        if (_cx >= _width)
        {
            // Last column: the cursor may be in the pending-wrap state; don't trust the column.
            _cx = -1;
        }

        return advance;
    }

    private void PlaceCursor(CellBuffer current)
    {
        if (!current.CursorVisible)
        {
            if (_cursorShown)
            {
                _out.Bytes(HideCursor);
                _cursorShown = false;
            }

            return;
        }

        if (_cursorShown && _cx == current.CursorX && _cy == current.CursorY)
        {
            return;
        }

        MoveTo(current.CursorX, current.CursorY);
        if (!_cursorShown)
        {
            _out.Bytes(ShowCursor);
            _cursorShown = true;
        }
    }

    private void MoveTo(int x, int y)
    {
        if (_cy == y && _cx == x)
        {
            return;
        }

        if (_cy == y && _cx >= 0 && x > _cx)
        {
            int n = x - _cx;
            _out.Bytes("\u001b["u8);
            if (n > 1)
            {
                _out.Int(n);
            }

            _out.Byte((byte)'C');
        }
        else if (x == 0 && _cy >= 0 && y == _cy + 1)
        {
            _out.Bytes("\r\n"u8);
        }
        else if (x == 0 && y == _cy)
        {
            _out.Byte((byte)'\r');
        }
        else
        {
            _out.Bytes("\u001b["u8);
            if (x == 0 && y == 0)
            {
                _out.Byte((byte)'H');
            }
            else
            {
                _out.Int(y + 1);
                if (x > 0)
                {
                    _out.Byte((byte)';');
                    _out.Int(x + 1);
                }

                _out.Byte((byte)'H');
            }
        }

        _cx = x;
        _cy = y;
    }

    private void SetPen(Style source)
    {
        if (_penKnown && source.Equals(_penSource))
        {
            return;
        }

        Style target = ColorMapping.Map(source, _mode);
        _penSource = source;
        if (_penKnown && target.Equals(_pen))
        {
            return;
        }

        _out.Bytes("\u001b["u8);
        bool sep = false;
        Style from = _pen;
        if (!_penKnown)
        {
            _out.Byte((byte)'0');
            sep = true;
            from = default;
        }

        Attr removed = from.Attrs & ~target.Attrs;
        Attr added = target.Attrs & ~from.Attrs;
        if ((removed & (Attr.Bold | Attr.Dim)) != 0)
        {
            Param(ref sep, 22);
            added |= target.Attrs & (Attr.Bold | Attr.Dim);
        }

        if ((removed & Attr.Italic) != 0) Param(ref sep, 23);
        if ((removed & Attr.Underline) != 0) Param(ref sep, 24);
        if ((removed & Attr.Blink) != 0) Param(ref sep, 25);
        if ((removed & Attr.Reverse) != 0) Param(ref sep, 27);
        if ((removed & Attr.Hidden) != 0) Param(ref sep, 28);
        if ((removed & Attr.Strike) != 0) Param(ref sep, 29);
        if ((added & Attr.Bold) != 0) Param(ref sep, 1);
        if ((added & Attr.Dim) != 0) Param(ref sep, 2);
        if ((added & Attr.Italic) != 0) Param(ref sep, 3);
        if ((added & Attr.Underline) != 0) Param(ref sep, 4);
        if ((added & Attr.Blink) != 0) Param(ref sep, 5);
        if ((added & Attr.Reverse) != 0) Param(ref sep, 7);
        if ((added & Attr.Hidden) != 0) Param(ref sep, 8);
        if ((added & Attr.Strike) != 0) Param(ref sep, 9);

        if (!target.Fg.Equals(from.Fg))
        {
            ColorParams(ref sep, target.Fg, foreground: true);
        }

        if (!target.Bg.Equals(from.Bg))
        {
            ColorParams(ref sep, target.Bg, foreground: false);
        }

        _out.Byte((byte)'m');
        _pen = target;
        _penKnown = true;
    }

    private void ColorParams(ref bool sep, Color color, bool foreground)
    {
        switch (color.Kind)
        {
            case ColorKind.Default:
                Param(ref sep, foreground ? 39 : 49);
                return;
            case ColorKind.Indexed when color.Index < 8:
                Param(ref sep, (foreground ? 30 : 40) + color.Index);
                return;
            case ColorKind.Indexed when color.Index < 16:
                Param(ref sep, (foreground ? 90 : 100) + color.Index - 8);
                return;
            case ColorKind.Indexed:
                Param(ref sep, foreground ? 38 : 48);
                _out.Bytes(";5;"u8);
                _out.Int(color.Index);
                return;
            default:
                Param(ref sep, foreground ? 38 : 48);
                _out.Bytes(";2;"u8);
                _out.Int(color.R);
                _out.Byte((byte)';');
                _out.Int(color.G);
                _out.Byte((byte)';');
                _out.Int(color.B);
                return;
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private void Param(ref bool sep, int value)
    {
        if (sep)
        {
            _out.Byte((byte)';');
        }

        _out.Int(value);
        sep = true;
    }
}
