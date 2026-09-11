namespace Tuinet;

public readonly struct Rect
{
    public Rect(int x, int y, int width, int height)
    {
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    public int X { get; }
    public int Y { get; }
    public int Width { get; }
    public int Height { get; }

    public Rect Inner =>
        Width < 2 || Height < 2
            ? new Rect(X, Y, 0, 0)
            : new Rect(X + 1, Y + 1, Width - 2, Height - 2);

    public bool Contains(int x, int y) =>
        (uint)(x - X) < (uint)Width && (uint)(y - Y) < (uint)Height;
}
