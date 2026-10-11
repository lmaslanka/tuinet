using Tuinet.Widgets;

namespace Tuinet.Comparisons;

/// <summary>Tuinet: <see cref="ListView{TSource}"/> in a <see cref="Block"/>, diffed and presented by <see cref="Terminal"/>.</summary>
internal sealed class TuinetList : IListScenario
{
    private static readonly Style Text = new(Rgb(Scenario.Text), Color.Default);
    private static readonly Style Border = new(Rgb(Scenario.Border), Color.Default);
    private static readonly Style Selected = new(Rgb(Scenario.SelectedFg), Rgb(Scenario.SelectedBg));
    private static readonly Style Status = new(Rgb(Scenario.StatusFg), Color.Default);

    private readonly CountingTty _tty;
    private readonly Terminal _terminal;
    private ListState _list;
    private long _frame;

    /// <param name="scrollRegions">
    /// <see cref="TerminalOptions.ScrollRegions"/>. On (the default), the rows inside the border scroll with one
    /// scroll-region move and only the rows that changed are drawn; off, every frame is a plain cell diff.
    /// </param>
    /// <param name="capture">Also copy the output here (to replay or inspect it), or null.</param>
    public TuinetList(bool scrollRegions = true, Stream? capture = null)
    {
        _tty = new CountingTty(Scenario.Width, Scenario.Height, capture);
        _terminal = new Terminal(_tty, new TerminalOptions { ColorMode = ColorMode.TrueColor, ScrollRegions = scrollRegions });
        Name = scrollRegions ? "Tuinet" : "Tuinet, no scroll regions";
    }

    public string Name { get; }
    public long Bytes => _tty.Bytes;
    public long Writes => _tty.Writes;

    public void Frame()
    {
        _list.Selected = Scenario.Selected(_frame++);
        CellBuffer frame = _terminal.BeginFrame();
        Span<Rect> rows = stackalloc Rect[2];
        Layout.Vertical(frame.Area, [Constraint.Fill(), Constraint.Length(1)], rows);
        var block = new Block { Title = Scenario.Title, BorderType = BorderType.Rounded, BorderStyle = Border, TitleStyle = Border };
        frame.Render(block, rows[0]);
        frame.Render(new ListView<TextItems>(new TextItems(Scenario.Items, Text)) { SelectedStyle = Selected }, block.Inner(rows[0]), ref _list);
        frame.SetString(0, rows[1].Y, Scenario.Status, Status);
        _terminal.Present();
    }

    public void Dispose() => _terminal.Dispose();

    private static Color Rgb((byte R, byte G, byte B) c) => Color.Rgb(c.R, c.G, c.B);

    /// <summary>Counts bytes and writes, like the benchmarks' NullTty.</summary>
    private sealed class CountingTty(int width, int height, Stream? capture) : ITty
    {
        public long Bytes { get; private set; }
        public long Writes { get; private set; }
        public Size Size { get; } = new(width, height);

        public void Write(ReadOnlySpan<byte> bytes)
        {
            Bytes += bytes.Length;
            Writes++;
            capture?.Write(bytes);
        }

        public int Read(Span<byte> buffer, int timeoutMs) => 0;
        public void Wake() { }
        public void Restore() { }
        public void Dispose() { }
    }
}
