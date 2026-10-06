namespace Tuinet;

public readonly record struct Size(int Width, int Height);

/// <summary>A screen rectangle. Width and height are never negative.</summary>
public readonly struct Rect : IEquatable<Rect>
{
    public Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = Math.Max(0, width);
        Height = Math.Max(0, height);
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    /// <summary>Exclusive right edge.</summary>
    public int Right => X + Width;

    /// <summary>Exclusive bottom edge.</summary>
    public int Bottom => Y + Height;

    public bool IsEmpty => Width == 0 || Height == 0;

    public Size Size => new(Width, Height);

    public bool Contains(int x, int y) =>
        (uint)(x - X) < (uint)Width && (uint)(y - Y) < (uint)Height;

    public Rect Intersect(Rect other)
    {
        int x0 = Math.Max(X, other.X);
        int y0 = Math.Max(Y, other.Y);
        int x1 = Math.Min(Right, other.Right);
        int y1 = Math.Min(Bottom, other.Bottom);
        return new Rect(x0, y0, x1 - x0, y1 - y0);
    }

    public Rect Inset(int all) => Inset(all, all);

    public Rect Inset(int horizontal, int vertical) =>
        new(X + horizontal, Y + vertical, Width - 2 * horizontal, Height - 2 * vertical);

    public Rect Offset(int dx, int dy) => new(X + dx, Y + dy, Width, Height);

    /// <summary>A <paramref name="width"/> × <paramref name="height"/> rect centered in this one, clamped to fit.</summary>
    public Rect Centered(int width, int height)
    {
        width = Math.Clamp(width, 0, Width);
        height = Math.Clamp(height, 0, Height);
        return new Rect(X + (Width - width) / 2, Y + (Height - height) / 2, width, height);
    }

    /// <summary>A rect of <paramref name="size"/> centered in this one, clamped to fit.</summary>
    public Rect Centered(Size size) => Centered(size.Width, size.Height);

    /// <summary>
    /// Where to open a popup of <paramref name="size"/> next to <paramref name="anchor"/> inside these bounds
    /// (usually the screen): below it if it fits, else above if it fits there, else on the side with more room,
    /// cut to fit. It starts at the anchor's left edge, moved left to stay inside. A 0×0 anchor is a point,
    /// e.g. the mouse pointer for a context menu.
    /// </summary>
    public Rect PlaceNear(Rect anchor, Size size)
    {
        int width = Math.Clamp(size.Width, 0, Width);
        int height = Math.Max(0, size.Height);
        int x = Math.Max(X, Math.Min(anchor.X, Right - width));
        int below = Math.Clamp(Bottom - anchor.Bottom, 0, Height);
        int above = Math.Clamp(anchor.Y - Y, 0, Height);
        if (height <= below || height > above && below >= above)
        {
            return new Rect(x, Bottom - below, width, Math.Min(height, below));
        }

        height = Math.Min(height, above);
        return new Rect(x, Y + above - height, width, height);
    }

    /// <summary>The <paramref name="index"/>-th one-row slice, or empty if out of range.</summary>
    public Rect Row(int index) =>
        (uint)index < (uint)Height ? new Rect(X, Y + index, Width, 1) : new Rect(X, Y, 0, 0);

    public bool Equals(Rect other) =>
        X == other.X && Y == other.Y && Width == other.Width && Height == other.Height;

    public override bool Equals(object? obj) => obj is Rect other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(X, Y, Width, Height);
    public static bool operator ==(Rect left, Rect right) => left.Equals(right);
    public static bool operator !=(Rect left, Rect right) => !left.Equals(right);

    public override string ToString() => $"({X},{Y} {Width}x{Height})";
}
