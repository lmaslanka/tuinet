namespace Tuinet.Screenshots;

/// <summary>An RGB pixel grid with integer blending, so results don't depend on the machine's floating point.</summary>
public sealed class Canvas(int width, int height)
{
    public int Width { get; } = width;
    public int Height { get; } = height;
    public Rgb[] Pixels { get; } = new Rgb[width * height];

    public byte[] EncodePng() => Png.Encode(Pixels, Width, Height);

    public void Fill(int left, int top, int width, int height, Rgb color)
    {
        int x0 = Math.Max(left, 0), x1 = Math.Min(left + width, Width);
        for (int y = Math.Max(top, 0); y < Math.Min(top + height, Height); y++)
        {
            if (x1 > x0)
            {
                Pixels.AsSpan(y * Width + x0, x1 - x0).Fill(color);
            }
        }
    }

    public void Blend(int x, int y, Rgb color, int alpha)
    {
        if ((uint)x < (uint)Width && (uint)y < (uint)Height)
        {
            ref Rgb pixel = ref Pixels[y * Width + x];
            pixel = pixel.Mix(color, alpha);
        }
    }

    internal void Blend(Mask mask, int left, int top, Rgb color)
    {
        for (int y = 0; y < mask.Height; y++)
        {
            for (int x = 0; x < mask.Width; x++)
            {
                byte alpha = mask.Alpha[y * mask.Width + x];
                if (alpha != 0)
                {
                    Blend(left + mask.X + x, top + mask.Y + y, color, alpha);
                }
            }
        }
    }
}

/// <summary>Coverage (0-255, row by row) of a <paramref name="Width"/>×<paramref name="Height"/> box at (X, Y) from its cell's corner.</summary>
internal sealed record Mask(int X, int Y, int Width, int Height, byte[] Alpha)
{
    /// <summary>The same mask cut to its non-zero pixels, moved by (<paramref name="dx"/>, <paramref name="dy"/>).</summary>
    public Mask Trim(int dx, int dy)
    {
        int minX = Width, minY = Height, maxX = -1, maxY = -1;
        for (int y = 0; y < Height; y++)
        {
            for (int x = 0; x < Width; x++)
            {
                if (Alpha[y * Width + x] != 0)
                {
                    minX = Math.Min(minX, x);
                    maxX = Math.Max(maxX, x);
                    minY = Math.Min(minY, y);
                    maxY = Math.Max(maxY, y);
                }
            }
        }

        if (maxX < 0)
        {
            return new Mask(0, 0, 0, 0, []);
        }

        int width = maxX - minX + 1, height = maxY - minY + 1;
        var alpha = new byte[width * height];
        for (int y = 0; y < height; y++)
        {
            Alpha.AsSpan((minY + y) * Width + minX, width).CopyTo(alpha.AsSpan(y * width));
        }

        return new Mask(X + dx + minX, Y + dy + minY, width, height, alpha);
    }
}
