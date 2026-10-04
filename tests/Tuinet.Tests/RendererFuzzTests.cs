using System.Globalization;
using System.Text;
using Tuinet.Testing;

namespace Tuinet.Tests;

/// <summary>
/// Property test for the renderer: random frames are presented through a real <see cref="Terminal"/>,
/// the emitted bytes are replayed by a minimal VT emulator, and the reconstructed screen must equal
/// the frame cell for cell (glyph, colors, attributes) and cursor. Catches any bad diff, cursor
/// move, wide-glyph or SGR-delta decision.
/// </summary>
public class RendererFuzzTests
{
    private static readonly Style[] Styles =
    [
        default,
        new(Color.Red, default),
        new(Color.Rgb(10, 20, 30), Color.Rgb(200, 210, 220)),
        new(Color.Indexed(200), Color.BrightBlue, Attr.Bold),
        new(default, Color.Green, Attr.Underline | Attr.Italic),
        new(Color.BrightWhite, default, Attr.Dim | Attr.Bold),
        new(Color.Rgb(10, 20, 30), Color.Rgb(200, 210, 220), Attr.Reverse | Attr.Strike),
    ];

    private static readonly string[] Words =
    [
        "a", "bc", "the quick", "你好", "x你y", "🚀!", "──", "é", "z",

        // Grapheme clusters, and lone pieces that would merge with a neighbour if drawn back to back.
        "e\u0301", "👨‍👩‍👧", "❤️", "🇵🇱", "🇵", "🇱", "👍🏽", "👍", "🏽", "\u0915\u093F", "\u1100", "가", "1\uFE0F\u20E3",
    ];

    [Theory]
    [InlineData(1, true, false)]
    [InlineData(2, true, false)]
    [InlineData(3, true, false)]
    [InlineData(4, true, false)]
    [InlineData(5, true, false)]
    [InlineData(6, true, false)]
    [InlineData(7, true, false)]
    [InlineData(8, true, false)]
    [InlineData(1, false, false)]
    [InlineData(2, false, false)]
    [InlineData(1, true, true)]
    [InlineData(2, true, true)]
    [InlineData(3, true, true)]
    [InlineData(4, true, true)]
    public void Screen_matches_every_frame(int seed, bool scrollRegions, bool legacyTerminal)
    {
        var random = new Random(seed);
        var tty = new TestTty(23, 7);
        using var terminal = new Terminal(tty, new TerminalOptions { ScrollRegions = scrollRegions });
        var screen = new Emulator(23, 7, legacyTerminal);
        screen.Feed(tty.Written);
        var model = new CellBuffer(23, 7);

        for (int frame = 0; frame < 300; frame++)
        {
            // Mutate a persistent model a little (sparse changes), sometimes a lot.
            int edits = random.Next(10) == 0 ? 40 : random.Next(1, 6);
            if (random.Next(3) == 0)
            {
                Shift(model, random);
                edits = random.Next(3);   // a scrolled band, plus a few unrelated changes
            }

            for (int i = 0; i < edits; i++)
            {
                Mutate(model, random);
            }

            if (random.Next(4) == 0)
            {
                model.SetCursor(random.Next(23), random.Next(7));
            }
            else if (random.Next(3) == 0)
            {
                model.HideCursor();
            }

            CellBuffer buffer = terminal.BeginFrame();
            Copy(model, buffer);
            tty.ClearWritten();
            terminal.Present();
            screen.Feed(tty.Written);
            screen.AssertMatches(buffer, $"seed {seed} frame {frame}");
        }

        // The band shifts above must have gone through the scroll path (and never without it).
        Assert.True(scrollRegions ? screen.LineMoves > 0 : screen.LineMoves == 0, $"{screen.LineMoves} IL/DL");
    }

