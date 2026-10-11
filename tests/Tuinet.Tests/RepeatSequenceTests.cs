using System.Text;
using Tuinet.Testing;
using Tuinet.Widgets;

namespace Tuinet.Tests;

/// <summary>
/// On terminals known to have REP (they name themselves in reply to XTVERSION at startup), a glyph followed by more of
/// the same is drawn once and repeated with <c>CSI n b</c> when that is fewer bytes.
/// </summary>
public class RepeatSequenceTests
{
    private const string Esc = "\u001b";
    private const string Kitty = "\u001bP>|kitty(0.36.4)\u001b\\";
    private static readonly Style Red = new(Color.Red, Color.Default);
    private static readonly Style Blue = new(Color.Blue, Color.Default);

    [Theory]
    [InlineData("\u001bP>|XTerm(390)\u001b\\", true)]
    [InlineData("\u001bP>|kitty(0.36.4)\u001b\\", true)]
    [InlineData("\u001bP>|foot(1.18.1)\u001b\\", true)]
    [InlineData("\u001bP>|WezTerm 20240203-110809-5046fc22\u001b\\", true)]
    [InlineData("\u001bP>|ghostty 1.1.3\u001b\\", true)]
    [InlineData("\u001bP>|tmux 3.4\u001b\\", false)]          // has REP, but drops it after a non-ASCII glyph
    [InlineData("\u001bP>|xterm.js(5.5.0)\u001b\\", false)]   // not xterm
    [InlineData("\u001bP>|iTerm2 3.5.4\u001b\\", false)]      // not checked
    [InlineData("\u001bP>|\u001b\\", false)]
    [InlineData("\u001bP>kitty\u001b\\", false)]              // not an XTVERSION reply
    public void The_parser_reads_the_terminal_name(string reply, bool expected)
    {
        var parser = new VtParser();
        parser.Feed(Encoding.UTF8.GetBytes(reply + "x"));
        Assert.Equal(expected, parser.RepeatSequences);
        Assert.True(parser.TryTake(out Event ev));
        Assert.True(ev.Key.IsChar('x'));   // the reply itself is not an event
        Assert.False(parser.TryTake(out _));
    }

    [Fact]
    public void A_reply_split_across_reads_is_still_read()
    {
        var parser = new VtParser();
        foreach (string part in (string[])["\u001b", "P", ">|fo", "ot(1.18)\u001b", "\\"])
        {
            parser.Feed(Encoding.UTF8.GetBytes(part));
        }

        Assert.True(parser.RepeatSequences);
        Assert.False(parser.TryTake(out _));
    }

    [Fact]
    public void A_long_reply_is_skipped_whole()
    {
        var parser = new VtParser();
        parser.Feed(Encoding.UTF8.GetBytes($"{Esc}P>|kitty {new string('9', 200)}{Esc}\\y"));
        Assert.False(parser.RepeatSequences);
        Assert.True(parser.TryTake(out Event ev));
        Assert.True(ev.Key.IsChar('y'));
        Assert.False(parser.TryTake(out _));
    }

    [Theory]
    [InlineData("\u001bP", new[] { "Alt+P" })]
    [InlineData("\u001bPx", new[] { "Alt+P", "x" })]
    [InlineData("\u001bP\u001b[A", new[] { "Alt+P", "Up" })]
    [InlineData("\u001bP>a", new[] { "Alt+P", ">", "a" })]   // looked like a reply, then the input went quiet
    public void Alt_shift_p_is_still_a_key(string input, string[] expected)
    {
        var parser = new VtParser();
        parser.Feed(Encoding.UTF8.GetBytes(input));
        parser.FlushIncomplete();
        var keys = new List<string>();
        while (parser.TryTake(out Event ev))
        {
            KeyEvent key = ev.Key;
            keys.Add(key.Code == KeyCode.Char
                ? ((key.Modifiers & Modifiers.Alt) != 0 ? "Alt+" : "") + key.Rune
                : key.Code.ToString());
        }

        Assert.Equal(expected, keys);
        Assert.False(parser.RepeatSequences);
    }

