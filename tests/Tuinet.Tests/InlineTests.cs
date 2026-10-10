using System.Text;
using Tuinet.Testing;
using Tuinet.Widgets;

namespace Tuinet.Tests;

/// <summary>Inline mode: a live band under the prompt instead of the alternate screen.</summary>
public class InlineTests
{
    private static TerminalOptions Inline(int height, int timeoutMs = 1000) =>
        new() { Inline = new InlineOptions(height) { CursorReportTimeoutMs = timeoutMs }, ColorMode = ColorMode.TrueColor };

    /// <summary>A terminal whose cursor is at 0-based (<paramref name="row"/>, <paramref name="column"/>) when it starts.</summary>
    private static Terminal Start(TestTty tty, int height, int row, int column = 0)
    {
        tty.Enqueue($"\u001b[{row + 1};{column + 1}R");
        return new Terminal(tty, Inline(height));
    }

    [Fact]
    public void Start_reserves_the_band_on_the_cursor_line_without_the_alternate_screen()
    {
        var tty = new TestTty(20, 10);
        using var terminal = Start(tty, height: 3, row: 4);
        Assert.Equal(new Size(20, 3), terminal.Size);
        Assert.Equal(
            "\u001b[?25l\u001b[?7l\u001b[?2027h" + "\u001b[6n"
            + "\u001b[?2026h\u001b[0m\u001b[5H\u001b[J" + "\u001b[?69$p\r\u001b[K" + "\u001b[?2026l",
            tty.WrittenText);
    }

    [Fact]
    public void Start_after_text_on_the_cursor_line_uses_the_next_line()
    {
        var tty = new TestTty(20, 10);
        using var terminal = Start(tty, height: 3, row: 4, column: 7);
        Assert.EndsWith("\u001b[6H\u001b[J\u001b[?69$p\r\u001b[K\u001b[?2026l", tty.WrittenText);
    }

    [Fact]
    public void Start_near_the_bottom_scrolls_the_screen_to_make_room()
    {
        var tty = new TestTty(20, 10);
        using var terminal = Start(tty, height: 3, row: 8);
        // Rows 8..10 don't fit: one line feed on the last row scrolls the prompt up, and the band is rows 7..9.
        Assert.EndsWith("\u001b[0m\u001b[10H\n\u001b[8H\u001b[J\u001b[?69$p\r\u001b[K\u001b[?2026l", tty.WrittenText);
    }

    [Fact]
    public void Without_a_cursor_report_the_band_goes_at_the_bottom()
    {
        var tty = new TestTty(20, 10);
        using var terminal = new Terminal(tty, Inline(3, timeoutMs: 50));
        Assert.EndsWith("\u001b[10H\n\n\n\u001b[8H\u001b[J\u001b[?69$p\r\u001b[K\u001b[?2026l", tty.WrittenText);
    }

    [Fact]
    public void Input_that_arrives_during_the_query_is_kept()
    {
        var tty = new TestTty(20, 10);
        tty.Enqueue("x\u001b[3;1Ry");
        using var terminal = new Terminal(tty, Inline(2));
        Assert.True(terminal.Poll(out Event ev, 0));
        Assert.True(ev.Key.IsChar('x'));
        Assert.True(terminal.Poll(out ev, 0));
        Assert.True(ev.Key.IsChar('y'));
        Assert.False(terminal.Poll(out _, 0));
    }

    [Fact]
    public void Frames_are_drawn_at_the_band_offset()
    {
        var tty = new TestTty(20, 10);
        using var terminal = Start(tty, height: 3, row: 4);
        tty.ClearWritten();
        CellBuffer frame = terminal.BeginFrame();
        frame.SetString(3, 2, "hi");
        terminal.Present();
        // Band row 2 is screen row 6; then the hidden cursor is parked at the band's top-left (row 4).
        Assert.Equal("\u001b[?2026h\u001b[7;4Hhi\u001b[5H\u001b[?2026l", tty.WrittenText);
    }

