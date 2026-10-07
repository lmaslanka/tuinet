using System.Text;

namespace Tuinet.Widgets;

/// <summary>Eighth-block glyphs for sub-cell precision, shared by the bars, the scrollbar and the charts.</summary>
internal static class Eighths
{
    private const string LowerBlocks = "▁▂▃▄▅▆▇█";
    private const string LeftBlocks = "▏▎▍▌▋▊▉█";

    public static readonly Rune Full = new('█');

    /// <summary>A cell filled <paramref name="eighths"/>/8 from the bottom (1..8).</summary>
    public static Rune Lower(int eighths) => new(LowerBlocks[eighths - 1]);

    /// <summary>A cell filled <paramref name="eighths"/>/8 from the left (1..8).</summary>
    public static Rune Left(int eighths) => new(LeftBlocks[eighths - 1]);

    /// <summary>
    /// <paramref name="value"/> / <paramref name="max"/> of <paramref name="cells"/> cells, in eighths (clamped). NaN, negative
    /// and zero give 0; any other positive value gives at least 1, so it stays visible next to a zero.
    /// </summary>
    public static int Of(double value, double max, int cells)
    {
        if (!(value > 0) || !(max > 0) || cells <= 0)
        {
            return 0;
        }

        int total = cells * 8;
        return value >= max ? total : Math.Clamp((int)Math.Round(value / max * total), 1, total);
    }
}
