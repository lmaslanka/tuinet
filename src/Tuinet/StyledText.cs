using System.Globalization;

namespace Tuinet;

/// <summary><paramref name="Length"/> chars of a <see cref="StyledText"/> drawn in <paramref name="Style"/>.</summary>
public readonly record struct StyledRun(int Length, Style Style);

/// <summary>
/// Text with several styles: the text, runs that cover it from the start (each run's style is layered over
/// <see cref="Style"/>), and the base style for anything the runs don't cover. A view over caller memory,
/// so it costs nothing to build: runs are typically a collection expression on the stack, or come from a
/// <see cref="StyledTextBuilder"/> or <see cref="Markup.Parse"/>.
/// <code>
/// buffer.SetText(x, y, new StyledText("j/k move", [new(3, keyStyle), new(5, dimStyle)]));
/// </code>
/// </summary>
public readonly ref struct StyledText
{
    public StyledText(ReadOnlySpan<char> text, Style style = default)
    {
        Text = text;
        Style = style;
    }

    public StyledText(ReadOnlySpan<char> text, ReadOnlySpan<StyledRun> runs, Style style = default)
    {
        Text = text;
        Runs = runs;
        Style = style;
    }

    private StyledText(ReadOnlySpan<char> text, ReadOnlySpan<StyledRun> runs, Style style, int runOffset)
    {
        Text = text;
        Runs = runs;
        Style = style;
        RunOffset = runOffset;
    }

    public ReadOnlySpan<char> Text { get; }
    public ReadOnlySpan<StyledRun> Runs { get; }

    /// <summary>The base style: runs are layered over it, and it covers text past the last run.</summary>
    public Style Style { get; }

    /// <summary>Chars of <c>Runs[0]</c> that come before <see cref="Text"/> (after <see cref="Slice"/>).</summary>
    internal int RunOffset { get; }

    public bool IsEmpty => Text.IsEmpty;

    public static implicit operator StyledText(string? text) => new(text);

    /// <summary><paramref name="length"/> chars from <paramref name="start"/>, with the runs that cover them.</summary>
    public StyledText Slice(int start, int length)
    {
        ReadOnlySpan<char> text = Text.Slice(start, length);
        int skip = RunOffset + start;
        int run = 0;
        while (run < Runs.Length && skip >= Runs[run].Length)
        {
            skip -= Runs[run].Length;
            run++;
        }

        return run < Runs.Length ? new StyledText(text, Runs[run..], Style, skip) : new StyledText(text, Style);
    }

    /// <summary>The same text and runs, with the base style layered over <paramref name="under"/>.</summary>
    internal StyledText Over(Style under) => new(Text, Runs, under.Patch(Style), RunOffset);

    public override string ToString() => Text.ToString();
}

/// <summary>
/// Builds a <see cref="StyledText"/> into caller memory (usually <c>stackalloc</c>), merging adjacent runs
/// with the same style. Never throws when full: what fits is kept and <see cref="Overflowed"/> is set.
/// <code>
/// var keys = new StyledTextBuilder(stackalloc char[64], stackalloc StyledRun[8]);
/// keys.Append("j/k", key);
/// keys.Append(" move  ", dim);
/// keys.Append(count, key);
/// buffer.SetText(x, y, keys.Build());
/// </code>
/// </summary>
public ref struct StyledTextBuilder
{
    private readonly Span<char> _chars;
    private readonly Span<StyledRun> _runs;
    private int _length;
    private int _count;

    public StyledTextBuilder(Span<char> chars, Span<StyledRun> runs)
    {
        _chars = chars;
        _runs = runs;
    }

    /// <summary>Chars written so far.</summary>
    public readonly int Length => _length;

    /// <summary>Something didn't fit in the char or run buffer and was cut.</summary>
    public bool Overflowed { get; private set; }

    public void Append(scoped ReadOnlySpan<char> text, Style style = default)
    {
        int room = _chars.Length - _length;
        if (text.Length > room)
        {
            Overflowed = true;
            text = text[..room];
            if (!text.IsEmpty && char.IsHighSurrogate(text[^1]))
            {
                text = text[..^1];   // never keep half a surrogate pair
            }
        }

        if (text.IsEmpty || !AddRun(text.Length, style))
        {
            return;
        }

        text.CopyTo(_chars[_length..]);
        _length += text.Length;
    }

    /// <summary>Format <paramref name="value"/> (a number, date, …) straight into the buffer.</summary>
    public void Append<T>(T value, Style style = default, ReadOnlySpan<char> format = default)
        where T : ISpanFormattable
    {
        if (!value.TryFormat(_chars[_length..], out int written, format, CultureInfo.InvariantCulture))
        {
            Overflowed = true;
            return;
        }

        if (written > 0 && AddRun(written, style))
        {
            _length += written;
        }
    }

    public readonly StyledText Build(Style style = default) => new(_chars[.._length], _runs[.._count], style);

    private bool AddRun(int length, Style style)
    {
        if (_count > 0 && _runs[_count - 1].Style == style)
        {
            _runs[_count - 1] = new StyledRun(_runs[_count - 1].Length + length, style);
            return true;
        }

        if (_count == _runs.Length)
        {
            Overflowed = true;
            return false;
        }

        _runs[_count++] = new StyledRun(length, style);
        return true;
    }
}

