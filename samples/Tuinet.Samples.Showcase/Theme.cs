namespace Tuinet.Samples.Showcase;

/// <summary>
/// Dark "engineering diagram" palette: near-black navy, light-gray text, muted-gray secondary
/// text, and saturated accents used for borders, headings and status.
/// </summary>
internal static class Theme
{
    public static readonly Color Bg = Color.Hex(0x0D1117);
    public static readonly Color Panel = Color.Hex(0x111722);
    public static readonly Color Raised = Color.Hex(0x1C2433);
    public static readonly Color Text = Color.Hex(0xE2E8F0);
    public static readonly Color Muted = Color.Hex(0x7D8696);
    public static readonly Color Faint = Color.Hex(0x3A4252);
    public static readonly Color Green = Color.Hex(0x34D399);
    public static readonly Color Amber = Color.Hex(0xF5A623);
    public static readonly Color Blue = Color.Hex(0x38BDF8);
    public static readonly Color Violet = Color.Hex(0xA78BFA);
    public static readonly Color Coral = Color.Hex(0xF87171);
    public static readonly Color Pink = Color.Hex(0xEC4899);

    public static readonly Style Screen = new(Text, Bg);
    public static readonly Style Dialog = new(Text, Panel);
    public static readonly Style Body = new(Text, default);
    public static readonly Style Dim = new(Muted, default);
    public static readonly Style Faded = new(Faint, default);
    public static readonly Style Strong = new(Text, default, Attr.Bold);

    /// <summary>Selected list row: lifted background, keeps each column's own color.</summary>
    public static readonly Style RowSelected = new(default, Raised, Attr.Bold);

    public static Style Accent(Color color) => new(color, default);
    public static Style Heading(Color color) => new(color, default, Attr.Bold);

    public static Color PriorityColor(int priority) => priority switch
    {
        0 => Muted,
        1 => Blue,
        2 => Amber,
        _ => Coral,
    };

    public static Color KindColor(int kind) => kind switch
    {
        0 => Green,
        1 => Coral,
        2 => Muted,
        3 => Violet,
        _ => Pink,
    };
}
