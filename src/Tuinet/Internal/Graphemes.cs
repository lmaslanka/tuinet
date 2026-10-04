using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Tuinet;

/// <summary>
/// Grapheme clusters: what a reader sees as one character, possibly several code points
/// (e + combining accent, 👨‍👩‍👧, ❤️, 🇵🇱, Hangul jamo). Boundaries follow UAX #29 via
/// <see cref="StringInfo.GetNextTextElementLength(ReadOnlySpan{char})"/>, with fast paths so plain
/// Latin and CJK text never calls it.
/// <para>
/// Multi-code-point clusters are interned process-wide: a cell stores the cluster's id, and equal
/// clusters get equal ids in every buffer, so the renderer's byte-wise row diff stays correct.
/// Only the first sighting of a cluster allocates.
/// </para>
/// </summary>
internal static class Graphemes
{
    /// <summary>Past this many distinct clusters, new ones degrade to their first code point.</summary>
    private const int MaxEntries = 1 << 20;

    /// <summary>Longer clusters (pathological stacks of marks) are cut to this many chars when stored.</summary>
    public const int MaxChars = 64;

    // Lookups are lock-free (the common case: a cluster seen before); inserts take the lock to assign ids.
    private static readonly Lock Gate = new();
    private static readonly ConcurrentDictionary<string, int> Ids = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> IdsBySpan =
        Ids.GetAlternateLookup<ReadOnlySpan<char>>();
    private static int _count;

    private static Entry[] _entries = new Entry[64];

    /// <summary>
    /// The cluster at the start of <paramref name="text"/> (non-empty): its length in chars, first code point,
    /// width in columns, and whether it is more than one code point.
    /// </summary>
    public static int Next(ReadOnlySpan<char> text, out Rune first, out int width, out bool multi)
    {
        int length = Length(text);
        Rune.DecodeFromUtf16(text, out first, out int consumed);
        multi = length > consumed;
        width = multi ? Width(text[..length], out _) : UnicodeWidth.Of(first.Value);
        return length;
    }

    /// <summary>Length in chars of the cluster at the start of <paramref name="text"/> (non-empty).</summary>
    public static int Length(ReadOnlySpan<char> text)
    {
        if (text.Length == 1)
        {
            return 1;
        }

        char c = text[0];
        char next = text[1];

        // Two code points that can never join (no extenders, no Hangul jamo, no regional indicators,
        // no prepend marks): break between them without the full rule set.
        if (Simple(c) && Simple(next))
        {
            return 1;
        }

        return StringInfo.GetNextTextElementLength(text);
    }

    /// <summary>
    /// Columns a multi-code-point cluster occupies: the sum of its code points' widths capped at 2, and 2
    /// when it asks for emoji presentation (VS16). 0 when it starts with a zero-width code point (a stray
    /// mark): such clusters are not drawn. <paramref name="legacy"/> is the plain sum, which is how far a
    /// terminal without grapheme support moves the cursor.
    /// </summary>
    public static int Width(ReadOnlySpan<char> cluster, out int legacy)
    {
        int sum = 0;
        int first = -1;
        bool emoji = false;
        foreach (Rune rune in cluster.EnumerateRunes())
        {
            int w = UnicodeWidth.Of(rune.Value);
            if (first < 0)
            {
                first = w;
            }

            sum += w;
            emoji |= rune.Value == 0xFE0F;
        }

        legacy = sum;
        if (first <= 0)
        {
            return 0;
        }

        return emoji ? 2 : Math.Min(2, sum);
    }

    /// <summary>Id of <paramref name="cluster"/> (interning it on first use), or -1 when the store is full.</summary>
    public static int Intern(ReadOnlySpan<char> cluster)
    {
        if (cluster.Length > MaxChars)
        {
            cluster = cluster[..(char.IsLowSurrogate(cluster[MaxChars]) ? MaxChars - 1 : MaxChars)];
        }

        if (IdsBySpan.TryGetValue(cluster, out int id))
        {
            return id;
        }

        lock (Gate)
        {
            if (IdsBySpan.TryGetValue(cluster, out id))
            {
                return id;
            }

            if (_count >= MaxEntries)
            {
                return -1;
            }

            string text = cluster.ToString();
            id = _count;
            Entry[] entries = _entries;
            if (id == entries.Length)
            {
                // Readers index the array without the lock: publish a full copy, never mutate a shared slot.
                Array.Resize(ref entries, entries.Length * 2);
            }

            Rune.DecodeFromUtf16(text, out Rune firstRune, out _);
            Width(text, out int legacy);
            entries[id] = new Entry(text, firstRune, (byte)Math.Min(legacy, 255));
            // Publish the entry before the id: a lock-free reader that finds the id must find its entry.
            Volatile.Write(ref _entries, entries);
            Ids[text] = id;
            _count++;
            return id;
        }
    }

    public static string Text(int id) => Volatile.Read(ref _entries)[id].Text;

    public static Rune First(int id) => Volatile.Read(ref _entries)[id].First;

    /// <summary>Columns a terminal without grapheme support advances for cluster <paramref name="id"/>.</summary>
    public static int LegacyWidth(int id) => Volatile.Read(ref _entries)[id].Legacy;

    /// <summary>
    /// Could code point <paramref name="right"/> join <paramref name="left"/> into one cluster? Used by the
    /// renderer: two cells drawn back to back must not merge on the terminal.
    /// </summary>
    public static bool MayJoin(Rune left, Rune right)
    {
        if (Simple(left.Value) && Simple(right.Value))
        {
            return false;
        }

        Span<char> pair = stackalloc char[4];
        int n = left.EncodeToUtf16(pair);
        n += right.EncodeToUtf16(pair[n..]);
        return StringInfo.GetNextTextElementLength(pair[..n]) == n;
    }

    /// <summary>
    /// Code points that never join their neighbours: Latin below the combining marks; punctuation, arrows,
    /// math, box drawing, blocks, shapes, symbols, dingbats and braille (U+2010–U+2027, U+2030–U+205F,
    /// U+2190–U+2BFF: no extenders, joiners or prepend marks there); CJK ideographs, kana (without the
    /// combining sound marks) and precomposed Hangul syllables. Surrogates are not simple.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Simple(int c) =>
        c < 0x300 ||
        (uint)(c - 0x4E00) <= 0x9FFF - 0x4E00 ||
        (uint)(c - 0xAC00) <= 0xD7A3 - 0xAC00 ||
        (uint)(c - 0x3041) <= 0x3096 - 0x3041 ||
        (uint)(c - 0x30A1) <= 0x30FA - 0x30A1 ||
        (uint)(c - 0x2190) <= 0x2BFF - 0x2190 ||
        (uint)(c - 0x2010) <= 0x2027 - 0x2010 ||
        (uint)(c - 0x2030) <= 0x205F - 0x2030;

    private readonly record struct Entry(string Text, Rune First, byte Legacy);
}
