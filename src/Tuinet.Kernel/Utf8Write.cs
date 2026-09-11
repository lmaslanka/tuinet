using System.Buffers;

namespace Tuinet;

internal static class Utf8Write
{
    public static void Bytes(IBufferWriter<byte> writer, ReadOnlySpan<byte> data)
    {
        Span<byte> dest = writer.GetSpan(data.Length);
        data.CopyTo(dest);
        writer.Advance(data.Length);
    }

    public static void Int(IBufferWriter<byte> writer, int value)
    {
        if (value == 0)
        {
            Bytes(writer, "0"u8);
            return;
        }

        Span<byte> tmp = stackalloc byte[10];
        int n = 0;
        int v = value;
        while (v > 0)
        {
            tmp[n++] = (byte)('0' + (v % 10));
            v /= 10;
        }

        Span<byte> dest = writer.GetSpan(n);
        for (int i = 0; i < n; i++)
            dest[i] = tmp[n - 1 - i];
        writer.Advance(n);
    }
}
