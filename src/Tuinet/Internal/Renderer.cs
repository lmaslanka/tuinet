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
/// Runs of blank cells are erased (EL to the end of the row, ECH inside it) when that is fewer bytes.
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
    private readonly bool _erase;
    private VtBuffer _out = null!;
    private Cell[] _blank = [];
    private bool _started;
    private int _width;
    private int _cx = -1;
    private int _cy = -1;
    private int _top;
    private Style _pen;
    private Style _penSource;
    private bool _penKnown;
    private bool _cursorShown;

    public Renderer(ColorMode mode, bool scrollRegions = true, bool eraseSequences = true)
    {
        _mode = mode;
        _scrollRegions = scrollRegions;
        _erase = eraseSequences;
    }

    public ColorMode Mode => _mode;

    /// <summary>
    /// Screen row of buffer row 0. Zero on the alternate screen; in inline mode, where the live band starts.
    /// All cursor addressing and scroll margins are offset by it.
    /// </summary>
    public int Top
    {
        get => _top;
        set
        {
            // The tracked cursor stays where it is on screen: re-express it relative to the new top.
            _cy = _cy < 0 || _cy + _top - value < 0 ? -1 : _cy + _top - value;
            _cx = _cy < 0 ? -1 : _cx;
            _top = value;
        }
    }

    /// <summary>Row (buffer coordinates) the terminal cursor is on, or -1 if unknown.</summary>
    public int CursorRow => _cy;

    /// <summary>
    /// Inline mode: after a frame with no visible cursor, leave the cursor at the start of row 0. A terminal that
    /// reflows on resize keeps it on that row's first line, so the cursor position reports where the band starts.
    /// </summary>
    public bool ParkCursor { get; set; }

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
        Open(output);
        Frame(current, previous);
    }

    /// <summary>Start writing to <paramref name="output"/>; inline operations and <see cref="Frame"/> share one synchronized update.</summary>
    public void Open(VtBuffer output)
    {
        _out = output;
        _started = false;
    }

    /// <summary>Diff <paramref name="current"/> against <paramref name="previous"/> and end the synchronized update.</summary>
    public void Frame(CellBuffer current, CellBuffer previous)
    {
        _width = current.Width;
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
            if (current.RowMayJoin(y))
            {
                RenderRow<JoinChecks>(cur, prev, y, first, last);
            }
            else
            {
                RenderRow<NoJoinChecks>(cur, prev, y, first, last);
            }
        }

        _out.Reserve(64);
        PlaceCursor(current);
        if (_started)
        {
            _out.Bytes(SyncEnd);
            _started = false;
        }
    }

    /// <summary>End the synchronized update started by inline operations without rendering a frame.</summary>
    public void Close()
    {
        if (_started)
        {
            _out.Reserve(16);
            _out.Bytes(SyncEnd);
            _started = false;
        }
    }

    /// <summary>Erase from buffer row <paramref name="y"/> to the end of the screen, with the default background.</summary>
    public void EraseBelow(int y)
    {
        Begin();
        _out.Reserve(32);
        SetPen(default);
        MoveTo(0, y);
        _out.Bytes("\u001b[J"u8);
    }

    /// <summary>
    /// Scroll the whole screen up <paramref name="lines"/> lines (into the scrollback) with line feeds on its
    /// last row, <paramref name="screenHeight"/> - 1. The new rows are blank. Leaves the cursor on the last row.
    /// </summary>
    public void ScrollScreen(int screenHeight, int lines)
    {
        Begin();
        _out.Reserve(32 + lines);
        SetPen(default);
        MoveTo(0, screenHeight - 1 - Top);
        for (int i = 0; i < lines; i++)
        {
            _out.Byte((byte)'\n');
        }
    }

    /// <summary>Draw <paramref name="row"/> on buffer row <paramref name="y"/>, which must be blank on screen.</summary>
    public void PrintRow(ReadOnlySpan<Cell> row, int y)
    {
        _width = row.Length;
        ReadOnlySpan<Cell> blank = BlankRow();
        int first = CommonPrefix(row, blank);
        if (first == row.Length)
        {
            return;
        }

        int last = row.Length - 1;
        while (last > first && row[last].Equals(blank[last]))
        {
            last--;
        }

        Begin();
        _out.Reserve((last - first + 2) * MaxCellBytes + 64);
        RenderRow<JoinChecks>(row, blank, y, first, last);
    }

    /// <summary>
    /// Put the cursor at the start of the line after buffer row <paramref name="y"/> (scrolling at the bottom),
    /// or at the start of row 0 when <paramref name="y"/> is negative.
    /// </summary>
    public void LineAfter(int y)
    {
        _out.Reserve(48);
        SetPen(default);
        MoveTo(0, Math.Max(0, y));
        if (y >= 0)
        {
            _out.Bytes("\r\n"u8);
        }

        _cx = -1;
        _cy = -1;
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
        _out.Int(Top + top + 1);
        _out.Byte((byte)';');
        _out.Int(Top + bottom + 1);
        _out.Bytes("r\u001b["u8);
        _out.Int(Top + top + 1);
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

    /// <summary>
    /// Rows whose glyphs could merge with a neighbour on the terminal (CellBuffer tracks it per row) take
    /// <see cref="JoinChecks"/>; all others take <see cref="NoJoinChecks"/>, which the JIT compiles to the
    /// plain loop with no cluster logic at all.
    /// </summary>
    private void RenderRow<TJoin>(ReadOnlySpan<Cell> cur, ReadOnlySpan<Cell> prev, int y, int first, int last)
        where TJoin : struct, IJoinPolicy
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
        int spacesUntil = -1;   // blanks up to here were already judged cheaper to write than to erase

        // The cell just emitted, to keep the next one from joining it into one cluster on the terminal.
        Rune previous = default;
        bool previousComplex = false;
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

            ref readonly Cell cell = ref cur[x];
            // Erasing pays off only for runs of 4+ blanks. Most dirty cells aren't spaces: test that first, then the
            // 4th cell, so neither glyphs nor the single spaces between words get as far as TryErase.
            if (_erase && cell.RawRune.Value == ' ' && x + 3 < width && x > spacesUntil
                && IsErasable(in cur[x + 3]) && IsErasable(in cell))
            {
                int end = TryErase(cur, prev, x, last, y, ref positioned, ref forceUntil, ref spacesUntil);
                if (end > x)
                {
                    x = end;
                    previous = default;
                    previousComplex = false;
                    continue;
                }
            }

            bool contiguous = positioned;
            if (!positioned)
            {
                MoveTo(x, y);
                positioned = true;
            }

            int advance;
            if (!TJoin.Enabled || (!previousComplex && Graphemes.Simple(cell.RawRune.Value) && !cell.IsGrapheme))
            {
                advance = Emit(in cell, x);   // the common case: nothing here can join a neighbour
            }
            else
            {
                advance = EmitComplex(in cell, x, y, contiguous ? previous : default, ref positioned, ref forceUntil);
                previousComplex = !Graphemes.Simple(cell.RawRune.Value) || cell.IsGrapheme;
            }

            if (TJoin.Enabled)
            {
                previous = cell.RawRune;
            }

            // Overwriting the left half of a wide glyph with a narrow one: the terminal blanks the
            // right half, so the cell after it must be repainted even if it compares equal.
            if (advance == 1 && prev[x].Width == 2)
            {
                forceUntil = Math.Max(forceUntil, x + 1);
            }

            x += advance;
        }
    }

    /// <summary>
    /// A blank whose look is just its background: erase sequences fill cells with the background and nothing
    /// else, so a blank that is underlined, struck through or reversed must be written as a space.
    /// </summary>
    private static bool IsErasable(in Cell cell) =>
        cell.RawRune.Value == ' ' && cell.Width == 1 && !cell.IsGrapheme
        && (cell.Style.Attrs & (Attr.Underline | Attr.Reverse | Attr.Strike)) == 0;

    /// <summary>
    /// Erase the run of blanks with one background that starts at dirty cell <paramref name="x"/>, if that is
    /// fewer bytes than writing them: EL (<c>CSI K</c>, 3 bytes) when the run reaches the end of the row, ECH
    /// (<c>CSI n X</c>) inside it, counting the jump past the erased cells (ECH doesn't move the cursor).
    /// Erased cells take the pen's background (BCE). Returns where to continue, or <paramref name="x"/> to
    /// write the blanks as spaces after all.
    /// </summary>
    private int TryErase(ReadOnlySpan<Cell> cur, ReadOnlySpan<Cell> prev, int x, int last, int y,
        ref bool positioned, ref int forceUntil, ref int spacesUntil)
    {
        int width = cur.Length;
        Style style = cur[x].Style;
        int end = x + 1;
        int changed = x;   // the run's last cell that differs from the screen
        while (end < width && IsErasable(in cur[end]) && cur[end].Style.Bg.Equals(style.Bg))
        {
            if (end <= forceUntil || (end <= last && !cur[end].Equals(prev[end])))
            {
                changed = end;
            }

            end++;
        }

        // Writing the run as spaces stops at its last changed cell.
        int spaces = changed - x + 1;
        bool toEnd = end == width;
        int count = toEnd ? width - x : spaces;
        bool more = !toEnd && (last > changed || forceUntil > changed);
        int cost = toEnd ? 3 : 3 + Digits(count) + (more ? 3 + Digits(count) : 0);
        if (cost >= spaces)
        {
            spacesUntil = changed;
            return x;
        }

        if (!positioned)
        {
            MoveTo(x, y);
        }

        _out.Reserve(80);
        if (!_penKnown || (_pen.Attrs & Attr.Reverse) != 0 || !_pen.Bg.Equals(ColorMapping.Map(style.Bg, _mode)))
        {
            SetPen(style);
        }

        if (toEnd)
        {
            _out.Bytes("\u001b[K"u8);
            positioned = false;
            return width;
        }

        _out.Bytes("\u001b["u8);
        _out.Int(count);
        _out.Byte((byte)'X');

        // A wide glyph on screen across the run's right edge is erased whole: repaint the cell after the run.
        if (prev[x + count - 1].Width == 2)
        {
            forceUntil = Math.Max(forceUntil, x + count);
        }

        positioned = false;   // the cursor is still at x
        return x + count;
    }

    private static int Digits(int n) => n < 10 ? 1 : n < 100 ? 2 : n < 1000 ? 3 : 4;

    /// <summary>
    /// Emit a cell that is a cluster, or that could join the cell drawn just before it into one cluster
    /// on the terminal (<paramref name="previous"/>; default when the cursor was just moved).
    /// </summary>
    [MethodImpl(MethodImplOptions.NoInlining)]
    private int EmitComplex(in Cell cell, int x, int y, Rune previous, ref bool positioned, ref int forceUntil)
    {
        if (previous.Value != 0 && Graphemes.MayJoin(previous, cell.Rune))
        {
            // Drawn back to back, the two cells would merge (two lone regional indicators, an emoji then
            // a lone skin tone, a letter then a spacing mark): move explicitly, which ends the cluster.
            _cx = -1;
            MoveTo(x, y);
        }

        if (!cell.IsGrapheme)
        {
            int single = Emit(in cell, x);
            if (cell.RawRune.Value >= 0x1F000)
            {
                // Emoji-plane code points on their own (a lone regional indicator or skin tone) get
                // different widths in different terminals, and some attach them to the previous glyph:
                // place the next cell absolutely.
                _cx = -1;
                positioned = false;
            }

            return single;
        }

        int advance = cell.Width;
        if (advance == 2 && x + 1 >= _width)
        {
            Cell blank = Cell.Blank(cell.Style);   // a wide cluster with no room: draw a blank, as Emit does
            return Emit(in blank, x);
        }

        if (!_penKnown || !cell.Style.Equals(_penSource))
        {
            SetPen(cell.Style);
        }

        EmitCluster(cell.GraphemeId, x, advance);

        // Terminals disagree on how far a cluster moves the cursor (see EmitCluster). Reposition for
        // the next cell, and repaint the cells a terminal without grapheme support may have drawn over.
        positioned = false;
        forceUntil = Math.Max(forceUntil, Math.Min(_width - 1, x + Graphemes.LegacyWidth(cell.GraphemeId) - 1));
        return advance;
    }

    /// <summary>Compile-time switch for <see cref="RenderRow{TJoin}"/> (folded away by the JIT).</summary>
    private interface IJoinPolicy
    {
        static abstract bool Enabled { get; }
    }

    private readonly struct JoinChecks : IJoinPolicy
    {
        public static bool Enabled => true;
    }

    private readonly struct NoJoinChecks : IJoinPolicy
    {
        public static bool Enabled => false;
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
            if (c.Width != 1 || c.IsGrapheme || c.RawRune.Value >= 0x80 || !c.Style.Equals(_penSource))
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Emit a single-code-point cell. Clusters go through <see cref="EmitComplex"/>.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private int Emit(in Cell cell, int x)
    {
        int advance = cell.Width;
        Rune rune = cell.RawRune;
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

    /// <summary>
    /// Write a multi-code-point cluster. A grapheme-aware terminal advances by its width; an older one by
    /// the sum of its code points' widths, which can be less (❤️ = 1 + 0) or more (👨‍👩‍👧 = 6). When it can
    /// be less, blank the cluster's cells first so nothing stale shows beside it. Either way the cursor
    /// column is unknown afterwards; the caller repositions and repaints what a wider draw covered.
    /// </summary>
    private void EmitCluster(int id, int x, int width)
    {
        string text = Graphemes.Text(id);
        _out.Reserve(text.Length * 3 + width + 32);
        if (Graphemes.LegacyWidth(id) < width)
        {
            for (int i = 0; i < width; i++)
            {
                _out.Byte((byte)' ');
            }

            _cx = -1;
            MoveTo(x, _cy);
        }

        foreach (Rune rune in text.EnumerateRunes())
        {
            _out.Rune(rune);
        }

        _cx = -1;
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

            if (ParkCursor && _started)
            {
                MoveTo(0, 0);
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
            if (x == 0 && y + Top == 0)
            {
                _out.Byte((byte)'H');
            }
            else
            {
                _out.Int(y + Top + 1);
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
