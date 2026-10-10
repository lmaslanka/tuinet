using System.Globalization;
using System.Text;

namespace Tuinet.Tests;

/// <summary>
/// Just enough of a VT terminal to replay what the renderer emits, cell by cell (glyph, colors, attributes,
/// cursor). Strict: anything the renderer shouldn't emit fails. Two kinds of terminal: grapheme-aware (code
/// points that extend the last printed cluster join it, and its width can grow to 2) and legacy (every code
/// point advances by its own width; zero-width ones attach to the previous cell, like xterm). Any cursor move
/// ends the current cluster. With allowScroll (inline mode), a line feed on the last row scrolls the screen
/// into <see cref="Scrollback"/>. Hyperlinks (OSC 8) and the cursor shape (DECSCUSR) are tracked and checked too;
/// titles (OSC 2) and clipboard writes (OSC 52) are recorded. With leftRightMargins the terminal has DECLRMM and
/// DECSLRM: IL/DL move only the columns between the margins, which must not split a wide glyph; without it, any use fails.
/// </summary>
internal sealed class VtEmulator(int width, int height, bool legacy = false, bool allowScroll = false, bool leftRightMargins = false)
{
    private (string Text, Style Style, bool Cont)[,] _grid = Blank(width, height);
    private string?[,] _links = new string?[width, height];
    private string? _link;
    private CursorShape _shape;
    private int _lastX = -1;
    private int _lastY;
    private int _w = width;
    private int _h = height;
    private int _x;
    private int _y;
    private int _top;
    private int _bottom = height - 1;
    private int _left;
    private int _right = width - 1;
    private bool _lrmm;
    private Style _pen;
    private bool _cursorVisible;

    /// <summary>IL/DL sequences replayed so far, and those of them inside left/right margins.</summary>
    public int LineMoves { get; private set; }

    public int ColumnMoves { get; private set; }

    /// <summary>Left/right margin support queries (DECRQM 69) received.</summary>
    public int MarginQueries { get; private set; }

    /// <summary>With allowScroll: rows scrolled off the top by line feeds on the last row, oldest first.</summary>
    public List<string> Scrollback { get; } = [];

    /// <summary>EL/ECH sequences replayed so far, and the cells ECH erased.</summary>
    public int LineErases { get; private set; }

    public int ErasedCells { get; private set; }

    public int CursorX => _x;
    public int CursorY => _y;

    /// <summary>Titles (OSC 2) and clipboard payloads (OSC 52, base64) received, oldest first.</summary>
    public List<string> Titles { get; } = [];

    public List<string> Clipboard { get; } = [];

    /// <summary>Hyperlinks opened (OSC 8 with a URL) and cursor shape changes (DECSCUSR) replayed so far.</summary>
    public int LinkOpens { get; private set; }

    public int ShapeChanges { get; private set; }

    /// <summary>The hyperlink open now (OSC 8), or null.</summary>
    public string? OpenLink => _link;

    /// <summary>The hyperlink of screen cell (<paramref name="x"/>, <paramref name="y"/>), or null.</summary>
    public string? LinkAt(int x, int y) => _links[x, y];

    /// <summary>The glyphs of screen row <paramref name="y"/>.</summary>
    public string RowText(int y)
    {
        var builder = new StringBuilder();
        for (int x = 0; x < _w; x++)
        {
            if (!_grid[x, y].Cont)
            {
                builder.Append(_grid[x, y].Text);
            }
        }

        return builder.ToString();
    }

    public void Resize(int w, int h)
    {
        _w = w;
        _h = h;
        _grid = Blank(w, h);
        _links = new string?[w, h];
        _top = 0;
        _bottom = h - 1;
        _left = 0;
        _right = w - 1;
    }

    public void Feed(byte[] bytes)
    {
        string text = Encoding.UTF8.GetString(bytes);
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c == '\u001b' && text[i + 1] == ']')
            {
                // OSC, ended by ST (ESC \) or BEL. Its payload must be printable: a control would end it early.
                int end = i + 2;
                while (text[end] != '\u001b' && text[end] != '\u0007')
                {
                    Assert.True(text[end] >= 0x20, $"control 0x{(int)text[end]:X2} inside OSC");
                    end++;
                }

                Osc(text[(i + 2)..end]);
                i = text[end] == '\u0007' ? end + 1 : end + 2;
                continue;
            }

