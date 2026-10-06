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

    /// <summary>The rune field holds an interned multi-code-point cluster id (see <see cref="Graphemes"/>).</summary>
    Grapheme = 2,
}

/// <summary>
/// One terminal cell: glyph, style, display width, hyperlink. Exactly 16 bytes, blittable and canonical,
/// so the renderer compares whole rows with a vectorized memcmp.
/// </summary>
[StructLayout(LayoutKind.Sequential, Size = 16)]
public readonly struct Cell : IEquatable<Cell>
{
    // The width and the flags use the low 2 bits of their bytes; the upper 6 bits of both hold a 12-bit link id.
    private readonly Rune _rune;
    private readonly Style _style;
    private readonly byte _width;
    private readonly byte _flags;

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
        _flags = 0;
    }

    internal Cell(Rune rune, Style style, int width, CellFlags flags)
    {
        _rune = rune;
        _style = style;
        _width = (byte)width;
        _flags = (byte)flags;
    }

    internal static readonly Rune Space = new(' ');

    public static Cell Empty { get; } = new(Space, default, 1, CellFlags.None);

    public static Cell Blank(Style style) => new(Space, style, 1, CellFlags.None);

    internal static Cell Continuation(Style style) => new(default, style, 0, CellFlags.Continuation);

    /// <summary>Make a template cell (no flags) hold grapheme cluster <paramref name="id"/>, in place.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void PatchGrapheme(ref Cell cell, int id, int width)
    {
        Unsafe.As<Cell, int>(ref cell) = id;
        ref byte bytes = ref Unsafe.As<Cell, byte>(ref cell);
        Unsafe.Add(ref bytes, WidthOffset) = (byte)width;
        Unsafe.Add(ref bytes, WidthOffset + 1) = (byte)CellFlags.Grapheme;
    }

    /// <summary>Overwrite the glyph of a narrow template cell in place (hot path for text writes).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void Patch(ref Cell cell, Rune rune, int width)
    {
        Unsafe.As<Cell, Rune>(ref cell) = rune;
        Unsafe.Add(ref Unsafe.As<Cell, byte>(ref cell), WidthOffset) = (byte)width;
    }

    /// <summary>Make <paramref name="cell"/> a hyperlink to <see cref="Links"/> entry <paramref name="id"/> (0: none), in place.</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static void SetLink(ref Cell cell, int id)
    {
        ref byte width = ref Unsafe.Add(ref Unsafe.As<Cell, byte>(ref cell), WidthOffset);
        ref byte flags = ref Unsafe.Add(ref width, 1);
        width = (byte)((width & LowBits) | ((id & 0x3F) << 2));
        flags = (byte)((flags & LowBits) | ((id >> 6) << 2));
    }

    private const int WidthOffset = 14;
    private const int ColorsOffset = 4;
    private const int LowBits = 0x03;

    /// <summary>Foreground and background of a cell as one 8-byte value (cheap equality on hot paths).</summary>
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static ulong ColorBits(ref Cell cell) =>
        Unsafe.ReadUnaligned<ulong>(ref Unsafe.Add(ref Unsafe.As<Cell, byte>(ref cell), ColorsOffset));

    /// <summary>The glyph; for a multi-code-point cluster, its first code point (see <see cref="Text"/>).</summary>
    public Rune Rune => IsGrapheme ? Graphemes.First(GraphemeId) : _rune;

    /// <summary>The cell's text: a whole grapheme cluster (e.g. "👨‍👩‍👧"), or its single rune.</summary>
    public string Text => IsGrapheme ? Graphemes.Text(GraphemeId) : _rune.ToString();

    /// <summary>Holds a cluster of several code points (combining marks, emoji sequences, flags).</summary>
    public bool IsGrapheme => (_flags & (byte)CellFlags.Grapheme) != 0;

    internal int GraphemeId => _rune.Value;

    /// <summary>The rune field as stored: a code point, or a cluster id when <see cref="IsGrapheme"/>.</summary>
    internal Rune RawRune => _rune;
    public Style Style => _style;

    /// <summary>Columns this cell's glyph occupies: 1 or 2, or 0 for the right half of a wide glyph.</summary>
    public int Width => _width & LowBits;

    public bool IsContinuation => (_flags & (byte)CellFlags.Continuation) != 0;

    internal CellFlags Flags => (CellFlags)(_flags & LowBits);

    /// <summary>The hyperlink this cell belongs to (OSC 8), or null. See <see cref="CellBuffer.SetLink"/>.</summary>
    public string? Link => LinkId == 0 ? null : Links.Url(LinkId);

    /// <summary>Id of <see cref="Link"/> in <see cref="Links"/>, or 0.</summary>
    internal int LinkId => (_width >> 2) | ((_flags >> 2) << 6);

    /// <summary>
    /// The width and flags bytes as one value, for hot-path tests: it equals <see cref="PlainNarrow"/> for a narrow
    /// single-code-point glyph with no link.
    /// </summary>
    internal ushort Tail
    {
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        get => Unsafe.ReadUnaligned<ushort>(ref Unsafe.Add(ref Unsafe.As<Cell, byte>(ref Unsafe.AsRef(in this)), WidthOffset));
    }

    internal static ushort PlainNarrow => BitConverter.IsLittleEndian ? (ushort)0x0001 : (ushort)0x0100;

    public Cell WithStyle(Style style) => new(_rune, style, _width, (CellFlags)_flags);

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

    public override string ToString() => IsContinuation ? "<cont>" : $"'{Text}' {_style}";
}
