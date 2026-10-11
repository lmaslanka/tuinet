namespace Tuinet.Screenshots;

/// <summary>
/// The look of a screenshot. Sizes are in pixels at <see cref="Scale"/> 1 (the defaults match the README images: a
/// 15px JetBrains Mono on a 9×20 cell grid); <see cref="Scale"/> multiplies them all for high-density images.
/// </summary>
public sealed record ScreenshotOptions
{
    public int Scale { get; init; } = 1;

    public float FontSize { get; init; } = 15;
    public int CellWidth { get; init; } = 9;
    public int CellHeight { get; init; } = 20;

    /// <summary>Thickness of light box-drawing lines; heavy lines are twice that.</summary>
    public int LineWidth { get; init; } = 2;

    /// <summary>Draw the window: backdrop, rounded frame, traffic-light dots and title. False: just the cell grid.</summary>
    public bool Window { get; init; } = true;

    /// <summary>Backdrop around the window.</summary>
    public int Margin { get; init; } = 22;

    /// <summary>Window edge to the grid on the left and right.</summary>
    public int Padding { get; init; } = 22;

    /// <summary>Window top edge to the grid: the title bar.</summary>
    public int TitleBarHeight { get; init; } = 45;

    /// <summary>Grid to the window's bottom edge.</summary>
    public int PaddingBottom { get; init; } = 33;

    public int CornerRadius { get; init; } = 12;

    /// <summary>The terminal's default colors, for cells with <see cref="Color.Default"/>; also the window's fill.</summary>
    public Rgb Foreground { get; init; } = Rgb.Hex(0xE2E8F0);
    public Rgb Background { get; init; } = Rgb.Hex(0x0D1117);

    /// <summary>The first 16 palette colors; 16-255 are the standard xterm cube and greys.</summary>
    public IReadOnlyList<Rgb> Ansi { get; init; } = Palette.GitHubDark;

    public Rgb Cursor { get; init; } = Rgb.Hex(0xE2E8F0);
    public Rgb Backdrop { get; init; } = Rgb.Hex(0x05070B);
    public Rgb Border { get; init; } = Rgb.Hex(0x232936);
    public Rgb Title { get; init; } = Rgb.Hex(0x7D8696);

    /// <summary>The close, minimize and zoom dots.</summary>
    public IReadOnlyList<Rgb> Dots { get; init; } = [Rgb.Hex(0xF87171), Rgb.Hex(0xF5A623), Rgb.Hex(0x34D399)];

    /// <summary>How far <see cref="Attr.Dim"/> moves text toward its background (0: not at all, 1: invisible).</summary>
    public float Dim { get; init; } = 0.4f;
}
