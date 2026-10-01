namespace Tuinet.Tests;

public class RectTests
{
    [Fact]
    public void Inset_shrinks_each_side()
    {
        Assert.Equal(new Rect(3, 2, 8, 6), new Rect(2, 1, 10, 8).Inset(1));
    }

    [Fact]
    public void Negative_sizes_clamp_to_zero()
    {
        Rect rect = new Rect(0, 0, 1, 1).Inset(2);
        Assert.True(rect.IsEmpty);
        Assert.Equal(0, rect.Width);
    }

    [Fact]
    public void Intersect_overlap_and_disjoint()
    {
        Assert.Equal(new Rect(5, 5, 5, 5), new Rect(0, 0, 10, 10).Intersect(new Rect(5, 5, 10, 10)));
        Assert.True(new Rect(0, 0, 2, 2).Intersect(new Rect(5, 5, 1, 1)).IsEmpty);
    }

    [Fact]
    public void Centered_is_clamped_to_parent()
    {
        Assert.Equal(new Rect(25, 3, 30, 18), new Rect(0, 0, 80, 24).Centered(30, 18));
        Assert.Equal(new Rect(0, 0, 10, 5), new Rect(0, 0, 10, 5).Centered(60, 18));
    }
}
