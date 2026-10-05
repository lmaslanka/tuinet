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
        var screen = new VtEmulator(23, 7, legacyTerminal);
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
        var screen = new VtEmulator(30, 16);
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
        var screen = new VtEmulator(10, 4);
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
}
