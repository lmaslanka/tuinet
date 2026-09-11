using System.Buffers;
using System.Text;
using Tuinet;

namespace Tuinet.Kernel.Tests;

public class DiffTests
{
    [Fact]
    public void Changed_cell_emits_cursor_and_rune()
    {
        var previous = new CellBuffer(4, 2);
        var current = new CellBuffer(4, 2);
        current.Put(0, 0, new Rune('A'));

        string output = Write(current, previous);

        Assert.Contains("\u001b[?2026h", output);
        Assert.Contains("\u001b[1;1H", output);
        Assert.Contains("A", output);
        Assert.Contains("\u001b[?2026l", output);
    }

    [Fact]
    public void Unchanged_buffer_emits_nothing()
    {
        var previous = new CellBuffer(4, 2);
        previous.Put(0, 0, new Rune('A'));
        var current = new CellBuffer(4, 2);
        current.Put(0, 0, new Rune('A'));

        Assert.Equal("", Write(current, previous));
    }

    [Fact]
    public void Rgb_style_emits_truecolor_sgr()
    {
        var previous = new CellBuffer(4, 1);
        var current = new CellBuffer(4, 1);
        var style = new Style(Color.FromRgb(255, 0, 0), Color.Default);
        current.Put(0, 0, new Rune('X'), style);

        string output = Write(current, previous);

        Assert.Contains("\u001b[38;2;255;0;0m", output);
        Assert.Contains("X", output);
    }

    private static string Write(CellBuffer current, CellBuffer previous)
    {
        var writer = new ArrayBufferWriter<byte>();
        Diff.Write(current, previous, writer);
        return Encoding.UTF8.GetString(writer.WrittenSpan);
    }
}
