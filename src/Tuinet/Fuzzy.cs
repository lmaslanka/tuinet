namespace Tuinet;

/// <summary>
/// Fuzzy matching for filtering lists by what was typed (a command palette, a file picker): the pattern's chars must
/// appear in the text in order, not necessarily together. Higher scores are better matches. Allocation-free, O(n).
/// </summary>
/// <remarks>
/// fzf "v1" style: a forward scan finds the first match, then a backward scan from its end tightens it to the
/// shortest window, which is scored. Matches at word starts (after a space, '-', '_', '/', '.', ':' or '\'), on
/// camelCase humps, at the very start and in runs score higher; gaps and a late start score lower. Case is ignored
/// unless the pattern has an uppercase letter (smart case). Chars are compared one UTF-16 unit at a time.
/// </remarks>
public static class Fuzzy
{
    private const int Match = 16;
    private const int BonusStart = 10;        // the text's first char
    private const int BonusBoundary = 9;      // a word start
    private const int BonusCamel = 7;         // a camelCase hump or the first digit after a letter
    private const int BonusConsecutive = 4;   // the least a char right after the previous match gets
    private const int FirstCharMultiplier = 2;
    private const int GapStart = -3;
    private const int GapExtension = -1;
    private const int MaxStartPenalty = 8;

    /// <summary>
    /// How well <paramref name="pattern"/> matches <paramref name="text"/>: -1 if it doesn't, otherwise 0 or more.
    /// An empty pattern matches everything with 0, so a stable sort keeps the original order.
    /// </summary>
    public static int Score(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text) => Score(pattern, text, default, record: false);

    /// <summary>
    /// <see cref="Score(ReadOnlySpan{char}, ReadOnlySpan{char})"/>, also writing the UTF-16 index in
    /// <paramref name="text"/> of each pattern char's match into <paramref name="matched"/> (for highlighting).
    /// <paramref name="matched"/> must hold at least <c>pattern.Length</c> ints; it is left alone when there's no match.
    /// </summary>
    public static int Score(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text, Span<int> matched)
    {
        if (matched.Length < pattern.Length)
        {
            throw new ArgumentException("Needs room for one index per pattern char.", nameof(matched));
        }

        return Score(pattern, text, matched, record: true);
    }

    private static int Score(ReadOnlySpan<char> pattern, ReadOnlySpan<char> text, Span<int> matched, bool record)
    {
        if (pattern.IsEmpty)
        {
            return 0;
        }

        if (pattern.Length > text.Length)
        {
            return -1;
        }

        bool caseSensitive = HasUpper(pattern);

        // Forward: the first place every pattern char appears in order.
        int p = 0;
        int end = -1;
        for (int t = 0; t < text.Length; t++)
        {
            if (Same(text[t], pattern[p], caseSensitive) && ++p == pattern.Length)
            {
                end = t + 1;
                break;
            }
        }

        if (end < 0)
        {
            return -1;
        }

        // Backward from the end: the latest start, so the window is as short as it gets from there.
        int start = end - 1;
        p = pattern.Length - 1;
        for (int t = end - 1; t >= 0; t--)
        {
            if (Same(text[t], pattern[p], caseSensitive) && --p < 0)
            {
                start = t;
                break;
            }
        }

        // Score the window, matching greedily from its start (which the backward scan proved works).
        int score = -Math.Min(start, MaxStartPenalty);
        int last = -2;   // no previous match, not even at index -1
        int runBonus = 0;
        bool inGap = false;
        p = 0;
        for (int t = start; t < end && p < pattern.Length; t++)
        {
            if (!Same(text[t], pattern[p], caseSensitive))
            {
                score += inGap ? GapExtension : GapStart;
                inGap = true;
                continue;
            }

            int bonus = Bonus(text, t);
            if (last == t - 1)
            {
                // A run keeps the bonus of the char that started it, so "Edit" stays a word-start match throughout.
                bonus = Math.Max(bonus, Math.Max(runBonus, BonusConsecutive));
            }
            else
            {
                runBonus = bonus;
            }

            score += Match + (p == 0 ? bonus * FirstCharMultiplier : bonus);
            if (record)
            {
                matched[p] = t;
            }

            last = t;
            inGap = false;
            p++;
        }

        return Math.Max(0, score);
    }

    private static int Bonus(ReadOnlySpan<char> text, int index)
    {
        if (index == 0)
        {
            return BonusStart;
        }

        char previous = text[index - 1];
        char current = text[index];
        if (previous is ' ' or '-' or '_' or '/' or '.' or ':' or '\\' or '\t')
        {
            return BonusBoundary;
        }

        if (char.IsLower(previous) && char.IsUpper(current) || char.IsLetter(previous) && char.IsDigit(current))
        {
            return BonusCamel;
        }

        return 0;
    }

    private static bool Same(char text, char pattern, bool caseSensitive) =>
        text == pattern || !caseSensitive && char.ToLowerInvariant(text) == pattern;

    /// <summary>Smart case: an uppercase pattern char makes the match case-sensitive; otherwise the pattern is already lowercase.</summary>
    private static bool HasUpper(ReadOnlySpan<char> pattern)
    {
        foreach (char c in pattern)
        {
            if (char.IsUpper(c))
            {
                return true;
            }
        }

        return false;
    }
}
