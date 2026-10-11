namespace Tuinet.Screenshots;

/// <summary>
/// A small zlib (RFC 1950/1951) compressor: LZ77 with hash chains and lazy matching, coded with the fixed Huffman
/// tables. Ours rather than the runtime's because zlib implementations may pick different matches on different CPUs
/// or versions, and a PNG must come out byte-identical everywhere.
/// </summary>
internal static class Deflate
{
    private const int Window = 32768;
    private const int MinMatch = 3;
    private const int MaxMatch = 258;
    private const int MaxChain = 256;
    private const int HashBits = 15;

    public static byte[] Zlib(ReadOnlySpan<byte> data)
    {
        var bits = new BitWriter();
        bits.WriteByte(0x78);   // deflate, 32K window
        bits.WriteByte(0xDA);   // best compression; header % 31 == 0
        bits.Write(1, 1);       // final block
        bits.Write(1, 2);       // fixed Huffman codes

        var head = new int[1 << HashBits];
        var prev = new int[Window];
        Array.Fill(head, -1);
        int i = 0;
        while (i < data.Length)
        {
            (int length, int distance) = Longest(data, i, head, prev);
            if (length >= MinMatch && i + 1 < data.Length)
            {
                // Lazy: a longer match one byte later is worth a literal now.
                Insert(data, i, head, prev);
                (int next, _) = Longest(data, i + 1, head, prev);
                if (next > length)
                {
                    Literal(bits, data[i]);
                    i++;
                    continue;
                }

                Match(bits, length, distance);
                for (int k = 1; k < length; k++)
                {
                    Insert(data, i + k, head, prev);
                }

                i += length;
            }
            else
            {
                Insert(data, i, head, prev);
                Literal(bits, data[i]);
                i++;
            }
        }

        Symbol(bits, 256);   // end of block
        bits.Flush();
        uint adler = Adler32(data);
        for (int shift = 24; shift >= 0; shift -= 8)
        {
            bits.WriteByte((byte)(adler >> shift));
        }

        return bits.ToArray();
    }

    private static int Hash(ReadOnlySpan<byte> data, int i) =>
        ((data[i] << 10) ^ (data[i + 1] << 5) ^ data[i + 2]) & ((1 << HashBits) - 1);

    /// <summary>Record position <paramref name="i"/> in the chains (idempotent, so lazy matching can insert early).</summary>
    private static void Insert(ReadOnlySpan<byte> data, int i, int[] head, int[] prev)
    {
        if (i + MinMatch > data.Length)
        {
            return;
        }

        int h = Hash(data, i);
        if (head[h] == i)
        {
            return;
        }

        prev[i % Window] = head[h];
        head[h] = i;
    }

    private static (int Length, int Distance) Longest(ReadOnlySpan<byte> data, int i, int[] head, int[] prev)
    {
        if (i + MinMatch > data.Length)
        {
            return (0, 0);
        }

        int best = 0, distance = 0, max = Math.Min(MaxMatch, data.Length - i);
        int candidate = head[Hash(data, i)];
        if (candidate == i)
        {
            candidate = prev[i % Window];
        }

        for (int chain = 0; candidate >= 0 && i - candidate <= Window - 1 && chain < MaxChain; chain++)
        {
            if (data[candidate + best] == data[i + best])
            {
                int length = 0;
                while (length < max && data[candidate + length] == data[i + length])
                {
                    length++;
                }

                if (length > best)
                {
                    best = length;
                    distance = i - candidate;
                    if (length == max)
                    {
                        break;
                    }
                }
            }

            int next = prev[candidate % Window];
            if (next >= candidate)
            {
                break;   // the slot was reused by a newer position: the chain ends
            }

            candidate = next;
        }

        return best >= MinMatch ? (best, distance) : (0, 0);
    }

    private static readonly int[] LengthBase = [3, 4, 5, 6, 7, 8, 9, 10, 11, 13, 15, 17, 19, 23, 27, 31, 35, 43, 51, 59, 67, 83, 99, 115, 131, 163, 195, 227, 258];
    private static readonly int[] LengthExtra = [0, 0, 0, 0, 0, 0, 0, 0, 1, 1, 1, 1, 2, 2, 2, 2, 3, 3, 3, 3, 4, 4, 4, 4, 5, 5, 5, 5, 0];
    private static readonly int[] DistanceBase = [1, 2, 3, 4, 5, 7, 9, 13, 17, 25, 33, 49, 65, 97, 129, 193, 257, 385, 513, 769, 1025, 1537, 2049, 3073, 4097, 6145, 8193, 12289, 16385, 24577];
    private static readonly int[] DistanceExtra = [0, 0, 0, 0, 1, 1, 2, 2, 3, 3, 4, 4, 5, 5, 6, 6, 7, 7, 8, 8, 9, 9, 10, 10, 11, 11, 12, 12, 13, 13];

    private static void Literal(BitWriter bits, byte value) => Symbol(bits, value);

    private static void Match(BitWriter bits, int length, int distance)
    {
        int l = LengthBase.Length - 1;
        while (LengthBase[l] > length)
        {
            l--;
        }

        Symbol(bits, 257 + l);
        bits.Write(length - LengthBase[l], LengthExtra[l]);

        int d = DistanceBase.Length - 1;
        while (DistanceBase[d] > distance)
        {
            d--;
        }

        bits.WriteReversed(d, 5);
        bits.Write(distance - DistanceBase[d], DistanceExtra[d]);
    }

    /// <summary>A literal/length symbol in the fixed code (RFC 1951 3.2.6).</summary>
    private static void Symbol(BitWriter bits, int symbol)
    {
        switch (symbol)
        {
            case < 144:
                bits.WriteReversed(0x30 + symbol, 8);
                break;
            case < 256:
                bits.WriteReversed(0x190 + symbol - 144, 9);
                break;
            case < 280:
                bits.WriteReversed(symbol - 256, 7);
                break;
            default:
                bits.WriteReversed(0xC0 + symbol - 280, 8);
                break;
        }
    }

    private static uint Adler32(ReadOnlySpan<byte> data)
    {
        uint a = 1, b = 0;
        foreach (byte value in data)
        {
            a = (a + value) % 65521;
            b = (b + a) % 65521;
        }

        return (b << 16) | a;
    }

    /// <summary>Bits packed least significant first, as deflate wants.</summary>
    private sealed class BitWriter
    {
        private readonly MemoryStream _bytes = new();
        private uint _buffer;
        private int _count;

        public void Write(int value, int count)
        {
            _buffer |= (uint)value << _count;
            _count += count;
            while (_count >= 8)
            {
                _bytes.WriteByte((byte)_buffer);
                _buffer >>= 8;
                _count -= 8;
            }
        }

        /// <summary>A Huffman code: sent most significant bit first.</summary>
        public void WriteReversed(int code, int count)
        {
            int reversed = 0;
            for (int i = 0; i < count; i++)
            {
                reversed = (reversed << 1) | ((code >> i) & 1);
            }

            Write(reversed, count);
        }

        public void WriteByte(byte value) => Write(value, 8);

        /// <summary>Pad to a byte boundary.</summary>
        public void Flush()
        {
            if (_count > 0)
            {
                Write(0, 8 - _count);
            }
        }

        public byte[] ToArray() => _bytes.ToArray();
    }
}
