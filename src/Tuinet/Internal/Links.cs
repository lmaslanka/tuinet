using System.Collections.Concurrent;
using System.Text;

namespace Tuinet;

/// <summary>
/// Hyperlink targets (OSC 8), interned process-wide like <see cref="Graphemes"/>: a cell stores a 12-bit id, and
/// equal URLs get equal ids in every buffer, so the renderer's byte-wise row diff stays correct. Only the first
/// sighting of a URL allocates. URLs are stored as the terminal receives them: printable ASCII, with other
/// characters percent-encoded (UTF-8).
/// </summary>
internal static class Links
{
    /// <summary>Ids are 1..4095 (0 is "no link"). Past that many distinct URLs, new ones are not linked.</summary>
    public const int MaxEntries = (1 << 12) - 1;

    /// <summary>Longer URLs (after encoding) are not linked; terminals cap them around here too.</summary>
    public const int MaxLength = 2048;

    // Lookups are lock-free (the common case: a URL seen before); inserts take the lock to assign ids.
    private static readonly Lock Gate = new();
    private static readonly ConcurrentDictionary<string, int> Ids = new(StringComparer.Ordinal);
    private static readonly ConcurrentDictionary<string, int>.AlternateLookup<ReadOnlySpan<char>> IdsBySpan =
        Ids.GetAlternateLookup<ReadOnlySpan<char>>();
    private static int _count;

    private static Entry[] _entries = new Entry[16];

    /// <summary>
    /// Id of <paramref name="url"/> (interning it on first use), or 0 when it is empty, too long, holds control
    /// characters (which could smuggle escape sequences to the terminal) or the store is full.
    /// </summary>
    public static int Intern(ReadOnlySpan<char> url)
    {
        if (url.IsEmpty || url.Length > MaxLength)
        {
            return 0;
        }

        Span<char> encoded = stackalloc char[MaxLength];
        if (!Encode(url, encoded, out int length))
        {
            return 0;
        }

        ReadOnlySpan<char> key = encoded[..length];
        if (IdsBySpan.TryGetValue(key, out int id))
        {
            return id;
        }

        lock (Gate)
        {
            if (IdsBySpan.TryGetValue(key, out id))
            {
                return id;
            }

            if (_count >= MaxEntries)
            {
                return 0;
            }

            string text = key.ToString();
            id = _count + 1;
            Entry[] entries = _entries;
            if (id >= entries.Length)
            {
                // Readers index the array without the lock: publish a full copy, never mutate a shared slot.
                Array.Resize(ref entries, Math.Min(entries.Length * 2, MaxEntries + 1));
            }

            entries[id] = new Entry(text, Encoding.ASCII.GetBytes(text));
            // Publish the entry before the id: a lock-free reader that finds the id must find its entry.
            Volatile.Write(ref _entries, entries);
            _count++;
            Ids[text] = id;
            return id;
        }
    }

    public static string Url(int id) => Volatile.Read(ref _entries)[id].Url;

    /// <summary>The URL as the bytes to send (ASCII).</summary>
    public static ReadOnlySpan<byte> Bytes(int id) => Volatile.Read(ref _entries)[id].Bytes;

    /// <summary>Copy <paramref name="url"/> to <paramref name="dest"/>, percent-encoding spaces and non-ASCII.</summary>
    private static bool Encode(ReadOnlySpan<char> url, Span<char> dest, out int length)
    {
        length = 0;
        Span<byte> utf8 = stackalloc byte[4];
        int i = 0;
        while (i < url.Length)
        {
            char c = url[i];
            if (c is > ' ' and < (char)0x7F)
            {
                if (length == dest.Length)
                {
                    return false;
                }

                dest[length++] = c;
                i++;
                continue;
            }

            if (c < ' ' || c is >= (char)0x7F and <= (char)0x9F
                || Rune.DecodeFromUtf16(url[i..], out Rune rune, out int consumed) != System.Buffers.OperationStatus.Done)
            {
                return false;   // controls and broken surrogates: refuse the link rather than guess
            }

            int n = rune.EncodeToUtf8(utf8);
            if (length + 3 * n > dest.Length)
            {
                return false;
            }

            for (int b = 0; b < n; b++)
            {
                dest[length++] = '%';
                dest[length++] = "0123456789ABCDEF"[utf8[b] >> 4];
                dest[length++] = "0123456789ABCDEF"[utf8[b] & 0xF];
            }

            i += consumed;
        }

        return true;
    }

    private readonly record struct Entry(string Url, byte[] Bytes);
}
