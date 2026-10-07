using System.Text;

namespace Tuinet.Widgets;

/// <summary>
/// A horizontal bar filled to <see cref="Ratio"/> on the first row of its area. With the default
/// '█' fill, the boundary cell uses eighth-blocks (▏▎▍▌▋▊▉) for sub-cell precision.
/// </summary>
public readonly ref struct ProgressBar : IWidget
{
    public ProgressBar(double ratio) => Ratio = ratio;

    /// <summary>Fill fraction, clamped to 0..1.</summary>
    public double Ratio { get; init; }

    public Style FilledStyle { get; init; }
    public Style EmptyStyle { get; init; }
    public char FilledChar { get; init; } = '█';
    public char EmptyChar { get; init; } = '░';

    /// <summary>Use eighth-blocks at the boundary (only applies when <see cref="FilledChar"/> is '█').</summary>
    public bool Smooth { get; init; } = true;

    public void Render(Rect area, CellBuffer buffer)
    {
        if (area.IsEmpty)
        {
            return;
        }

        int width = area.Width;
        double ratio = double.IsNaN(Ratio) ? 0 : Math.Clamp(Ratio, 0, 1);
        int eighths = (int)Math.Round(ratio * width * 8);
        int full = eighths / 8;
        int partial = Smooth && FilledChar == '█' ? eighths % 8 : 0;

        var filled = new Rune(FilledChar);
        var empty = new Rune(EmptyChar);
        int y = area.Y;
        for (int i = 0; i < width; i++)
        {
            int x = area.X + i;
            if (i < full)
            {
                buffer.SetRune(x, y, filled, FilledStyle);
            }
            else if (i == full && partial > 0)
            {
                var edge = new Style(FilledStyle.Fg, EmptyStyle.Bg, FilledStyle.Attrs);
                buffer.SetRune(x, y, Eighths.Left(partial), edge);
            }
            else
            {
                buffer.SetRune(x, y, empty, EmptyStyle);
            }
        }
    }
}

/// <summary>Animation frames for activity indicators. Pick a frame from elapsed time, not frame count.</summary>
public static class Spinner
{
    public static ReadOnlySpan<char> Line => "|/-\\";
    public static ReadOnlySpan<char> Dots => "⠋⠙⠹⠸⠼⠴⠦⠧⠇⠏";
    public static ReadOnlySpan<char> Arc => "◜◠◝◞◡◟";

    /// <summary>The frame for <paramref name="elapsedMs"/> at <paramref name="intervalMs"/> per frame.</summary>
    public static char Frame(ReadOnlySpan<char> frames, long elapsedMs, int intervalMs = 100) =>
        frames[(int)(Math.Max(0, elapsedMs) / Math.Max(1, intervalMs) % frames.Length)];
}
