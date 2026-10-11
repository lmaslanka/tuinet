using System.Text;
using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="ProgressBar"/> (smooth, with eighth-blocks at the edge, and as a segmented meter) and
/// <see cref="Spinner"/>s, all driven by elapsed time, not frame count. The page animates (the loop draws ~30 frames
/// a second) until space pauses it; r restarts.
/// </summary>
public sealed class ProgressPage : Page
{
    private const string Snippet = """
        double ratio = elapsed % Cycles[i] / (double)Cycles[i];
        buffer.Render(new ProgressBar(ratio)
        {
            FilledStyle = Theme.Accent(Colors[i]),
            EmptyStyle = Theme.Faded,
        }, bar);
        // …
        buffer.Render(new ProgressBar(load)
        {
            FilledChar = '■',
            EmptyChar = '□',
            FilledStyle = Theme.Accent(Theme.Green),
            EmptyStyle = Theme.Faded,
        }, meter);
        // …
        char frame = Spinner.Frame(Frames(i), elapsed);
        buffer.SetRune(x, y, new Rune(frame),
            Theme.Accent(Theme.Blue));
        """;

    private static readonly (string, string)[] Keys = [("space", "pause"), ("r", "restart")];

    private static readonly string[] Tasks = ["download", "compile", "test"];
    private static readonly int[] Cycles = [7000, 4000, 11000];
    private static readonly Color[] Colors = [Theme.Blue, Theme.Violet, Theme.Green];
    private static readonly string[] SpinnerNames = ["dots", "line", "arc"];

    private long _origin;
    private long _frozen;
    private bool _paused;

    public ProgressPage() : base("Progress", "ProgressBar · Spinner", Snippet, Keys)
    {
    }

    public bool Paused => _paused;

    public override bool IsAnimating(long nowMs) => !_paused;

    private long Elapsed(long nowMs) => _paused ? _frozen : nowMs - _origin;

    public override bool Handle(Event ev, long nowMs)
    {
        if (ev.Kind != EventKind.Key)
        {
            return false;
        }

        if (ev.Key.IsChar(' '))
        {
            if (_paused)
            {
                _origin = nowMs - _frozen;
            }
            else
            {
                _frozen = nowMs - _origin;
            }

            _paused = !_paused;
        }
        else if (ev.Key.IsChar('r'))
        {
            (_origin, _frozen) = (nowMs, 0);
        }
        else
        {
            return false;
        }

        return true;
    }

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        long elapsed = Elapsed(nowMs);
        Span<char> text = stackalloc char[32];
        int y = area.Y;

        // A task per bar, each on its own cycle: a heading with the percentage on the right, the bar under it.
        for (int i = 0; i < Tasks.Length && y + 1 < area.Bottom; i++, y += 3)
        {
            var bar = new Rect(area.X, y + 1, area.Width, 1);
            double ratio = elapsed % Cycles[i] / (double)Cycles[i];
            buffer.Render(new ProgressBar(ratio)
            {
                FilledStyle = Theme.Accent(Colors[i]),
                EmptyStyle = Theme.Faded,
            }, bar);
            int length = new SpanText(text).Append(ratio * 100, "F1", width: 5).Append('%').Text.Length;
            Caption(buffer, area, y, Tasks[i], Colors[i], text[..length]);
        }

        // A segmented meter: whole cells only, rising and falling.
        double load = 0.5 + 0.45 * Math.Sin(elapsed / 900.0);
        if (y + 1 < area.Bottom)
        {
            int length = new SpanText(text).Append((int)Math.Round(load * 10)).Append(" / 10").Text.Length;
            Caption(buffer, area, y, "load", Theme.Green, text[..length]);
            var meter = new Rect(area.X, y + 1, Math.Min(area.Width, 20), 1);
            buffer.Render(new ProgressBar(load)
            {
                FilledChar = '■',
                EmptyChar = '□',
                FilledStyle = Theme.Accent(Theme.Green),
                EmptyStyle = Theme.Faded,
            }, meter);
            y += 3;
        }

        // Three spinners, each with its name.
        int x = area.X;
        for (int i = 0; i < SpinnerNames.Length && y < area.Bottom && x < area.Right; i++)
        {
            char frame = Spinner.Frame(Frames(i), elapsed);
            buffer.SetRune(x, y, new Rune(frame), Theme.Accent(Theme.Blue));
            x = buffer.SetString(x + 2, y, SpinnerNames[i], Theme.Dim, area.Right - x - 2) + 4;
        }

        if (y + 2 < area.Bottom)
        {
            int length = new SpanText(text).Append(elapsed / 1000.0, "F1").Append(" s").Text.Length;
            int end = buffer.SetString(area.X, y + 2, _paused ? "paused · " : "running · ", _paused ? Theme.Accent(Theme.Amber) : Theme.Accent(Theme.Green));
            buffer.SetString(end, y + 2, text[..length], Theme.Dim, area.Right - end);
        }
    }

    private static ReadOnlySpan<char> Frames(int i) => i switch
    {
        0 => Spinner.Dots,
        1 => Spinner.Line,
        _ => Spinner.Arc,
    };

    private static void Caption(CellBuffer buffer, Rect area, int y, ReadOnlySpan<char> title, Color color, ReadOnlySpan<char> detail)
    {
        int end = buffer.SetString(area.X, y, title, Theme.Heading(color), area.Width);
        int x = area.Right - detail.Length;
        if (x > end + 1)
        {
            buffer.SetString(x, y, detail, Theme.Strong);
        }
    }
}
