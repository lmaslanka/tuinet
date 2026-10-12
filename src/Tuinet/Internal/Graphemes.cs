using System.Buffers;
using System.Collections.Concurrent;
using System.Globalization;
using System.Runtime.CompilerServices;
using System.Text;

namespace Tuinet;

/// <summary>
/// Grapheme clusters: what a reader sees as one character, possibly several code points
/// (e + combining accent, 👨‍👩‍👧, ❤️, 🇵🇱, Hangul jamo). Boundaries follow UAX #29 via
/// <see cref="StringInfo.GetNextTextElementLength(ReadOnlySpan{char})"/>, with fast paths so text in any
/// script without marks (Latin, Cyrillic, Greek, Hebrew, Arabic letters, CJK, Hangul syllables…) never calls it.
/// <para>
/// Multi-code-point clusters are interned process-wide: a cell stores the cluster's id, and equal
/// clusters get equal ids in every buffer, so the renderer's byte-wise row diff stays correct.
/// Only the first sighting of a cluster allocates.
/// </para>
/// </summary>
internal static class Graphemes
{
    /// <summary>
    /// Past this many distinct clusters, new ones degrade to their first code point. Entries are never released, so
    /// the cap bounds what untrusted text (a log viewer, a chat) can make the process keep: 64 K clusters of at most
    /// <see cref="MaxChars"/> chars is a few MB.
    /// </summary>
    private const int MaxEntries = 1 << 16;

    /// <summary>Longer clusters (pathological stacks of marks) are cut to this many chars when stored.</summary>
    public const int MaxChars = 64;

    /// <summary>' ' to '~'. (IndexOfAnyExceptInRange allocates on this path; SearchValues doesn't.)</summary>
    public static readonly SearchValues<char> PrintableAscii =
        SearchValues.Create(" !\"#$%&'()*+,-./0123456789:;<=>?@ABCDEFGHIJKLMNOPQRSTUVWXYZ[\\]^_`abcdefghijklmnopqrstuvwxyz{|}~");

    // Lookups are lock-free (the common case: a cluster seen before); inserts take the lock to assign ids.
    private static readonly Lock Gate = new();
    private static readonly ConcurrentDictionary<string, int> Ids = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> IdsBySpan =
        Ids.GetAlternateLookup<ReadOnlySpan<char>>();
    private static int _count;

    private static Entry[] _entries = new Entry[64];

    /// <summary>
    /// The cluster at the start of <paramref name="text"/> (non-empty): its length in chars, first code point,
    /// width in columns, and whether it is more than one code point. A code point that can't join the one after
    /// it (see <see cref="Simple"/>) is answered without decoding or segmenting.
    /// </summary>
    public static int Next(ReadOnlySpan<char> text, out Rune first, out int width, out bool multi)
    {
        char c = text[0];
        int info = UnicodeWidth.Info(c);
        if ((info & UnicodeWidth.NeverJoins) != 0 && (text.Length == 1 || Simple(text[1])))
        {
            // Simple chars are never surrogates, so the rune needs no validation.
            first = Unsafe.BitCast<int, Rune>(c);
            width = info & UnicodeWidth.WidthMask;
            multi = false;
            return 1;
        }

        int length = text.Length == 1 ? 1 : StringInfo.GetNextTextElementLength(text);
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

        // Two code points that can never join: break between them without the full rule set.
        if (Simple(text[0]) && Simple(text[1]))
        {
            return 1;
        }

        return StringInfo.GetNextTextElementLength(text);
    }

    /// <summary>
    /// Chars of printable ASCII (one column each) at the start of <paramref name="text"/>, looking at no more than
    /// <paramref name="max"/> (at least 1). A last char followed by something that could join it (a combining
    /// mark, VS16…) is left out, so the caller's cluster path takes the pair. 0 when the text doesn't start with a
    /// usable run.
    /// </summary>
    public static int AsciiRun(ReadOnlySpan<char> text, int max)
    {
        int n = Math.Min(text.Length, max);
        int stop = text[..Math.Min(text.Length, n + 1)].IndexOfAnyExcept(PrintableAscii);   // one past n: a mark there?
        if (stop < 0 || stop > n)
        {
            return n;
        }

        return stop > 0 && text[stop] >= 0x300 && !Simple(text[stop]) ? stop - 1 : stop;
    }

    /// <summary>
    /// Chars at the start of <paramref name="text"/> that are single code points which can't join their follower
    /// (see <see cref="Simple"/>), so each is one glyph of a known width, taking no more than
    /// <paramref name="maxColumns"/> columns in all; <paramref name="columns"/> is what they take. Stops before a
    /// char that might join the next (the cluster path takes the pair) and before one that doesn't fit. One table
    /// lookup per char: the follower's lookup is reused when it becomes the current char.
    /// </summary>
    public static int SimpleRun(ReadOnlySpan<char> text, int maxColumns, out int columns)
    {
        columns = 0;
        int i = 0;
        if (text.IsEmpty)
        {
            return 0;
        }

        int info = UnicodeWidth.Info(text[0]);
        while ((info & UnicodeWidth.NeverJoins) != 0)
        {
            int next = i + 1 < text.Length ? UnicodeWidth.Info(text[i + 1]) : UnicodeWidth.NeverJoins;
            int w = info & UnicodeWidth.WidthMask;
            if ((next & UnicodeWidth.NeverJoins) == 0 || columns + w > maxColumns)
            {
                break;
            }

            columns += w;
            if (++i == text.Length)
            {
                break;
            }

            info = next;
        }

        return i;
    }

    /// <summary>
    /// Like <see cref="SimpleRun"/> for cell writes: chars at the start of <paramref name="text"/> (at most
    /// <paramref name="max"/>, which is at least 1) that are each one glyph of <paramref name="width"/> columns
    /// (1 or 2) that never joins, each followed by a simple char or the end. One table lookup per char.
    /// </summary>
    public static int GlyphRun(ReadOnlySpan<char> text, int max, int width)
    {
        int wanted = UnicodeWidth.NeverJoins | width;
        int i = 0;
        int info = UnicodeWidth.Info(text[0]);
        while ((info & (UnicodeWidth.NeverJoins | UnicodeWidth.WidthMask)) == wanted)
        {
            int next = i + 1 < text.Length ? UnicodeWidth.Info(text[i + 1]) : UnicodeWidth.NeverJoins;
            if ((next & UnicodeWidth.NeverJoins) == 0)
            {
                break;
            }

            if (++i == max || i == text.Length)
            {
                break;
            }

            info = next;
        }

        return i;
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
    /// Code points that never join another simple code point into one cluster (UAX #29): everything but
    /// extenders, joiners, spacing and prepend marks, regional indicators, Hangul jamo, surrogates and the emoji
    /// plane (where the renderer places the next cell explicitly). Letters of every script are simple; their
    /// combining marks are not. Generated into UnicodeWidth.g.cs from GraphemeBreakProperty and emoji-data; the
    /// first 768 code points (Latin, before the combining marks) are answered without the table.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static bool Simple(int c) => (uint)c < 0x300 || (UnicodeWidth.Info(c) & UnicodeWidth.NeverJoins) != 0;

    private readonly record struct Entry(string Text, Rune First, byte Legacy);
}