    [Theory]
    [InlineData(11)]
    [InlineData(12)]
    [InlineData(13)]
    [InlineData(14)]
    public void Screen_matches_while_bands_scroll(int seed)
    {
        var random = new Random(seed);
        var tty = new TestTty(30, 16);
        using var terminal = new Terminal(tty);
        var screen = new Emulator(30, 16);
        screen.Feed(tty.Written);
        var model = new CellBuffer(30, 16);
        for (int i = 0; i < 200; i++)
        {
            Mutate(model, random);
        }

        for (int frame = 0; frame < 300; frame++)
        {
            Shift(model, random);
            for (int i = random.Next(3); i > 0; i--)
            {
                Mutate(model, random);
            }

            // Paint the pen into a non-default background now and then, so a scroll must reset it first.
            if (random.Next(4) == 0)
            {
                model.SetString(random.Next(30), random.Next(16), "#", new Style(Color.Red, Color.Blue));
            }

            CellBuffer buffer = terminal.BeginFrame();
            Copy(model, buffer);
            tty.ClearWritten();
            terminal.Present();
            screen.Feed(tty.Written);
            screen.AssertMatches(buffer, $"seed {seed} frame {frame}");
        }

        Assert.True(screen.LineMoves > 50, $"{screen.LineMoves} IL/DL");
    }

    [Fact]
    public void Screen_matches_after_resize()
    {
        var random = new Random(7);
        var tty = new TestTty(10, 4);
        using var terminal = new Terminal(tty);
        var screen = new Emulator(10, 4);
        screen.Feed(tty.Written);
        for (int frame = 0; frame < 50; frame++)
        {
            if (frame % 10 == 5)
            {
                tty.Resize(random.Next(5, 30), random.Next(2, 9));
                screen.Resize(tty.Size.Width, tty.Size.Height);
            }

            CellBuffer buffer = terminal.BeginFrame();
            for (int i = 0; i < 8; i++)
            {
                Mutate(buffer, random);
            }

            tty.ClearWritten();
            terminal.Present();
            screen.Feed(tty.Written);
            screen.AssertMatches(buffer, $"frame {frame}");
        }
    }

    private static void Mutate(CellBuffer buffer, Random random)
    {
        int x = random.Next(-2, buffer.Width);
        int y = random.Next(buffer.Height);
        Style style = Styles[random.Next(Styles.Length)];
        switch (random.Next(4))
        {
            case 0:
                buffer.Fill(new Rect(x, y, random.Next(1, 8), random.Next(1, 3)), style);
                break;
            default:
                buffer.SetString(x, y, Words[random.Next(Words.Length)], style);
                break;
        }
    }

    /// <summary>Scroll a band of rows by ±k (whole width, or sometimes not), filling the vacated rows.</summary>
    private static void Shift(CellBuffer buffer, Random random)
    {
        int top = random.Next(buffer.Height - 1);
        int bottom = random.Next(top + 1, buffer.Height);
        int k = random.Next(1, bottom - top + 1) * (random.Next(2) == 0 ? 1 : -1);
        bool partial = random.Next(5) == 0;
        int width = partial ? random.Next(1, buffer.Width) : buffer.Width;
        Span<Cell> cells = buffer.Cells;
        var copy = cells.ToArray();
        for (int y = top; y <= bottom; y++)
        {
            int from = y + k;
            Span<Cell> row = cells.Slice(y * buffer.Width, width);
            if (from >= top && from <= bottom)
            {
                copy.AsSpan(from * buffer.Width, width).CopyTo(row);
            }
            else
            {
                row.Fill(Cell.Blank(Styles[random.Next(Styles.Length)]));
                buffer.SetString(random.Next(buffer.Width), y, Words[random.Next(Words.Length)], Styles[random.Next(Styles.Length)]);
            }

            // A partial-width copy can cut a wide glyph in half: repair the edge like a real write would.
            if (partial && cells[y * buffer.Width + width - 1].Width == 2 || partial && width < buffer.Width && cells[y * buffer.Width + width].IsContinuation)
            {
                buffer.Fill(new Rect(width - 1, y, 2, 1), Style.Default);
            }
        }
    }

