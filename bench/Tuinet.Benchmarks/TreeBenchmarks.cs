using BenchmarkDotNet.Attributes;
using Tuinet;
using Tuinet.Widgets;

namespace Tuinet.Benchmarks;

/// <summary>
/// A 1,000,000-node tree (10,000 groups of 99 leaves) with 1,000 groups open: 109,000 rows in a 200×60
/// <see cref="TreeView{TSource}"/>. A frame draws only the rows on screen, so its time doesn't depend on how far down
/// the view is scrolled; the rows are rebuilt only when the tree's shape changes.
/// </summary>
[MemoryDiagnoser]
public class TreeBenchmarks
{
    private readonly CellBuffer _buffer = new(200, 60);
    private readonly TreeState _state = new();

    [Params(0, 50_000, 99_000)]
    public int Offset { get; set; }

    [GlobalSetup]
    public void Setup()
    {
        for (int group = 0; group < Groups.Count; group += 10)
        {
            _state.Expand(group);
        }

        _state.Rebuild(default(Groups));
    }

    [Benchmark(Description = "tree frame")]
    public void Render()
    {
        TreeState state = _state;
        state.List.Selected = Offset;
        state.List.Offset = Offset;
        _buffer.Clear();
        _buffer.Render(new TreeView<Groups>(default) { Scrollbar = ScrollbarMode.Auto, SelectedStyle = new Style(Color.Black, Color.White) }, _buffer.Area, ref state);
    }

    /// <summary>A shape change: walk the open tree again (109,000 rows).</summary>
    [Benchmark(Description = "tree rebuild")]
    public int Rebuild()
    {
        _state.Invalidate();
        _state.Rebuild(default(Groups));
        return _state.Count;
    }

    /// <summary>Ids 0…9,999 are groups; leaf j of group g is 10,000 + 99·g + j. Labels are formatted per visible row.</summary>
    private readonly struct Groups : ITreeSource
    {
        public const int Count = 10_000;
        private const int Leaves = 99;

        public int ChildCount(int node) => node == TreeState.Root ? Count : node < Count ? Leaves : 0;

        public int Child(int node, int index) => node == TreeState.Root ? index : Count + node * Leaves + index;

        public bool HasChildren(int node) => node < Count;

        public int Parent(int node) => node < Count ? TreeState.Root : (node - Count) / Leaves;

        public void RenderLabel(int node, Rect area, CellBuffer buffer, bool selected)
        {
            Span<char> text = stackalloc char[24];
            text.TryWrite($"node {node}", out int length);
            buffer.SetString(area.X, area.Y, text[..length], default, area.Width);
        }
    }
}
