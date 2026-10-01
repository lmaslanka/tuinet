using BenchmarkDotNet.Attributes;
using Tuinet;
using Tuinet.Widgets;

namespace Tuinet.Benchmarks;

/// <summary>A whole app frame through <see cref="Terminal"/>: clear, layout, widgets, diff, write.</summary>
[MemoryDiagnoser]
public class FrameBenchmarks
{
    private static readonly string[] Items = [.. Enumerable.Range(0, 5000).Select(i => $"refs/heads/feature/item-{i:D5}")];
    private static readonly Style Highlight = new(Color.Rgb(16, 16, 16), Color.Rgb(200, 200, 200));
    private Terminal _terminal = null!;
    private ListState _list;
    private int _tick;

    [GlobalSetup]
    public void Setup() => _terminal = new Terminal(new NullTty(200, 60));

    [GlobalCleanup]
    public void Cleanup() => _terminal.Dispose();

    /// <summary>Hold 'j' on a 5000-item list: selection moves one row per frame.</summary>
    [Benchmark(Description = "list scroll frame")]
    public int ListScroll()
    {
        _list.Selected = _tick++ % Items.Length;
        CellBuffer frame = _terminal.BeginFrame();
        Span<Rect> rows = stackalloc Rect[2];
        Layout.Vertical(frame.Area, [Constraint.Fill(), Constraint.Length(1)], rows);
        var block = new Block { Title = "branches", BorderType = BorderType.Rounded };
        frame.Render(block, rows[0]);
        frame.Render(new ListView<TextItems>(new TextItems(Items)) { SelectedStyle = Highlight }, block.Inner(rows[0]), ref _list);
        frame.SetString(0, rows[1].Y, "j/k move  q quit");
        _terminal.Present();
        return _terminal.LastFrameBytes;
    }
}
