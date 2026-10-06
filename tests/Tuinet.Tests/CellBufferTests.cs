using System.Runtime.CompilerServices;
using System.Text;

namespace Tuinet.Tests;

public class CellBufferTests
{
    [Fact]
    public void Cell_is_sixteen_bytes()
    {
        Assert.Equal(16, Unsafe.SizeOf<Cell>());
        Assert.Equal(10, Unsafe.SizeOf<Style>());
        Assert.Equal(4, Unsafe.SizeOf<Color>());
    }

    [Fact]
    public void Width_field_offset_matches_layout()
    {
        var cell = new Cell(new Rune('a'), new Style(Color.Red, Color.Blue, Attr.Bold));
        Cell.Patch(ref cell, new Rune(0x4F60), 2);
        Assert.Equal(new Rune(0x4F60), cell.Rune);
        Assert.Equal(2, cell.Width);
        Assert.Equal(new Style(Color.Red, Color.Blue, Attr.Bold), cell.Style);
        Assert.False(cell.IsContinuation);
        ulong bits = Cell.ColorBits(ref cell);
        var other = new Cell(new Rune('b'), new Style(Color.Red, Color.Blue));
        Assert.Equal(bits, Cell.ColorBits(ref other));
        var different = new Cell(new Rune('b'), new Style(Color.Red, Color.Green, Attr.Bold));
        Assert.NotEqual(bits, Cell.ColorBits(ref different));
    }

    [Fact]
    public void Set_rune_stores_glyph_and_style()
    {
        var buffer = new CellBuffer(4, 2);
        var style = new Style(Color.Red, Color.Default, Attr.Bold);
        Assert.Equal(1, buffer.SetRune(1, 0, new Rune('A'), style));
        Assert.Equal(new Rune('A'), buffer[1, 0].Rune);
        Assert.Equal(style, buffer[1, 0].Style);
    }

