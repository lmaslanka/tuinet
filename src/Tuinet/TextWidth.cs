using System.Text;

namespace Tuinet;

/// <summary>Terminal display width of text, in columns.</summary>
public static class TextWidth
{
    /// <summary>0 for controls and combining marks (never drawn), 2 for wide CJK and emoji, otherwise 1.</summary>
    public static int Of(Rune rune) => UnicodeWidth.Of(rune.Value);

    public static int Of(ReadOnlySpan<char> text)
    {
        int width = 0;
        int i = 0;
        while (i < text.Length)
        {
            char c = text[i];
            if (c < 0x80)
            {
                width += (uint)(c - 0x20) < 0x5F ? 1 : 0;
                i++;
                continue;
            }

            Rune.DecodeFromUtf16(text[i..], out Rune rune, out int consumed);
            width += UnicodeWidth.Of(rune.Value);
            i += consumed;
        }

        return width;
    }
}
