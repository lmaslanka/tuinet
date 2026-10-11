using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="Layout"/>: a header, a body split three ways and a footer, each region drawn as a dashed box with its
/// constraint and the size it got. ←/→ change the sidebar's percent, ↑/↓ the spacing.
/// </summary>
public sealed class LayoutPage() : Page("Layout", "Layout · Constraint", Snippet, Keys)
{
    private const string Snippet = """
        Span<Rect> rows = stackalloc Rect[3];
        Layout.Vertical(area, [Constraint.Length(3),
            Constraint.Fill(), Constraint.Length(3)],
            rows, _spacing);

        Span<Rect> cols = stackalloc Rect[3];
        Layout.Horizontal(rows[1], [
            Constraint.Percent(_percent),
            Constraint.Fill(2), Constraint.Fill(1)],
            cols, _spacing);

        Region(buffer, rows[0], "Length(3)", Theme.Blue);
        Region(buffer, cols[0], percent, Theme.Amber);
        Region(buffer, cols[1], "Fill(2)", Theme.Green);
        Region(buffer, cols[2], "Fill(1)", Theme.Violet);
        Region(buffer, rows[2], "Length(3)", Theme.Coral);
        """;

    private static readonly (string, string)[] Keys = [("←/→", "percent"), ("↑/↓", "spacing")];

    private int _percent = 30;
    private int _spacing = 1;

    public int Percent => _percent;
    public int Spacing => _spacing;

    public override bool Handle(Event ev, long nowMs)
    {
        if (ev.Kind != EventKind.Key)
        {
            return false;
        }

        KeyEvent key = ev.Key;
        if (key.Is(KeyCode.Left)) _percent = Math.Max(10, _percent - 5);
        else if (key.Is(KeyCode.Right)) _percent = Math.Min(70, _percent + 5);
        else if (key.Is(KeyCode.Up)) _spacing = Math.Min(3, _spacing + 1);
        else if (key.Is(KeyCode.Down)) _spacing = Math.Max(0, _spacing - 1);
        else return false;

        return true;
    }

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        Span<char> percent = stackalloc char[16];
        int length = new SpanText(percent).Append("Percent(").Append(_percent).Append(')').Text.Length;
        percent = percent[..length];

        Span<Rect> rows = stackalloc Rect[3];
        Layout.Vertical(area, [Constraint.Length(3),
            Constraint.Fill(), Constraint.Length(3)],
            rows, _spacing);

        Span<Rect> cols = stackalloc Rect[3];
        Layout.Horizontal(rows[1], [
            Constraint.Percent(_percent),
            Constraint.Fill(2), Constraint.Fill(1)],
            cols, _spacing);

        Region(buffer, rows[0], "Length(3)", Theme.Blue);
        Region(buffer, cols[0], percent, Theme.Amber);
        Region(buffer, cols[1], "Fill(2)", Theme.Green);
        Region(buffer, cols[2], "Fill(1)", Theme.Violet);
        Region(buffer, rows[2], "Length(3)", Theme.Coral);
    }

    /// <summary>
    /// A rect as a dashed box, with its constraint and the size it got centered inside: on two lines when it's tall
    /// enough, else on one.
    /// </summary>
    private static void Region(CellBuffer buffer, Rect rect, ReadOnlySpan<char> constraint, Color color)
    {
        buffer.Render(new Block { BorderType = BorderType.Dashed, BorderStyle = Theme.Accent(color) }, rect);
        Rect inner = rect.Inset(1, 1);
        if (inner.IsEmpty)
        {
            return;
        }

        var size = new StyledTextBuilder(stackalloc char[16], stackalloc StyledRun[3]);
        size.Append(rect.Width, Theme.Dim);
        size.Append("×", Theme.Faded);
        size.Append(rect.Height, Theme.Dim);
        if (inner.Height >= 2)
        {
            Centered(buffer, inner.Centered(inner.Width, 2), new StyledText(constraint, Theme.Heading(color)), constraint.Length);
            Centered(buffer, inner.Centered(inner.Width, 2).Row(1), size.Build(), size.Length);
            return;
        }

        var label = new StyledTextBuilder(stackalloc char[32], stackalloc StyledRun[2]);
        label.Append(constraint, Theme.Heading(color));
        label.Append("  ");
        Rect line = inner.Centered(Math.Min(label.Length + size.Length, inner.Width), 1);
        int x = buffer.SetText(line.X, line.Y, label.Build(), line.Width, Overflow.Ellipsis);
        buffer.SetText(x, line.Y, size.Build(), line.Right - x, Overflow.Ellipsis);
    }

    /// <summary>Text centered on the first row of <paramref name="row"/> (one column per char here).</summary>
    private static void Centered(CellBuffer buffer, Rect row, StyledText text, int width)
    {
        Rect line = row.Centered(Math.Min(width, row.Width), 1);
        buffer.SetText(line.X, row.Y, text, line.Width, Overflow.Ellipsis);
    }
}