    private static void Copy(CellBuffer from, CellBuffer to)
    {
        for (int y = 0; y < from.Height; y++)
        {
            for (int x = 0; x < from.Width; x++)
            {
                Cell cell = from[x, y];
                if (!cell.IsContinuation)
                {
                    to.SetCell(x, y, cell);
                }
            }
        }

        if (from.CursorVisible)
        {
            to.SetCursor(from.CursorX, from.CursorY);
        }
    }

    /// <summary>
    /// Just enough of a VT terminal to replay what the renderer emits. Two kinds of terminal:
    /// grapheme-aware (code points that extend the last printed cluster join it, and its width can grow
    /// to 2) and legacy (every code point advances by its own width; zero-width ones attach to the
    /// previous cell, like xterm). Any cursor move ends the current cluster.
    /// </summary>
    private sealed class Emulator(int width, int height, bool legacy = false)
    {
        private (string Text, Style Style, bool Cont)[,] _grid = Blank(width, height);
        private int _lastX = -1;
        private int _lastY;
        private int _w = width;
        private int _h = height;
        private int _x;
        private int _y;
        private int _top;
        private int _bottom = height - 1;
        private Style _pen;
        private bool _cursorVisible;

        /// <summary>IL/DL sequences replayed so far.</summary>
        public int LineMoves { get; private set; }

        public void Resize(int w, int h)
        {
            _w = w;
            _h = h;
            _grid = Blank(w, h);
            _top = 0;
            _bottom = h - 1;
        }

        public void Feed(byte[] bytes)
        {
            string text = Encoding.UTF8.GetString(bytes);
            int i = 0;
            while (i < text.Length)
            {
                char c = text[i];
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
                    Assert.True(_y != _bottom, "LF at the bottom margin scrolled the screen");
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

        public void AssertMatches(CellBuffer buffer, string context)
        {
            for (int y = 0; y < _h; y++)
            {
                for (int x = 0; x < _w; x++)
                {
                    Cell expected = buffer[x, y];
                    var actual = _grid[x, y];
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

                    Assert.True(expected.Style == actual.Style, $"style {actual.Style} != {expected.Style} at {where}");
                }
            }

            Assert.Equal(buffer.CursorVisible, _cursorVisible);
            if (buffer.CursorVisible)
            {
                Assert.Equal((buffer.CursorX, buffer.CursorY), (_x, _y));
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
            if (width == 2)
            {
                _grid[_x + 1, _y] = ("", _pen, true);
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
                case 'J':
                    Assert.Equal("2", param);
                    _grid = Blank(_w, _h, _pen.Bg);
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
                case 'L' or 'M':
                    // IL / DL: insert or delete lines at the cursor row, inside the margins; new lines
                    // take the current background (BCE). The cursor goes to the left column.
                    Assert.True(_y >= _top && _y <= _bottom, $"IL/DL outside the margins at row {_y}");
                    int n = param.Length > 0 ? int.Parse(param) : 1;
                    LineMoves++;
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

                    _x = 0;
                    break;
                case 'h' or 'l':
                    if (param == "?25")
                    {
                        _cursorVisible = final == 'h';
                    }

                    break;
                default:
                    Assert.Fail($"unexpected CSI {param}{final}");
                    break;
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
        private void MoveRows(int from, int to, int delta)
        {
            if (delta < 0)
            {
                for (int y = from; y <= to; y++)
                {
                    for (int x = 0; x < _w; x++)
                    {
                        _grid[x, y - 1] = _grid[x, y];
                    }
                }
            }
            else
            {
                for (int y = to; y >= from; y--)
                {
                    for (int x = 0; x < _w; x++)
                    {
                        _grid[x, y + 1] = _grid[x, y];
                    }
                }
            }
        }

        private void BlankRow(int y)
        {
            for (int x = 0; x < _w; x++)
            {
                _grid[x, y] = (" ", new Style(default, _pen.Bg), false);
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
}
