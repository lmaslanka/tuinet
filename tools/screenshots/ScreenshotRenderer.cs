using System.Globalization;
using System.Numerics;
using SixLabors.Fonts;
using TColor = Tuinet.Color;

namespace Tuinet.Screenshots;

/// <summary>
/// Draws a <see cref="CellBuffer"/> as a terminal window and encodes it as PNG. Deterministic: bundled fonts, our own
/// rasterizer, integer cell geometry and blending, and no metadata in the file, so the same buffer gives the same bytes.
/// <para>
/// Each cell's background is filled first, then the glyphs on top (so glyphs may overhang a neighbour, as in a
/// terminal), with underline and strikethrough, then the cursor. Box drawing, block elements and braille are drawn as
/// shapes (<see cref="Geometry"/>) so borders and bars join without gaps; everything else comes from the font.
/// </para>
/// </summary>
public sealed class ScreenshotRenderer
{
    private readonly TerminalFonts _fonts;
    private readonly ScreenshotOptions _options;
    private readonly int _scale;
    private readonly int _cellWidth;
    private readonly int _cellHeight;
    private readonly float _fontSize;
    private readonly Geometry _geometry;

    /// <summary>Text origin from the cell top that puts the baseline <see cref="_baseline"/> pixels down.</summary>
    private readonly int _glyphTop;
    private readonly int _baseline;
    private readonly int _underline;
    private readonly int _strike;
    private readonly int _decorationWidth;

    private readonly Dictionary<(string Text, Face Face, int Cells), Mask> _glyphs = [];
    private readonly Dictionary<int, Mask> _shapes = [];
    private readonly SortedSet<string> _missing = new(StringComparer.Ordinal);

    public ScreenshotRenderer(TerminalFonts fonts, ScreenshotOptions? options = null)
    {
        _fonts = fonts;
        _options = options ?? new ScreenshotOptions();
        _scale = Math.Max(1, _options.Scale);
        _cellWidth = _options.CellWidth * _scale;
        _cellHeight = _options.CellHeight * _scale;
        _fontSize = _options.FontSize * _scale;
        _geometry = new Geometry(_cellWidth, _cellHeight, _options.LineWidth * _scale);

        // Center the font's line box in the cell (a half pixel goes below), then measure where the font puts the baseline.
        Font font = _fonts.Get(bold: false, italic: false, _fontSize);
        HorizontalMetrics metrics = font.FontMetrics.HorizontalMetrics;
        float em = _fontSize / font.FontMetrics.UnitsPerEm;
        float ascent = metrics.Ascender * em, lineHeight = (metrics.Ascender - metrics.Descender + metrics.LineGap) * em;
        _baseline = (int)MathF.Ceiling((_cellHeight - lineHeight) / 2 + ascent);
        _glyphTop = _baseline - MeasureBaseline(font);
        _underline = _baseline + Math.Max(1, (int)MathF.Round(_fontSize * 0.12f));
        _strike = _baseline - (int)MathF.Round(_fontSize * 0.28f);
        _decorationWidth = Math.Max(1, (int)MathF.Round(_fontSize / 15));
    }

    /// <summary>Text drawn so far that the font has no glyph for (shown as the font's missing-glyph box).</summary>
    public IReadOnlyCollection<string> Missing => _missing;

    /// <summary>Render <paramref name="buffer"/>, with <paramref name="title"/> in the window's title bar, as PNG bytes.</summary>
    public byte[] RenderPng(CellBuffer buffer, string? title = null) => Render(buffer, title).EncodePng();

    public Canvas Render(CellBuffer buffer, string? title = null)
    {
        ScreenshotOptions o = _options;
        int gridWidth = buffer.Width * _cellWidth, gridHeight = buffer.Height * _cellHeight;
        int margin = o.Window ? o.Margin * _scale : 0;
        int gridX = o.Window ? margin + o.Padding * _scale : 0;
        int gridY = o.Window ? margin + o.TitleBarHeight * _scale : 0;
        int width = o.Window ? gridX * 2 + gridWidth : gridWidth;
        int height = o.Window ? gridY + gridHeight + (o.PaddingBottom + o.Margin) * _scale : gridHeight;

        var canvas = new Canvas(width, height);
        if (o.Window)
        {
            DrawWindow(canvas, margin);
        }

        DrawCells(canvas, buffer, gridX, gridY);
        if (o.Window && !string.IsNullOrEmpty(title))
        {
            int titleX = (width - TextWidth.Of(title) * _cellWidth) / 2;
            int titleY = margin + DotsY * _scale - _cellHeight / 2;
            DrawText(canvas, title, titleX, titleY, o.Title);
        }

        return canvas;
    }

    /// <summary>Title bar layout at scale 1: the dots' and title's middle, the first dot's center, dot spacing and size.</summary>
    private const int DotsY = 18, FirstDotX = 24, DotSpacing = 20, DotDiameter = 12;

