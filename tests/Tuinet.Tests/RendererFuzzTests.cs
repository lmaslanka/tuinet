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

    private static readonly string[] Words = ["a", "bc", "the quick", "你好", "x你y", "🚀!", "──", "é", "z"];

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    [InlineData(5)]
    public void Screen_matches_every_frame(int seed)
    {
        var random = new Random(seed);
        var tty = new TestTty(23, 7);
        using var terminal = new Terminal(tty);
        var screen = new Emulator(23, 7);
        screen.Feed(tty.Written);
        var model = new CellBuffer(23, 7);

        for (int frame = 0; frame < 300; frame++)
        {
            // Mutate a persistent model a little (sparse changes), sometimes a lot.
            int edits = random.Next(10) == 0 ? 40 : random.Next(1, 6);
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

    /// <summary>Just enough of a VT terminal to replay what the renderer emits.</summary>
    private sealed class Emulator(int width, int height)
    {
        private (Rune Rune, Style Style, bool Cont)[,] _grid = Blank(width, height);
        private int _w = width;
        private int _h = height;
        private int _x;
        private int _y;
        private Style _pen;
        private bool _cursorVisible;

        public void Resize(int w, int h)
        {
            _w = w;
            _h = h;
            _grid = Blank(w, h);
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
                    i = end + 1;
                    continue;
                }

                if (c == '\r')
                {
                    _x = 0;
                    i++;
                    continue;
                }

                if (c == '\n')
                {
                    _y++;
                    Assert.True(_y < _h, "LF scrolled the screen");
                    i++;
                    continue;
                }

                Assert.True(c >= 0x20, $"control byte 0x{(int)c:X2} emitted");
                Rune.DecodeFromUtf16(text.AsSpan(i), out Rune rune, out int consumed);
                i += consumed;
                Put(rune);
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
                    Assert.True(expected.IsContinuation == actual.Cont, $"continuation mismatch at {where}");
                    if (!expected.IsContinuation)
                    {
                        Assert.True(expected.Rune == actual.Rune, $"glyph '{actual.Rune}' != '{expected.Rune}' at {where}");
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
                        builder.Append(_grid[x, y].Rune);
                    }
                }

                builder.Append('\n');
            }

            return builder.ToString();
        }

        private void Put(Rune rune)
        {
            int width = TextWidth.Of(rune);
            Assert.True(_x + width <= _w, $"glyph '{rune}' written past the right edge at ({_x},{_y})");

            // Like a real terminal: overwriting half of a wide glyph destroys the other half.
            for (int dx = 0; dx < width; dx++)
            {
                int cx = _x + dx;
                if (_grid[cx, _y].Cont && cx > 0)
                {
                    _grid[cx - 1, _y] = (new Rune(' '), _grid[cx - 1, _y].Style, false);
                }

                if (cx + 1 < _w && _grid[cx + 1, _y].Cont && TextWidth.Of(_grid[cx, _y].Rune) == 2)
                {
                    _grid[cx + 1, _y] = (new Rune(' '), _grid[cx + 1, _y].Style, false);
                }
            }

            _grid[_x, _y] = (rune, _pen, false);
            if (width == 2)
            {
                _grid[_x + 1, _y] = (default, _pen, true);
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

        private static (Rune, Style, bool)[,] Blank(int w, int h, Color bg = default)
        {
            var grid = new (Rune, Style, bool)[w, h];
            for (int x = 0; x < w; x++)
            {
                for (int y = 0; y < h; y++)
                {
                    grid[x, y] = (new Rune(' '), new Style(default, bg), false);
                }
            }

            return grid;
        }
    }
}
