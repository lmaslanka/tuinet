using Tuinet.Widgets;

namespace Tuinet.Tests;

public class ScrollbarTests
{
    private static readonly Style Thumb = new(Color.Green, Color.Black);
    private static readonly Style Track = new(Color.BrightBlack, Color.Blue);

    private static readonly string[] Ten = ["r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8", "r9"];

    private static MouseEvent Mouse(MouseKind kind, int x, int y) => new(kind, MouseButton.Left, x, y, Modifiers.None);

    private static string Column(CellBuffer buffer, int x)
    {
        var text = new System.Text.StringBuilder();
        for (int y = 0; y < buffer.Height; y++)
        {
            text.Append(buffer[x, y].Text);
        }

        return text.ToString();
    }

    private static CellBuffer Vertical(int length, int content, int viewport, int position, bool smooth = true)
    {
        var buffer = new CellBuffer(1, length);
        buffer.Render(new Scrollbar(content, viewport, position) { Smooth = smooth, ThumbStyle = Thumb, TrackStyle = Track }, buffer.Area);
        return buffer;
    }

    [Theory]
    [InlineData(0, "█│││││││││")]
    [InlineData(45, "│││││█││││")]
    [InlineData(90, "│││││││││█")]
    [InlineData(500, "│││││││││█")]   // past the end clamps
    [InlineData(-5, "█│││││││││")]
    public void Whole_cell_thumb_at_start_middle_and_end(int position, string expected) =>
        Assert.Equal(expected, Column(Vertical(10, 100, 10, position, smooth: false), 0));

    [Theory]
    [InlineData(0, "██││││││││")]
    [InlineData(10, "│██│││││││")]
    [InlineData(80, "││││││││██")]
    public void Thumb_is_sized_by_the_visible_share(int position, string expected) =>
        Assert.Equal(expected, Column(Vertical(10, 100, 20, position), 0));

    [Fact]
    public void Thumb_ends_use_eighth_blocks()
    {
        // 80 eighths of track, a 16-eighth thumb starting at eighth 2 (64 * 3/80, rounded).
        CellBuffer buffer = Vertical(10, 100, 20, 3);
        Assert.Equal("▆█▆│││││││", Column(buffer, 0));

        var edge = new Style(Thumb.Fg, Track.Bg);
        Assert.Equal(edge, buffer[0, 0].Style);                     // bottom 6/8 of the cell is thumb
        Assert.Equal(edge.With(Attr.Reverse), buffer[0, 2].Style);  // top 2/8: the lower 6/8 drawn in reverse
        Assert.Equal(Thumb, buffer[0, 1].Style);
        Assert.Equal(Track, buffer[0, 3].Style);
    }

    [Fact]
    public void Horizontal_thumb_ends_use_left_blocks()
    {
        var buffer = new CellBuffer(10, 1);
        buffer.Render(new Scrollbar(100, 20, 3) { Orientation = Direction.Horizontal, ThumbStyle = Thumb, TrackStyle = Track }, buffer.Area);
        Assert.Equal("▎█▎───────", buffer.RowText(0));
        var edge = new Style(Thumb.Fg, Track.Bg);
        Assert.Equal(edge.With(Attr.Reverse), buffer[0, 0].Style);  // right 6/8 is thumb: the left 2/8 in reverse
        Assert.Equal(edge, buffer[2, 0].Style);
    }

    [Fact]
    public void Tiny_viewport_keeps_a_one_cell_thumb()
    {
        Assert.Equal("█│││", Column(Vertical(4, 1000, 1, 0), 0));
        Assert.Equal("│▄▄│", Column(Vertical(4, 1000, 1, 500), 0));
        Assert.Equal("│││█", Column(Vertical(4, 1000, 1, 999), 0));
        Assert.Equal("█", Column(Vertical(1, 1000, 1, 500), 0));
    }

    [Theory]
    [InlineData(5, 5)]
    [InlineData(3, 10)]
    [InlineData(0, 0)]
    public void Content_that_fits_fills_the_track(int content, int viewport) =>
        Assert.Equal("████", Column(Vertical(4, content, viewport, 0), 0));

    [Fact]
    public void Custom_glyphs_turn_off_eighths()
    {
        var buffer = new CellBuffer(1, 10);
        buffer.Render(new Scrollbar(100, 20, 3) { ThumbChar = '┃', TrackChar = ' ' }, buffer.Area);
        Assert.Equal("┃┃        ", Column(buffer, 0));
    }

    [Theory]
    [InlineData(2, 0)]
    [InlineData(6, 40)]    // thumb middle under cell 4 of 10
    [InlineData(11, 90)]
    [InlineData(50, 90)]   // outside the track clamps
    [InlineData(-3, 0)]
    public void Position_at_centers_the_thumb_on_the_pointer(int y, int expected) =>
        Assert.Equal(expected, Scrollbar.PositionAt(new Rect(5, 2, 1, 10), 5, y, 100, 10));

    [Fact]
    public void Position_at_horizontal_and_when_everything_fits()
    {
        Assert.Equal(90, Scrollbar.PositionAt(new Rect(0, 0, 10, 1), 9, 0, 100, 10, Direction.Horizontal));
        Assert.Equal(0, Scrollbar.PositionAt(new Rect(0, 0, 1, 10), 0, 9, 5, 10));
    }

