namespace Tuinet;

public readonly struct Color : IEquatable<Color>
{
    private readonly byte _r;
    private readonly byte _g;
    private readonly byte _b;
    private readonly byte _kind;

    private Color(byte r, byte g, byte b, byte kind)
    {
        _r = r;
        _g = g;
        _b = b;
        _kind = kind;
    }

    public byte R => _r;
    public byte G => _g;
    public byte B => _b;
    public bool IsDefault => _kind == 0;

    public static Color Default => default;

    public static Color FromRgb(byte r, byte g, byte b) => new(r, g, b, 1);

    public bool Equals(Color other) => _r == other._r && _g == other._g && _b == other._b && _kind == other._kind;

    public override bool Equals(object? obj) => obj is Color other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(_r, _g, _b, _kind);

    public static bool operator ==(Color left, Color right) => left.Equals(right);

    public static bool operator !=(Color left, Color right) => !left.Equals(right);
}
