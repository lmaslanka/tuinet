using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Tuinet;

/// <summary>
/// Diffs two cell buffers into VT bytes. Tracks the terminal's cursor and SGR state so it emits
/// only what changed: clean rows are skipped with a vectorized memcmp, gaps inside a row are
/// jumped (CUF) or re-emitted (whichever is fewer bytes), and style changes are SGR deltas.
/// </summary>
internal sealed class Renderer
{
    // Worst case per cell: SGR with every attribute + two RGB colors (~64) + cursor move + 4-byte glyph.
    private const int MaxCellBytes = 96;

    private static ReadOnlySpan<byte> SyncStart => "\u001b[?2026h"u8;
    private static ReadOnlySpan<byte> SyncEnd => "\u001b[?2026l"u8;
    private static ReadOnlySpan<byte> ShowCursor => "\u001b[?25h"u8;
    private static ReadOnlySpan<byte> HideCursor => "\u001b[?25l"u8;

    private readonly ColorMode _mode;
    private VtBuffer _out = null!;
    private int _width;
    private int _cx = -1;
    private int _cy = -1;
    private Style _pen;
    private Style _penSource;
    private bool _penKnown;
    private bool _cursorShown;

    public Renderer(ColorMode mode) => _mode = mode;

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
        bool started = false;

        for (int y = 0; y < current.Height; y++)
        {
            ReadOnlySpan<Cell> cur = current.Row(y);
            ReadOnlySpan<Cell> prev = previous.Row(y);
            int first = MemoryMarshal.AsBytes(cur).CommonPrefixLength(MemoryMarshal.AsBytes(prev));
            if (first == cur.Length * Unsafe.SizeOf<Cell>())
            {
                continue;
            }

            first /= Unsafe.SizeOf<Cell>();
            int last = cur.Length - 1;
            while (last > first && cur[last].Equals(prev[last]))
            {
                last--;
            }

            if (!started)
            {
                _out.Reserve(32);
                _out.Bytes(SyncStart);
                if (_cursorShown)
                {
                    _out.Bytes(HideCursor);
                    _cursorShown = false;
                }

                started = true;
            }

            _out.Reserve((last - first + 2) * MaxCellBytes + 64);
            RenderRow(cur, prev, y, first, last);
        }

        _out.Reserve(64);
        PlaceCursor(current);
        if (started)
        {
            _out.Bytes(SyncEnd);
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