    [Fact]
    public void List_auto_scrollbar_takes_the_rightmost_column()
    {
        var state = new ListState();
        var buffer = new CellBuffer(8, 3);
        buffer.Render(new ListView<TextItems>(new TextItems(Ten)) { Scrollbar = ScrollbarMode.Auto }, buffer.Area, ref state);
        Assert.Equal("r0     █", buffer.RowText(0));
        Assert.Equal("r1     │", buffer.RowText(1));
        Assert.Equal("r2     │", buffer.RowText(2));
    }

    [Fact]
    public void List_items_are_one_column_narrower_with_a_scrollbar()
    {
        string[] items = ["abcdefghij", "b", "c", "d"];
        var state = new ListState();
        var buffer = new CellBuffer(5, 2);
        buffer.Render(new ListView<TextItems>(new TextItems(items)) { Scrollbar = ScrollbarMode.Auto }, buffer.Area, ref state);
        Assert.Equal("abc…█", buffer.RowText(0));
    }

    [Fact]
    public void Auto_hides_the_scrollbar_when_everything_fits_and_always_shows_it()
    {
        string[] two = ["r0", "r1"];
        var state = new ListState();
        var buffer = new CellBuffer(6, 3);
        buffer.Render(new ListView<TextItems>(new TextItems(two)) { Scrollbar = ScrollbarMode.Auto }, buffer.Area, ref state);
        Assert.Equal("r0    ", buffer.RowText(0));

        buffer.Clear();
        buffer.Render(new ListView<TextItems>(new TextItems(two)) { Scrollbar = ScrollbarMode.Always }, buffer.Area, ref state);
        Assert.Equal("███", Column(buffer, 5));
    }

    [Fact]
    public void Clicking_and_dragging_the_list_scrollbar_scrolls_without_selecting()
    {
        var state = new ListState(selected: 1);
        var buffer = new CellBuffer(8, 3);
        var list = new ListView<TextItems>(new TextItems(Ten)) { Scrollbar = ScrollbarMode.Auto };
        buffer.Render(list, buffer.Area, ref state);

        Assert.True(state.HandleMouse(Mouse(MouseKind.Down, 7, 2), Ten.Length));
        Assert.Equal(7, state.Offset);
        Assert.Equal(1, state.Selected);

        buffer.Render(list, buffer.Area, ref state);
        Assert.Equal(7, state.Offset);                                          // render doesn't scroll back
        Assert.Equal("r7     │", buffer.RowText(0));

        Assert.True(state.HandleMouse(Mouse(MouseKind.Drag, 20, 0), Ten.Length));   // off the bar, still dragging
        Assert.Equal(0, state.Offset);
        Assert.True(state.HandleMouse(Mouse(MouseKind.Up, 20, 0), Ten.Length));
        Assert.False(state.HandleMouse(Mouse(MouseKind.Drag, 20, 2), Ten.Length));
        Assert.Equal(0, state.Offset);
    }

    [Fact]
    public void Table_scrollbar_runs_beside_the_body_and_header_hit_testing_follows()
    {
        TableColumn[] columns = [new("a", Constraint.Fill()), new("b", Constraint.Length(2))];
        string[][] rows = [.. Enumerable.Range(0, 10).Select(i => new[] { $"x{i}", $"y{i}" })];
        var state = new ListState();
        var buffer = new CellBuffer(8, 5);
        var table = new Table<TextRows>(new TextRows(rows), columns) { HeaderSeparator = true, Scrollbar = ScrollbarMode.Auto };
        buffer.Render(table, buffer.Area, ref state);

        Assert.Equal("a    b  ", buffer.RowText(0));
        Assert.Equal("────────", buffer.RowText(1));
        Assert.Equal("x0   y0█", buffer.RowText(2));
        Assert.Equal("x1   y1│", buffer.RowText(3));
        Assert.Equal(1, table.HeaderColumnAt(5, 0, state));
        Assert.Equal(-1, table.HeaderColumnAt(7, 0, state));

        Assert.True(state.HandleMouse(Mouse(MouseKind.Down, 7, 4), rows.Length));
        Assert.Equal(7, state.Offset);
    }

    [Fact]
    public void Paragraph_auto_scrollbar_shows_scroll_position()
    {
        var buffer = new CellBuffer(4, 2);
        buffer.Render(new Paragraph("1\n2\n3\n4\n5") { Scrollbar = ScrollbarMode.Auto }, buffer.Area);
        Assert.Equal("1  █", buffer.RowText(0));
        Assert.Equal("2  │", buffer.RowText(1));

        buffer.Clear();
        buffer.Render(new Paragraph("1\n2\n3\n4\n5") { Scrollbar = ScrollbarMode.Auto, Scroll = 3 }, buffer.Area);
        Assert.Equal("4  │", buffer.RowText(0));
        Assert.Equal("5  █", buffer.RowText(1));

        buffer.Clear();
        buffer.Render(new Paragraph("1\n2") { Scrollbar = ScrollbarMode.Auto }, buffer.Area);
        Assert.Equal("1   ", buffer.RowText(0));
    }

    [Fact]
    public void Paragraph_rewraps_one_column_narrower_beside_the_scrollbar()
    {
        // 3 lines at width 5, 4 at width 4 ("abcd", "e", "fg", "h"): the thumb is sized for 4.
        var buffer = new CellBuffer(5, 2);
        buffer.Render(new Paragraph("abcde\nfg\nh") { Wrap = TextWrap.Char, Scrollbar = ScrollbarMode.Auto, Scroll = 1 }, buffer.Area);
        Assert.Equal("e   ▄", buffer.RowText(0));
        Assert.Equal("fg  ▄", buffer.RowText(1));
        Assert.Equal(Attr.Reverse, buffer[4, 1].Style.Attrs);
    }
}
