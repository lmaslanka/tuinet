namespace Tuinet.Tests;

public class LayoutTests
{
    private static readonly Rect Area = new(0, 0, 100, 20);

    [Fact]
    public void Header_body_footer()
    {
        Span<Rect> rows = stackalloc Rect[3];
        Layout.Vertical(Area, [Constraint.Length(1), Constraint.Fill(), Constraint.Length(1)], rows);
        Assert.Equal(new Rect(0, 0, 100, 1), rows[0]);
        Assert.Equal(new Rect(0, 1, 100, 18), rows[1]);
        Assert.Equal(new Rect(0, 19, 100, 1), rows[2]);
    }

    [Fact]
    public void Fill_weights_split_proportionally()
    {
        Span<Rect> cols = stackalloc Rect[2];
        Layout.Horizontal(Area, [Constraint.Fill(1), Constraint.Fill(3)], cols);
        Assert.Equal(25, cols[0].Width);
        Assert.Equal(75, cols[1].Width);
        Assert.Equal(25, cols[1].X);
    }

    [Fact]
    public void Percent_and_spacing()
    {
        Span<Rect> cols = stackalloc Rect[2];
        Layout.Horizontal(Area, [Constraint.Percent(30), Constraint.Fill()], cols, spacing: 2);
        Assert.Equal(29, cols[0].Width);
        Assert.Equal(31, cols[1].X);
        Assert.Equal(69, cols[1].Width);
    }

    [Fact]
    public void Overflow_shrinks_from_the_end()
    {
        Span<Rect> rows = stackalloc Rect[3];
        Layout.Vertical(new Rect(0, 0, 10, 5), [Constraint.Length(3), Constraint.Length(3), Constraint.Length(3)], rows);
        Assert.Equal(3, rows[0].Height);
        Assert.Equal(2, rows[1].Height);
        Assert.Equal(0, rows[2].Height);
    }

    [Fact]
    public void Min_and_max_grow_when_there_is_no_fill()
    {
        Span<Rect> cols = stackalloc Rect[2];
        Layout.Horizontal(Area, [Constraint.Max(10), Constraint.Min(5)], cols);
        Assert.Equal(10, cols[0].Width);
        Assert.Equal(90, cols[1].Width);
    }

    [Fact]
    public void Fill_takes_space_before_min_grows()
    {
        Span<Rect> cols = stackalloc Rect[2];
        Layout.Horizontal(Area, [Constraint.Min(5), Constraint.Fill()], cols);
        Assert.Equal(5, cols[0].Width);
        Assert.Equal(95, cols[1].Width);
    }

    [Fact]
    public void Rounding_remainder_is_not_lost()
    {
        Span<Rect> cols = stackalloc Rect[3];
        Layout.Horizontal(Area, [Constraint.Fill(), Constraint.Fill(), Constraint.Fill()], cols);
        Assert.Equal(100, cols[0].Width + cols[1].Width + cols[2].Width);
    }
}
