using System.Text;
using SixLabors.Fonts;
using SixLabors.Fonts.Unicode;

namespace Tuinet.Screenshots;

/// <summary>
/// The four faces of one monospace family plus fallback fonts for what it lacks, loaded from files: never from the
/// system, so output is the same everywhere.
/// </summary>
public sealed class TerminalFonts
{
    private readonly FontFamily _family;

    private TerminalFonts(FontFamily family, IReadOnlyList<FontFamily> fallbacks)
    {
        _family = family;
        Fallbacks = fallbacks;
    }

    /// <summary>Families searched, in order, for glyphs the main family lacks (emoji, CJK…).</summary>
    public IReadOnlyList<FontFamily> Fallbacks { get; }

    /// <summary>
    /// The bundled JetBrains Mono from fonts/ next to the executable, with any other .ttf or .otf files there as
    /// fallbacks in name order.
    /// </summary>
    public static TerminalFonts Bundled()
    {
        string directory = Path.Combine(AppContext.BaseDirectory, "fonts");
        string[] others = [.. Directory.EnumerateFiles(directory)
            .Where(path => path.EndsWith(".ttf", StringComparison.OrdinalIgnoreCase) || path.EndsWith(".otf", StringComparison.OrdinalIgnoreCase))
            .Where(path => !Path.GetFileName(path).StartsWith("JetBrainsMono-", StringComparison.Ordinal))
            .Order(StringComparer.Ordinal)];
        return Load(directory, "JetBrainsMono", others);
    }

    /// <summary>
    /// Load <c>{prefix}-Regular.ttf</c>, <c>-Bold</c>, <c>-Italic</c> and <c>-BoldItalic</c> from
    /// <paramref name="directory"/>, and <paramref name="fallbacks"/>. Regular is required; a missing face falls back to
    /// the nearest one.
    /// </summary>
    public static TerminalFonts Load(string directory, string prefix, params IEnumerable<string> fallbacks)
    {
        var collection = new FontCollection();
        FontFamily family = collection.Add(Path.Combine(directory, prefix + "-Regular.ttf"));
        foreach (string face in (string[])["Bold", "Italic", "BoldItalic"])
        {
            string path = Path.Combine(directory, $"{prefix}-{face}.ttf");
            if (File.Exists(path))
            {
                collection.Add(path);
            }
        }

        var fallbackCollection = new FontCollection();
        FontFamily[] fallbackFamilies = [.. fallbacks.Select(fallbackCollection.Add).Distinct()];
        return new TerminalFonts(family, fallbackFamilies);
    }

    public Font Get(bool bold, bool italic, float size)
    {
        FontStyle style = (bold, italic) switch
        {
            (true, true) => FontStyle.BoldItalic,
            (true, false) => FontStyle.Bold,
            (false, true) => FontStyle.Italic,
            _ => FontStyle.Regular,
        };

        // Without the bold italic face, bold wins: weight shows more than slant at small sizes.
        foreach (FontStyle candidate in (FontStyle[])[style, bold ? FontStyle.Bold : FontStyle.Italic, FontStyle.Regular])
        {
            if (_family.TryGetMetrics(candidate, out _))
            {
                return _family.CreateFont(size, candidate);
            }
        }

        return _family.CreateFont(size);
    }

    /// <summary>Does the regular face, or a fallback, have a glyph for every code point of <paramref name="text"/>?</summary>
    public bool Covers(string text, float size)
    {
        Font[] fonts = [Get(bold: false, italic: false, size), .. Fallbacks.Select(f => f.CreateFont(size))];
        foreach (Rune rune in text.EnumerateRunes())
        {
            if (!fonts.Any(font => Has(font, rune)))
            {
                return false;
            }
        }

        return true;
    }

    private static bool Has(Font font, Rune rune) =>
        font.TryGetGlyphs(new CodePoint(rune.Value), out IReadOnlyList<Glyph>? glyphs) && glyphs.Count > 0 && glyphs[0].GlyphMetrics.GlyphId != 0;
}
