namespace Tuinet;

/// <summary>Downsamples colors to what the terminal supports. Identity for <see cref="ColorMode.TrueColor"/>.</summary>
internal static class ColorMapping
{
    private static ReadOnlySpan<byte> CubeLevels => [0, 95, 135, 175, 215, 255];

    // xterm default palette for the 16 ANSI colors.
    private static ReadOnlySpan<byte> Ansi16 =>
    [
        0, 0, 0, 205, 0, 0, 0, 205, 0, 205, 205, 0, 0, 0, 238, 205, 0, 205, 0, 205, 205, 229, 229, 229,
        127, 127, 127, 255, 0, 0, 0, 255, 0, 255, 255, 0, 92, 92, 255, 255, 0, 255, 0, 255, 255, 255, 255, 255,
    ];

    public static Style Map(Style style, ColorMode mode) => mode switch
    {
        ColorMode.TrueColor => style,
        ColorMode.None => new Style(Color.Default, Color.Default, style.Attrs),
        _ => new Style(Map(style.Fg, mode), Map(style.Bg, mode), style.Attrs),
    };

    public static Color Map(Color color, ColorMode mode)
    {
        if (color.IsDefault || mode == ColorMode.TrueColor)
        {
            return color;
        }

        if (mode == ColorMode.None)
        {
            return Color.Default;
        }

        if (color.Kind == ColorKind.Indexed)
        {
            if (mode == ColorMode.Indexed256 || color.Index < 16)
            {
                return color;
            }

            (byte r, byte g, byte b) = IndexedToRgb(color.Index);
            return Nearest16(r, g, b);
        }

        return mode == ColorMode.Indexed256 ? Nearest256(color.R, color.G, color.B) : Nearest16(color.R, color.G, color.B);
    }

    private static Color Nearest256(byte r, byte g, byte b)
    {
        int ri = CubeIndex(r), gi = CubeIndex(g), bi = CubeIndex(b);
        int cubeDistance = Distance(r, g, b, CubeLevels[ri], CubeLevels[gi], CubeLevels[bi]);

        int avg = (r + g + b) / 3;
        int grayIndex = Math.Clamp((avg - 3) / 10, 0, 23);
        int grayLevel = 8 + grayIndex * 10;
        int grayDistance = Distance(r, g, b, grayLevel, grayLevel, grayLevel);

        return grayDistance < cubeDistance
            ? Color.Indexed((byte)(232 + grayIndex))
            : Color.Indexed((byte)(16 + 36 * ri + 6 * gi + bi));
    }

    private static Color Nearest16(byte r, byte g, byte b)
    {
        int best = 0;
        int bestDistance = int.MaxValue;
        for (int i = 0; i < 16; i++)
        {
            int d = Distance(r, g, b, Ansi16[i * 3], Ansi16[i * 3 + 1], Ansi16[i * 3 + 2]);
            if (d < bestDistance)
            {
                bestDistance = d;
                best = i;
            }
        }

        return Color.Indexed((byte)best);
    }

    private static (byte, byte, byte) IndexedToRgb(byte index)
    {
        if (index >= 232)
        {
            byte level = (byte)(8 + (index - 232) * 10);
            return (level, level, level);
        }

        int i = index - 16;
        return (CubeLevels[i / 36], CubeLevels[i / 6 % 6], CubeLevels[i % 6]);
    }

    private static int CubeIndex(int v) => v < 48 ? 0 : v < 115 ? 1 : (v - 35) / 40;

    private static int Distance(int r1, int g1, int b1, int r2, int g2, int b2)
    {
        // Weighted for perceived brightness; cheap and good enough for palette fallback.
        int dr = r1 - r2, dg = g1 - g2, db = b1 - b2;
        return 2 * dr * dr + 4 * dg * dg + 3 * db * db;
    }
}
