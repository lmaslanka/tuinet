using System.Text;

namespace Tuinet;

/// <summary>Terminal display width of text, in columns.</summary>
public static class TextWidth
{
    /// <summary>0 for controls and combining marks (never drawn), 2 for wide CJK and emoji, otherwise 1.</summary>
    public static int Of(Rune rune) => UnicodeWidth.Of(rune.Value);

    /// <summary>
    /// Columns <paramref name="text"/> takes when written with <see cref="CellBuffer.SetString"/>: grapheme
    /// clusters count once (e + combining accent is 1, 👨‍👩‍👧 is 2), controls and stray marks count 0.
    /// </summary>
    public static int Of(ReadOnlySpan<char> text)
    {
        int width = 0;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c < 0x80 && (i + 1 == text.Length || text[i + 1] < 0x300))
            {
                width += (uint)(c - 0x20) < 0x5F ? 1 : 0;
                i++;
                continue;
            }

            i += Graphemes.Next(text[i..], out _, out int w, out _);
            width += w;
        }

        return width;
    }
}
