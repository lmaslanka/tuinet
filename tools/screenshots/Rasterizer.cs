using System.Numerics;
using SixLabors.Fonts;

namespace Tuinet.Screenshots;

/// <summary>
/// Anti-aliased coverage of closed paths (nonzero winding), by exact signed-area accumulation (the font-rs method):
/// each edge adds the area it sweeps to the pixels it crosses, and a running sum along each row gives the coverage.
/// Scalar float math only and points snapped to 1/64 px, so the result is the same on every machine.
/// </summary>
internal sealed class Rasterizer
{
    private readonly float[] _area;
    private Vector2 _start;
    private Vector2 _pen;

    public Rasterizer(int width, int height)
    {
        Width = width;
        Height = height;
        _area = new float[width * height + 2];   // an edge at x = width spills into the next row's first slot
    }

    public int Width { get; }
    public int Height { get; }

    /// <summary>Where every added point is moved by (glyph placement).</summary>
    public Vector2 Offset { get; set; }

    public void MoveTo(Vector2 point)
    {
        Close();
        _start = _pen = Snap(point + Offset);
    }

    public void LineTo(Vector2 point)
    {
        Vector2 to = Snap(point + Offset);
        Line(_pen, to);
        _pen = to;
    }

    public void QuadraticTo(Vector2 control, Vector2 point)
    {
        Vector2 c = Snap(control + Offset), to = Snap(point + Offset), from = _pen;
        int steps = Steps(Vector2.Distance(from, c) + Vector2.Distance(c, to));
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps, u = 1 - t;
            LineToSnapped(i == steps ? to : u * u * from + 2 * u * t * c + t * t * to);
        }
    }

    public void CubicTo(Vector2 control1, Vector2 control2, Vector2 point)
    {
        Vector2 c1 = Snap(control1 + Offset), c2 = Snap(control2 + Offset), to = Snap(point + Offset), from = _pen;
        int steps = Steps(Vector2.Distance(from, c1) + Vector2.Distance(c1, c2) + Vector2.Distance(c2, to));
        for (int i = 1; i <= steps; i++)
        {
            float t = (float)i / steps, u = 1 - t;
            LineToSnapped(i == steps ? to : u * u * u * from + 3 * u * u * t * c1 + 3 * u * t * t * c2 + t * t * t * to);
        }
    }

    /// <summary>Close the current figure back to its start.</summary>
    public void Close()
    {
        if (_pen != _start)
        {
            Line(_pen, _start);
        }

        _pen = _start;
    }

    /// <summary>A closed polygon through <paramref name="points"/>.</summary>
    public void Polygon(ReadOnlySpan<Vector2> points)
    {
        MoveTo(points[0]);
        for (int i = 1; i < points.Length; i++)
        {
            LineTo(points[i]);
        }

        Close();
    }

    /// <summary>The coverage so far, one byte per pixel, row by row.</summary>
    public byte[] Coverage()
    {
        Close();
        var coverage = new byte[Width * Height];
        float sum = 0;
        for (int i = 0; i < coverage.Length; i++)
        {
            sum += _area[i];
            float a = MathF.Min(MathF.Abs(sum), 1);
            coverage[i] = (byte)(a * 255 + 0.5f);
        }

        return coverage;
    }

    /// <summary>Enough segments to keep a curve within a fraction of a pixel of its true shape.</summary>
    private static int Steps(float length) => Math.Clamp((int)MathF.Ceiling(MathF.Sqrt(length * 3)), 1, 64);

    private void LineToSnapped(Vector2 point)
    {
        Vector2 to = Snap(point);
        Line(_pen, to);
        _pen = to;
    }

    private static Vector2 Snap(Vector2 p) => new(MathF.Round(p.X * 64) / 64, MathF.Round(p.Y * 64) / 64);

    /// <summary>
    /// Add an edge, clipped to the canvas columns: the parts left or right of the canvas become vertical edges on its
    /// border, which keeps the winding (and so the coverage) of everything inside.
    /// </summary>
    private void Line(Vector2 p0, Vector2 p1)
    {
        foreach (float border in (ReadOnlySpan<float>)[0, Width])
        {
            if ((p0.X < border && p1.X > border) || (p0.X > border && p1.X < border))
            {
                var cut = new Vector2(border, p0.Y + (p1.Y - p0.Y) * (border - p0.X) / (p1.X - p0.X));
                Line(p0, cut);
                Line(cut, p1);
                return;
            }
        }

        p0.X = Math.Clamp(p0.X, 0, Width);
        p1.X = Math.Clamp(p1.X, 0, Width);
        Accumulate(p0, p1);
    }

    private void Accumulate(Vector2 p0, Vector2 p1)
    {
        if (p0.Y == p1.Y)
        {
            return;
        }

        float dir = 1;
        if (p0.Y > p1.Y)
        {
            (p0, p1) = (p1, p0);
            dir = -1;
        }

        float dxdy = (p1.X - p0.X) / (p1.Y - p0.Y);
        float x = p0.X;
        if (p0.Y < 0)
        {
            x -= p0.Y * dxdy;
        }

        int yEnd = Math.Min(Height, (int)MathF.Ceiling(p1.Y));
        for (int y = Math.Max(0, (int)p0.Y); y < yEnd; y++)
        {
            int row = y * Width;
            float dy = MathF.Min(y + 1, p1.Y) - MathF.Max(y, p0.Y);
            float xNext = x + dxdy * dy;
            float d = dy * dir;
            float x0 = MathF.Min(x, xNext), x1 = MathF.Max(x, xNext);
            float x0Floor = MathF.Floor(x0), x1Ceil = MathF.Ceiling(x1);
            int x0i = (int)x0Floor, x1i = (int)x1Ceil;
            if (x1i <= x0i + 1)
            {
                // Within one pixel: split the area at the edge's mean x.
                float xm = 0.5f * (x + xNext) - x0Floor;
                _area[row + x0i] += d - d * xm;
                _area[row + x0i + 1] += d * xm;
            }
            else
            {
                float s = 1 / (x1 - x0);
                float x0f = x0 - x0Floor;
                float a0 = 0.5f * s * (1 - x0f) * (1 - x0f);
                float x1f = x1 - x1Ceil + 1;
                float am = 0.5f * s * x1f * x1f;
                _area[row + x0i] += d * a0;
                if (x1i == x0i + 2)
                {
                    _area[row + x0i + 1] += d * (1 - a0 - am);
                }
                else
                {
                    float a1 = s * (1.5f - x0f);
                    _area[row + x0i + 1] += d * (a1 - a0);
                    for (int xi = x0i + 2; xi < x1i - 1; xi++)
                    {
                        _area[row + xi] += d * s;
                    }

                    float a2 = a1 + (x1i - x0i - 3) * s;
                    _area[row + x1i - 1] += d * (1 - a2 - am);
                }

                _area[row + x1i] += d * am;
            }

            x = xNext;
        }
    }
}

/// <summary>Feeds the outlines SixLabors.Fonts lays out into a <see cref="Rasterizer"/>.</summary>
internal sealed class GlyphOutlines(Rasterizer target) : IGlyphRenderer
{
    public void BeginText(in FontRectangle bounds)
    {
    }

    public bool BeginGlyph(in FontRectangle bounds, in GlyphRendererParameters parameters) => true;

    public void BeginFigure()
    {
    }

    public void MoveTo(Vector2 point) => target.MoveTo(point);

    public void LineTo(Vector2 point) => target.LineTo(point);

    public void QuadraticBezierTo(Vector2 secondControlPoint, Vector2 point) => target.QuadraticTo(secondControlPoint, point);

    public void CubicBezierTo(Vector2 secondControlPoint, Vector2 thirdControlPoint, Vector2 point) =>
        target.CubicTo(secondControlPoint, thirdControlPoint, point);

    public void EndFigure() => target.Close();

    public void EndGlyph()
    {
    }

    public void EndText()
    {
    }

    public TextDecorations EnabledDecorations() => TextDecorations.None;

    public void SetDecoration(TextDecorations textDecorations, Vector2 start, Vector2 end, float thickness)
    {
    }
}
