using System.Text;
using Tuinet;

namespace Tuinet.Kernel.Tests;

public class CellTests
{
    [Fact]
    public void Ascii_letter_occupies_one_column()
    {
        Assert.Equal(Cell.NarrowWidth, Cell.WidthOf(new Rune('A')));
    }

    [Fact]
    public void Cjk_ideograph_occupies_two_columns()
    {
        Assert.Equal(Cell.WideWidth, Cell.WidthOf(new Rune(0x4F60)));
    }

    [Fact]
    public void Combining_mark_occupies_zero_columns()
    {
        Assert.Equal(Cell.ZeroWidth, Cell.WidthOf(new Rune(0x0301)));
    }
}
