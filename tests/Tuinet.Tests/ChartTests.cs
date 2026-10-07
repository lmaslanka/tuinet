using Tuinet.Widgets;

namespace Tuinet.Tests;

public class ChartTests
{
    private static readonly Style Red = new(Color.Red, Color.Default);
    private static readonly Style Blue = new(Color.Blue, Color.Default);

    private static CellBuffer Sparkline(int width, int height, Sparkline sparkline)
    {
        var buffer = new CellBuffer(width, height);
        buffer.Render(sparkline, buffer.Area);
        return buffer;
    }

    private static CellBuffer Chart(int width, int height, BarChart chart)
    {
        var buffer = new CellBuffer(width, height);
        buffer.Render(chart, buffer.Area);
        return buffer;
    }

    [Fact]
    public void Sparkline_levels_for_known_values()
    {
        Assert.Equal(" █▄▁", Sparkline(4, 1, new Sparkline([0, 8, 4, 1]) { Max = 8 }).RowText(0));
    }

    [Fact]
    public void Sparkline_rows_stack_into_eight_levels_each()
    {
        CellBuffer buffer = Sparkline(4, 2, new Sparkline([16, 12, 4, 1]) { Max = 16 });
        Assert.Equal("█▄  ", buffer.RowText(0));
        Assert.Equal("██▄▁", buffer.RowText(1));
    }

    [Fact]
    public void Sparkline_max_is_the_largest_visible_value_unless_fixed()
    {
        Assert.Equal("▄█", Sparkline(2, 1, new Sparkline([2, 4])).RowText(0));
        Assert.Equal("▂▄", Sparkline(2, 1, new Sparkline([2, 4]) { Max = 8 }).RowText(0));
        Assert.Equal("██", Sparkline(2, 1, new Sparkline([2, 4]) { Max = 2 }).RowText(0));   // clamped
    }

    [Fact]
    public void Sparkline_scale_forgets_a_spike_once_it_scrolls_out()
    {
        Assert.Equal("▄█", Sparkline(2, 1, new Sparkline([100, 1, 2])).RowText(0));
    }

    [Fact]
    public void Sparkline_leaves_gaps_for_nan_and_negative_values()
    {
        Assert.Equal("  █", Sparkline(3, 1, new Sparkline([double.NaN, -3, 8])).RowText(0));
    }

    [Fact]
    public void Sparkline_shows_any_positive_value()
    {
        Assert.Equal(" ▁█", Sparkline(3, 1, new Sparkline([0, 0.01, 100])).RowText(0));
    }

    [Fact]
    public void Sparkline_shows_the_newest_values_on_the_right()
    {
        double[] values = [1, 2, 3, 4, 5, 6, 7, 8, 9, 10];
        Assert.Equal("▆▆▇█", Sparkline(4, 1, new Sparkline(values)).RowText(0));
        Assert.Equal("  █", Sparkline(3, 1, new Sparkline([8])).RowText(0));
    }

    [Fact]
    public void Sparkline_two_spans_draw_like_one_joined_span()
    {
        double[] values = [5, 0, 3, 9, 1, 7, 2, 8, 4, 6];
        foreach (int split in (int[])[0, 3, 7, 10])
        {
            CellBuffer joined = Sparkline(6, 2, new Sparkline(values) { MaxStyle = Red });
            CellBuffer ring = Sparkline(6, 2, new Sparkline(values.AsSpan(0, split), values.AsSpan(split)) { MaxStyle = Red });
            Assert.Equal(joined.ToString(), ring.ToString());
            Assert.Equal(joined[3, 1].Style, ring[3, 1].Style);
        }
    }

    [Fact]
    public void Sparkline_max_style_marks_the_newest_peak()
    {
        CellBuffer buffer = Sparkline(4, 1, new Sparkline([4, 2, 4, 1]) { Style = Blue, MaxStyle = new Style(Color.Default, Color.Red) });
        Assert.Equal(Blue, buffer[0, 0].Style);
        Assert.Equal(new Style(Color.Blue, Color.Red), buffer[2, 0].Style);
        Assert.Equal(Blue, buffer[3, 0].Style);
    }

    [Fact]
    public void Sparkline_draws_nothing_for_zeros_empty_input_or_no_room()
    {
        Assert.Equal("   ", Sparkline(3, 2, new Sparkline([0, 0, 0])).RowText(1));
        Assert.Equal("   ", Sparkline(3, 1, new Sparkline([])).RowText(0));
        var buffer = new CellBuffer(3, 1);
        buffer.Render(new Sparkline([1, 2, 3]), new Rect(1, 0, 0, 1));
        Assert.Equal("   ", buffer.RowText(0));
    }

    [Fact]
    public void Vertical_bars_grow_up_with_eighth_block_tops_and_labels_below()
    {
        Bar[] bars = [new(8, "a"), new(4, "b"), new(1, "c")];
        CellBuffer buffer = Chart(5, 3, new BarChart(bars) { BarWidth = 1, Max = 8, ValueFormat = null });
        Assert.Equal("█    ", buffer.RowText(0));
        Assert.Equal("█ █ ▂", buffer.RowText(1));
        Assert.Equal("a b c", buffer.RowText(2));
    }

