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

    [Fact]
    public void Centered_takes_a_size()
    {
        Assert.Equal(new Rect(0, 0, 80, 24).Centered(30, 18), new Rect(0, 0, 80, 24).Centered(new Size(30, 18)));
    }

    private static readonly Rect Screen = new(0, 0, 20, 10);

    [Fact]
    public void Place_near_opens_below_when_it_fits()
    {
        Assert.Equal(new Rect(3, 3, 8, 4), Screen.PlaceNear(new Rect(3, 2, 8, 1), new Size(8, 4)));
        Assert.Equal(new Rect(3, 3, 8, 7), Screen.PlaceNear(new Rect(3, 2, 8, 1), new Size(8, 7)));   // exactly fits
    }

    [Fact]
    public void Place_near_flips_above_when_only_above_fits()
    {
        Assert.Equal(new Rect(3, 4, 8, 4), Screen.PlaceNear(new Rect(3, 8, 8, 1), new Size(8, 4)));
    }

    [Fact]
    public void Place_near_cuts_to_the_bigger_side_when_neither_fits()
    {
        Assert.Equal(new Rect(0, 4, 5, 6), Screen.PlaceNear(new Rect(0, 3, 5, 1), new Size(5, 9)));   // 6 below, 3 above
        Assert.Equal(new Rect(0, 0, 5, 6), Screen.PlaceNear(new Rect(0, 6, 5, 1), new Size(5, 9)));   // 3 below, 6 above
        Assert.Equal(new Rect(0, 5, 5, 5), Screen.PlaceNear(new Rect(0, 5, 5, 0), new Size(5, 9)));   // a tie goes below
    }

    [Fact]
    public void Place_near_moves_left_to_stay_inside()
    {
        Assert.Equal(new Rect(12, 3, 8, 2), Screen.PlaceNear(new Rect(16, 2, 4, 1), new Size(8, 2)));
        Assert.Equal(new Rect(0, 3, 20, 2), Screen.PlaceNear(new Rect(16, 2, 4, 1), new Size(30, 2)));   // wider than the bounds
    }

    [Fact]
    public void Place_near_a_point_opens_at_the_point()
    {
        Assert.Equal(new Rect(5, 4, 6, 3), Screen.PlaceNear(new Rect(5, 4, 0, 0), new Size(6, 3)));
        Assert.Equal(new Rect(5, 6, 6, 3), Screen.PlaceNear(new Rect(5, 9, 0, 0), new Size(6, 3)));   // flips: ends above the point
    }

    [Fact]
    public void Place_near_stays_in_offset_bounds_with_an_anchor_outside()
    {
        var bounds = new Rect(10, 10, 20, 10);
        Assert.Equal(new Rect(10, 10, 6, 3), bounds.PlaceNear(new Rect(0, 0, 4, 1), new Size(6, 3)));
        Assert.Equal(new Rect(24, 17, 6, 3), bounds.PlaceNear(new Rect(40, 40, 4, 1), new Size(6, 3)));
    }
}
