using System.Text;
using Tuinet.Testing;
using Tuinet.Widgets;

namespace Tuinet.Tests;

/// <summary>
/// On terminals with left/right margins (DECLRMM, asked with DECRQM at startup), a band narrower than the screen that
/// scrolls is moved by the terminal: margins around it, then delete/insert line.
/// </summary>
public class LeftRightMarginTests
{
    private const string Esc = "\u001b";
    private const string Supported = "\u001b[?69;2$y";

    [Theory]
    [InlineData("\u001b[?69;1$y", true)]
    [InlineData("\u001b[?69;2$y", true)]
    [InlineData("\u001b[?69;3$y", true)]
    [InlineData("\u001b[?69;0$y", false)]   // not recognized (foot, tmux)
    [InlineData("\u001b[?69;4$y", false)]   // permanently reset
    [InlineData("\u001b[?2026;2$y", false)] // another mode
    [InlineData("\u001b[?69;2y", false)]    // not a mode report
    public void The_parser_reads_the_mode_report(string reply, bool expected)
    {
        var parser = new VtParser();
        parser.Feed(Encoding.UTF8.GetBytes(reply + "x"));
        Assert.Equal(expected, parser.LeftRightMargins);
        Assert.True(parser.TryTake(out Event ev));
        Assert.True(ev.Key.IsChar('x'));   // the report itself is not an event
        Assert.False(parser.TryTake(out _));
    }