    /// <summary>Backdrop, the rounded window with its 1px frame, and the three dots.</summary>
    private void DrawWindow(Canvas canvas, int margin)
    {
        ScreenshotOptions o = _options;
        canvas.Pixels.AsSpan().Fill(o.Backdrop);
        float left = margin, top = margin, right = canvas.Width - margin, bottom = canvas.Height - margin;
        float radius = o.CornerRadius * _scale, border = _scale;
        FillShape(canvas, o.Border, r => RoundedRectangle(r, left, top, right, bottom, radius));
        FillShape(canvas, o.Background, r => RoundedRectangle(r, left + border, top + border, right - border, bottom - border, radius - border));
        for (int i = 0; i < o.Dots.Count; i++)
        {
            float x = margin + (FirstDotX + DotSpacing * i) * _scale, y = margin + DotsY * _scale;
            FillShape(canvas, o.Dots[i], r => Ellipse(r, new Vector2(x, y), DotDiameter * _scale / 2f));
        }
    }

    private static void FillShape(Canvas canvas, Rgb color, Action<Rasterizer> shape)
    {
        var rasterizer = new Rasterizer(canvas.Width, canvas.Height);
        shape(rasterizer);
        byte[] coverage = rasterizer.Coverage();
        for (int i = 0; i < coverage.Length; i++)
        {
            if (coverage[i] != 0)
            {
                canvas.Pixels[i] = canvas.Pixels[i].Mix(color, coverage[i]);
            }
        }
    }

    /// <summary>Kappa: control point distance for a quarter circle drawn as one cubic.</summary>
    private const float Kappa = 0.5522848f;

    private static void RoundedRectangle(Rasterizer r, float left, float top, float right, float bottom, float radius)
    {
        float k = radius * (1 - Kappa);
        r.MoveTo(new Vector2(left + radius, top));
        r.LineTo(new Vector2(right - radius, top));
        r.CubicTo(new Vector2(right - k, top), new Vector2(right, top + k), new Vector2(right, top + radius));
        r.LineTo(new Vector2(right, bottom - radius));
        r.CubicTo(new Vector2(right, bottom - k), new Vector2(right - k, bottom), new Vector2(right - radius, bottom));
        r.LineTo(new Vector2(left + radius, bottom));
        r.CubicTo(new Vector2(left + k, bottom), new Vector2(left, bottom - k), new Vector2(left, bottom - radius));
        r.LineTo(new Vector2(left, top + radius));
        r.CubicTo(new Vector2(left, top + k), new Vector2(left + k, top), new Vector2(left + radius, top));
        r.Close();
    }

    private static void Ellipse(Rasterizer r, Vector2 center, float radius) =>
        RoundedRectangle(r, center.X - radius, center.Y - radius, center.X + radius, center.Y + radius, radius);

