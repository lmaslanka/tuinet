using System.Globalization;
using System.Text;

namespace Tuinet;

public readonly struct Cell : IEquatable<Cell>
{
    public Rune Rune { get; }
    public Style Style { get; }
    public bool IsContinuation { get; }

    public const int ZeroWidth = 0;
    public const int NarrowWidth = 1;
    public const int WideWidth = 2;

    public static Cell Empty { get; } = new(new Rune(' '), Style.Default);

    public Cell(Rune rune, Style style, bool isContinuation = false)
    {
        Rune = rune;
        Style = style;
        IsContinuation = isContinuation;
    }

    public int Width => IsContinuation ? ZeroWidth : WidthOf(Rune);

    public static int WidthOf(Rune rune)
    {
        uint u = (uint)rune.Value;
        if (u == 0 || u < 0x20 || (u >= 0x7F && u < 0xA0))
        {
            return ZeroWidth;
        }

        var cat = Rune.GetUnicodeCategory(rune);
        if (cat is UnicodeCategory.NonSpacingMark or UnicodeCategory.EnclosingMark)
        {
            return ZeroWidth;
        }

        return IsWide(u) ? WideWidth : NarrowWidth;
    }

    private static readonly uint[] WideRanges =
    [
            0x1100, 0x115F,
            0x231A, 0x231B,
            0x2329, 0x232A,
            0x23E9, 0x23EC,
            0x23F0, 0x23F0,
            0x23F3, 0x23F3,
            0x25FD, 0x25FE,
            0x2614, 0x2615,
            0x2648, 0x2653,
            0x267F, 0x267F,
            0x2693, 0x2693,
            0x26A1, 0x26A1,
            0x26AA, 0x26AB,
            0x26BD, 0x26BE,
            0x26C4, 0x26C5,
            0x26CE, 0x26CE,
            0x26D4, 0x26D4,
            0x26EA, 0x26EA,
            0x26F2, 0x26F3,
            0x26F5, 0x26F5,
            0x26FA, 0x26FA,
            0x26FD, 0x26FD,
            0x2705, 0x2705,
            0x270A, 0x270B,
            0x2728, 0x2728,
            0x274C, 0x274C,
            0x274E, 0x274E,
            0x2753, 0x2755,
            0x2757, 0x2757,
            0x2795, 0x2797,
            0x27B0, 0x27B0,
            0x27BF, 0x27BF,
            0x2B1B, 0x2B1C,
            0x2B50, 0x2B50,
            0x2B55, 0x2B55,
            0x2E80, 0x2E99,
            0x2E9B, 0x2EF3,
            0x2F00, 0x2FD5,
            0x2FF0, 0x2FFB,
            0x3000, 0x303E,
            0x3041, 0x3096,
            0x3099, 0x30FF,
            0x3105, 0x312F,
            0x3131, 0x318E,
            0x3190, 0x31E3,
            0x31F0, 0x321E,
            0x3220, 0x3247,
            0x3250, 0x4DBF,
            0x4E00, 0xA48C,
            0xA490, 0xA4C6,
            0xA960, 0xA97C,
            0xAC00, 0xD7A3,
            0xF900, 0xFAFF,
            0xFE10, 0xFE19,
            0xFE30, 0xFE52,
            0xFE54, 0xFE66,
            0xFE68, 0xFE6B,
            0xFF01, 0xFF60,
            0xFFE0, 0xFFE6,
            0x16FE0, 0x16FE4,
            0x16FF0, 0x16FF1,
            0x1B000, 0x1B122,
            0x1F200, 0x1F202,
            0x1F210, 0x1F23B,
            0x1F240, 0x1F248,
            0x1F250, 0x1F251,
            0x1F300, 0x1F64F,
            0x1F900, 0x1F9FF,
            0x20000, 0x2FFFD,
            0x30000, 0x3FFFD,
    ];

    private static bool IsWide(uint u)
    {
        uint[] ranges = WideRanges;
        int lo = 0;
        int hi = ranges.Length / 2 - 1;
        while (lo <= hi)
        {
            int mid = (lo + hi) >>> 1;
            uint min = ranges[mid * 2];
            uint max = ranges[mid * 2 + 1];
            if (u < min)
            {
                hi = mid - 1;
            }
            else if (u > max)
            {
                lo = mid + 1;
            }
            else
            {
                return true;
            }
        }

        return false;
    }

    public bool Equals(Cell other) =>
        Rune == other.Rune && Style.Equals(other.Style) && IsContinuation == other.IsContinuation;
    public override bool Equals(object? obj) => obj is Cell other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Rune, Style, IsContinuation);

    public static bool operator ==(Cell left, Cell right) => left.Equals(right);
    public static bool operator !=(Cell left, Cell right) => !left.Equals(right);
}
