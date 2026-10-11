using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="Block"/>: every border type, and one with only its top and bottom edges, each showing the size of its
/// <see cref="Block.Inner"/>. ←/→ move the titles and footers left, center or right; space shows the footers.
/// </summary>
public sealed class BlockPage() : Page("Block", "Block · BorderType", Snippet, Keys)
{
    private const string Snippet = """
        for (int i = 0; i < cells.Length; i++)
        {
            var block = new Block
            {
                Borders = Edges[i],
                BorderType = Types[i],
                BorderStyle = Theme.Accent(Colors[i]),
                Title = Names[i],
                TitleAlignment = _alignment,
                Footer = _footers ? " footer " : "",
                FooterAlignment = _alignment,
            };
            buffer.Render(block, cells[i]);
            InnerSize(buffer, block.Inner(cells[i]));
        }
        """;

    private static readonly (string, string)[] Keys = [("←/→", "align titles"), ("space", "footers")];

    private static readonly BorderType[] Types =
        [BorderType.Plain, BorderType.Rounded, BorderType.Double, BorderType.Thick, BorderType.Dashed, BorderType.Plain];

    private static readonly Borders[] Edges =
        [Borders.All, Borders.All, Borders.All, Borders.All, Borders.All, Borders.Top | Borders.Bottom];

    private static readonly string[] Names = [" plain ", " rounded ", " double ", " thick ", " dashed ", " top+bottom "];
    private static readonly Color[] Colors = [Theme.Muted, Theme.Blue, Theme.Violet, Theme.Amber, Theme.Green, Theme.Coral];

    private Alignment _alignment;
    private bool _footers = true;

    public Alignment TitleAlignment => _alignment;

    public override bool Handle(Event ev, long nowMs)
    {
        if (ev.Kind != EventKind.Key)
        {
            return false;
        }

        KeyEvent key = ev.Key;
        if (key.Is(KeyCode.Left)) _alignment = (Alignment)Math.Max(0, (int)_alignment - 1);
        else if (key.Is(KeyCode.Right)) _alignment = (Alignment)Math.Min(2, (int)_alignment + 1);
        else if (key.IsChar(' ')) _footers = !_footers;
        else return false;

        return true;
    }

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        // Two rows of three.
        Span<Rect> rows = stackalloc Rect[2];
        Layout.Vertical(area, [Constraint.Fill(), Constraint.Fill()], rows, spacing: 1);
        Span<Rect> cells = stackalloc Rect[6];
        Layout.Horizontal(rows[0], [Constraint.Fill(), Constraint.Fill(), Constraint.Fill()], cells[..3], spacing: 1);
        Layout.Horizontal(rows[1], [Constraint.Fill(), Constraint.Fill(), Constraint.Fill()], cells[3..], spacing: 1);

        for (int i = 0; i < cells.Length; i++)
        {
            var block = new Block
            {
                Borders = Edges[i],
                BorderType = Types[i],
                BorderStyle = Theme.Accent(Colors[i]),
                Title = Names[i],
                TitleAlignment = _alignment,
                Footer = _footers ? " footer " : "",
                FooterAlignment = _alignment,
            };
            buffer.Render(block, cells[i]);
            InnerSize(buffer, block.Inner(cells[i]));
        }
    }

    /// <summary>"inner 12×5", centered in the block.</summary>
    private static void InnerSize(CellBuffer buffer, Rect inner)
    {
        if (inner.IsEmpty)
        {
            return;
        }

        var label = new StyledTextBuilder(stackalloc char[24], stackalloc StyledRun[4]);
        label.Append("inner ", Theme.Faded);
        label.Append(inner.Width, Theme.Dim);
        label.Append("×", Theme.Faded);
        label.Append(inner.Height, Theme.Dim);
        Rect line = inner.Centered(Math.Min(label.Length, inner.Width), 1);
        buffer.SetText(line.X, line.Y, label.Build(), line.Width);
    }
}
