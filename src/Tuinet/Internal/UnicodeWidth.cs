using System.Runtime.CompilerServices;

namespace Tuinet;

/// <summary>
/// O(1) per-code-point properties from a two-stage table in UnicodeWidth.g.cs: terminal column width (0, 1 or 2)
/// and whether a code point can never join a neighbour into one grapheme cluster (see <see cref="Graphemes.Simple"/>).
/// One lookup (<see cref="Info"/>) gives both.
/// </summary>
internal static partial class UnicodeWidth
{
    /// <summary>Mask of the width in an <see cref="Info"/> value.</summary>
    public const int WidthMask = 3;

    /// <summary>Bit set in an <see cref="Info"/> value for a code point that never joins (see <see cref="Graphemes.Simple"/>).</summary>
    public const int NeverJoins = 4;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Of(int codePoint)
    {
        if ((uint)(codePoint - 0x20) < 0x5F)
        {
            return 1;
        }

        return Lookup((uint)codePoint) & WidthMask;
    }

    /// <summary>
    /// Width (bits 0-1) and <see cref="NeverJoins"/> (bit 2) of a code point or UTF-16 code unit. Surrogates and
    /// anything out of range are width 0 and may join.
    /// </summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static int Info(int codePoint) => Lookup((uint)codePoint);

    private static int Lookup(uint cp)
    {
        if (cp >= 0x110000)
        {
            return 0;
        }

        int block = Stage1[(int)(cp >> 8)];
        int packed = Stage2[(block << 7) | (int)((cp & 0xFF) >> 1)];
        return (packed >> (int)((cp & 1) << 2)) & 0xF;
    }
}