    [Fact]
    public void Indexer_is_bounds_checked()
    {
        var buffer = new CellBuffer(4, 2);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[4, 0]);
        Assert.Throws<ArgumentOutOfRangeException>(() => buffer[0, -1]);
    }

    [Fact]
    public void Wide_rune_marks_next_cell_as_continuation()
    {
        var buffer = new CellBuffer(4, 1);
        Assert.Equal(2, buffer.SetRune(0, 0, new Rune(0x4F60)));
        Assert.Equal(2, buffer[0, 0].Width);
        Assert.True(buffer[1, 0].IsContinuation);
        Assert.Equal(0, buffer[1, 0].Width);
    }

    [Fact]
    public void Wide_rune_that_does_not_fit_is_not_written()
    {
        var buffer = new CellBuffer(4, 1);
        Assert.Equal(0, buffer.SetRune(3, 0, new Rune(0x4F60)));
        Assert.Equal(Cell.Empty, buffer[3, 0]);
    }

    [Fact]
    public void Narrow_over_wide_blanks_the_continuation()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.SetRune(0, 0, new Rune(0x4F60));
        buffer.SetRune(0, 0, new Rune('A'));
        Assert.Equal(new Rune('A'), buffer[0, 0].Rune);
        Assert.False(buffer[1, 0].IsContinuation);
        Assert.Equal(new Rune(' '), buffer[1, 0].Rune);
    }

    [Fact]
    public void Write_onto_continuation_blanks_the_lead()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.SetRune(0, 0, new Rune(0x4F60));
        buffer.SetRune(1, 0, new Rune('B'));
        Assert.Equal(new Rune(' '), buffer[0, 0].Rune);
        Assert.Equal(1, buffer[0, 0].Width);
        Assert.Equal(new Rune('B'), buffer[1, 0].Rune);
    }

    [Fact]
    public void Fill_splitting_a_wide_glyph_blanks_the_other_half()
    {
        var buffer = new CellBuffer(6, 1);
        buffer.SetString(0, 0, "你好");
        buffer.Fill(new Rect(1, 0, 2, 1), Style.Default);
        Assert.Equal("      ", buffer.RowText(0));
        Assert.False(buffer[3, 0].IsContinuation);
    }

    [Fact]
    public void Set_string_advances_by_display_width_and_returns_end_column()
    {
        var buffer = new CellBuffer(8, 1);
        Assert.Equal(4, buffer.SetString(0, 0, "A你B"));
        Assert.Equal(new Rune('A'), buffer[0, 0].Rune);
        Assert.Equal(new Rune(0x4F60), buffer[1, 0].Rune);
        Assert.Equal(new Rune('B'), buffer[3, 0].Rune);
    }

    [Fact]
    public void Set_string_chains_styled_segments()
    {
        var buffer = new CellBuffer(10, 1);
        int x = buffer.SetString(0, 0, "ab", new Style(Color.Red, default));
        x = buffer.SetString(x, 0, "cd", new Style(Color.Blue, default));
        Assert.Equal(4, x);
        Assert.Equal(Color.Blue, buffer[2, 0].Style.Fg);
    }

    [Fact]
    public void Set_string_clips_to_max_width()
    {
        var buffer = new CellBuffer(10, 1);
        Assert.Equal(3, buffer.SetString(0, 0, "abcdef", maxWidth: 3));
        Assert.Equal("abc       ", buffer.RowText(0));
    }

    [Fact]
    public void Set_string_clips_to_buffer_edge()
    {
        var buffer = new CellBuffer(4, 1);
        Assert.Equal(4, buffer.SetString(2, 0, "abcdef"));
        Assert.Equal("  ab", buffer.RowText(0));
    }

    [Fact]
    public void Set_string_ellipsis_marks_truncation()
    {
        var buffer = new CellBuffer(10, 1);
        buffer.SetString(0, 0, "abcdefgh", maxWidth: 5, overflow: Overflow.Ellipsis);
        Assert.StartsWith("abcd…", buffer.RowText(0));
        buffer.Clear();
        buffer.SetString(0, 0, "abc", maxWidth: 5, overflow: Overflow.Ellipsis);
        Assert.StartsWith("abc  ", buffer.RowText(0));
    }

    [Fact]
    public void Set_string_from_negative_x_skips_hidden_part()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.SetString(-2, 0, "abcd");
        Assert.Equal("cd  ", buffer.RowText(0));
    }

    [Fact]
    public void Controls_are_dropped_and_combining_marks_join_their_letter()
    {
        var buffer = new CellBuffer(8, 1);
        Assert.Equal(3, buffer.SetString(0, 0, "a\u0007\u001bb\u0301\u0085c"));
        Assert.Equal("ab\u0301c     ", buffer.RowText(0));
        Assert.True(buffer[1, 0].IsGrapheme);
        Assert.Equal("b\u0301", buffer[1, 0].Text);
        Assert.Equal(1, buffer[1, 0].Width);
    }

    [Fact]
    public void Cell_constructor_replaces_zero_width_runes_with_space()
    {
        var cell = new Cell(new Rune(0x1B));
        Assert.Equal(new Rune(' '), cell.Rune);
        Assert.Equal(1, cell.Width);
    }

    [Fact]
    public void Clear_resets_cells_and_hides_cursor()
    {
        var buffer = new CellBuffer(2, 1);
        buffer.SetRune(0, 0, new Rune('A'));
        buffer.SetCursor(1, 0);
        buffer.Clear();
        Assert.Equal(Cell.Empty, buffer[0, 0]);
        Assert.False(buffer.CursorVisible);
    }

    [Fact]
    public void Cursor_outside_buffer_is_hidden()
    {
        var buffer = new CellBuffer(2, 1);
        buffer.SetCursor(5, 0);
        Assert.False(buffer.CursorVisible);
    }

    [Fact]
    public void Fill_writes_spaces_with_style_inside_rect_only()
    {
        var buffer = new CellBuffer(8, 4);
        var style = new Style(Color.Rgb(16, 16, 16), Color.Rgb(0, 200, 200));
        buffer.Fill(new Rect(2, 1, 3, 2), style);

        Assert.Equal(style, buffer[2, 1].Style);
        Assert.Equal(style, buffer[4, 2].Style);
        Assert.Equal(Cell.Empty, buffer[1, 1]);
        Assert.Equal(Cell.Empty, buffer[5, 1]);
        Assert.Equal(Cell.Empty, buffer[2, 0]);
    }

    [Fact]
    public void Set_style_layers_over_existing_glyphs()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.SetString(0, 0, "ab", new Style(Color.Red, Color.Default));
        buffer.SetStyle(new Rect(0, 0, 4, 1), new Style(Color.Default, Color.Blue, Attr.Bold));
        Assert.Equal(new Rune('a'), buffer[0, 0].Rune);
        Assert.Equal(new Style(Color.Red, Color.Blue, Attr.Bold), buffer[0, 0].Style);
    }

    [Fact]
    public void Area_methods_ignore_areas_off_the_buffer()
    {
        // Right of the buffer, the clip is zero wide at x = Width but still has rows.
        var buffer = new CellBuffer(4, 3);
        foreach (Rect area in (Rect[])[new(4, 0, 2, 2), new(9, 1, 2, 2), new(0, 3, 2, 2), new(-5, 0, 2, 2)])
        {
            buffer.SetStyle(area, new Style(Color.Red, Color.Blue));
            buffer.SetLink(area, "https://example.com");
            buffer.Fill(area, new Style(Color.Red, Color.Blue));
            buffer.Erase(area, new Style(Color.Red, Color.Blue));
        }

        Assert.Equal(new CellBuffer(4, 3).ToString(), buffer.ToString());
        Assert.Equal(default, buffer[3, 0].Style);
    }

    [Fact]
    public void Resize_changes_dimensions_and_clears()
    {
        var buffer = new CellBuffer(4, 2);
        buffer.SetRune(0, 0, new Rune('A'));
        buffer.Resize(3, 5);
        Assert.Equal(new Size(3, 5), buffer.Size);
        Assert.Equal(Cell.Empty, buffer[0, 0]);
    }

    [Fact]
    public void To_string_renders_rows()
    {
        var buffer = new CellBuffer(3, 2);
        buffer.SetString(0, 0, "ab");
        buffer.SetString(0, 1, "你");
        Assert.Equal("ab \n你 ", buffer.ToString());
    }
}