    [Fact]
    public void Exit_keeps_the_frame_and_continues_after_its_last_row()
    {
        var tty = new TestTty(20, 10);
        var terminal = Start(tty, height: 4, row: 2);
        terminal.BeginFrame().SetString(0, 1, "done");
        terminal.Present();
        tty.ClearWritten();
        terminal.Dispose();
        // The cursor is parked on band row 0: down to row 1 (screen row 3), then the shell continues on the next
        // line. No ?1049l.
        Assert.Equal("\r\n\r\n\u001b[0m\u001b[?2027l\u001b[?7h\u001b[?25h", tty.WrittenText);
    }

    [Fact]
    public void Print_above_needs_inline_mode()
    {
        using var terminal = new Terminal(new TestTty());
        Assert.Throws<InvalidOperationException>(() => terminal.PrintAbove("log"));
    }

    [Fact]
    public void Print_above_moves_the_band_down_and_repaints_it()
    {
        var tty = new TestTty(20, 8);
        var screen = new VtEmulator(20, 8, allowScroll: true);
        screen.Feed("$ fetch\r\n"u8.ToArray());
        using var terminal = Start(tty, height: 2, row: 1);
        Present(terminal, "[####      ] 40%");
        terminal.PrintAbove("got a.txt");
        terminal.PrintAbove(new StyledText("got b.txt", new Style(Color.Green, default)));

        screen.Feed(tty.Written);
        Assert.Equal("$ fetch", screen.RowText(0).TrimEnd());
        Assert.Equal("got a.txt", screen.RowText(1).TrimEnd());
        Assert.Equal("got b.txt", screen.RowText(2).TrimEnd());
        AssertBand(screen, terminal, "[####      ] 40%", top: 3);
    }

    [Fact]
    public void Print_above_at_the_bottom_scrolls_old_lines_into_the_scrollback()
    {
        var tty = new TestTty(12, 5);
        var screen = new VtEmulator(12, 5, allowScroll: true);
        screen.Feed("$ fetch\r\n"u8.ToArray());
        using var terminal = Start(tty, height: 2, row: 1);
        Present(terminal, "working");
        for (int i = 1; i <= 6; i++)
        {
            terminal.PrintAbove($"line {i}");
        }

        terminal.PrintAbove("a long line that wraps");          // 22 columns: two rows of 12
        screen.Feed(tty.Written);

        string[] above = [.. screen.Scrollback, screen.RowText(0).TrimEnd(), screen.RowText(1).TrimEnd(), screen.RowText(2).TrimEnd()];
        Assert.Equal(["$ fetch", "line 1", "line 2", "line 3", "line 4", "line 5", "line 6", "a long line", "that wraps"],
            above.Select(line => line.TrimEnd()).ToArray());
        AssertBand(screen, terminal, "working", top: 3);
    }

    [Fact]
    public void Print_above_a_full_screen_band_goes_straight_to_the_scrollback()
    {
        var tty = new TestTty(10, 3);
        var screen = new VtEmulator(10, 3, allowScroll: true);
        using var terminal = Start(tty, height: 5, row: 0);           // taller than the screen: 3 rows
        Present(terminal, "busy");
        terminal.PrintAbove("one");
        terminal.PrintAbove("two\nthree");
        screen.Feed(tty.Written);
        Assert.Equal(["one", "two", "three"], screen.Scrollback.Select(line => line.TrimEnd()).ToArray());
        AssertBand(screen, terminal, "busy", top: 0);
    }

