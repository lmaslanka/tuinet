namespace Tuinet;

public enum ColorMode : byte
{
    /// <summary>24-bit RGB (<c>38;2;r;g;b</c>).</summary>
    TrueColor,

    /// <summary>xterm 256-color palette; RGB is mapped to the nearest cube or gray entry.</summary>
    Indexed256,

    /// <summary>The 16 ANSI colors only.</summary>
    Basic16,

    /// <summary>No colors (attributes such as bold and reverse still apply).</summary>
    None,
}

public sealed record TerminalOptions
{
    /// <summary>Report mouse clicks, drags and wheel as <see cref="EventKind.Mouse"/> (SGR 1006).</summary>
    public bool Mouse { get; init; }

    /// <summary>With <see cref="Mouse"/>: also report motion with no button held.</summary>
    public bool MouseMotion { get; init; }

    /// <summary>Deliver pastes as one <see cref="EventKind.Paste"/> event instead of a key per character.</summary>
    public bool BracketedPaste { get; init; }

    /// <summary>Report <see cref="EventKind.FocusGained"/> / <see cref="EventKind.FocusLost"/>.</summary>
    public bool FocusEvents { get; init; }

    /// <summary>Color depth to emit. <c>null</c>: detect from the environment in <see cref="Terminal.Open"/>, TrueColor otherwise.</summary>
    public ColorMode? ColorMode { get; init; }

    /// <summary>How long a lone ESC waits for the rest of an escape sequence before it is the Escape key.</summary>
    public int EscapeTimeoutMs { get; init; } = 20;

    /// <summary>
    /// When a band of full-width rows scrolls, let the terminal move them (scroll margins plus
    /// insert/delete line) instead of repainting every row. Turn off for a terminal that mishandles them.
    /// </summary>
    public bool ScrollRegions { get; init; } = true;

    /// <summary>Best guess at the terminal's color depth from NO_COLOR, COLORTERM, TERM_PROGRAM, WT_SESSION and TERM.</summary>
    public static ColorMode DetectColorMode()
    {
        if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable("NO_COLOR")))
        {
            return Tuinet.ColorMode.None;
        }

        string colorTerm = Environment.GetEnvironmentVariable("COLORTERM") ?? "";
        if (colorTerm is "truecolor" or "24bit")
        {
            return Tuinet.ColorMode.TrueColor;
        }

        if (OperatingSystem.IsWindows() || Environment.GetEnvironmentVariable("WT_SESSION") is not null)
        {
            return Tuinet.ColorMode.TrueColor;
        }

        switch (Environment.GetEnvironmentVariable("TERM_PROGRAM"))
        {
            case "Apple_Terminal":
                return Tuinet.ColorMode.Indexed256;
            case "iTerm.app" or "WezTerm" or "vscode" or "ghostty":
                return Tuinet.ColorMode.TrueColor;
        }

        string term = Environment.GetEnvironmentVariable("TERM") ?? "";
        if (term.Length == 0)
        {
            return Tuinet.ColorMode.TrueColor;
        }

        if (term == "dumb")
        {
            return Tuinet.ColorMode.None;
        }

        foreach (string modern in (ReadOnlySpan<string>)["kitty", "alacritty", "foot", "ghostty", "wezterm", "contour", "direct", "truecolor"])
        {
            if (term.Contains(modern, StringComparison.Ordinal))
            {
                return Tuinet.ColorMode.TrueColor;
            }
        }

        if (term.Contains("256color", StringComparison.Ordinal) || term.StartsWith("xterm", StringComparison.Ordinal))
        {
            return Tuinet.ColorMode.Indexed256;
        }

        return Tuinet.ColorMode.Basic16;
    }
}
