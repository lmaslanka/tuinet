using System.Numerics;

namespace Tuinet.Screenshots;

/// <summary>
/// Box drawing, block elements and braille drawn as shapes instead of font glyphs, as terminals do: font glyphs for
/// them rarely fill the cell exactly, which leaves gaps between rows of a border or a bar. Lines are centered on the
/// cell's middle pixel, so they meet the next cell's lines whatever the cell size.
/// </summary>
internal sealed class Geometry
{
    private readonly int _width;
    private readonly int _height;
    private readonly int _light;
    private readonly int _heavy;
    private readonly int _cx;
    private readonly int _cy;

    /// <summary>Distance from the middle to each line of a double line.</summary>
    private readonly int _gap;

    public Geometry(int width, int height, int light)
    {
        _width = width;
        _height = height;
        _light = light;
        _heavy = light * 2;
        _cx = width / 2;
        _cy = height / 2;
        _gap = light;
    }

    /// <summary>Is <paramref name="codePoint"/> drawn here (see <see cref="Draw"/> and <see cref="IsShade"/>)?</summary>
    public static bool Handles(int codePoint) => codePoint is >= 0x2500 and <= 0x259F or >= 0x2800 and <= 0x28FF;

    /// <summary>The shades ░▒▓: a dot pattern laid on screen coordinates, so it tiles across cells of any size.</summary>
    public static bool IsShade(int codePoint) => codePoint is >= 0x2591 and <= 0x2593;

    /// <summary>Does shade <paramref name="codePoint"/> cover the screen pixel (x, y)?</summary>
    public static bool ShadeCovers(int codePoint, int x, int y) => codePoint switch
    {
        0x2591 => y % 3 == 0 && x % 3 == (y / 3) % 2,          // sparse dots, alternate rows offset
        0x2592 => (x + y) % 2 == 0,                             // checkerboard
        _ => !(y % 2 == 0 && (x + y / 2) % 2 == 0),             // all but every fourth pixel
    };

    /// <summary>The coverage mask (one byte per pixel, row by row) of a one-cell shape.</summary>
    public byte[] Draw(int codePoint)
    {
        var mask = new byte[_width * _height];
        if (codePoint >= 0x2800)
        {
            Braille(mask, codePoint - 0x2800);
        }
        else if (codePoint >= 0x2580)
        {
            Block(mask, codePoint);
        }
        else if (codePoint is >= 0x256D and <= 0x2570)
        {
            Arc(mask, codePoint);
        }
        else if (codePoint is >= 0x2571 and <= 0x2573)
        {
            Diagonal(mask, codePoint);
        }
        else
        {
            Lines(mask, Arms[codePoint - 0x2500], Dashes(codePoint));
        }

        return mask;
    }

    /// <summary>
    /// Arm weights of U+2500..U+257F as "LRUD" (left, right, up, down): 0 none, 1 light, 2 heavy, 3 double. Arcs and
    /// diagonals (U+256D..U+2573) are drawn separately.
    /// </summary>
    private static readonly string[] Arms =
    [
        "1100", "2200", "0011", "0022", "1100", "2200", "0011", "0022", "1100", "2200", "0011", "0022", "0101", "0201", "0102", "0202",
        "1001", "2001", "1002", "2002", "0110", "0210", "0120", "0220", "1010", "2010", "1020", "2020", "0111", "0211", "0121", "0112",
        "0122", "0221", "0212", "0222", "1011", "2011", "1021", "1012", "1022", "2021", "2012", "2022", "1101", "2101", "1201", "2201",
        "1102", "2102", "1202", "2202", "1110", "2110", "1210", "2210", "1120", "2120", "1220", "2220", "1111", "2111", "1211", "2211",
        "1121", "1112", "1122", "2121", "1221", "2112", "1212", "2221", "2212", "2122", "1222", "2222", "1100", "2200", "0011", "0022",
        "3300", "0033", "0301", "0103", "0303", "3001", "1003", "3003", "0310", "0130", "0330", "3010", "1030", "3030", "0311", "0133",
        "0333", "3011", "1033", "3033", "3301", "1103", "3303", "3310", "1130", "3330", "3311", "1133", "3333", "0101", "1001", "1010",
        "0110", "0000", "0000", "0000", "1000", "0010", "0100", "0001", "2000", "0020", "0200", "0002", "1200", "0012", "2100", "0021",
    ];