    [Fact]
    public void Print_above_drops_control_characters_and_one_trailing_newline()
    {
        var tty = new TestTty(20, 6);
        var screen = new VtEmulator(20, 6, allowScroll: true);
        using var terminal = Start(tty, height: 1, row: 0);
        terminal.PrintAbove("evil \u001b]0;pwned\u0007 text\n");
        screen.Feed(tty.Written);                                       // the emulator fails on any stray control byte
        Assert.StartsWith("evil", screen.RowText(0));
        Assert.Equal("", screen.RowText(1).Trim());                      // the band, right after the one line
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    [InlineData(4)]
    public void Random_frames_and_prints_keep_the_band_and_the_log_intact(int seed)
    {
        var random = new Random(seed);
        int width = random.Next(8, 30);
        int height = random.Next(3, 12);
        int band = random.Next(1, height + 2);
        var tty = new TestTty(width, height);
        var screen = new VtEmulator(width, height, allowScroll: true);
        screen.Feed("$ go\r\n"u8.ToArray());
        using var terminal = Start(tty, band, row: 1);
        var log = new List<string> { "$ go" };
        CellBuffer shown = new(width, Math.Min(band, height));

        string[] words = ["alpha", "βeta", "日本", "🇵🇱", "é", "x", "longerword", "  "];
        Style[] styles = [default, new(Color.Red, default), new(default, Color.Blue, Attr.Bold), new(Color.Rgb(10, 200, 30), Color.Indexed(240))];
        for (int step = 0; step < 60; step++)
        {
            if (random.Next(3) == 0)
            {
                var text = new StringBuilder();
                int count = random.Next(1, 8);
                for (int i = 0; i < count; i++)
                {
                    text.Append(words[random.Next(words.Length)]).Append(' ');
                }

                string line = text.ToString().TrimEnd();
                terminal.PrintAbove(new StyledText(line, styles[random.Next(styles.Length)]));
                var rows = new CellBuffer(width, Math.Max(1, Paragraph.LineCount(line, width, TextWrap.Char)));
                rows.Render(new Paragraph(line) { Wrap = TextWrap.Char }, rows.Area);
                for (int y = 0; y < rows.Height; y++)
                {
                    log.Add(rows.RowText(y).TrimEnd());
                }
            }
            else
            {
                CellBuffer frame = terminal.BeginFrame();
                for (int y = 0; y < frame.Height; y++)
                {
                    frame.SetString(random.Next(width), y, words[random.Next(words.Length)], styles[random.Next(styles.Length)]);
                }

                if (random.Next(4) == 0)
                {
                    frame.SetCursor(random.Next(width), random.Next(frame.Height));
                }

                terminal.Present();
                Copy(frame, shown);
            }

            screen.Feed(tty.Written);
            tty.ClearWritten();
            int top = Math.Min(log.Count, height - shown.Height);
            string[] above = [.. screen.Scrollback, .. Enumerable.Range(0, top).Select(screen.RowText)];
            Assert.Equal(log, above.Select(line => line.TrimEnd()).ToList());
            screen.AssertMatches(shown, $"seed {seed} step {step}", top);
        }
    }

    [Fact]
    public void Resize_asks_where_the_band_went_and_repaints_it_there()
    {
        var tty = new TestTty(20, 10);
        using var terminal = Start(tty, height: 3, row: 6);
        Present(terminal, "x");                                         // the cursor is parked at the band's top-left
        tty.Enqueue("\u001b[3;1R");                                     // after the resize, that is screen row 2
        tty.Resize(30, 8);
        Assert.True(terminal.Poll(out Event ev, 0));
        Assert.Equal(EventKind.Resize, ev.Kind);
        Assert.Equal(new Size(30, 3), ev.Size);                        // the band, not the screen

        tty.ClearWritten();
        Present(terminal, "y");
        Assert.Equal("\u001b[6n" + "\u001b[?2026h\u001b[3H\u001b[Jy\r\u001b[?2026l", tty.WrittenText);
    }

    [Fact]
    public void Suspend_in_inline_mode_keeps_the_screen_and_places_a_new_band_on_resume()
    {
        var tty = new TestTty(20, 10);
        using var terminal = Start(tty, height: 2, row: 3);
        tty.ClearWritten();
        tty.Enqueue("\u001b[8;1R");                                     // the shell printed a few lines meanwhile
        terminal.Suspend();
        string text = tty.WrittenText;
        Assert.DoesNotContain("?1049", text);
        Assert.Contains("\u001b[6n", text[tty.Suspends[0]..]);
        Assert.EndsWith("\u001b[8H\u001b[J\u001b[?69$p\r\u001b[K\u001b[?2026l", text);
    }

    private static void Present(Terminal terminal, string text)
    {
        terminal.BeginFrame().SetString(0, 0, text);
        terminal.Present();
    }

    private static void AssertBand(VtEmulator screen, Terminal terminal, string text, int top)
    {
        var expected = new CellBuffer(terminal.Width, terminal.Height);
        expected.SetString(0, 0, text);
        screen.AssertMatches(expected, "band", top);
    }

    private static void Copy(CellBuffer from, CellBuffer to)
    {
        to.Clear();
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