    [Fact]
    public void Vertical_bar_has_a_partial_top_across_its_width()
    {
        CellBuffer buffer = Chart(3, 1, new BarChart([new Bar(3)]) { Max = 8, ValueFormat = null });
        Assert.Equal("▃▃▃", buffer.RowText(0));
    }

    [Fact]
    public void Vertical_values_sit_above_the_bar_or_inside_a_full_one()
    {
        Bar[] bars = [new(4, "x", Red), new(2, "y")];
        CellBuffer buffer = Chart(7, 4, new BarChart(bars) { BarStyle = Blue });
        Assert.Equal("█4█  2 ", buffer.RowText(0));
        Assert.Equal("███ ▄▄▄", buffer.RowText(1));
        Assert.Equal("███ ███", buffer.RowText(2));
        Assert.Equal(" x   y ", buffer.RowText(3));
        Assert.Equal(new Style(Color.Red, Color.Default, Attr.Reverse), buffer[1, 0].Style);   // the bar's colors, swapped
        Assert.Equal(Red, buffer[0, 1].Style);
        Assert.Equal(Blue, buffer[4, 1].Style);
    }

    [Fact]
    public void Value_format_null_hides_values_and_f1_shows_a_decimal()
    {
        Bar[] bars = [new(2.5)];
        Assert.Equal("   ", Chart(3, 2, new BarChart(bars) { Max = 5, ValueFormat = null }).RowText(0));
        Assert.Equal(2.5.ToString("F1"), Chart(3, 2, new BarChart(bars) { Max = 5, ValueFormat = "F1" }).RowText(0));
        Assert.Equal($" {2.5:0} ", Chart(3, 2, new BarChart(bars) { Max = 5 }).RowText(0));   // the default "0"
    }

    [Fact]
    public void Value_too_wide_for_the_bar_ends_in_an_ellipsis()
    {
        CellBuffer buffer = Chart(3, 2, new BarChart([new Bar(12345)]) { Max = 100_000 });
        Assert.Equal("12…", buffer.RowText(0));
    }

    [Fact]
    public void Labels_are_cut_to_the_bar_width_at_a_whole_glyph()
    {
        CellBuffer buffer = Chart(7, 2, new BarChart([new Bar(1, "世界"), new Bar(1, "feature")]) { ValueFormat = null });
        Assert.Equal("世  fea", buffer.RowText(1));
    }

    [Fact]
    public void Bars_that_do_not_fit_are_dropped_not_squashed()
    {
        Bar[] bars = [new(1, "a"), new(1, "b"), new(1, "c"), new(1, "d"), new(1, "e")];
        CellBuffer buffer = Chart(10, 2, new BarChart(bars) { ValueFormat = null });
        Assert.Equal("███ ███   ", buffer.RowText(0));
        Assert.Equal(" a   b    ", buffer.RowText(1));
    }

    [Fact]
    public void Values_over_max_are_clamped()
    {
        Assert.Equal("████", Chart(4, 1, new BarChart([new Bar(10)]) { Direction = Direction.Horizontal, Max = 5, ValueFormat = null }).RowText(0));
        Assert.Equal("███", Chart(3, 2, new BarChart([new Bar(10)]) { Max = 5, ValueFormat = null }).RowText(0));
    }

    [Fact]
    public void Horizontal_bars_grow_right_after_the_label_column()
    {
        Bar[] bars = [new(8, "ab"), new(3.5, "c")];
        CellBuffer buffer = Chart(11, 2, new BarChart(bars) { Direction = Direction.Horizontal, Gap = 0, Max = 8, ValueFormat = null });
        Assert.Equal("ab ████████", buffer.RowText(0));
        Assert.Equal("c  ███▌    ", buffer.RowText(1));
    }

    [Fact]
    public void Horizontal_values_follow_the_bar_and_rows_are_gapped()
    {
        Bar[] bars = [new(6, "ab"), new(3, "c"), new(0, "d")];
        CellBuffer buffer = Chart(11, 5, new BarChart(bars) { Direction = Direction.Horizontal, Max = 6 });
        Assert.Equal("ab ██████ 6", buffer.RowText(0));
        Assert.Equal("           ", buffer.RowText(1));
        Assert.Equal("c  ███ 3   ", buffer.RowText(2));
        Assert.Equal("d  0       ", buffer.RowText(4));
    }

    [Fact]
    public void Horizontal_label_column_is_at_most_a_third_of_the_width()
    {
        CellBuffer buffer = Chart(12, 1, new BarChart([new Bar(1, "a very long label")]) { Direction = Direction.Horizontal, ValueFormat = null });
        Assert.Equal("a ve ███████", buffer.RowText(0));
    }

    [Fact]
    public void Horizontal_bars_that_do_not_fit_are_dropped()
    {
        Bar[] bars = [new(1, "a"), new(1, "b"), new(1, "c")];
        CellBuffer buffer = Chart(4, 4, new BarChart(bars) { Direction = Direction.Horizontal, ValueFormat = null });
        Assert.Equal("a ██", buffer.RowText(0));
        Assert.Equal("b ██", buffer.RowText(2));
        Assert.Equal("    ", buffer.RowText(3));
    }
}