    /// <summary>Dash count of the dashed lines (┄┅┆┇ 3, ┈┉┊┋ 4, ╌╍╎╏ 2); 0 for solid lines.</summary>
    private static int Dashes(int codePoint) => codePoint switch
    {
        >= 0x2504 and <= 0x2507 => 3,
        >= 0x2508 and <= 0x250B => 4,
        >= 0x254C and <= 0x254F => 2,
        _ => 0,
    };

    private void Lines(byte[] mask, string arms, int dashes)
    {
        int left = arms[0] - '0', right = arms[1] - '0', up = arms[2] - '0', down = arms[3] - '0';
        if (dashes > 0)
        {
            // Dashed lines are straight: one axis, one weight.
            bool horizontal = left > 0;
            int t = Thickness(horizontal ? left : up);
            int length = horizontal ? _width : _height;
            for (int i = 0; i < dashes; i++)
            {
                int start = i * length / dashes, end = (i + 1) * length / dashes - Math.Max(1, length / dashes / 3);
                if (horizontal)
                {
                    Fill(mask, start, Start(_cy, t), end, Start(_cy, t) + t);
                }
                else
                {
                    Fill(mask, Start(_cx, t), start, Start(_cx, t) + t, end);
                }
            }

            return;
        }

        HorizontalArm(mask, left, -1, up, down);
        HorizontalArm(mask, right, +1, up, down);
        VerticalArm(mask, up, -1, left, right);
        VerticalArm(mask, down, +1, left, right);
    }

    /// <summary>
    /// The arm from the cell's left (<paramref name="dir"/> -1) or right (+1) edge to the middle, joined to the vertical
    /// arms: single and heavy arms overlap the middle by the widest arm; double lines stop at, or run on to, the lines
    /// of a double vertical arm, so corners and junctions close.
    /// </summary>
    private void HorizontalArm(byte[] mask, int weight, int dir, int up, int down)
    {
        if (weight == 0)
        {
            return;
        }

        if (weight == 3)
        {
            Span(mask, horizontal: true, dir, Stop(up, down), Start(_cy - _gap, _light), _light);
            Span(mask, horizontal: true, dir, Stop(down, up), Start(_cy + _gap, _light), _light);
            return;
        }

        int t = Thickness(weight);
        int at = up == 3 || down == 3 ? _cx + dir * _gap : _cx;
        int cover = up == 3 || down == 3 ? _light : Math.Max(t, Math.Max(Thickness(up), Thickness(down)));
        Span(mask, horizontal: true, dir, at, Start(_cy, t), t, cover);

        // A double line on the near side of the perpendicular arm stops at it; one on the far side runs to its far line.
        int Stop(int sameSide, int otherSide) =>
            sameSide == 3 ? _cx + dir * _gap : otherSide == 3 ? _cx - dir * _gap : _cx;
    }

    private void VerticalArm(byte[] mask, int weight, int dir, int left, int right)
    {
        if (weight == 0)
        {
            return;
        }

        if (weight == 3)
        {
            Span(mask, horizontal: false, dir, Stop(left, right), Start(_cx - _gap, _light), _light);
            Span(mask, horizontal: false, dir, Stop(right, left), Start(_cx + _gap, _light), _light);
            return;
        }

        int t = Thickness(weight);
        int at = left == 3 || right == 3 ? _cy + dir * _gap : _cy;
        int cover = left == 3 || right == 3 ? _light : Math.Max(t, Math.Max(Thickness(left), Thickness(right)));
        Span(mask, horizontal: false, dir, at, Start(_cx, t), t, cover);

        int Stop(int sameSide, int otherSide) =>
            sameSide == 3 ? _cy + dir * _gap : otherSide == 3 ? _cy - dir * _gap : _cy;
    }

