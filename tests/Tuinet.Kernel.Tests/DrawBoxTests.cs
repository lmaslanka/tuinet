using System.Text;
using Tuinet;

namespace Tuinet.Kernel.Tests;

public class DrawBoxTests
{
    [Fact]
    public void Box_writes_corners_and_edges()
    {
        var buffer = new CellBuffer(5, 4);
        buffer.DrawBox(new Rect(0, 0, 5, 4));

        Assert.Equal(new Rune('┌'), buffer[0, 0].Rune);
        Assert.Equal(new Rune('┐'), buffer[4, 0].Rune);
        Assert.Equal(new Rune('└'), buffer[0, 3].Rune);
        Assert.Equal(new Rune('┘'), buffer[4, 3].Rune);
        Assert.Equal(new Rune('─'), buffer[2, 0].Rune);
        Assert.Equal(new Rune('│'), buffer[0, 1].Rune);
    }

    [Fact]
    public void Box_title_sits_on_the_top_edge()
    {
        var buffer = new CellBuffer(12, 3);
        buffer.DrawBox(new Rect(0, 0, 12, 3), "files");
        Assert.Equal(new Rune('f'), buffer[2, 0].Rune);
        Assert.Equal(new Rune('s'), buffer[6, 0].Rune);
        Assert.Equal(new Rune('┌'), buffer[0, 0].Rune);
    }

    [Fact]
    public void Clipped_put_does_not_write_outside_rect()
    {
        var buffer = new CellBuffer(8, 4);
        var clip = new Rect(2, 1, 3, 2);
        buffer.Put(clip, 0, 0, "ABCD");
        Assert.Equal(new Rune('A'), buffer[2, 1].Rune);
        Assert.Equal(new Rune('C'), buffer[4, 1].Rune);
        Assert.Equal(Cell.Empty, buffer[5, 1]);
        Assert.Equal(Cell.Empty, buffer[2, 0]);
        Assert.Equal(Cell.Empty, buffer[1, 1]);
    }

    [Fact]
    public void Clipped_put_is_relative_to_rect_origin()
    {
        var buffer = new CellBuffer(8, 4);
        buffer.Put(new Rect(3, 2, 4, 1), 1, 0, new Rune('X'));
        Assert.Equal(new Rune('X'), buffer[4, 2].Rune);
    }
}
