using Tuinet;

namespace Tuinet.Kernel.Tests;

public class RectTests
{
    [Fact]
    public void Inner_is_inset_by_one_for_a_border()
    {
        var rect = new Rect(2, 1, 10, 8);
        Rect inner = rect.Inner;
        Assert.Equal(3, inner.X);
        Assert.Equal(2, inner.Y);
        Assert.Equal(8, inner.Width);
        Assert.Equal(6, inner.Height);
    }
}
