namespace Tuinet;

public readonly struct Style : IEquatable<Style>
{
    public Color Foreground { get; }
    public Color Background { get; }

    public static Style Default => default;

    public Style(Color foreground, Color background)
    {
        Foreground = foreground;
        Background = background;
    }

    public bool Equals(Style other) => Foreground == other.Foreground && Background == other.Background;

    public override bool Equals(object? obj) => obj is Style other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Foreground, Background);

    public static bool operator ==(Style left, Style right) => left.Equals(right);

    public static bool operator !=(Style left, Style right) => !left.Equals(right);
}
