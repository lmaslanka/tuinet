using System.Runtime.CompilerServices;

namespace Tuinet;

/// <summary>
/// Growable output byte buffer with unchecked-style appends. Callers reserve capacity up front
/// (per row in the renderer), so steady-state frames never allocate.
/// </summary>
internal sealed class VtBuffer
{
    private byte[] _data;
    private int _length;

    public VtBuffer(int capacity) => _data = new byte[Math.Max(256, capacity)];

    public int Length => _length;
    public ReadOnlySpan<byte> Written => _data.AsSpan(0, _length);
    public int Capacity => _data.Length;

    public void Clear() => _length = 0;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Reserve(int extra)
    {
        if (_length + extra > _data.Length)
        {
            Grow(extra);
        }
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Byte(byte b) => _data[_length++] = b;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public void Bytes(ReadOnlySpan<byte> bytes)
    {
        bytes.CopyTo(_data.AsSpan(_length));
        _length += bytes.Length;
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    public void Rune(System.Text.Rune rune) => _length += rune.EncodeToUtf8(_data.AsSpan(_length));

    /// <summary>Append a non-negative integer in decimal.</summary>
    public void Int(int value)
    {
        if ((uint)value < 10)
        {
            _data[_length++] = (byte)('0' + value);
            return;
        }

        if ((uint)value < 256)
        {
            Bytes(Decimal(value));
            return;
        }

        Span<byte> tmp = stackalloc byte[10];
        int n = 0;
        uint v = (uint)value;
        do
        {
            tmp[n++] = (byte)('0' + v % 10);
            v /= 10;
        }
        while (v != 0);

        while (n > 0)
        {
            _data[_length++] = tmp[--n];
        }
    }

    [MethodImpl(MethodImplOptions.NoInlining)]
    private void Grow(int extra) =>
        Array.Resize(ref _data, Math.Max(_data.Length * 2, _length + extra));

    // "0".."255" packed 3 bytes each, left-aligned; length from value. Color channels hit this path.
    private static readonly byte[] DecimalTable = CreateDecimalTable();

    private static ReadOnlySpan<byte> Decimal(int value)
    {
        int length = value < 10 ? 1 : value < 100 ? 2 : 3;
        return DecimalTable.AsSpan(value * 3, length);
    }

    private static byte[] CreateDecimalTable()
    {
        var table = new byte[256 * 3];
        for (int i = 0; i < 256; i++)
        {
            string s = i.ToString(System.Globalization.CultureInfo.InvariantCulture);
            for (int j = 0; j < s.Length; j++)
            {
                table[i * 3 + j] = (byte)s[j];
            }
        }

        return table;
    }
}
