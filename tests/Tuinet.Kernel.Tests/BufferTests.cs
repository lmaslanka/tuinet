using System.Text;
using Tuinet;

namespace Tuinet.Kernel.Tests;

public class BufferTests
{
    [Fact]
    public void Put_ascii_stores_rune_at_cell()
    {
        var buffer = new CellBuffer(4, 2);
        buffer.Put(1, 0, new Rune('A'));
        Assert.Equal(new Rune('A'), buffer[1, 0].Rune);
    }

    [Fact]
    public void Put_wide_rune_marks_next_cell_as_continuation()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.Put(0, 0, new Rune(0x4F60));
        Assert.False(buffer[0, 0].IsContinuation);
        Assert.True(buffer[1, 0].IsContinuation);
        Assert.Equal(new Rune(0x4F60), buffer[1, 0].Rune);
    }

    [Fact]
    public void Clear_resets_cells_to_empty()
    {
        var buffer = new CellBuffer(2, 1);
        buffer.Put(0, 0, new Rune('A'));
        buffer.Clear();
        Assert.Equal(Cell.Empty, buffer[0, 0]);
    }

    [Fact]
    public void Put_text_advances_by_display_width()
    {
        var buffer = new CellBuffer(8, 1);
        buffer.Put(0, 0, "A\u4F60B");
        Assert.Equal(new Rune('A'), buffer[0, 0].Rune);
        Assert.Equal(new Rune(0x4F60), buffer[1, 0].Rune);
        Assert.Equal(new Rune('B'), buffer[3, 0].Rune);
    }

    [Fact]
    public void Put_narrow_over_wide_clears_continuation()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.Put(0, 0, new Rune(0x4F60));
        buffer.Put(0, 0, new Rune('A'));
        Assert.Equal(new Rune('A'), buffer[0, 0].Rune);
        Assert.False(buffer[1, 0].IsContinuation);
        Assert.Equal(Cell.Empty, buffer[1, 0]);
    }

    [Fact]
    public void Put_onto_continuation_clears_lead()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.Put(0, 0, new Rune(0x4F60));
        buffer.Put(1, 0, new Rune('B'));
        Assert.Equal(Cell.Empty, buffer[0, 0]);
        Assert.Equal(new Rune('B'), buffer[1, 0].Rune);
        Assert.False(buffer[1, 0].IsContinuation);
    }
}