    private void DrawCells(Canvas canvas, CellBuffer buffer, int gridX, int gridY)
    {
        var foregrounds = new Rgb[buffer.Width * buffer.Height];
        bool blockCursor = buffer.CursorVisible && buffer.CursorShape is CursorShape.Default or CursorShape.Block or CursorShape.BlinkingBlock;

        // Backgrounds first: a glyph may overhang into the next cell.
        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                Cell cell = buffer[x, y];
                if (cell.IsContinuation)
                {
                    continue;
                }

                bool cursor = blockCursor && x == buffer.CursorX && y == buffer.CursorY;
                (Rgb fg, Rgb bg) = Resolve(cell.Style, cursor);
                foregrounds[y * buffer.Width + x] = fg;
                canvas.Fill(gridX + x * _cellWidth, gridY + y * _cellHeight, Math.Max(1, cell.Width) * _cellWidth, _cellHeight, bg);
            }
        }

        for (int y = 0; y < buffer.Height; y++)
        {
            for (int x = 0; x < buffer.Width; x++)
            {
                Cell cell = buffer[x, y];
                Attr attrs = cell.Style.Attrs;
                if (cell.IsContinuation || (attrs & Attr.Hidden) != 0)
                {
                    continue;
                }

                Rgb fg = foregrounds[y * buffer.Width + x];
                int left = gridX + x * _cellWidth, top = gridY + y * _cellHeight, width = Math.Max(1, cell.Width) * _cellWidth;
                DrawGlyph(canvas, cell, left, top, fg, attrs);
                if ((attrs & Attr.Underline) != 0)
                {
                    canvas.Fill(left, top + _underline, width, _decorationWidth, fg);
                }

                if ((attrs & Attr.Strike) != 0)
                {
                    canvas.Fill(left, top + _strike, width, _decorationWidth, fg);
                }
            }
        }

        if (buffer.CursorVisible && !blockCursor)
        {
            int left = gridX + buffer.CursorX * _cellWidth, top = gridY + buffer.CursorY * _cellHeight;
            int thickness = 2 * _scale;
            if (buffer.CursorShape is CursorShape.Bar or CursorShape.BlinkingBar)
            {
                canvas.Fill(left, top, thickness, _cellHeight, _options.Cursor);
            }
            else
            {
                canvas.Fill(left, top + _cellHeight - thickness, _cellWidth, thickness, _options.Cursor);
            }
        }
    }

    /// <summary>A cell's colors: defaults filled in, reverse applied, the block cursor inverted, dim blended.</summary>
    private (Rgb Fg, Rgb Bg) Resolve(Style style, bool cursor)
    {
        Rgb fg = Resolve(style.Fg, _options.Foreground), bg = Resolve(style.Bg, _options.Background);
        if ((style.Attrs & Attr.Reverse) != 0)
        {
            (fg, bg) = (bg, fg);
        }

        if (cursor)
        {
            (fg, bg) = (bg, _options.Cursor);
        }

        if ((style.Attrs & Attr.Dim) != 0)
        {
            fg = fg.Mix(bg, (int)MathF.Round(_options.Dim * 255));
        }

        return (fg, bg);
    }

    private Rgb Resolve(TColor color, Rgb fallback) => color.Kind switch
    {
        ColorKind.Rgb => new Rgb(color.R, color.G, color.B),
        ColorKind.Indexed => Palette.Indexed(color.Index, _options.Ansi),
        _ => fallback,
    };

    private void DrawGlyph(Canvas canvas, Cell cell, int left, int top, Rgb fg, Attr attrs)
    {
        int codePoint = cell.Rune.Value;
        if (!cell.IsGrapheme && cell.Width == 1 && Geometry.Handles(codePoint))
        {
            if (Geometry.IsShade(codePoint))
            {
                for (int y = top; y < top + _cellHeight; y++)
                {
                    for (int x = left; x < left + _cellWidth; x++)
                    {
                        if (Geometry.ShadeCovers(codePoint, x, y))
                        {
                            canvas.Blend(x, y, fg, 255);
                        }
                    }
                }

                return;
            }

            if (!_shapes.TryGetValue(codePoint, out Mask? shape))
            {
                _shapes[codePoint] = shape = new Mask(0, 0, _cellWidth, _cellHeight, _geometry.Draw(codePoint));
            }

            canvas.Blend(shape, left, top, fg);
            return;
        }

        if (codePoint == ' ' && !cell.IsGrapheme)
        {
            return;
        }

        Face face = ((attrs & Attr.Bold) != 0 ? Face.Bold : Face.Regular) | ((attrs & Attr.Italic) != 0 ? Face.Italic : Face.Regular);
        canvas.Blend(Glyph(cell.Text, face, Math.Max(1, cell.Width)), left, top, fg);
    }

    /// <summary>Draw <paramref name="text"/> left to right with the cell grid's metrics (the title).</summary>
    private void DrawText(Canvas canvas, string text, int left, int top, Rgb color)
    {
        TextElementEnumerator elements = StringInfo.GetTextElementEnumerator(text);
        while (elements.MoveNext())
        {
            string element = elements.GetTextElement();
            int cells = TextWidth.Of(element);
            if (cells > 0 && element != " ")
            {
                canvas.Blend(Glyph(element, Face.Regular, cells), left, top, color);
            }

            left += cells * _cellWidth;
        }
    }

    /// <summary>The coverage of <paramref name="text"/> centered in <paramref name="cells"/> cells, from the cache.</summary>
    private Mask Glyph(string text, Face face, int cells)
    {
        if (_glyphs.TryGetValue((text, face, cells), out Mask? cached))
        {
            return cached;
        }

        if (!_fonts.Covers(text, _fontSize))
        {
            _missing.Add(text);
        }

        // Room around the cells for glyphs that overhang them (italics, wide symbols).
        Font font = _fonts.Get((face & Face.Bold) != 0, (face & Face.Italic) != 0, _fontSize);
        int padX = _cellWidth * 2, padY = _cellHeight / 2;
        int width = cells * _cellWidth + 2 * padX, height = _cellHeight + 2 * padY;
        float advance = TextMeasurer.MeasureAdvance(text, Options(font)).Width;
        float x = padX + MathF.Round((cells * _cellWidth - advance) / 2);
        Mask mask = Rasterize(text, font, new Vector2(x, padY + _glyphTop), width, height).Trim(-padX, -padY);
        _glyphs[(text, face, cells)] = mask;
        return mask;
    }

    private TextOptions Options(Font font) => new(font)
    {
        FallbackFontFamilies = _fonts.Fallbacks,
        HintingMode = HintingMode.Standard,
        KerningMode = KerningMode.None,
    };

    private Mask Rasterize(string text, Font font, Vector2 origin, int width, int height)
    {
        var rasterizer = new Rasterizer(width, height) { Offset = origin };
        TextRenderer.RenderTextTo(new GlyphOutlines(rasterizer), text, Options(font));
        return new Mask(0, 0, width, height, rasterizer.Coverage());
    }

    /// <summary>Distance from the text origin down to the baseline: the bottom of a rendered "H".</summary>
    private int MeasureBaseline(Font font)
    {
        int size = _cellHeight * 2;
        Mask mask = Rasterize("H", font, Vector2.Zero, size, size);
        for (int y = size - 1; y >= 0; y--)
        {
            for (int x = 0; x < size; x++)
            {
                if (mask.Alpha[y * size + x] >= 128)
                {
                    return y + 1;
                }
            }
        }

        return (int)MathF.Round(_fontSize);
    }

    [Flags]
    private enum Face
    {
        Regular = 0,
        Bold = 1,
        Italic = 2,
    }
}
