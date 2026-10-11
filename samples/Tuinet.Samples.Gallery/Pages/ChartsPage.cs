using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="Sparkline"/> over a ring buffer that takes a sample every 100 ms (drawn from its two halves, without a
/// copy), and the same five values as a vertical and a horizontal <see cref="BarChart"/>. ←/→ pick a bar, ↑/↓ change
/// it; space pauses the feed.
/// </summary>
public sealed class ChartsPage : Page
{
    private const string Snippet = """
        buffer.Render(new Sparkline(_samples.AsSpan(_head),
            _samples.AsSpan(0, _head))
        {
            Style = Theme.Accent(Theme.Green),
            MaxStyle = Theme.Accent(Theme.Amber),
        }, spark);
        for (int i = 0; i < _bars.Length; i++)
        {
            Color color = i == _selected
                ? Theme.Amber : Theme.Blue;
            _bars[i] = new Bar(_values[i], Days[i],
                Theme.Accent(color));
        }
        // …
        buffer.Render(new BarChart(_bars)
        {
            BarWidth = 5, Gap = 2, Max = 100,
            LabelStyle = Theme.Dim,
        }, columns);
        buffer.Render(new BarChart(_bars)
        {
            Direction = Direction.Horizontal,
            Max = 100, Gap = 0,
        }, rows);
        """;

    private static readonly (string, string)[] Keys = [("←/→", "pick a bar"), ("↑/↓", "change it"), ("space", "pause")];

    public const int SampleMs = 100;
    private const int History = 96;

    private static readonly string[] Days = ["mon", "tue", "wed", "thu", "fri"];

    private readonly double[] _samples = new double[History];   // a ring: _head is the oldest, and the next to write
    private readonly double[] _values = [42, 68, 35, 80, 55];
    private readonly Bar[] _bars = new Bar[5];                   // refilled each frame: no allocation
    private int _head;
    private int _sampleCount;
    private long _lastSampleMs;
    private int _selected;
    private bool _paused;

    public ChartsPage() : base("Charts", "Sparkline · BarChart", Snippet, Keys)
    {
        for (int i = 0; i < History; i++)
        {
            AddSample();
        }
    }

    public double Value(int bar) => _values[bar];
    public int SelectedBar => _selected;
    public int SampleCount => _sampleCount;

    public override bool IsAnimating(long nowMs) => !_paused;

    public override bool Handle(Event ev, long nowMs)
    {
        if (ev.Kind == EventKind.Tick && !_paused && nowMs - _lastSampleMs >= SampleMs)
        {
            _lastSampleMs = nowMs;
            AddSample();
            return true;
        }

        if (ev.Kind != EventKind.Key)
        {
            return false;
        }

        KeyEvent key = ev.Key;
        if (key.Is(KeyCode.Left)) _selected = Math.Max(0, _selected - 1);
        else if (key.Is(KeyCode.Right)) _selected = Math.Min(_values.Length - 1, _selected + 1);
        else if (key.Is(KeyCode.Up)) _values[_selected] = Math.Min(100, _values[_selected] + 5);
        else if (key.Is(KeyCode.Down)) _values[_selected] = Math.Max(0, _values[_selected] - 5);
        else if (key.IsChar(' ')) _paused = !_paused;
        else return false;

        return true;
    }

    /// <summary>The next value of a made-up signal: two waves, so the line moves and the peak wanders.</summary>
    private void AddSample()
    {
        int n = _sampleCount++;
        _samples[_head] = 50 + 28 * Math.Sin(n * 0.21) + 14 * Math.Sin(n * 0.77 + 1);
        _head = (_head + 1) % History;
    }

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        // Headings over the sparkline and the vertical chart; the horizontal one follows a blank row.
        Span<Rect> parts = stackalloc Rect[7];
        Layout.Vertical(area, [Constraint.Length(1), Constraint.Length(4), Constraint.Length(1), Constraint.Length(1),
            Constraint.Length(7), Constraint.Length(1), Constraint.Length(5)], parts);
        Rect spark = parts[1];
        Rect columns = parts[4];
        Rect rows = parts[6];

        Span<char> text = stackalloc char[24];
        double last = _samples[(_head + History - 1) % History];
        int length = new SpanText(text).Append("last ").Append(last, "F0").Text.Length;
        Caption(buffer, parts[0], "LIVE · every 100 ms", Theme.Green, text[..length]);
        Caption(buffer, parts[3], "BY DAY", Theme.Blue, "↑/↓ changes the amber bar");

        buffer.Render(new Sparkline(_samples.AsSpan(_head),
            _samples.AsSpan(0, _head))
        {
            Style = Theme.Accent(Theme.Green),
            MaxStyle = Theme.Accent(Theme.Amber),
        }, spark);

        for (int i = 0; i < _bars.Length; i++)
        {
            Color color = i == _selected ? Theme.Amber : Theme.Blue;
            _bars[i] = new Bar(_values[i], Days[i],
                Theme.Accent(color));
        }

        // A vertical chart given no rows still draws its labels, on the row above: skip it on a short screen.
        if (!columns.IsEmpty)
            buffer.Render(new BarChart(_bars)
            {
                BarWidth = 5, Gap = 2, Max = 100,
                LabelStyle = Theme.Dim,
            }, columns);
        buffer.Render(new BarChart(_bars)
        {
            Direction = Direction.Horizontal,
            Max = 100, Gap = 0,
        }, rows);
    }

    private static void Caption(CellBuffer buffer, Rect row, ReadOnlySpan<char> title, Color color, ReadOnlySpan<char> detail)
    {
        if (row.IsEmpty)
        {
            return;
        }

        int end = buffer.SetString(row.X, row.Y, title, Theme.Heading(color), row.Width);
        int x = row.Right - detail.Length;
        if (!detail.IsEmpty && x > end + 1)
        {
            buffer.SetString(x, row.Y, detail, Theme.Dim);
        }
    }
}
