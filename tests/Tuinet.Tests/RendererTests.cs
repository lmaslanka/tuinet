using System.Text;

namespace Tuinet.Tests;

public class RendererTests
{
    private static readonly Style Red = new(Color.Rgb(255, 0, 0), Color.Default);

    [Fact]
    public void Changed_cell_emits_sync_cursor_and_rune()
    {
        var (prev, cur) = Buffers(4, 2);
        cur.SetRune(0, 0, new Rune('A'));
        Assert.Equal("\u001b[?2026h\u001b[HA\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Unchanged_buffer_emits_nothing()
    {
        var (prev, cur) = Buffers(4, 2);
        prev.SetRune(0, 0, new Rune('A'));
        cur.SetRune(0, 0, new Rune('A'));
        Assert.Equal("", Render(cur, prev));
    }

    [Fact]
    public void Rgb_foreground_is_one_truecolor_sgr()
    {
        var (prev, cur) = Buffers(4, 1);
        cur.SetString(0, 0, "XY", Red);
        string output = Render(cur, prev);
        Assert.Contains("\u001b[38;2;255;0;0mXY", output);
        Assert.Equal(1, Count(output, "38;2"));
    }

    [Fact]
    public void Style_change_emits_only_the_delta()
    {
        var (prev, cur) = Buffers(4, 1);
        var a = new Style(Color.Red, Color.Blue);
        var b = new Style(Color.Red, Color.Green);
        cur.SetString(0, 0, "a", a);
        cur.SetString(1, 0, "b", b);
        string output = Render(cur, prev);
        Assert.Contains("\u001b[31;44ma\u001b[42mb", output);
    }

    [Fact]
    public void Indexed_colors_use_short_codes()
    {
        var (prev, cur) = Buffers(4, 1);
        cur.SetString(0, 0, "a", new Style(Color.BrightRed, Color.Indexed(200)));
        Assert.Contains("\u001b[91;48;5;200ma", Render(cur, prev));
    }

    [Fact]
    public void Attributes_are_added_and_removed_precisely()
    {
        var (prev, cur) = Buffers(4, 1);
        cur.SetString(0, 0, "a", new Style(default, default, Attr.Bold | Attr.Underline));
        cur.SetString(1, 0, "b", new Style(default, default, Attr.Underline));
        cur.SetString(2, 0, "c", default);
        Assert.Contains("\u001b[1;4ma\u001b[22mb\u001b[24mc", Render(cur, prev));
    }

    [Fact]
    public void Clearing_bold_keeps_dim()
    {
        var (prev, cur) = Buffers(4, 1);
        cur.SetString(0, 0, "a", new Style(default, default, Attr.Bold | Attr.Dim));
        cur.SetString(1, 0, "b", new Style(default, default, Attr.Dim));
        Assert.Contains("a\u001b[22;2mb", Render(cur, prev));
    }

    [Fact]
    public void Distant_changes_on_a_row_jump_with_cuf()
    {
        var (prev, cur) = Buffers(80, 1);
        cur.SetRune(0, 0, new Rune('a'));
        cur.SetRune(50, 0, new Rune('b'));
        string output = Render(cur, prev);
        Assert.Equal("\u001b[?2026h\u001b[Ha\u001b[49Cb\u001b[?2026l", output);
    }

    [Fact]
    public void Small_clean_gap_is_rewritten_instead_of_jumped()
    {
        var (prev, cur) = Buffers(10, 1);
        prev.SetString(0, 0, "xyz");
        cur.SetString(0, 0, "ayc");
        Assert.Contains("\u001b[Hayc", Render(cur, prev));
    }

    [Fact]
    public void Next_row_start_uses_crlf()
    {
        var (prev, cur) = Buffers(10, 3);
        cur.SetRune(0, 0, new Rune('a'));
        cur.SetRune(0, 1, new Rune('b'));
        Assert.Contains("\u001b[Ha\r\nb", Render(cur, prev));
    }

    [Fact]
    public void Wide_glyph_is_emitted_once_and_advances_two_columns()
    {
        var (prev, cur) = Buffers(10, 1);
        cur.SetString(0, 0, "你b");
        Assert.Contains("\u001b[H你b\u001b", Render(cur, prev));
    }

    [Fact]
    public void Narrow_over_wide_repaints_the_freed_half()
    {
        var (prev, cur) = Buffers(10, 1);
        prev.SetString(0, 0, "你");
        cur.SetString(0, 0, "a");
        Assert.Contains("\u001b[Ha ", Render(cur, prev));
    }

    [Fact]
    public void Change_on_right_half_of_wide_glyph_starts_at_its_left_half()
    {
        var (prev, cur) = Buffers(10, 1);
        prev.SetString(0, 0, "你");
        cur.SetString(0, 0, "你", Red);
        string output = Render(cur, prev);
        Assert.Contains("\u001b[H", output);
        Assert.Contains("你", output);
    }

    [Fact]
    public void Last_column_write_does_not_trust_the_cursor_column()
    {
        // After a glyph in the last column the cursor may be in the pending-wrap state: the next cell is reached
        // with an absolute column (CHA, which clears that state) and a line feed, never a relative column move.
        var (prev, cur) = Buffers(4, 2);
        cur.SetRune(3, 0, new Rune('a'));
        cur.SetRune(3, 1, new Rune('b'));
        Assert.Contains("\u001b[1;4Ha\u001b[4G\nb", Render(cur, prev));
    }

    [Theory]
    [InlineData(ColorMode.Indexed256, "38;5;196")]
    [InlineData(ColorMode.Basic16, "91")]
    public void Rgb_is_downsampled(ColorMode mode, string expected)
    {
        var (prev, cur) = Buffers(4, 1);
        cur.SetString(0, 0, "a", Red);
        Assert.Contains($"\u001b[{expected}ma", Render(cur, prev, mode));
    }

    [Fact]
    public void No_color_mode_keeps_attributes_only()
    {
        var (prev, cur) = Buffers(4, 1);
        cur.SetString(0, 0, "a", Red.With(Attr.Reverse));
        string output = Render(cur, prev, ColorMode.None);
        Assert.Contains("\u001b[7ma", output);
        Assert.DoesNotContain("38;", output);
    }

    [Fact]
    public void Full_repaint_bytes_are_bounded()
    {
        var (prev, cur) = Buffers(200, 60);
        var style = new Style(Color.Rgb(200, 210, 215), Color.Rgb(20, 24, 28));
        for (int y = 0; y < 60; y++)
        {
            cur.Fill(cur.Area.Row(y), style);
            cur.SetString(0, y, "the quick brown fox jumps over the lazy dog", style);
        }

        int bytes = Encoding.UTF8.GetByteCount(Render(cur, prev));

        // One SGR for the whole screen, one byte per cell, and a CRLF per row.
        Assert.True(bytes < 200 * 60 + 60 * 2 + 64, $"{bytes} bytes");
    }

    [Fact]
    public void Rows_moving_up_are_scrolled_by_the_terminal()
    {
        CellBuffer prev = Lines("a", "b", "c", "d", "e");
        CellBuffer cur = Lines("b", "c", "d", "e", "f");
        // Margins over the band, delete one line at its top, clear margins, then paint only the new row.
        Assert.Equal("\u001b[?2026h\u001b[1;5r\u001b[1H\u001b[M\u001b[r\u001b[5Hf\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Rows_moving_down_are_scrolled_by_the_terminal()
    {
        CellBuffer prev = Lines("a", "b", "c", "d", "e");
        CellBuffer cur = Lines("z", "a", "b", "c", "d");
        Assert.Equal("\u001b[?2026h\u001b[1;5r\u001b[1H\u001b[L\u001b[r\u001b[Hz\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Scroll_margins_cover_only_the_band_that_moved()
    {
        CellBuffer prev = Lines("H", "a", "b", "c", "d", "F");
        CellBuffer cur = Lines("H", "b", "c", "d", "e", "F");
        Assert.Equal("\u001b[?2026h\u001b[2;5r\u001b[2H\u001b[M\u001b[r\u001b[5He\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Scroll_resets_a_colored_background_first()
    {
        var renderer = new Renderer(ColorMode.TrueColor);
        renderer.AfterClear();
        var output = new VtBuffer(64);
        CellBuffer blank = Lines("", "", "", "", "");
        CellBuffer first = Lines("a", "b", "c", "d", "e");
        first.SetString(3, 4, "#", new Style(Color.Default, Color.Blue));   // leaves the pen blue
        renderer.Render(first, blank, output);

        CellBuffer second = Lines("b", "c", "d", "e", "f");
        second.SetString(3, 3, "#", new Style(Color.Default, Color.Blue));
        output.Clear();
        renderer.Render(second, first, output);
        string text = Encoding.UTF8.GetString(output.Written);
        Assert.StartsWith("\u001b[?2026h\u001b[49m\u001b[1;5r", text);
    }

    [Fact]
    public void Short_bands_and_partial_width_moves_are_diffed_normally()
    {
        Assert.DoesNotContain("\u001b[r", Render(Lines("a", "b", "c", "x"), Lines("b", "c", "d", "x")));     // 3 rows, 2 match
        CellBuffer prev = Lines("a  |", "b  |", "c  |", "d  |", "e  |");
        CellBuffer cur = Lines("b  1", "c  2", "d  3", "e  4", "f  5");                                       // right edge differs
        Assert.DoesNotContain("\u001b[r", Render(cur, prev));
    }

    [Fact]
    public void Scroll_regions_can_be_turned_off()
    {
        string output = Render(Lines("b", "c", "d", "e", "f"), Lines("a", "b", "c", "d", "e"), scrollRegions: false);
        Assert.Equal("\u001b[?2026h\u001b[Hb\r\nc\r\nd\r\ne\r\nf\u001b[?2026l", output);
    }

    [Fact]
    public void A_cluster_is_sent_whole_and_the_next_cell_is_placed_absolutely()
    {
        var (prev, cur) = Buffers(4, 1);
        cur.SetString(0, 0, "e\u0301x");
        Assert.Equal("\u001b[?2026h\u001b[He\u0301\u001b[2Gx\u001b[?2026l", Render(cur, prev));   // CHA: the row is known, the column isn't
    }

    [Fact]
    public void Cells_an_older_terminal_draws_over_are_repainted_after_a_cluster()
    {
        // Without grapheme support the family is three emoji, 6 columns: repaint columns 2-5 afterwards.
        var (prev, cur) = Buffers(8, 1);
        cur.SetString(0, 0, "👨‍👩‍👧y");
        Assert.Equal("\u001b[?2026h\u001b[H👨‍👩‍👧\u001b[3Gy   \u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void A_cluster_an_older_terminal_draws_narrower_is_blanked_first()
    {
        // ❤️ is 2 columns; without grapheme support it's 1, so blank both cells before drawing it.
        var (prev, cur) = Buffers(4, 1);
        cur.SetString(0, 0, "❤️z");
        Assert.Equal("\u001b[?2026h\u001b[H  \r❤️\u001b[3Gz\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Cells_that_would_merge_on_the_terminal_are_separated_by_a_cursor_move()
    {
        var (prev, cur) = Buffers(4, 1);
        cur.SetString(0, 0, "🇵");
        cur.SetString(2, 0, "🇱");
        Assert.Equal("\u001b[?2026h\u001b[H🇵\u001b[3G🇱\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void A_cluster_that_starts_with_a_spacing_mark_is_kept_apart_from_the_letter_before_it()
    {
        // U+093F is a spacing vowel sign: drawn right after "a" it would attach to it on the terminal.
        var (prev, cur) = Buffers(4, 1);
        cur.SetString(0, 0, "a");
        cur.SetString(1, 0, "\u093F\u0901");
        Assert.True(cur[1, 0].IsGrapheme);
        Assert.Equal("\u001b[?2026h\u001b[Ha\u001b[2G\u093F\u0901\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void The_cell_after_a_lone_emoji_plane_glyph_is_placed_absolutely()
    {
        // Terminals disagree on a lone skin tone (tmux attaches it to the emoji before it and doesn't
        // advance), so nothing after it may rely on where the cursor ended up.
        var (prev, cur) = Buffers(6, 1);
        cur.SetString(0, 0, "👍");
        cur.SetString(2, 0, "🏽");
        cur.SetString(4, 0, "b");
        Assert.Equal("\u001b[?2026h\u001b[H👍\u001b[3G🏽\u001b[5Gb\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void A_column_of_changed_cells_at_the_right_edge_uses_cha_and_line_feeds()
    {
        // At the last column the cursor's column is unknown afterwards (pending wrap): CHA then LF, 6 bytes per
        // row instead of a 7-byte CUP (a scrollbar track or a right border).
        var (prev, cur) = Buffers(10, 5);
        for (int y = 0; y < 5; y++)
        {
            cur.SetRune(9, y, new Rune('│'));
        }

        Assert.Equal("\u001b[?2026h\u001b[1;10H│\u001b[10G\n│\u001b[10G\n│\u001b[10G\n│\u001b[10G\n│\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void A_column_of_changed_cells_inside_the_row_moves_down_with_line_feeds()
    {
        // The renderer runs with output post-processing off, so LF is a 1-byte "cursor down" in the same column.
        var (prev, cur) = Buffers(10, 5);
        for (int y = 0; y < 5; y++)
        {
            cur.SetRune(4, y, new Rune('│'));
        }

        // After each glyph the cursor is one past it: BS then LF, 2 bytes per row instead of a 6-byte CUP.
        Assert.Equal("\u001b[?2026h\u001b[1;5H│\b\n│\b\n│\b\n│\b\n│\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Cursor_moves_pick_the_shortest_sequence()
    {
        var renderer = new Renderer(ColorMode.TrueColor);
        renderer.AfterClear();
        var output = new VtBuffer(256);
        var blank = new CellBuffer(120, 30);
        var cur = new CellBuffer(120, 30);
        cur.SetRune(50, 10, new Rune('a'));
        renderer.Render(cur, blank, output);                         // cursor at (51, 10)
        Assert.Equal("\u001b[?2026h\u001b[11;51Ha\u001b[?2026l", Encoding.UTF8.GetString(output.Written));

        Assert.Equal("\b\bb", Move(cur, renderer, output, 49, 10, 'b'));       // two back: BS BS (2 bytes)
        Assert.Equal("\u001b[2Cc", Move(cur, renderer, output, 52, 10, 'c'));   // forward: CUF
        Assert.Equal("\u001b[10Dd", Move(cur, renderer, output, 43, 10, 'd'));  // ten back: CUB beats CHA (ESC[44G)
        Assert.Equal("\u001b[3Ge", Move(cur, renderer, output, 2, 10, 'e'));    // far back to a low column: CHA
        Assert.Equal("\u001b[2Af", Move(cur, renderer, output, 3, 8, 'f'));     // same column, up: CUU
        Assert.Equal("\u001b[5Bg", Move(cur, renderer, output, 4, 13, 'g'));    // same column, far down: CUD
        Assert.Equal("\r\n\nh", Move(cur, renderer, output, 0, 15, 'h'));     // start of a row two below: CR LF LF
        Assert.Equal("\u001b[20;21Hi", Move(cur, renderer, output, 20, 19, 'i'));  // anything else: CUP
    }

    private static string Move(CellBuffer buffer, Renderer renderer, VtBuffer output, int x, int y, char glyph)
    {
        var previous = new CellBuffer(buffer.Width, buffer.Height);
        buffer.CopyTo(previous);
        buffer.SetRune(x, y, new Rune(glyph));
        output.Clear();
        renderer.Render(buffer, previous, output);
        string text = Encoding.UTF8.GetString(output.Written);
        Assert.StartsWith("\u001b[?2026h", text);
        Assert.EndsWith("\u001b[?2026l", text);
        return text[8..^8];
    }

    [Fact]
    public void Frames_of_a_screen_taller_than_the_stack_limit_allocate_nothing_after_the_first()
    {
        var renderer = new Renderer(ColorMode.TrueColor);
        renderer.AfterClear();
        var output = new VtBuffer(1 << 16);
        var prev = new CellBuffer(80, 300);
        var cur = new CellBuffer(80, 300);
        for (int y = 0; y < 300; y += 7)
        {
            cur.SetString(0, y, "a row");
        }

        renderer.Render(cur, prev, output);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 20; i++)
        {
            output.Clear();
            renderer.Render(i % 2 == 0 ? prev : cur, i % 2 == 0 ? cur : prev, output);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Contains("a row", Encoding.UTF8.GetString(output.Written));
    }

    /// <summary>A 4-wide buffer with one line of text per row.</summary>
    private static CellBuffer Lines(params string[] rows)
    {
        var buffer = new CellBuffer(4, rows.Length);
        for (int y = 0; y < rows.Length; y++)
        {
            buffer.SetString(0, y, rows[y]);
        }

        return buffer;
    }

    private static (CellBuffer Previous, CellBuffer Current) Buffers(int width, int height) =>
        (new CellBuffer(width, height), new CellBuffer(width, height));

    private static string Render(CellBuffer current, CellBuffer previous, ColorMode mode = ColorMode.TrueColor, bool scrollRegions = true)
    {
        var renderer = new Renderer(mode, scrollRegions);
        renderer.AfterClear();
        var output = new VtBuffer(64);
        renderer.Render(current, previous, output);
        return Encoding.UTF8.GetString(output.Written);
    }

    private static int Count(string text, string part)
    {
        int count = 0;
        for (int i = text.IndexOf(part, StringComparison.Ordinal); i >= 0; i = text.IndexOf(part, i + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}
