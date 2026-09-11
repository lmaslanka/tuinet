using System.Text;
using Tuinet;

namespace Tuinet.Kernel.Tests;

public class FillTests
{
    [Fact]
    public void Fill_writes_spaces_with_background()
    {
        var buffer = new CellBuffer(8, 4);
        var style = new Style(Color.FromRgb(16, 16, 16), Color.FromRgb(0, 200, 200));
        buffer.Fill(new Rect(2, 1, 3, 2), style);

        Assert.Equal(new Rune(' '), buffer[2, 1].Rune);
        Assert.Equal(style, buffer[2, 1].Style);
        Assert.Equal(style, buffer[4, 2].Style);
        Assert.Equal(Cell.Empty, buffer[1, 1]);
        Assert.Equal(Cell.Empty, buffer[5, 1]);
        Assert.Equal(Cell.Empty, buffer[2, 0]);
    }
}
