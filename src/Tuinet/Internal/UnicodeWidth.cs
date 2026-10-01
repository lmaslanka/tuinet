using System.Runtime.CompilerServices;

namespace Tuinet;

/// <summary>O(1) terminal column width of a code point: 0, 1 or 2. Tables in UnicodeWidth.g.cs.</summary>
internal static partial class UnicodeWidth
{
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Of(int codePoint)
    {
        if ((uint)(codePoint - 0x20) < 0x5F)
        {
            return 1;
        }

        return Lookup((uint)codePoint);
    }

    private static int Lookup(uint cp)
    {
        if (cp >= 0x110000)
        {
            return 0;
        }

        int block = Stage1[(int)(cp >> 8)];
        int packed = Stage2[(block << 6) | (int)((cp & 0xFF) >> 2)];
        return (packed >> (int)((cp & 3) << 1)) & 3;
    }
}
