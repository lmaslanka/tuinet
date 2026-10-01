namespace Tuinet.Widgets;

/// <summary>A one-row button: the label padded by two cells each side, filled with its style.</summary>
public readonly ref struct Button : IWidget
{
    public Button(ReadOnlySpan<char> label) => Label = label;

    public ReadOnlySpan<char> Label { get; init; }
    public Style Style { get; init; }
    public Style FocusedStyle { get; init; }
    public bool Focused { get; init; }

    /// <summary>Columns a button with <paramref name="label"/> occupies.</summary>
    public static int WidthOf(ReadOnlySpan<char> label) => TextWidth.Of(label) + 4;

    public void Render(Rect area, CellBuffer buffer)
    {
        if (area.IsEmpty)
        {
            return;
        }

        Style style = Focused ? FocusedStyle : Style;
        var row = new Rect(area.X, area.Y, Math.Min(area.Width, WidthOf(Label)), 1);
        buffer.Fill(row, style);
        buffer.SetString(row.X + 2, row.Y, Label, style, row.Width - 2);
    }
}
