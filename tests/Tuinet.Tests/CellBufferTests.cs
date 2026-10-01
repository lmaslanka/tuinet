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
    public void Controls_and_combining_marks_are_dropped()
    {
        var buffer = new CellBuffer(8, 1);
        Assert.Equal(3, buffer.SetString(0, 0, "a\u0007\u001bb́\u0085c"));
        Assert.Equal("abc     ", buffer.RowText(0));
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
