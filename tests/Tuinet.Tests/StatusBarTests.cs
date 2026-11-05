using Tuinet.Widgets;

namespace Tuinet.Tests;

public class StatusBarTests
{
    private static readonly Style Bar = new(Color.White, Color.Blue);

    private static string Draw(int width, StyledText left = default, StyledText center = default, StyledText right = default)
    {
        var buffer = new CellBuffer(width, 1);
        buffer.Render(new StatusBar { Left = left, Center = center, Right = right, Style = Bar }, buffer.Area);
        return buffer.RowText(0);
    }

    [Fact]
    public void Places_the_segments_left_centered_and_right()
    {
        Assert.Equal("NORMAL     file.cs        12:3", Draw(30, "NORMAL", "file.cs", "12:3"));
        Assert.Equal("           file.cs            ", Draw(30, center: "file.cs"));
        Assert.Equal("                          12:3", Draw(30, right: "12:3"));
    }

    [Fact]
    public void Fills_the_row_and_layers_the_segments_over_it()
    {
        var buffer = new CellBuffer(20, 2);
        var key = new Style(Color.Yellow, Color.Default, Attr.Bold);
        var right = new StyledText("q quit", [new StyledRun(1, key)]);
        buffer.Render(new StatusBar { Left = "ok", Right = right, Style = Bar }, new Rect(0, 1, 20, 5));
        Assert.Equal(default, buffer[0, 0].Style);                    // one row, whatever the area's height
        Assert.Equal(Bar, buffer[0, 1].Style);
        Assert.Equal(Bar, buffer[10, 1].Style);
        Assert.Equal(new Style(Color.Yellow, Color.Blue, Attr.Bold), buffer[14, 1].Style);   // default colors keep the fill's
        Assert.Equal(Bar, buffer[16, 1].Style);
    }

    [Fact]
    public void The_right_segment_wins_a_collision_and_the_left_is_cut()
    {
        Assert.Equal("abcde… 12:34", Draw(12, "abcdefghij", right: "12:34"));
        Assert.Equal("abcd…", Draw(5, "x", right: "abcdefgh"));          // a right side wider than the row hides the left
        Assert.Equal("a 12:3", Draw(6, "a", right: "12:3"));
        Assert.Equal("… 12:3", Draw(6, "ab", right: "12:3"));            // only the ellipsis is left of the left
    }

    [Fact]
    public void The_center_moves_into_the_gap_and_is_cut_to_it()
    {
        Assert.Equal("longer left mid    r", Draw(20, "longer left", "mid", "r"));     // off center, beside the left
        Assert.Equal("left centered… right", Draw(20, "left", "centered text", "right"));
        Assert.Equal("left right", Draw(10, "left", "center", "right"));               // no gap: no center
    }

    [Fact]
    public void Wide_glyphs_at_the_cut_are_never_split()
    {
        // 日本語テキスト is 14 columns: in 8, three glyphs and the ellipsis fit, and the last column stays blank.
        var buffer = new CellBuffer(8, 1);
        buffer.Render(new StatusBar { Left = "日本語テキスト", Style = Bar }, buffer.Area);
        Assert.Equal("日本語… ", buffer.RowText(0));
        Assert.Equal(Bar, buffer[7, 0].Style);

        Assert.Equal("日…  12:3", Draw(9, "日本語", right: "12:3"));
        Assert.Equal("   世界  ok", Draw(11, center: "世界", right: "ok"));
    }

    [Fact]
    public void Empty_areas_draw_nothing()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.Render(new StatusBar { Left = "a", Style = Bar }, new Rect(0, 0, 0, 1));
        buffer.Render(new StatusBar { Left = "a", Style = Bar }, new Rect(10, 0, 4, 1));   // off the buffer
        Assert.Equal(default, buffer[0, 0].Style);
        Assert.Equal("    ", buffer.RowText(0));
    }
}
