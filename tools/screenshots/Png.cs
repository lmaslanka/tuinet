using System.Buffers.Binary;
using System.Text;

namespace Tuinet.Screenshots;

/// <summary>
/// A minimal PNG encoder: 8-bit RGB, one IDAT compressed by <see cref="Deflate"/>, and no time, text or other metadata
/// chunks, so the bytes depend only on the pixels.
/// </summary>
public static class Png
{
    public static byte[] Encode(Rgb[] pixels, int width, int height)
    {
        ArgumentOutOfRangeException.ThrowIfNotEqual(pixels.Length, width * height);
        using var output = new MemoryStream();
        output.Write([0x89, (byte)'P', (byte)'N', (byte)'G', 0x0D, 0x0A, 0x1A, 0x0A]);

        Span<byte> header = stackalloc byte[13];
        BinaryPrimitives.WriteInt32BigEndian(header, width);
        BinaryPrimitives.WriteInt32BigEndian(header[4..], height);
        header[8] = 8;    // bits per channel
        header[9] = 2;    // truecolor
        Chunk(output, "IHDR", header);

        int stride = width * 3;
        var filteredRows = new byte[(stride + 1) * height];
        var previous = new byte[stride];
        var current = new byte[stride];
        var filtered = new byte[stride + 1];
        for (int y = 0; y < height; y++)
        {
            for (int x = 0; x < width; x++)
            {
                Rgb p = pixels[y * width + x];
                current[x * 3] = p.R;
                current[x * 3 + 1] = p.G;
                current[x * 3 + 2] = p.B;
            }

            // The usual heuristic: the filter whose output has the smallest sum of absolute (signed) bytes.
            long bestScore = long.MaxValue;
            for (byte filter = 0; filter <= 4; filter++)
            {
                long score = Filter(filter, current, previous, filtered);
                if (score < bestScore)
                {
                    bestScore = score;
                    filtered.CopyTo(filteredRows, y * (stride + 1));
                }
            }

            (previous, current) = (current, previous);
        }

        Chunk(output, "IDAT", Deflate.Zlib(filteredRows));
        Chunk(output, "IEND", []);
        return output.ToArray();
    }

    /// <summary>Apply PNG filter <paramref name="type"/> to a row of 3-byte pixels; returns its score.</summary>
    private static long Filter(byte type, byte[] row, byte[] above, byte[] output)
    {
        output[0] = type;
        long score = 0;
        for (int i = 0; i < row.Length; i++)
        {
            int left = i >= 3 ? row[i - 3] : 0, up = above[i], upLeft = i >= 3 ? above[i - 3] : 0;
            int predictor = type switch
            {
                1 => left,
                2 => up,
                3 => (left + up) / 2,
                4 => Paeth(left, up, upLeft),
                _ => 0,
            };
            byte value = (byte)(row[i] - predictor);
            output[i + 1] = value;
            score += Math.Abs((int)(sbyte)value);
        }

        return score;
    }

    private static int Paeth(int a, int b, int c)
    {
        int p = a + b - c;
        int pa = Math.Abs(p - a), pb = Math.Abs(p - b), pc = Math.Abs(p - c);
        return pa <= pb && pa <= pc ? a : pb <= pc ? b : c;
    }

    private static void Chunk(Stream output, string type, ReadOnlySpan<byte> data)
    {
        Span<byte> word = stackalloc byte[4];
        BinaryPrimitives.WriteInt32BigEndian(word, data.Length);
        output.Write(word);
        byte[] typeBytes = Encoding.ASCII.GetBytes(type);
        output.Write(typeBytes);
        output.Write(data);
        uint crc = Crc32.Update(Crc32.Update(0xFFFFFFFF, typeBytes), data) ^ 0xFFFFFFFF;
        BinaryPrimitives.WriteUInt32BigEndian(word, crc);
        output.Write(word);
    }

    private static class Crc32
    {
        private static readonly uint[] Table = Build();

        public static uint Update(uint crc, ReadOnlySpan<byte> data)
        {
            foreach (byte b in data)
            {
                crc = Table[(crc ^ b) & 0xFF] ^ (crc >> 8);
            }

            return crc;
        }

        private static uint[] Build()
        {
            var table = new uint[256];
            for (uint n = 0; n < 256; n++)
            {
                uint c = n;
                for (int k = 0; k < 8; k++)
                {
                    c = (c & 1) != 0 ? 0xEDB88320 ^ (c >> 1) : c >> 1;
                }

                table[n] = c;
            }

            return table;
        }
    }
}
