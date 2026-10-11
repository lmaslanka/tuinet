using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// Text: a line of markup, a line built with <see cref="StyledTextBuilder"/> (numbers formatted in place), and a
/// <see cref="Paragraph"/> that wraps, aligns and scrolls. w cycles the wrap mode, a the alignment, ↑/↓ scroll.
/// </summary>
public sealed class TextPage() : Page("Text", "Paragraph · StyledText · Markup", Snippet, Keys)
{
    private const string Snippet = """
        buffer.SetMarkup(area.X, area.Y,
            "[b fg=#38BDF8]bold[/] "
            + "[i]italic[/] [u]underline[/] [s]strike[/] "
            + "[reverse] reverse [/] [fg=#F5A623]amber[/]",
            Theme.Body, area.Width);

        var status = new StyledTextBuilder(
            stackalloc char[48], stackalloc StyledRun[4]);
        status.Append(WrapNames[(int)_wrap], Theme.Strong);
        status.Append(" wrap · line ", Theme.Dim);
        status.Append(_scroll + 1, Theme.Strong);
        buffer.SetText(area.X, area.Y + 1, status.Build());

        buffer.Render(new Paragraph(Text, Theme.Body)
        {
            Wrap = _wrap,
            Alignment = _alignment,
            Scroll = _scroll,
            Scrollbar = ScrollbarMode.Auto,
            ScrollbarThumbStyle = Theme.Accent(Theme.Muted),
        }, text);
        """;

    private static readonly (string, string)[] Keys = [("w", "wrap"), ("a", "align"), ("↑/↓", "scroll")];
    private static readonly string[] WrapNames = ["none", "word", "char"];

    private const string Text =
        "Immediate mode: there is no widget tree to keep in sync. Each frame, your app draws its state into a cell "
        + "buffer, and TUI.NET diffs that buffer against the screen and writes the difference.\n\n"
        + "A paragraph wraps at spaces (word), anywhere (char), or not at all (none), and aligns each line left, "
        + "center or right. Words longer than a line, like supercalifragilisticexpialidocious, are broken mid-word.\n\n"
        + "Scroll counts visual lines, so a wrapped paragraph scrolls row by row. With Scrollbar = Auto the bar shows "
        + "only while the text is longer than the box, and the text wraps one column narrower while it does.\n\n"
        + "Styled text keeps its runs across wrapped lines; markup and the builder both write into memory you give "
        + "them, so drawing this page every frame allocates nothing.";

    private TextWrap _wrap = TextWrap.Word;
    private Alignment _alignment;
    private int _scroll;

    public TextWrap Wrap => _wrap;
    public int Scroll => _scroll;

    public override bool Handle(Event ev, long nowMs)
    {
        if (ev.Kind != EventKind.Key)
        {
            return false;
        }

        KeyEvent key = ev.Key;
        if (key.IsChar('w')) _wrap = (TextWrap)(((int)_wrap + 1) % 3);
        else if (key.IsChar('a')) _alignment = (Alignment)(((int)_alignment + 1) % 3);
        else if (key.Is(KeyCode.Up)) _scroll--;
        else if (key.Is(KeyCode.Down)) _scroll++;   // clamped when drawn, where the width is known
        else return false;

        return true;
    }

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        // The paragraph's box below the two styled lines; its text wraps a column narrower while the bar shows.
        var box = new Rect(area.X, area.Y + 3, area.Width, Math.Max(0, area.Height - 3));
        var block = new Block { BorderType = BorderType.Rounded, BorderStyle = Theme.Faded };
        Rect text = block.Inner(box).Inset(1, 0);
        int lines = Paragraph.LineCount(Text, text.Width, _wrap);
        if (lines > text.Height)
        {
            lines = Paragraph.LineCount(Text, text.Width - 1, _wrap);
        }

        _scroll = Math.Clamp(_scroll, 0, Math.Max(0, lines - text.Height));
        buffer.Render(block, box);

        buffer.SetMarkup(area.X, area.Y,
            "[b fg=#38BDF8]bold[/] "
            + "[i]italic[/] [u]underline[/] [s]strike[/] "
            + "[reverse] reverse [/] [fg=#F5A623]amber[/]",
            Theme.Body, area.Width);

        var status = new StyledTextBuilder(
            stackalloc char[48], stackalloc StyledRun[4]);
        status.Append(WrapNames[(int)_wrap], Theme.Strong);
        status.Append(" wrap · line ", Theme.Dim);
        status.Append(_scroll + 1, Theme.Strong);
        buffer.SetText(area.X, area.Y + 1, status.Build());

        buffer.Render(new Paragraph(Text, Theme.Body)
        {
            Wrap = _wrap,
            Alignment = _alignment,
            Scroll = _scroll,
            Scrollbar = ScrollbarMode.Auto,
            ScrollbarThumbStyle = Theme.Accent(Theme.Muted),
        }, text);
    }
}
