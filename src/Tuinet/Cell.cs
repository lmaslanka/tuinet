using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace Tuinet;

[Flags]
internal enum CellFlags : byte
{
    None = 0,

    /// <summary>Right half of a wide glyph; the glyph lives in the cell to the left.</summary>
    Continuation = 1,

    /// <summary>Reserved: rune field indexes a per-frame grapheme-cluster arena.</summary>
    Grapheme = 2,
}

/// <summary>
/// One terminal cell: glyph, style, display width. Exactly 16 bytes, blittable and canonical,
/// so the renderer compares whole rows with a vectorized memcmp.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct Cell : IEquatable<Cell>
{
    private readonly Rune _rune;
    private readonly Style _style;
    private readonly byte _width;
    private readonly CellFlags _flags;

    /// <summary>A cell holding <paramref name="rune"/>. Zero-width runes (controls, combining marks) become a space.</summary>
    public Cell(Rune rune, Style style = default)
    {
        int width = UnicodeWidth.Of(rune.Value);
        if (width == 0)
        {
            rune = Space;
            width = 1;
        }

        _rune = rune;
        _style = style;
        _width = (byte)width;
        _flags = CellFlags.None;
    }

    internal Cell(Rune rune, Style style, int width, CellFlags flags)
    {
        _rune = rune;
        _style = style;
        _width = (byte)width;
        _flags = flags;
    }

    internal static readonly Rune Space = new(' ');

    public static Cell Empty { get; } = new(Space, default, 1, CellFlags.None);

    public static Cell Blank(Style style) => new(Space, style, 1, CellFlags.None);

    internal static Cell Continuation(Style style) => new(default, style, 0, CellFlags.Continuation);

    /// <summary>Overwrite the glyph of a narrow template cell in place (hot path for text writes).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Patch(ref Cell cell, Rune rune, int width)
    {
        Unsafe.As<Cell, Rune>(ref cell) = rune;
        Unsafe.Add(ref Unsafe.As<Cell, byte>(ref cell), WidthOffset) = (byte)width;
    }

    private const int WidthOffset = 14;

    public Rune Rune => _rune;
    public Style Style => _style;

    /// <summary>Columns this cell's glyph occupies: 1 or 2, or 0 for the right half of a wide glyph.</summary>
    public int Width => _width;

    public bool IsContinuation => (_flags & CellFlags.Continuation) != 0;

    internal CellFlags Flags => _flags;

    public Cell WithStyle(Style style) => new(_rune, style, _width, _flags);

    public bool Equals(Cell other)
    {
        ref ulong a = ref Unsafe.As<Cell, ulong>(ref Unsafe.AsRef(in this));
        ref ulong b = ref Unsafe.As<Cell, ulong>(ref Unsafe.AsRef(in other));
        return a == b && Unsafe.Add(ref a, 1) == Unsafe.Add(ref b, 1);
    }

    public override bool Equals(object? obj) => obj is Cell other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(_rune, _style, _width, _flags);
    public static bool operator ==(Cell left, Cell right) => left.Equals(right);
    public static bool operator !=(Cell left, Cell right) => !left.Equals(right);

    public override string ToString() => IsContinuation ? "<cont>" : $"'{_rune}' {_style}";
}