public class LayeringTests
{
    private static readonly Style Panel = new(Color.White, Color.Blue);

    [Fact]
    public void Text_with_default_background_keeps_the_panel_background()
    {
        var buffer = new CellBuffer(10, 1);
        buffer.Fill(buffer.Area, Panel);
        buffer.SetString(0, 0, "ab你", new Style(Color.Red, default, Attr.Bold));
        Assert.Equal(new Style(Color.Red, Color.Blue, Attr.Bold), buffer[0, 0].Style);
        Assert.Equal(new Style(Color.Red, Color.Blue, Attr.Bold), buffer[2, 0].Style);
        Assert.Equal(new Style(Color.Red, Color.Blue, Attr.Bold), buffer[3, 0].Style);
        Assert.Equal(Panel, buffer[4, 0].Style);
    }

    [Fact]
    public void Default_style_text_inherits_both_colors_but_not_attributes()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.Fill(buffer.Area, Panel.With(Attr.Underline));
        buffer.SetRune(1, 0, new Rune('x'));
        Assert.Equal(Panel, buffer[1, 0].Style);
    }

    [Fact]
    public void Explicit_colors_replace()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.Fill(buffer.Area, Panel);
        var explicitStyle = new Style(Color.Green, Color.Black);
        buffer.SetString(0, 0, "ab", explicitStyle);
        Assert.Equal(explicitStyle, buffer[1, 0].Style);
    }

    [Fact]
    public void Layering_follows_changing_backgrounds_within_one_string()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.Fill(new Rect(0, 0, 2, 1), Panel);
        buffer.SetString(0, 0, "abcd", new Style(Color.Red, default));
        Assert.Equal(Color.Blue, buffer[1, 0].Style.Bg);
        Assert.Equal(Color.Default, buffer[2, 0].Style.Bg);
    }
}