/// <summary>
/// A small markup for styled text, parsed into caller memory without allocating.
/// <list type="bullet">
/// <item><c>[b]</c> bold, <c>[dim]</c>, <c>[i]</c> italic, <c>[u]</c> underline, <c>[blink]</c>, <c>[reverse]</c>,
/// <c>[hidden]</c>, <c>[s]</c> strike (long names work too: <c>[bold]</c>, <c>[italic]</c>, …).</item>
/// <item><c>[fg=red]</c>, <c>[bg=#1C2433]</c>: a name (<c>black</c> … <c>white</c>, <c>bright-red</c> …,
/// <c>default</c>), <c>#RRGGBB</c>, or a palette index 0–255. A bare color name or <c>#RRGGBB</c> sets the
/// foreground; a bare number is just text.</item>
/// <item>Several in one tag: <c>[b fg=#F5A623]</c>. <c>[/]</c> closes the latest tag.</item>
/// <item><c>[[</c> is a literal <c>[</c>. A tag that doesn't parse is kept as text, so <c>[1/3]</c> needs no escaping.</item>
/// </list>
/// </summary>
public static class Markup
{
    private const int MaxDepth = 16;

    /// <summary>
    /// Parse <paramref name="markup"/>. The visible text goes into <paramref name="chars"/> (at most
    /// <c>markup.Length</c> are needed) and runs into <paramref name="runs"/> (at most one per tag and text
    /// piece); what doesn't fit is cut. Styles are layered over <paramref name="style"/>.
    /// </summary>
    public static StyledText Parse(ReadOnlySpan<char> markup, Span<char> chars, Span<StyledRun> runs, Style style = default)
    {
        var builder = new StyledTextBuilder(chars, runs);
        Span<Style> stack = stackalloc Style[MaxDepth];
        int depth = 0;
        Style current = default;
        int i = 0;
        while (i < markup.Length)
        {
            int open = markup[i..].IndexOf('[');
            if (open < 0)
            {
                builder.Append(markup[i..], current);
                break;
            }

            builder.Append(markup.Slice(i, open), current);
            i += open;
            if (i + 1 < markup.Length && markup[i + 1] == '[')
            {
                builder.Append("[", current);
                i += 2;
                continue;
            }

            int close = markup[i..].IndexOf(']');
            ReadOnlySpan<char> tag = close < 0 ? default : markup.Slice(i + 1, close - 1);
            if (tag.SequenceEqual("/"))
            {
                if (depth > 0)
                {
                    current = stack[--depth];
                }

                i += close + 1;
                continue;
            }

            if (close > 1 && depth < MaxDepth && TryParseTag(tag, current, out Style next))
            {
                stack[depth++] = current;
                current = next;
                i += close + 1;
                continue;
            }

            builder.Append("[", current);   // not a tag: literal text
            i++;
        }

        return builder.Build(style);
    }

    /// <summary>Layer the attributes and colors in <paramref name="tag"/> (e.g. "b fg=red") over <paramref name="style"/>.</summary>
    public static bool TryParseTag(ReadOnlySpan<char> tag, Style style, out Style result)
    {
        result = style;
        Color fg = style.Fg;
        Color bg = style.Bg;
        Attr attrs = style.Attrs;
        bool any = false;
        foreach (Range part in tag.Split(' '))
        {
            ReadOnlySpan<char> word = tag[part];
            if (word.IsEmpty)
            {
                continue;
            }

            if (word.StartsWith("fg="))
            {
                if (!TryParseColor(word[3..], out fg))
                {
                    return false;
                }
            }
            else if (word.StartsWith("bg="))
            {
                if (!TryParseColor(word[3..], out bg))
                {
                    return false;
                }
            }
            else if (Attribute(word) is Attr attr)
            {
                attrs |= attr;
            }
            else if (char.IsAsciiDigit(word[0]) || !TryParseColor(word, out fg))
            {
                return false;   // bare numbers aren't colors: "[1]" and "[2/3]" stay text
            }

            any = true;
        }

        result = new Style(fg, bg, attrs);
        return any;
    }

    /// <summary>A color name (<c>red</c>, <c>bright-blue</c>, <c>default</c>), <c>#RRGGBB</c>, or a palette index 0–255.</summary>
    public static bool TryParseColor(ReadOnlySpan<char> text, out Color color)
    {
        color = default;
        if (text.Length == 7 && text[0] == '#' && uint.TryParse(text[1..], NumberStyles.HexNumber, CultureInfo.InvariantCulture, out uint rgb))
        {
            color = Color.Hex(rgb);
            return true;
        }

        if (text.Length is > 0 and <= 3 && byte.TryParse(text, NumberStyles.None, CultureInfo.InvariantCulture, out byte index))
        {
            color = Color.Indexed(index);
            return true;
        }

        bool bright = text.StartsWith("bright-");
        int basic = (bright ? text[7..] : text) switch
        {
            "black" => 0,
            "red" => 1,
            "green" => 2,
            "yellow" => 3,
            "blue" => 4,
            "magenta" => 5,
            "cyan" => 6,
            "white" => 7,
            "default" when !bright => -1,
            _ => -2,
        };

        if (basic == -2)
        {
            return false;
        }

        color = basic < 0 ? Color.Default : Color.Indexed((byte)(basic + (bright ? 8 : 0)));
        return true;
    }

    private static Attr? Attribute(ReadOnlySpan<char> word) => word switch
    {
        "b" or "bold" => Attr.Bold,
        "dim" => Attr.Dim,
        "i" or "italic" => Attr.Italic,
        "u" or "underline" => Attr.Underline,
        "blink" => Attr.Blink,
        "reverse" => Attr.Reverse,
        "hidden" => Attr.Hidden,
        "s" or "strike" => Attr.Strike,
        _ => null,
    };
}
