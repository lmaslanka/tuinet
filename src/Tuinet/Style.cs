using System.Runtime.InteropServices;

namespace Tuinet;

[Flags]
public enum Attr : ushort
{
    None = 0,
    Bold = 1 << 0,
    Dim = 1 << 1,
    Italic = 1 << 2,
    Underline = 1 << 3,
    Blink = 1 << 4,
    Reverse = 1 << 5,
    Hidden = 1 << 6,
    Strike = 1 << 7,
}

/// <summary>Foreground, background and text attributes. Ten bytes, no padding.</summary>
[StructLayout(LayoutKind.Sequential, Pack = 2)]
public readonly struct Style : IEquatable<Style>
{
    public Style(Color fg, Color bg, Attr attrs = Attr.None)
    {
        Fg = fg;
        Bg = bg;
        Attrs = attrs;
    }

    public Color Fg { get; }
    public Color Bg { get; }
    public Attr Attrs { get; }

    public static Style Default => default;

    public Style WithFg(Color fg) => new(fg, Bg, Attrs);
    public Style WithBg(Color bg) => new(Fg, bg, Attrs);
    public Style With(Attr attrs) => new(Fg, Bg, Attrs | attrs);
    public Style Without(Attr attrs) => new(Fg, Bg, Attrs & ~attrs);

    /// <summary>
    /// Layers <paramref name="top"/> over this style: its non-default colors win, attributes are combined.
    /// </summary>
    public Style Patch(Style top) => new(
        top.Fg.IsDefault ? Fg : top.Fg,
        top.Bg.IsDefault ? Bg : top.Bg,
        Attrs | top.Attrs);

    public bool Equals(Style other) =>
        Fg.Bits == other.Fg.Bits && Bg.Bits == other.Bg.Bits && Attrs == other.Attrs;

    public override bool Equals(object? obj) => obj is Style other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Fg.Bits, Bg.Bits, Attrs);
    public static bool operator ==(Style left, Style right) => left.Equals(right);
    public static bool operator !=(Style left, Style right) => !left.Equals(right);

    public override string ToString() => $"fg={Fg} bg={Bg} attrs={Attrs}";
}