    [Fact]
    public void The_terminal_asks_at_startup_before_clearing_the_screen()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        Assert.Contains($"{Esc}[?2027h{Esc}[?69$p{Esc}[0m{Esc}[2J", tty.WrittenText);
    }

    [Fact]
    public void Margins_are_active_once_the_terminal_reports_them()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        Assert.False(terminal.LeftRightMarginsActive);
        tty.Enqueue(Supported);
        Assert.False(terminal.Poll(out _, 0));
        Assert.True(terminal.LeftRightMarginsActive);
    }

    [Fact]
    public void Without_scroll_regions_the_terminal_neither_asks_nor_uses_them()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty, new TerminalOptions { ScrollRegions = false });
        Assert.DoesNotContain("$p", tty.WrittenText);
        tty.Enqueue(Supported);
        terminal.Poll(out _, 0);
        Assert.False(terminal.LeftRightMarginsActive);
    }

    [Fact]
    public void A_list_beside_a_panel_is_moved_by_the_terminal()
    {
        CellBuffer prev = Split(first: 0);
        CellBuffer cur = Split(first: 1);
        // Left/right margin mode, margins around the list (with the panel's border, which lines up too), delete a line,
        // margins reset and mode off; then only the new row's cells are painted.
        Assert.Equal(
            $"{Esc}[?2026h{Esc}[?69h{Esc}[1;5r{Esc}[3;20s{Esc}[1;3H{Esc}[M{Esc}[s{Esc}[r{Esc}[?69l{Esc}[5;4H│item 5{Esc}[?2026l",
            Render(cur, prev, margins: true));
    }

    [Fact]
    public void A_list_beside_a_panel_scrolling_down_inserts_a_line()
    {
        CellBuffer prev = Split(first: 1);
        CellBuffer cur = Split(first: 0);
        Assert.Equal(
            $"{Esc}[?2026h{Esc}[?69h{Esc}[1;5r{Esc}[3;20s{Esc}[1;3H{Esc}[L{Esc}[s{Esc}[r{Esc}[?69l{Esc}[1;4H│item 0{Esc}[?2026l",
            Render(cur, prev, margins: true));
    }

    [Fact]
    public void Without_margins_the_list_rows_are_repainted()
    {
        // Every row's changed digit is rewritten.
        Assert.Equal(
            $"{Esc}[?2026h{Esc}[1;10H1{Esc}[2;10H2{Esc}[3;10H3{Esc}[4;10H4{Esc}[5;10H5{Esc}[?2026l",
            Render(Split(first: 1), Split(first: 0), margins: false));
    }

    [Fact]
    public void A_narrow_band_is_not_worth_the_margin_sequences()
    {
        // Three rows of a 4-column list: 9 cells line up, far fewer bytes than the ~45 the margins cost.
        var prev = new CellBuffer(12, 4);
        var cur = new CellBuffer(12, 4);
        for (int y = 0; y < 4; y++)
        {
            prev.SetString(0, y, $"P{y}");
            cur.SetString(0, y, $"P{y}");
            prev.SetString(8, y, $"ab{y}");
            cur.SetString(8, y, $"ab{y + 1}");
        }

        Assert.DoesNotContain("?69h", Render(cur, prev, margins: true));
    }

    [Fact]
    public void Margins_never_split_a_wide_glyph()
    {
        // On the last row a wide glyph covers columns 1-2, where the margin would go: the margin moves right of it.
        CellBuffer prev = Split(first: 0);
        CellBuffer cur = Split(first: 1);
        prev.SetString(1, 4, "世");
        cur.SetString(1, 4, "世");
        var renderer = new Renderer(ColorMode.TrueColor) { LeftRightMargins = true };
        renderer.AfterClear();
        var screen = new VtEmulator(20, 5, leftRightMargins: true);
        var output = new VtBuffer(64);
        renderer.Render(prev, new CellBuffer(20, 5), output);
        screen.Feed(output.Written.ToArray());
        output.Clear();
        renderer.Render(cur, prev, output);
        screen.Feed(output.Written.ToArray());
        Assert.Contains($"{Esc}[1;5r{Esc}[4;20s", Encoding.UTF8.GetString(output.Written));
        screen.AssertMatches(cur, "after the scroll");
        Assert.Equal(1, screen.ColumnMoves);
    }

    [Theory]
    [InlineData(31, false)]
    [InlineData(32, false)]
    [InlineData(33, false)]
    [InlineData(34, false)]
    [InlineData(35, true)]
    [InlineData(36, true)]
    public void Screen_matches_while_split_bands_scroll(int seed, bool legacyTerminal)
    {
        var (terminal, tty, screen) = Start(40, 14, Supported, legacyTerminal, leftRightMargins: true);
        using (terminal)
        {
            Assert.True(terminal.LeftRightMarginsActive);
            Run(terminal, tty, screen, new Random(seed), $"seed {seed}");
            Assert.True(screen.ColumnMoves > 50, $"{screen.ColumnMoves} column moves of {screen.LineMoves} IL/DL");
        }
    }

    [Theory]
    [InlineData("")]
    [InlineData("\u001b[?69;0$y")]
    [InlineData("\u001b[?69;4$y")]
    public void Without_a_positive_reply_the_margins_are_never_used(string reply)
    {
        // The emulator has no left/right margins: any use of them fails.
        var (terminal, tty, screen) = Start(40, 14, reply, legacyTerminal: false, leftRightMargins: false);
        using (terminal)
        {
            Assert.False(terminal.LeftRightMarginsActive);
            Run(terminal, tty, screen, new Random(41), $"reply '{reply}'");
            Assert.Equal(0, screen.ColumnMoves);
        }
    }

    [Fact]
    public void Inline_mode_asks_in_the_band_and_erases_what_a_terminal_might_print()
    {
        var tty = new TestTty(20, 10);
        tty.Enqueue("\u001b[5;1R");
        using var terminal = new Terminal(tty, new TerminalOptions { Inline = new InlineOptions(3) });
        Assert.EndsWith($"{Esc}[5H{Esc}[J{Esc}[?69$p\r{Esc}[K{Esc}[?2026l", tty.WrittenText);
    }

    [Fact]
    public void Inline_bands_scroll_between_margins_too()
    {
        var tty = new TestTty(80, 24);
        tty.Enqueue("\u001b[6;1R" + Supported);
        using var terminal = new Terminal(tty, new TerminalOptions { Inline = new InlineOptions(14) });
        terminal.Poll(out _, 0);
        Assert.True(terminal.LeftRightMarginsActive);
        var screen = new VtEmulator(80, 24, allowScroll: true, leftRightMargins: true);
        screen.Feed(tty.Written);
        var list = new ListState();
        for (int i = 0; i < 30; i++)
        {
            list.Selected = i;
            CellBuffer frame = terminal.BeginFrame();
            SplitScreen(frame, ref list, i);
            tty.ClearWritten();
            terminal.Present();
            screen.Feed(tty.Written);
            screen.AssertMatches(frame, $"frame {i}", top: 5);
        }

        // The list has 11 rows, so frames 11-29 scroll it.
        Assert.True(screen.ColumnMoves >= 15, $"{screen.ColumnMoves} column moves");
    }

    [Fact]
    public void A_list_beside_a_changing_panel_costs_a_fraction_of_a_repaint()
    {
        int with = SteadyScrollBytes(margins: true);
        int without = SteadyScrollBytes(margins: false);
        Assert.True(with * 3 < without, $"{with} bytes with margins, {without} without");
    }

    [Fact]
    public void Split_scroll_frames_allocate_nothing()
    {
        var tty = new AllocationTests.NullTty(120, 40) { Input = Encoding.UTF8.GetBytes(Supported + "x") };
        using var terminal = new Terminal(tty);
        Assert.True(terminal.Poll(out _, 0));
        tty.Input = [];
        Assert.True(terminal.LeftRightMarginsActive);
        var list = new ListState();
        for (int i = 0; i < 50; i++)
        {
            ScrollFrame(terminal, ref list, i);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 50; i < 1050; i++)
        {
            ScrollFrame(terminal, ref list, i);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    private static void ScrollFrame(Terminal terminal, ref ListState list, int i)
    {
        list.Selected = i % 500;
        CellBuffer frame = terminal.BeginFrame();
        SplitScreen(frame, ref list, i);
        terminal.Present();
    }

    /// <summary>Holding ↓ in a list beside a details panel that follows the selection: bytes of one steady frame.</summary>
    private static int SteadyScrollBytes(bool margins)
    {
        var tty = new AllocationTests.NullTty(120, 40) { Input = Encoding.UTF8.GetBytes((margins ? Supported : "") + "x") };
        using var terminal = new Terminal(tty);
        terminal.Poll(out _, 0);
        tty.Input = [];
        var list = new ListState();
        for (int i = 0; i < 100; i++)
        {
            ScrollFrame(terminal, ref list, i);
        }

        return terminal.LastFrameBytes;
    }

    private static readonly string[] Names = ["parser", "renderer", "layout", "docs", "input", "terminal", "widgets"];
    private static readonly string[] Kinds = ["fix", "feature", "chore", "refactor", "perf"];
    private static readonly string[] PanelLines = [.. Enumerable.Range(0, 40).Select(i => $"{Names[i % Names.Length]} owner {i}")];
    private static readonly string[] Items =
        [.. Enumerable.Range(0, 500).Select(i => $"{Kinds[i * 3 % Kinds.Length]}/{Names[i * 5 % Names.Length]}-{i * 7919 % 1000}")];

    /// <summary>A list on the left, a details panel on the right that shows the selected item, a status line.</summary>
    private static void SplitScreen(CellBuffer frame, ref ListState list, int i)
    {
        Span<Rect> rows = stackalloc Rect[2];
        Layout.Vertical(frame.Area, [Constraint.Fill(), Constraint.Length(1)], rows);
        Span<Rect> columns = stackalloc Rect[2];
        Layout.Horizontal(rows[0], [Constraint.Fill(), Constraint.Length(30)], columns, spacing: 1);
        var block = new Block { Title = "branches", BorderType = BorderType.Rounded };
        frame.Render(block, columns[0]);
        frame.Render(new ListView<TextItems>(new TextItems(Items))
        {
            SelectedStyle = new Style(Color.Black, Color.White),
            Scrollbar = ScrollbarMode.Auto,
        }, block.Inner(columns[0]), ref list);
        var details = new Block { Title = "details", BorderType = BorderType.Rounded };
        frame.Render(details, columns[1]);
        Rect inner = details.Inner(columns[1]);
        Span<char> text = stackalloc char[32];
        frame.SetString(inner.X, inner.Y, Items[list.Selected]);
        frame.SetString(inner.X, inner.Y + 1, text[..Format(text, "size ", list.Selected * 37 % 1000)]);
        for (int y = 3; y < inner.Height; y++)
        {
            frame.SetString(inner.X, inner.Y + y, PanelLines[y % PanelLines.Length]);
        }

        frame.SetString(0, rows[1].Y, text[..Format(text, "j/k move  q quit  frame ", i)]);
    }

    private static int Format(Span<char> destination, string label, int value)
    {
        label.CopyTo(destination);
        value.TryFormat(destination[label.Length..], out int written);
        return label.Length + written;
    }

    private static (Terminal Terminal, TestTty Tty, VtEmulator Screen) Start(int width, int height, string reply, bool legacyTerminal, bool leftRightMargins)
    {
        var tty = new TestTty(width, height);
        var terminal = new Terminal(tty);
        tty.Enqueue(reply);
        terminal.Poll(out _, 0);
        var screen = new VtEmulator(width, height, legacyTerminal, leftRightMargins: leftRightMargins);
        screen.Feed(tty.Written);
        Assert.Equal(1, screen.MarginQueries);
        return (terminal, tty, screen);
    }

    /// <summary>
    /// Random frames over a model whose rectangles scroll (a list beside panels), with panels that change as they
    /// scroll and unrelated edits; the emulated screen must match every frame.
    /// </summary>
    private static void Run(Terminal terminal, TestTty tty, VtEmulator screen, Random random, string context)
    {
        int width = terminal.Width;
        int height = terminal.Height;
        var model = new CellBuffer(width, height);
        for (int i = 0; i < 300; i++)
        {
            Mutate(model, random);
        }

        for (int frame = 0; frame < 300; frame++)
        {
            int top = random.Next(height - 4);
            int bottom = random.Next(top + 3, height);
            int left = random.Next(width - 8);
            int right = random.Next(left + 6, width + 1);   // exclusive
            int k = random.Next(1, Math.Min(4, bottom - top)) * (random.Next(2) == 0 ? 1 : -1);
            ShiftRect(model, random, top, bottom, left, right, k);
            if (random.Next(2) == 0)
            {
                // The panel beside it changes too (details of the new selection).
                int x = right < width - 2 ? random.Next(right, width) : random.Next(0, Math.Max(1, left));
                for (int y = top; y <= bottom; y++)
                {
                    model.SetString(x, y, Words[random.Next(Words.Length)], Styles[random.Next(Styles.Length)]);
                }
            }

            for (int i = random.Next(3); i > 0; i--)
            {
                Mutate(model, random);
            }

            if (random.Next(3) == 0)
            {
                model.SetCursor(random.Next(width), random.Next(height));
            }
            else
            {
                model.HideCursor();
            }

            CellBuffer buffer = terminal.BeginFrame();
            Copy(model, buffer);
            tty.ClearWritten();
            terminal.Present();
            screen.Feed(tty.Written);
            screen.AssertMatches(buffer, $"{context} frame {frame}");
        }
    }

    private static readonly Style[] Styles =
    [
        default,
        new(Color.Red, default),
        new(Color.Rgb(10, 20, 30), Color.Rgb(200, 210, 220)),
        new(Color.Indexed(200), Color.BrightBlue, Attr.Bold),
        new(default, Color.Green, Attr.Underline),
    ];

    private static readonly string[] Words = ["a", "bc", "the quick", "你好", "x你y", "🚀!", "──", "é", "👨‍👩‍👧", "❤️", "z"];

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
            case 1:
                buffer.SetLink(new Rect(x, y, random.Next(1, 10), 1), random.Next(3) == 0 ? "" : "https://a.example/1");
                break;
            default:
                buffer.SetString(x, y, Words[random.Next(Words.Length)], style);
                break;
        }
    }

    /// <summary>Scroll columns [left, right) of rows [top, bottom] by k, filling the vacated rows with new content.</summary>
    private static void ShiftRect(CellBuffer buffer, Random random, int top, int bottom, int left, int right, int k)
    {
        int width = buffer.Width;
        Span<Cell> cells = buffer.Cells;
        Cell[] copy = cells.ToArray();
        for (int y = top; y <= bottom; y++)
        {
            int from = y + k;
            Span<Cell> row = cells.Slice(y * width + left, right - left);
            if (from >= top && from <= bottom)
            {
                copy.AsSpan(from * width + left, right - left).CopyTo(row);
            }
            else
            {
                row.Fill(Cell.Blank(Styles[random.Next(Styles.Length)]));
                buffer.SetString(left + random.Next(right - left), y, Words[random.Next(Words.Length)], Styles[random.Next(Styles.Length)]);
                buffer.Fill(new Rect(right, y, width - right, 1), Style.Default);   // keep the new text inside the band
            }

            // A copy can cut a wide glyph at either edge: repair it like a real write would.
            if (left > 0 && (cells[y * width + left].IsContinuation || cells[y * width + left - 1].Width == 2))
            {
                buffer.Fill(new Rect(left - 1, y, 2, 1), Style.Default);
            }

            if (right < width && (cells[y * width + right].IsContinuation || cells[y * width + right - 1].Width == 2))
            {
                buffer.Fill(new Rect(right - 1, y, 2, 1), Style.Default);
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

    /// <summary>A 20-wide screen: a panel ("P0 │") in columns 0-3 that stays put, a list of items from <paramref name="first"/>.</summary>
    private static CellBuffer Split(int first)
    {
        var buffer = new CellBuffer(20, 5);
        for (int y = 0; y < 5; y++)
        {
            buffer.SetString(0, y, $"P{y} │item {first + y}");
        }

        return buffer;
    }

    private static string Render(CellBuffer current, CellBuffer previous, bool margins)
    {
        var renderer = new Renderer(ColorMode.TrueColor) { LeftRightMargins = margins };
        renderer.AfterClear();
        var output = new VtBuffer(64);
        renderer.Render(current, previous, output);
        return Encoding.UTF8.GetString(output.Written);
    }
}
