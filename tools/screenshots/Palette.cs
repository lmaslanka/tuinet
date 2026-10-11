namespace Tuinet.Screenshots;

/// <summary>The 256-color terminal palette: 16 themeable ANSI colors, then the xterm 6×6×6 cube and 24 greys.</summary>
public static class Palette
{
    /// <summary>GitHub Dark's ANSI colors, which suit the default #0D1117 background.</summary>
    public static IReadOnlyList<Rgb> GitHubDark { get; } =
    [
        Rgb.Hex(0x484F58), Rgb.Hex(0xFF7B72), Rgb.Hex(0x3FB950), Rgb.Hex(0xD29922),
        Rgb.Hex(0x58A6FF), Rgb.Hex(0xBC8CFF), Rgb.Hex(0x39C5CF), Rgb.Hex(0xB1BAC4),
        Rgb.Hex(0x6E7681), Rgb.Hex(0xFFA198), Rgb.Hex(0x56D364), Rgb.Hex(0xE3B341),
        Rgb.Hex(0x79C0FF), Rgb.Hex(0xD2A8FF), Rgb.Hex(0x56D4DD), Rgb.Hex(0xFFFFFF),
    ];

    /// <summary>Color <paramref name="index"/> of the 256-color palette, with <paramref name="ansi"/> as its first 16.</summary>
    public static Rgb Indexed(byte index, IReadOnlyList<Rgb> ansi)
    {
        if (index < 16)
        {
            return ansi[index];
        }

        if (index < 232)
        {
            int i = index - 16;
            return new Rgb(CubeLevel(i / 36), CubeLevel(i / 6 % 6), CubeLevel(i % 6));
        }

        byte grey = (byte)(8 + (index - 232) * 10);
        return new Rgb(grey, grey, grey);
    }

    private static byte CubeLevel(int step) => step == 0 ? (byte)0 : (byte)(55 + step * 40);
}

/// <summary>An opaque 24-bit color.</summary>
public readonly record struct Rgb(byte R, byte G, byte B)
{
    /// <summary>A color from <c>0xRRGGBB</c>.</summary>
    public static Rgb Hex(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb);

    /// <summary>This color moved <paramref name="alpha"/>/255 of the way to <paramref name="to"/>, in integers.</summary>
    public Rgb Mix(Rgb to, int alpha) => new(
        (byte)((R * (255 - alpha) + to.R * alpha + 127) / 255),
        (byte)((G * (255 - alpha) + to.G * alpha + 127) / 255),
        (byte)((B * (255 - alpha) + to.B * alpha + 127) / 255));
}