    [Fact]
    public void The_terminal_asks_at_startup_before_clearing_the_screen()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        Assert.Contains($"{Esc}[?69$p{Esc}[>0q{Esc}[0m{Esc}[2J", tty.WrittenText);
    }

    [Fact]
    public void Repeat_is_active_once_the_terminal_names_itself()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        Assert.False(terminal.RepeatSequencesActive);
        tty.Enqueue(Kitty);
        Assert.False(terminal.Poll(out _, 0));
        Assert.True(terminal.RepeatSequencesActive);
    }

    [Fact]
    public void Turned_off_the_terminal_neither_asks_nor_uses_it()
    {
        var tty = new TestTty(20, 2);
        using var terminal = new Terminal(tty, new TerminalOptions { RepeatSequences = false });
        Assert.DoesNotContain(">0q", tty.WrittenText);
        tty.Enqueue(Kitty);
        terminal.Poll(out _, 0);
        Assert.False(terminal.RepeatSequencesActive);
        tty.ClearWritten();
        terminal.BeginFrame().SetString(0, 0, new string('─', 20));
        terminal.Present();
        Assert.DoesNotContain("b", tty.WrittenText);
    }

    [Fact]
    public void Inline_mode_asks_in_the_band_and_erases_what_a_terminal_might_print()
    {
        var tty = new TestTty(20, 10);
        tty.Enqueue("\u001b[5;1R");
        using var terminal = new Terminal(tty, new TerminalOptions { Inline = new InlineOptions(3), ScrollRegions = false });
        Assert.EndsWith($"{Esc}[5H{Esc}[J{Esc}[>0q\r{Esc}[K{Esc}[?2026l", tty.WrittenText);
    }

    [Fact]
    public void A_rule_is_one_glyph_and_a_repeat()
    {
        var (prev, cur) = Buffers(20, 1);
        cur.SetString(0, 0, new string('─', 20));
        // 9 bytes instead of 60; the run ends in the last column.
        Assert.Equal($"{Esc}[?2026h{Esc}[H─{Esc}[19b{Esc}[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Without_repeat_the_bytes_are_the_glyphs()
    {
        var (prev, cur) = Buffers(20, 1);
        cur.SetString(0, 0, new string('─', 20));
        Assert.Equal($"{Esc}[?2026h{Esc}[H{new string('─', 20)}{Esc}[?2026l", Render(cur, prev, repeat: false));
    }

    [Theory]
    [InlineData('=', 5, false)]   // ASCII: 4 more cost 4 bytes either way
    [InlineData('=', 6, true)]    // 5 more: CSI 5 b is 4 bytes
    [InlineData('é', 3, false)]   // 2-byte UTF-8: 2 more cost 4 bytes either way
    [InlineData('é', 4, true)]
    [InlineData('─', 2, false)]   // box drawing (3 bytes): 1 more is 3 bytes, CSI 1 b 4
    [InlineData('─', 3, true)]
    [InlineData('█', 3, true)]
    public void Repeat_is_used_only_when_it_is_shorter(char glyph, int length, bool repeated)
    {
        var (prev, cur) = Buffers(20, 1);
        cur.SetString(2, 0, new string(glyph, length));
        string expected = repeated ? $"{glyph}{Esc}[{length - 1}b" : new string(glyph, length);
        Assert.Equal($"{Esc}[?2026h{Esc}[1;3H{expected}{Esc}[?2026l", Render(cur, prev));
    }

    [Fact]
    public void A_style_change_starts_a_new_run()
    {
        var (prev, cur) = Buffers(20, 1);
        cur.SetString(0, 0, "────────", Red);
        cur.SetString(8, 0, "────────", Blue);
        cur.SetString(16, 0, "ab");
        // Each run is repeated right after its own glyph; the SGR comes before the glyph, never between it and REP.
        Assert.Equal($"{Esc}[?2026h{Esc}[H{Esc}[31m─{Esc}[7b{Esc}[34m─{Esc}[7b{Esc}[39mab{Esc}[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Clean_cells_inside_a_run_are_repeated_too()
    {
        var (prev, cur) = Buffers(20, 1);
        prev.SetString(4, 0, "──────");
        cur.SetString(0, 0, "──────────────");
        // Cheaper than painting 4 glyphs, jumping 6 cells and painting 4 more.
        Assert.Equal($"{Esc}[?2026h{Esc}[H─{Esc}[13b{Esc}[?2026l", Render(cur, prev));
    }

    [Fact]
    public void A_run_stops_at_the_last_cell_that_changed()
    {
        var (prev, cur) = Buffers(20, 1);
        prev.SetString(5, 0, "───────────────");
        cur.SetString(0, 0, "────────────────────");
        Assert.Equal($"{Esc}[?2026h{Esc}[H─{Esc}[4b{Esc}[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Wide_glyphs_clusters_and_links_are_never_repeated()
    {
        var (prev, cur) = Buffers(20, 3);
        cur.SetString(0, 0, "世世世世世世");
        cur.SetString(0, 1, string.Concat(Enumerable.Repeat("e\u0301", 8)));
        cur.SetString(0, 2, "────────");
        cur.SetLink(new Rect(0, 2, 8, 1), "https://example.com");
        string output = Render(cur, prev);
        Assert.DoesNotMatch(@"\x1b\[\d*b", output);
        Assert.Contains("世世世世世世", output);
        Assert.Equal(8, output.Split("e\u0301").Length - 1);   // each cluster written out (and placed on its own)
        Assert.Contains("────────", output);
        AssertScreen(cur, prev);
    }

    [Fact]
    public void A_run_of_plain_blanks_inside_a_row_prefers_repeat_to_erase_and_jump()
    {
        var (prev, cur) = Buffers(30, 1);
        prev.SetString(0, 0, new string('a', 30));
        cur.SetString(0, 0, "a" + new string(' ', 20) + "aaaabaaaa");
        // ECH 20 and a jump past the blanks would be 10 bytes ("CSI 20 X CSI 24 C" with the jump to the 'b').
        Assert.Equal($"{Esc}[?2026h{Esc}[1;2H {Esc}[19b{Esc}[4Cb{Esc}[?2026l", Render(cur, prev));
        Assert.Equal($"{Esc}[?2026h{Esc}[1;2H{Esc}[20X{Esc}[24Cb{Esc}[?2026l", Render(cur, prev, repeat: false));
    }

    [Fact]
    public void Erasing_to_the_end_of_a_row_or_the_last_change_stays_cheaper()
    {
        var (prev, cur) = Buffers(30, 1);
        prev.SetString(0, 0, new string('a', 30));
        cur.SetString(0, 0, "a");
        Assert.Equal($"{Esc}[?2026h{Esc}[1;2H{Esc}[K{Esc}[?2026l", Render(cur, prev));

        cur.SetString(0, 0, "a" + new string(' ', 20) + new string('a', 9));
        Assert.Equal($"{Esc}[?2026h{Esc}[1;2H{Esc}[20X{Esc}[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Blanks_that_show_an_attribute_are_repeated()
    {
        var (prev, cur) = Buffers(12, 1);
        prev.SetString(0, 0, "hello world!");
        cur.SetString(0, 0, "hello");
        cur.Fill(new Rect(5, 0, 7, 1), new Style(Color.Default, Color.Default, Attr.Reverse));
        Assert.Equal($"{Esc}[?2026h{Esc}[1;6H{Esc}[7m {Esc}[6b{Esc}[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Rules_on_consecutive_rows_continue_after_the_last_column()
    {
        var (prev, cur) = Buffers(10, 2);
        cur.SetString(0, 0, new string('─', 10));
        cur.SetString(0, 1, new string('═', 10));
        Assert.Equal($"{Esc}[?2026h{Esc}[H─{Esc}[9b\r\n═{Esc}[9b{Esc}[?2026l", Render(cur, prev));
        AssertScreen(cur, prev);
    }

    [Fact]
    public void A_wide_glyph_under_the_end_of_a_run_is_repainted()
    {
        var (prev, cur) = Buffers(12, 1);
        prev.SetString(0, 0, "abcd世xyz");
        cur.SetString(0, 0, "─────");
        cur.SetString(5, 0, " xyz");
        AssertScreen(cur, prev);
    }

    [Fact]
    public void Repeated_glyphs_never_join_a_cluster_after_them()
    {
        // A row with clusters takes the join checks: the run is repeated, then the cluster placed on its own.
        var (prev, cur) = Buffers(16, 1);
        cur.SetString(0, 0, "eeeeeee\u0301eeeeee👍🏽");
        Assert.Contains($"e{Esc}[5b", Render(cur, prev));
        AssertScreen(cur, prev);
    }

    [Fact]
    public void A_scrolled_band_paints_its_new_rule_with_repeat()
    {
        var prev = new CellBuffer(20, 6);
        var cur = new CellBuffer(20, 6);
        for (int y = 0; y < 6; y++)
        {
            prev.SetString(0, y, $"row {y} " + new string('─', 10));
            cur.SetString(0, y, $"row {y + 1} " + new string('─', 10));
        }

        cur.SetString(0, 5, new string('━', 20));
        var screen = new VtEmulator(20, 6, repeat: true);
        string output = AssertScreen(cur, prev, screen);
        Assert.Contains($"{Esc}[M", output);   // the band moved
        Assert.Contains($"━{Esc}[19b", output);
        Assert.True(screen.LineMoves > 0 && screen.Repeats > 0);
    }

    [Fact]
    public void Terminal_frames_use_repeat_once_detected()
    {
        var tty = new TestTty(30, 3);
        using var terminal = new Terminal(tty);
        var screen = new VtEmulator(30, 3, repeat: true);
        tty.Enqueue(Kitty);
        terminal.Poll(out _, 0);
        screen.Feed(tty.Written);
        Assert.Equal(1, screen.VersionQueries);

        tty.ClearWritten();
        CellBuffer frame = terminal.BeginFrame();
        frame.Render(new Block { BorderType = BorderType.Rounded, Title = "box" }, frame.Area);
        terminal.Present();
        screen.Feed(tty.Written);
        screen.AssertMatches(frame, "a box");
        Assert.Contains($"╰─{Esc}[27b╯", tty.WrittenText);
        Assert.Equal(2, screen.Repeats);   // the top and bottom borders
        Assert.True(terminal.LastFrameBytes < 100, $"{terminal.LastFrameBytes} bytes");
    }

    [Fact]
    public void Inline_print_above_uses_repeat()
    {
        var tty = new TestTty(20, 10);
        tty.Enqueue("\u001b[5;1R");
        using var terminal = new Terminal(tty, new TerminalOptions { Inline = new InlineOptions(2) });
        tty.Enqueue(Kitty);
        terminal.Poll(out _, 0);
        tty.ClearWritten();
        terminal.PrintAbove(new string('=', 20));
        Assert.Contains($"={Esc}[19b", tty.WrittenText);
    }

    private static (CellBuffer Previous, CellBuffer Current) Buffers(int width, int height) =>
        (new CellBuffer(width, height), new CellBuffer(width, height));

    private static string Render(CellBuffer current, CellBuffer previous, bool repeat = true)
    {
        var renderer = new Renderer(ColorMode.TrueColor) { Repeat = repeat };
        renderer.AfterClear();
        var output = new VtBuffer(64);
        renderer.Render(current, previous, output);
        return Encoding.UTF8.GetString(output.Written);
    }

    /// <summary>Replay <paramref name="previous"/> then <paramref name="current"/> in an emulator with REP: the screen must match.</summary>
    private static string AssertScreen(CellBuffer current, CellBuffer previous, VtEmulator? screen = null)
    {
        screen ??= new VtEmulator(current.Width, current.Height, repeat: true);
        var renderer = new Renderer(ColorMode.TrueColor) { Repeat = true };
        renderer.AfterClear();
        var output = new VtBuffer(64);
        renderer.Render(previous, new CellBuffer(current.Width, current.Height), output);
        screen.Feed(output.Written.ToArray());
        screen.AssertMatches(previous, "before");
        output.Clear();
        renderer.Render(current, previous, output);
        screen.Feed(output.Written.ToArray());
        screen.AssertMatches(current, "after");
        return Encoding.UTF8.GetString(output.Written);
    }
}
