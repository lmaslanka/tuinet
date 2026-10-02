using BenchmarkDotNet.Attributes;
using Tuinet;
using Tuinet.Widgets;

namespace Tuinet.Benchmarks;

/// <summary>
/// Widgets drawing into a 200×60 buffer, with no diff or terminal: the widget's own cost. Compare with
/// <see cref="FrameBenchmarks"/> to see how much of a frame is diff + encode.
/// </summary>
[MemoryDiagnoser]
public class TableBenchmarks
{
    private static readonly string[] Names = [.. Enumerable.Range(0, 5000).Select(i => $"refs/heads/feature/item-{i:D5}")];
    private static readonly Style Highlight = new(Color.Rgb(16, 16, 16), Color.Rgb(200, 200, 200));

    private static readonly TableColumn[] Mixed =
    [
        new("branch", Constraint.Fill()),
        new("commits", Constraint.Length(8), Alignment.Right),
        new("ahead", Constraint.Length(6), Alignment.Right),
        new("state", Constraint.Length(8), Alignment.Center),
    ];

    private static readonly TableColumn[] AllLeft =
    [
        new("branch", Constraint.Fill()),
        new("commits", Constraint.Length(8)),
        new("ahead", Constraint.Length(6)),
        new("state", Constraint.Length(8)),
    ];

    private readonly CellBuffer _buffer = new(200, 60);
    private ListState _state = new(selected: 1000);

    [Benchmark(Baseline = true, Description = "list")]
    public void List()
    {
        _buffer.Clear();
        _buffer.Render(new ListView<TextItems>(new TextItems(Names)) { SelectedStyle = Highlight }, _buffer.Area, ref _state);
    }

    [Benchmark(Description = "table, plain")]
    public void Plain() => Table(Mixed, separators: false, rule: false, stripes: false);

    [Benchmark(Description = "table, all left")]
    public void Left() => Table(AllLeft, separators: false, rule: false, stripes: false);

    [Benchmark(Description = "table, + separators")]
    public void Separators() => Table(Mixed, separators: true, rule: false, stripes: false);

    [Benchmark(Description = "table, + rule")]
    public void Rule() => Table(Mixed, separators: false, rule: true, stripes: false);

    [Benchmark(Description = "table, + stripes")]
    public void Stripes() => Table(Mixed, separators: false, rule: false, stripes: true);

    [Benchmark(Description = "table, everything")]
    public void Everything() => Table(Mixed, separators: true, rule: true, stripes: true);

    [Benchmark(Description = "block border")]
    public void Border()
    {
        _buffer.Clear();
        _buffer.Render(new Block { Title = "branches", BorderType = BorderType.Rounded }, _buffer.Area);
    }

    private void Table(TableColumn[] columns, bool separators, bool rule, bool stripes)
    {
        _buffer.Clear();
        _buffer.Render(new Table<Branches>(new Branches(Names), columns)
        {
            HeaderStyle = new Style(Color.Default, Color.Default, Attr.Bold),
            HeaderSeparator = rule,
            ColumnSeparator = separators ? '│' : '\0',
            AlternateRowStyle = stripes ? new Style(Color.Default, Color.Rgb(20, 20, 20)) : default,
            SelectedStyle = Highlight,
        }, _buffer.Area, ref _state);
    }

    internal readonly struct Branches(string[] names) : ITableSource
    {
        public int RowCount => names.Length;

        public ReadOnlySpan<char> Cell(int row, int column, Span<char> scratch, out Style style)
        {
            style = default;
            int written;
            switch (column)
            {
                case 0:
                    return names[row];
                case 1:
                    (row * 7 % 1000).TryFormat(scratch, out written);
                    return scratch[..written];
                case 2:
                    (row % 13).TryFormat(scratch, out written);
                    return scratch[..written];
                default:
                    return row % 5 == 0 ? "stale" : "active";
            }
        }
    }
}
