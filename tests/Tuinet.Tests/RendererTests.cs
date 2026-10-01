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
        var (prev, cur) = Buffers(4, 2);
        cur.SetRune(3, 0, new Rune('a'));
        cur.SetRune(3, 1, new Rune('b'));
        Assert.Contains("\u001b[1;4Ha\u001b[2;4Hb", Render(cur, prev));
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

    private static (CellBuffer Previous, CellBuffer Current) Buffers(int width, int height) =>
        (new CellBuffer(width, height), new CellBuffer(width, height));

    private static string Render(CellBuffer current, CellBuffer previous, ColorMode mode = ColorMode.TrueColor)
    {
        var renderer = new Renderer(mode);
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