public class EraseTests
{
    [Fact]
    public void Erase_blanks_glyphs_and_keeps_background()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.Fill(buffer.Area, new Style(Color.White, Color.Blue));
        buffer.SetString(0, 0, "a你");
        buffer.Erase(new Rect(0, 0, 2, 1), new Style(Color.Red, default));
        Assert.Equal("    ", buffer.RowText(0));
        Assert.Equal(new Style(Color.Red, Color.Blue), buffer[0, 0].Style);
        Assert.False(buffer[2, 0].IsContinuation);
        Assert.Equal(Color.Blue, buffer[2, 0].Style.Bg);
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Vectorized_set_style_matches_style_patch(int seed)
    {
        var random = new Random(seed);
        for (int round = 0; round < 200; round++)
        {
            CellBuffer buffer = RandomBuffer(random, 12, 4);
            CellBuffer before = Copy(buffer);
            Style style = RandomStyle(random);
            var area = new Rect(random.Next(-2, 12), random.Next(-1, 4), random.Next(0, 14), random.Next(0, 5));

            buffer.SetStyle(area, style);

            Rect clipped = area.Intersect(buffer.Area);
            for (int y = 0; y < 4; y++)
            {
                for (int x = 0; x < 12; x++)
                {
                    Cell old = before[x, y];
                    Cell expected = clipped.Contains(x, y) ? old.WithStyle(old.Style.Patch(style)) : old;
                    Assert.Equal(expected, buffer[x, y]);
                }
            }
        }
    }

    [Theory]
    [InlineData(1)]
    [InlineData(2)]
    [InlineData(3)]
    public void Area_set_rune_matches_one_glyph_at_a_time(int seed)
    {
        var random = new Random(seed);
        Rune[] runes = [new('─'), new('│'), new('x'), new(0x4F60), new(0x0301)];
        for (int round = 0; round < 200; round++)
        {
            CellBuffer bulk = RandomBuffer(random, 12, 4);
            CellBuffer single = Copy(bulk);
            Rune rune = runes[random.Next(runes.Length)];
            Style style = RandomStyle(random);
            var area = new Rect(random.Next(-2, 12), random.Next(-1, 4), random.Next(0, 14), random.Next(0, 5));

            bulk.SetRune(area, rune, style);

            Rect clipped = area.Intersect(single.Area);
            int step = Math.Max(1, TextWidth.Of(rune));
            for (int y = clipped.Y; y < clipped.Bottom; y++)
            {
                for (int x = clipped.X; x + step <= clipped.Right; x += step)
                {
                    single.SetRune(x, y, rune, style);
                }
            }

            Assert.Equal(single.ToString(), bulk.ToString());
            for (int y = 0; y < 4; y++)
            {
                Assert.True(single.Row(y).SequenceEqual(bulk.Row(y)), $"row {y}, round {round}");
            }
        }
    }

    [Fact]
    public void Ellipsis_still_applies_to_wide_text_at_the_edge()
    {
        var buffer = new CellBuffer(5, 2);
        buffer.SetString(0, 0, "日本語", default, 5, Overflow.Ellipsis);    // 6 columns into 5
        buffer.SetString(0, 1, "ab日", default, 4, Overflow.Ellipsis);      // exactly 4: fits
        Assert.Equal("日本…", buffer.RowText(0));
        Assert.Equal("ab日 ", buffer.RowText(1));
    }

    private static CellBuffer RandomBuffer(Random random, int width, int height)
    {
        var buffer = new CellBuffer(width, height);
        string[] texts = ["ab", "日本", "x", "你好吗", "  "];
        for (int i = 0; i < 12; i++)
        {
            buffer.SetString(random.Next(-1, width), random.Next(height), texts[random.Next(texts.Length)], RandomStyle(random));
        }

        return buffer;
    }

    private static CellBuffer Copy(CellBuffer from)
    {
        var copy = new CellBuffer(from.Width, from.Height);
        from.Cells.CopyTo(copy.Cells);
        return copy;
    }

    private static Style RandomStyle(Random random)
    {
        Color[] colors = [Color.Default, Color.Red, Color.Indexed(200), Color.Rgb(1, 2, 3), Color.Hex(0xFFFFFF)];
        return new Style(colors[random.Next(colors.Length)], colors[random.Next(colors.Length)], (Attr)random.Next(0, 256));
    }
}
