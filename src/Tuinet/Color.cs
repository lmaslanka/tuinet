using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tuinet;

public enum ColorKind : byte
{
    Default,
    Indexed,
    Rgb,
}

/// <summary>
/// A terminal color: the terminal default, a palette index (0-255), or 24-bit RGB.
/// Four bytes, compared bitwise. Downsampled at emit time to the terminal's <see cref="ColorMode"/>.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 4)]
public readonly struct Color : IEquatable<Color>
{
    private readonly byte _r;
    private readonly byte _g;
    private readonly byte _b;
    private readonly ColorKind _kind;

    private Color(byte r, byte g, byte b, ColorKind kind)
    {
        _r = r;
        _g = g;
        _b = b;
        _kind = kind;
    }

    public ColorKind Kind => _kind;
    public byte R => _r;
    public byte G => _g;
    public byte B => _b;

    /// <summary>Palette index for <see cref="ColorKind.Indexed"/> colors.</summary>
    public byte Index => _r;

    public bool IsDefault => _kind == ColorKind.Default;

    public static Color Default => default;

    public static Color Rgb(byte r, byte g, byte b) => new(r, g, b, ColorKind.Rgb);

    /// <summary>24-bit color from <c>0xRRGGBB</c>.</summary>
    public static Color Hex(uint rgb) => new((byte)(rgb >> 16), (byte)(rgb >> 8), (byte)rgb, ColorKind.Rgb);

    public static Color Indexed(byte index) => new(index, 0, 0, ColorKind.Indexed);

    public static Color Black => Indexed(0);
    public static Color Red => Indexed(1);
    public static Color Green => Indexed(2);
    public static Color Yellow => Indexed(3);
    public static Color Blue => Indexed(4);
    public static Color Magenta => Indexed(5);
    public static Color Cyan => Indexed(6);
    public static Color White => Indexed(7);
    public static Color BrightBlack => Indexed(8);
    public static Color BrightRed => Indexed(9);
    public static Color BrightGreen => Indexed(10);
    public static Color BrightYellow => Indexed(11);
    public static Color BrightBlue => Indexed(12);
    public static Color BrightMagenta => Indexed(13);
    public static Color BrightCyan => Indexed(14);
    public static Color BrightWhite => Indexed(15);

    internal uint Bits
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Unsafe.As<Color, uint>(ref Unsafe.AsRef(in this));
    }

    public bool Equals(Color other) => Bits == other.Bits;
    public override bool Equals(object? obj) => obj is Color other && Equals(other);
    public override int GetHashCode() => (int)Bits;
    public static bool operator ==(Color left, Color right) => left.Bits == right.Bits;
    public static bool operator !=(Color left, Color right) => left.Bits != right.Bits;

    public override string ToString() => _kind switch
    {
        ColorKind.Rgb => $"#{_r:X2}{_g:X2}{_b:X2}",
        ColorKind.Indexed => $"idx{_r}",
        _ => "default",
    };
}