    /// <summary>
    /// A line of thickness <paramref name="t"/> starting at <paramref name="across"/> on the other axis, from the edge
    /// in direction <paramref name="dir"/> to a stroke of width <paramref name="cover"/> centered on <paramref name="at"/>.
    /// </summary>
    private void Span(byte[] mask, bool horizontal, int dir, int at, int across, int t, int cover = 0)
    {
        cover = Math.Max(cover, _light);
        int near = Start(at, cover), far = near + cover;
        if (horizontal)
        {
            Fill(mask, dir < 0 ? 0 : near, across, dir < 0 ? far : _width, across + t);
        }
        else
        {
            Fill(mask, across, dir < 0 ? 0 : near, across + t, dir < 0 ? far : _height);
        }
    }

    private int Thickness(int weight) => weight switch
    {
        0 => 0,
        2 => _heavy,
        _ => _light,
    };

    /// <summary>First pixel of a stroke <paramref name="t"/> wide centered on pixel <paramref name="center"/>.</summary>
    private static int Start(int center, int t) => center - t / 2;

    /// <summary>╭╮╯╰: a quarter circle joining the middles of two edges, plus straight lines out to them.</summary>
    private void Arc(byte[] mask, int codePoint)
    {
        // Direction of the horizontal (dx) and vertical (dy) arm.
        (int dx, int dy) = codePoint switch
        {
            0x256D => (1, 1),
            0x256E => (-1, 1),
            0x256F => (-1, -1),
            _ => (1, -1),
        };

        // The stroke's center line runs where the straight lines do; the arc is centered off the cell's corner side.
        float t = _light, x = Start(_cx, _light) + t / 2, y = Start(_cy, _light) + t / 2;
        float r = Math.Min(Math.Min(x, _width - x), Math.Min(y, _height - y));
        var c = new Vector2(x + dx * r, y + dy * r);
        float outer = r + t / 2, inner = Math.Max(0, r - t / 2);
        float edgeX = dx > 0 ? _width : 0, edgeY = dy > 0 ? _height : 0;

        var rasterizer = new Rasterizer(_width, _height);
        rasterizer.MoveTo(new Vector2(edgeX, y - dy * t / 2));
        QuarterCircle(rasterizer, c, outer, dx, dy, fromHorizontal: true);
        rasterizer.LineTo(new Vector2(x - dx * t / 2, edgeY));
        rasterizer.LineTo(new Vector2(x + dx * t / 2, edgeY));
        rasterizer.LineTo(new Vector2(x + dx * t / 2, c.Y));
        QuarterCircle(rasterizer, c, inner, dx, dy, fromHorizontal: false);
        rasterizer.LineTo(new Vector2(edgeX, y + dy * t / 2));
        Add(mask, rasterizer.Coverage());
    }

    /// <summary>
    /// A quarter circle of <paramref name="radius"/> around <paramref name="c"/>, as a cubic (no trigonometry, so no
    /// libm differences between machines), between its point toward -dy (the horizontal arm) and toward -dx.
    /// </summary>
    private static void QuarterCircle(Rasterizer rasterizer, Vector2 c, float radius, int dx, int dy, bool fromHorizontal)
    {
        const float Kappa = 0.5522848f;
        var horizontal = new Vector2(c.X, c.Y - dy * radius);
        var vertical = new Vector2(c.X - dx * radius, c.Y);
        var towardVertical = new Vector2(-dx * Kappa * radius, 0);
        var towardHorizontal = new Vector2(0, -dy * Kappa * radius);
        if (fromHorizontal)
        {
            rasterizer.LineTo(horizontal);
            rasterizer.CubicTo(horizontal + towardVertical, vertical + towardHorizontal, vertical);
        }
        else
        {
            rasterizer.LineTo(vertical);
            rasterizer.CubicTo(vertical + towardHorizontal, horizontal + towardVertical, horizontal);
        }
    }

    private void Diagonal(byte[] mask, int codePoint)
    {
        if (codePoint != 0x2572)
        {
            Line(mask, new Vector2(_width, 0), new Vector2(0, _height));    // ╱
        }

        if (codePoint != 0x2571)
        {
            Line(mask, new Vector2(0, 0), new Vector2(_width, _height));    // ╲
        }
    }