            if (c == '\u001b')
            {
                Assert.Equal('[', text[i + 1]);
                int end = i + 2;
                while (text[end] is < '@' or > '~')
                {
                    end++;
                }

                Csi(text[(i + 2)..end], text[end]);
                _lastX = -1;
                i = end + 1;
                continue;
            }

            if (c == '\r')
            {
                _lastX = -1;
                _x = 0;
                i++;
                continue;
            }

            if (c == '\n')
            {
                _lastX = -1;
                if (allowScroll && _y == _h - 1)
                {
                    // Inline mode: a line feed on the last row scrolls the whole screen into the scrollback.
                    Assert.True(_top == 0 && _bottom == _h - 1, "LF scrolled inside margins");
                    Scrollback.Add(RowText(0));
                    MoveRows(1, _h - 1, -1);
                    BlankRow(_h - 1);
                    i++;
                    continue;
                }

                Assert.True(_y != _bottom, "LF at the bottom margin scrolled the screen");
                AssertFullWidth("LF");
                _y++;
                Assert.True(_y < _h, "LF scrolled the screen");
                i++;
                continue;
            }

            Assert.True(c >= 0x20, $"control byte 0x{(int)c:X2} emitted");
            Rune.DecodeFromUtf16(text.AsSpan(i), out Rune rune, out int consumed);
            i += consumed;
            Print(rune);
        }
    }

    /// <summary>The screen from row <paramref name="top"/> must show <paramref name="buffer"/> exactly, cursor included.</summary>
    public void AssertMatches(CellBuffer buffer, string context, int top = 0)
    {
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < _w; x++)
            {
                Cell expected = buffer[x, y];
                var actual = _grid[x, top + y];
                string where = $"{context}: cell ({x},{y})\nexpected:\n{buffer}\nactual:\n{this}";
                // A legacy terminal fills a wide cluster's second cell with the blank drawn before it (❤️) or
                // with a later code point of the cluster (the spacing vowel of कि): either is fine.
                bool coveredByCluster = legacy && expected.IsContinuation && x > 0 && buffer[x - 1, y].IsGrapheme && !actual.Cont;
                Assert.True(expected.IsContinuation == actual.Cont || coveredByCluster, $"continuation mismatch at {where}");
                if (expected.IsGrapheme && legacy)
                {
                    // A legacy terminal keeps only the first code point of a cluster in its cell.
                    Assert.True(actual.Text.StartsWith(expected.Rune.ToString(), StringComparison.Ordinal), $"glyph '{actual.Text}' != '{expected.Text}' at {where}");
                }
                else if (!expected.IsContinuation)
                {
                    Assert.True(expected.Text == actual.Text, $"glyph '{actual.Text}' != '{expected.Text}' at {where}");
                }

                // A blank that is only its background looks the same whatever its foreground or other
                // attributes: erase sequences leave exactly that.
                const Attr visibleOnBlank = Attr.Underline | Attr.Reverse | Attr.Strike;
                bool plainBlank = expected.Text == " " && actual.Text == " " && !expected.IsGrapheme
                    && (expected.Style.Attrs & visibleOnBlank) == 0 && (actual.Style.Attrs & visibleOnBlank) == 0;
                Assert.True(plainBlank ? expected.Style.Bg == actual.Style.Bg : expected.Style == actual.Style,
                    $"style {actual.Style} != {expected.Style} at {where}");
            }
        }

        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < _w; x++)
            {
                Cell expected = buffer[x, y];
                if (!expected.IsContinuation)
                {
                    Assert.True(expected.Link == _links[x, top + y],
                        $"{context}: link '{_links[x, top + y]}' != '{expected.Link}' at cell ({x},{y})");
                }
            }
        }

        Assert.Null(_link);   // nothing stays linked between frames
        Assert.Equal(buffer.CursorVisible, _cursorVisible);
        if (buffer.CursorVisible)
        {
            Assert.Equal((buffer.CursorX, buffer.CursorY + top), (_x, _y));
            Assert.Equal(buffer.CursorShape, _shape);
        }
    }

    public override string ToString()
    {
        var builder = new StringBuilder();
        for (int y = 0; y < _h; y++)
        {
            for (int x = 0; x < _w; x++)
            {
                if (!_grid[x, y].Cont)
                {
                    builder.Append(_grid[x, y].Text);
                }
            }

            builder.Append('\n');
        }

        return builder.ToString();
    }

    private void Print(Rune rune)
    {
        int width = TextWidth.Of(rune);
        if (_lastX >= 0)
        {
            string joined = _grid[_lastX, _lastY].Text + rune;
            bool extends = StringInfo.GetNextTextElementLength(joined) == joined.Length;
            if (legacy ? width == 0 : extends)
            {
                Style style = _grid[_lastX, _lastY].Style;
                int before = TextWidth.Of(_grid[_lastX, _lastY].Text);
                _grid[_lastX, _lastY] = (joined, style, false);
                if (!legacy && TextWidth.Of(joined) == 2 && before == 1)
                {
                    // VS16 made the cluster wide: it takes the next cell too.
                    Assert.True(_lastX + 1 < _w, "cluster grew past the right edge");
                    if (_lastX + 2 < _w && _grid[_lastX + 2, _lastY].Cont)
                    {
                        _grid[_lastX + 2, _lastY] = (" ", _grid[_lastX + 2, _lastY].Style, false);
                    }

                    _grid[_lastX + 1, _lastY] = ("", style, true);
                    _links[_lastX + 1, _lastY] = _links[_lastX, _lastY];
                    _x = Math.Min(_lastX + 2, _w - 1);
                }

                return;
            }
        }

        if (width == 0)
        {
            return;
        }

        if (legacy && _x + width > _w)
        {
            return;   // a legacy terminal drawing a cluster as several glyphs ran out of room
        }

        _lastX = _x;
        _lastY = _y;
        Put(rune.ToString(), width);
    }

    private void Put(string glyph, int width)
    {
        AssertFullWidth($"'{glyph}'");
        Assert.True(_x + width <= _w, $"glyph '{glyph}' written past the right edge at ({_x},{_y})");

        // Like a real terminal: overwriting half of a wide glyph destroys the other half.
        for (int dx = 0; dx < width; dx++)
        {
            int cx = _x + dx;
            if (_grid[cx, _y].Cont && cx > 0)
            {
                _grid[cx - 1, _y] = (" ", _grid[cx - 1, _y].Style, false);
            }

            if (cx + 1 < _w && _grid[cx + 1, _y].Cont && TextWidth.Of(_grid[cx, _y].Text) == 2)
            {
                _grid[cx + 1, _y] = (" ", _grid[cx + 1, _y].Style, false);
            }
        }

        _grid[_x, _y] = (glyph, _pen, false);
        _links[_x, _y] = _link;
        if (width == 2)
        {
            _grid[_x + 1, _y] = ("", _pen, true);
            _links[_x + 1, _y] = _link;
        }

        // DECAWM off: the cursor stops at the last column.
        _x = Math.Min(_x + width, _w - 1);
    }

    private void Csi(string param, char final)
    {
        switch (final)
        {
            case 'H':
                string[] parts = param.Split(';');
                _y = (parts[0].Length > 0 ? int.Parse(parts[0]) : 1) - 1;
                _x = (parts.Length > 1 ? int.Parse(parts[1]) : 1) - 1;
                break;
            case 'C':
                _x = Math.Min(_x + (param.Length > 0 ? int.Parse(param) : 1), _w - 1);
                break;
            case 'J' when param == "2":
                _grid = Blank(_w, _h, _pen.Bg);
                _links = new string?[_w, _h];
                break;
            case 'J' when param is "" or "0":
                // Erase below: the rest of the cursor's row and every row under it.
                for (int y = _y; y < _h; y++)
                {
                    for (int x = y == _y ? _x : 0; x < _w; x++)
                    {
                        _grid[x, y] = (" ", new Style(default, _pen.Bg), false);
                        _links[x, y] = null;
                    }
                }

                break;
            case 'K':
                // EL: erase from the cursor to the end of the line. The cursor doesn't move.
                Assert.True(param is "" or "0", $"unexpected EL {param}");
                Erase(_x, _w - _x);
                break;
            case 'X':
                // ECH: erase n cells from the cursor, clipped at the right edge. The cursor doesn't move.
                Erase(_x, Math.Min(param.Length > 0 ? int.Parse(param) : 1, _w - _x));
                ErasedCells += Math.Min(param.Length > 0 ? int.Parse(param) : 1, _w - _x);
                break;
            case 'n':
                Assert.Equal("6", param);   // cursor position query: the reply is the test's business
                break;
            case 'm':
                Sgr(param);
                break;
            case 'r':
                // DECSTBM: set the scroll margins (none: the whole screen) and home the cursor.
                string[] margins = param.Split(';');
                _top = param.Length > 0 ? int.Parse(margins[0]) - 1 : 0;
                _bottom = param.Length > 0 ? int.Parse(margins[1]) - 1 : _h - 1;
                Assert.True(_top < _bottom && _bottom < _h, $"bad margins {param}");
                _x = 0;
                _y = 0;
                break;
            case 's':
                // DECSLRM: set the left/right margins (none: the whole width) and home the cursor. Without DECLRMM
                // this would be SCOSC (save cursor), which the renderer never sends.
                Assert.True(_lrmm, $"CSI {param}s without left/right margin mode");
                string[] sides = param.Split(';');
                _left = param.Length > 0 ? int.Parse(sides[0]) - 1 : 0;
                _right = param.Length > 0 ? int.Parse(sides[1]) - 1 : _w - 1;
                Assert.True(_left < _right && _right < _w, $"bad left/right margins {param}");
                _x = 0;
                _y = 0;
                break;
            case 'p':
                // DECRQM for left/right margin mode: the reply is the test's business.
                Assert.Equal("?69$", param);
                MarginQueries++;
                break;
            case 'L' or 'M':
                // IL / DL: insert or delete lines at the cursor row, inside the margins; new lines
                // take the current background (BCE). The cursor goes to the left margin.
                Assert.True(_y >= _top && _y <= _bottom, $"IL/DL outside the margins at row {_y}");
                Assert.True(_x >= _left && _x <= _right, $"IL/DL outside the left/right margins at column {_x}");
                int n = param.Length > 0 ? int.Parse(param) : 1;
                LineMoves++;
                if (_left > 0 || _right < _w - 1)
                {
                    ColumnMoves++;
                    for (int y = _top; y <= _bottom; y++)
                    {
                        Assert.False(_grid[_left, y].Cont, $"left margin {_left} splits a wide glyph on row {y}");
                        Assert.False(_right + 1 < _w && _grid[_right + 1, y].Cont, $"right margin {_right} splits a wide glyph on row {y}");
                    }
                }

                for (int i = 0; i < n; i++)
                {
                    if (final == 'M')
                    {
                        MoveRows(_y + 1, _bottom, -1);
                        BlankRow(_bottom);
                    }
                    else
                    {
                        MoveRows(_y, _bottom - 1, +1);
                        BlankRow(_y);
                    }
                }

                _x = _left;
                break;
            case 'q':
                // DECSCUSR: "n q" (the space is an intermediate byte).
                Assert.EndsWith(" ", param);
                _shape = (CursorShape)int.Parse(param.AsSpan(0, param.Length - 1));
                Assert.InRange((int)_shape, 0, 6);
                ShapeChanges++;
                break;
            case 'h' or 'l':
                if (param == "?25")
                {
                    _cursorVisible = final == 'h';
                }
                else if (param == "?69")
                {
                    // DECLRMM. Resetting it resets the left/right margins, as on xterm.
                    Assert.True(leftRightMargins, "left/right margin mode used on a terminal without it");
                    _lrmm = final == 'h';
                    _left = _lrmm ? _left : 0;
                    _right = _lrmm ? _right : _w - 1;
                }

                break;
            default:
                Assert.Fail($"unexpected CSI {param}{final}");
                break;
        }
    }

    private void Osc(string payload)
    {
        if (payload.StartsWith("8;", StringComparison.Ordinal))
        {
            string url = payload[(payload.IndexOf(';', 2) + 1)..];
            Assert.All(url, c => Assert.InRange(c, '!', '~'));   // URIs are sent as printable ASCII
            _link = url.Length == 0 ? null : url;
            LinkOpens += _link is null ? 0 : 1;
        }
        else if (payload.StartsWith("2;", StringComparison.Ordinal))
        {
            Titles.Add(payload[2..]);
        }
        else if (payload.StartsWith("52;c;", StringComparison.Ordinal))
        {
            Clipboard.Add(payload[5..]);
        }
        else
        {
            Assert.Fail($"unexpected OSC {payload}");
        }
    }

    private void Sgr(string param)
    {
        int[] p = param.Length == 0 ? [0] : [.. param.Split(';').Select(int.Parse)];
        Color fg = _pen.Fg, bg = _pen.Bg;
        Attr attrs = _pen.Attrs;
        for (int i = 0; i < p.Length; i++)
        {
            int v = p[i];
            switch (v)
            {
                case 0: fg = default; bg = default; attrs = Attr.None; break;
                case 1: attrs |= Attr.Bold; break;
                case 2: attrs |= Attr.Dim; break;
                case 3: attrs |= Attr.Italic; break;
                case 4: attrs |= Attr.Underline; break;
                case 5: attrs |= Attr.Blink; break;
                case 7: attrs |= Attr.Reverse; break;
                case 8: attrs |= Attr.Hidden; break;
                case 9: attrs |= Attr.Strike; break;
                case 22: attrs &= ~(Attr.Bold | Attr.Dim); break;
                case 23: attrs &= ~Attr.Italic; break;
                case 24: attrs &= ~Attr.Underline; break;
                case 25: attrs &= ~Attr.Blink; break;
                case 27: attrs &= ~Attr.Reverse; break;
                case 28: attrs &= ~Attr.Hidden; break;
                case 29: attrs &= ~Attr.Strike; break;
                case >= 30 and <= 37: fg = Color.Indexed((byte)(v - 30)); break;
                case >= 90 and <= 97: fg = Color.Indexed((byte)(v - 90 + 8)); break;
                case >= 40 and <= 47: bg = Color.Indexed((byte)(v - 40)); break;
                case >= 100 and <= 107: bg = Color.Indexed((byte)(v - 100 + 8)); break;
                case 39: fg = default; break;
                case 49: bg = default; break;
                case 38 or 48:
                    Color color = p[i + 1] == 5
                        ? Color.Indexed((byte)p[i + 2])
                        : Color.Rgb((byte)p[i + 2], (byte)p[i + 3], (byte)p[i + 4]);
                    i += p[i + 1] == 5 ? 2 : 4;
                    if (v == 38) fg = color; else bg = color;
                    break;
                default:
                    Assert.Fail($"unexpected SGR {v}");
                    break;
            }
        }

        _pen = new Style(fg, bg, attrs);
    }

    /// <summary>Move rows [from, to] by <paramref name="delta"/> (-1 up, +1 down).</summary>
    /// <summary>Erase <paramref name="count"/> cells from column <paramref name="x"/>; a wide glyph cut in half goes whole.</summary>
    private void Erase(int x, int count)
    {
        if (count <= 0)
        {
            return;
        }

        AssertFullWidth("erase");
        LineErases++;
        int end = x + count;
        if (_grid[x, _y].Cont && x > 0)
        {
            _grid[x - 1, _y] = (" ", _grid[x - 1, _y].Style, false);
        }

        if (end < _w && _grid[end, _y].Cont)
        {
            _grid[end, _y] = (" ", _grid[end, _y].Style, false);
        }

        for (int i = x; i < end; i++)
        {
            _grid[i, _y] = (" ", new Style(default, _pen.Bg), false);   // BCE: only the background is kept
            _links[i, _y] = null;
        }
    }

    private void MoveRows(int from, int to, int delta)
    {
        if (delta < 0)
        {
            for (int y = from; y <= to; y++)
            {
                for (int x = _left; x <= _right; x++)
                {
                    _grid[x, y - 1] = _grid[x, y];
                    _links[x, y - 1] = _links[x, y];
                }
            }
        }
        else
        {
            for (int y = to; y >= from; y--)
            {
                for (int x = _left; x <= _right; x++)
                {
                    _grid[x, y + 1] = _grid[x, y];
                    _links[x, y + 1] = _links[x, y];
                }
            }
        }
    }

    /// <summary>Text and graphic output, and erasing, happen only with the margins reset by the renderer.</summary>
    private void AssertFullWidth(string what) =>
        Assert.True(_left == 0 && _right == _w - 1, $"{what} with left/right margins {_left}..{_right} set");

    private void BlankRow(int y)
    {
        for (int x = _left; x <= _right; x++)
        {
            _grid[x, y] = (" ", new Style(default, _pen.Bg), false);
            _links[x, y] = null;
        }
    }

    private static (string, Style, bool)[,] Blank(int w, int h, Color bg = default)
    {
        var grid = new (string, Style, bool)[w, h];
        for (int x = 0; x < w; x++)
        {
            for (int y = 0; y < h; y++)
            {
                grid[x, y] = (" ", new Style(default, bg), false);
            }
        }

        return grid;
    }
}
