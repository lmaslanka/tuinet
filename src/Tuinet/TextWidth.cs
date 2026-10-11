using System.Text;

namespace Tuinet;

/// <summary>Terminal display width of text, in columns.</summary>
public static class TextWidth
{
    /// <summary>0 for controls and combining marks (never drawn), 2 for wide CJK and emoji, otherwise 1.</summary>
    public static int Of(Rune rune) => UnicodeWidth.Of(rune.Value);

    /// <summary>Columns the styled text takes (styles don't change widths).</summary>
    public static int Of(StyledText text) => Of(text.Text);

    /// <summary>
    /// Columns <paramref name="text"/> takes when written with <see cref="CellBuffer.SetString"/>: grapheme
    /// clusters count once (e + combining accent is 1, 👨‍👩‍👧 is 2), controls and stray marks count 0.
    /// Runs of printable ASCII are counted with one scan, and letters of any script without marks (Cyrillic,
    /// Greek, CJK…) with a table lookup each; only clusters are segmented.
    /// </summary>
    public static int Of(ReadOnlySpan<char> text)
    {
        int width = 0;
        int i = 0;
        while (i < text.Length)
        {
            int run = Graphemes.AsciiRun(text[i..], text.Length - i);
            if (run == 0)
            {
                run = Graphemes.SimpleRun(text[i..], int.MaxValue, out int columns);
                width += columns;
            }
            else
            {
                width += run;
            }

            if (run > 0)
            {
                i += run;
                continue;
            }

            i += Graphemes.Next(text[i..], out _, out int w, out _);
            width += w;
        }

        return width;
    }
}