    /// <summary>A line of the light thickness from <paramref name="from"/> to <paramref name="to"/>, run past both ends.</summary>
    private void Line(byte[] mask, Vector2 from, Vector2 to)
    {
        Vector2 along = Vector2.Normalize(to - from) * _light;
        var across = new Vector2(-along.Y / 2, along.X / 2);
        from -= along;
        to += along;
        var rasterizer = new Rasterizer(_width, _height);
        rasterizer.Polygon([from + across, to + across, to - across, from - across]);
        Add(mask, rasterizer.Coverage());
    }

    private static void Add(byte[] mask, byte[] coverage)
    {
        for (int i = 0; i < mask.Length; i++)
        {
            mask[i] = Math.Max(mask[i], coverage[i]);
        }
    }

    /// <summary>U+2580..U+259F: halves, eighths and quadrants (the shades are <see cref="ShadeCovers"/>).</summary>
    private void Block(byte[] mask, int codePoint)
    {
        int w = _width, h = _height;
        switch (codePoint)
        {
            case 0x2580:
                Fill(mask, 0, 0, w, h / 2);
                break;
            case >= 0x2581 and <= 0x2588:
                Fill(mask, 0, h - Eighths(h, codePoint - 0x2580), w, h);
                break;
            case >= 0x2589 and <= 0x258F:
                Fill(mask, 0, 0, Eighths(w, 0x2590 - codePoint), h);
                break;
            case 0x2590:
                Fill(mask, w / 2, 0, w, h);
                break;
            case 0x2594:
                Fill(mask, 0, 0, w, Eighths(h, 1));
                break;
            case 0x2595:
                Fill(mask, w - Eighths(w, 1), 0, w, h);
                break;
            case >= 0x2596:
                int quadrants = Quadrants[codePoint - 0x2596];
                for (int bit = 0; bit < 4; bit++)
                {
                    if ((quadrants & (1 << bit)) != 0)
                    {
                        int x = bit % 2 * (w / 2), y = bit / 2 * (h / 2);
                        Fill(mask, x, y, x == 0 ? w / 2 : w, y == 0 ? h / 2 : h);
                    }
                }

                break;
        }
    }

    /// <summary>▖▗▘▙▚▛▜▝▞▟ as bits: 1 upper left, 2 upper right, 4 lower left, 8 lower right.</summary>
    private static readonly int[] Quadrants = [4, 8, 1, 1 | 4 | 8, 1 | 8, 1 | 2 | 4, 1 | 2 | 8, 2, 2 | 4, 2 | 4 | 8];

    private static int Eighths(int size, int n) => (size * n + 4) / 8;

    /// <summary>U+2800..U+28FF: a 2×4 grid of dots, bit i of <paramref name="dots"/> for dot i + 1.</summary>
    private void Braille(byte[] mask, int dots)
    {
        int size = Math.Max(1, (_width + 2) / 5);
        ReadOnlySpan<(int Column, int Row)> layout = [(0, 0), (0, 1), (0, 2), (1, 0), (1, 1), (1, 2), (0, 3), (1, 3)];
        for (int i = 0; i < 8; i++)
        {
            if ((dots & (1 << i)) != 0)
            {
                int x = (_width * (2 * layout[i].Column + 1) + 2) / 4 - size / 2;
                int y = (_height * (2 * layout[i].Row + 1) + 4) / 8 - size / 2;
                Fill(mask, x, y, x + size, y + size);
            }
        }
    }

    private void Fill(byte[] mask, int x0, int y0, int x1, int y1)
    {
        x0 = Math.Max(x0, 0);
        y0 = Math.Max(y0, 0);
        x1 = Math.Min(x1, _width);
        y1 = Math.Min(y1, _height);
        for (int y = y0; y < y1; y++)
        {
            mask.AsSpan(y * _width + x0, Math.Max(0, x1 - x0)).Fill(255);
        }
    }
}
