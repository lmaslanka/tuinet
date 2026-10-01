namespace Tuinet.Widgets;

/// <summary>
/// A checkbox: symbol, space, label on one row; or, with <see cref="Boxed"/>, a 5×3 rounded box
/// (about square, since cells are twice as tall as wide) filled when checked, label to its right.
/// </summary>
public readonly ref struct Checkbox : IWidget
{
    public Checkbox(ReadOnlySpan<char> label, bool isChecked)
    {
        Label = label;
        Checked = isChecked;
    }

    public ReadOnlySpan<char> Label { get; init; }
    public bool Checked { get; init; }
    public bool Focused { get; init; }
    public Style Style { get; init; }

    /// <summary>Layered over <see cref="Style"/> when focused.</summary>
    public Style FocusedStyle { get; init; }

    /// <summary>Layered over the symbol when checked.</summary>
    public Style CheckedStyle { get; init; }

    /// <summary>Layered over the symbol when unchecked (e.g. a dim fill for an empty square).</summary>
    public Style UncheckedStyle { get; init; }

    public ReadOnlySpan<char> CheckedSymbol { get; init; } = "[x]";
    public ReadOnlySpan<char> UncheckedSymbol { get; init; } = "[ ]";

    /// <summary>Draw a large 5×3 box instead of a one-row symbol (needs an area at least 3 rows tall).</summary>
    public bool Boxed { get; init; }

    /// <summary>Boxed: what fills the three interior cells when checked.</summary>
    public ReadOnlySpan<char> BoxedSymbol { get; init; } = "███";

    /// <summary>Boxed: border style; <see cref="CheckedStyle"/> or <see cref="FocusedStyle"/> is layered over it.</summary>
    public Style BorderStyle { get; init; }

    public void Render(Rect area, CellBuffer buffer)
    {
        if (area.IsEmpty)
        {
            return;
        }

        Style style = Focused ? Style.Patch(FocusedStyle) : Style;
        if (Boxed && area.Height >= 3 && area.Width >= 5)
        {
            RenderBoxed(area, buffer, style);
            return;
        }

        Style symbolStyle = style.Patch(Checked ? CheckedStyle : UncheckedStyle);
        int right = area.Right;
        int x = buffer.SetString(area.X, area.Y, Checked ? CheckedSymbol : UncheckedSymbol, symbolStyle, right - area.X);
        x = buffer.SetString(x, area.Y, " ", style, right - x);
        buffer.SetString(x, area.Y, Label, style, right - x, Overflow.Ellipsis);
    }

    private void RenderBoxed(Rect area, CellBuffer buffer, Style labelStyle)
    {
        var box = new Rect(area.X, area.Y, 5, 3);
        Style border = Focused ? BorderStyle.Patch(FocusedStyle) : Checked ? BorderStyle.Patch(CheckedStyle) : BorderStyle;
        buffer.Render(new Block { BorderType = BorderType.Rounded, BorderStyle = border }, box);
        if (Checked)
        {
            buffer.SetString(box.X + 1, box.Y + 1, BoxedSymbol, BorderStyle.Patch(CheckedStyle), 3);
        }

        int x = box.Right + 1;
        buffer.SetString(x, box.Y + 1, Label, labelStyle, area.Right - x, Overflow.Ellipsis);
    }
}
