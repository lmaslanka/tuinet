namespace Tuinet.Widgets;

/// <summary>Items for a <see cref="ListView{TSource}"/>. Only visible rows are rendered (virtualized).</summary>
public interface IListSource
{
    int Count { get; }

    /// <summary>Draw item <paramref name="index"/> into a one-row <paramref name="area"/>.</summary>
    void RenderItem(int index, Rect area, CellBuffer buffer, bool selected);
}

/// <summary>
/// Selection and scroll position of a list. Owned by the app, updated by input and by render. Render
/// scrolls to keep <see cref="Selected"/> visible whenever it changes (or the list is resized); in between,
/// <see cref="Offset"/> can scroll on its own, e.g. with the mouse wheel.
/// </summary>
public struct ListState
{
    private Rect _body;
    private int _renderedOffset;
    private int _followed;   // Selected + 1 when render last scrolled to it; 0 before the first render

    public ListState(int selected = 0) => Selected = selected;

    /// <summary>Selected index, or -1 for none.</summary>
    public int Selected { get; set; }

    /// <summary>First visible index. Render adjusts it to keep <see cref="Selected"/> visible when that changes.</summary>
    public int Offset { get; set; }

    /// <summary>Visible rows at the last render (used for paging).</summary>
    public int Viewport { get; private set; }

    /// <summary>Where the widget was last rendered (a table's includes its header), for mouse hit-testing.</summary>
    public Rect Area { get; private set; }

    public void Next(int count) => Selected = count == 0 ? -1 : Math.Min(Selected + 1, count - 1);
    public void Previous(int count) => Selected = count == 0 ? -1 : Math.Max(Selected - 1, 0);
    public void First(int count) => Selected = count == 0 ? -1 : 0;
    public void Last(int count) => Selected = count - 1;
    public void PageDown(int count) => Selected = count == 0 ? -1 : Math.Min(Selected + Math.Max(1, Viewport), count - 1);
    public void PageUp(int count) => Selected = count == 0 ? -1 : Math.Max(Selected - Math.Max(1, Viewport), 0);

    /// <summary>Scroll the view by <paramref name="rows"/> (negative is up) without moving the selection.</summary>
    public void Scroll(int rows, int count) => Offset = Math.Clamp(Offset + rows, 0, Math.Max(0, count - Viewport));

    /// <summary>The item drawn at cell (<paramref name="x"/>, <paramref name="y"/>) at the last render, or -1.</summary>
    public readonly int RowAt(int x, int y, int count)
    {
        if (!_body.Contains(x, y))
        {
            return -1;
        }

        int index = _renderedOffset + (y - _body.Y);
        return index < count ? index : -1;
    }

    /// <summary>
    /// A click on a row selects it; the wheel over the list scrolls it by <paramref name="wheelRows"/> per notch.
    /// Returns whether the event was used (false for events elsewhere), so the caller can route it on.
    /// </summary>
    public bool HandleMouse(MouseEvent ev, int count, int wheelRows = 3)
    {
        if (ev.IsWheel && ev.IsIn(Area))
        {
            Scroll(ev.WheelDelta * wheelRows, count);
            return true;
        }

        int row = ev.IsClick ? RowAt(ev.X, ev.Y, count) : -1;
        if (row < 0)
        {
            return false;
        }

        Selected = row;
        return true;
    }

    /// <summary>Make the next render scroll to <see cref="Selected"/> even if it hasn't changed.</summary>
    internal void Reveal() => _followed = 0;

    /// <summary>Called by render: <paramref name="area"/> is the whole widget, <paramref name="body"/> its item rows.</summary>
    internal void Follow(int count, Rect area, Rect body)
    {
        int height = body.Height;
        if (Selected >= count)
        {
            Selected = count - 1;
        }

        if (Selected >= 0 && (Selected + 1 != _followed || height != Viewport))
        {
            if (Selected < Offset)
            {
                Offset = Selected;
            }
            else if (Selected >= Offset + height)
            {
                Offset = Selected - height + 1;
            }
        }

        Viewport = height;
        _followed = Selected + 1;
        Offset = Math.Clamp(Offset, 0, Math.Max(0, count - height));
        Area = area;
        _body = body;
        _renderedOffset = Offset;
    }
}

/// <summary>A scrolling, selectable list over any <see cref="IListSource"/>.</summary>
public readonly ref struct ListView<TSource> : IStatefulWidget<ListState>
    where TSource : IListSource, allows ref struct
{
    private readonly TSource _source;

    public ListView(TSource source) => _source = source;

    /// <summary>Layered over the selected row after the item renders.</summary>
    public Style SelectedStyle { get; init; }

    /// <summary>Drawn before the selected item (e.g. "> "); other rows are indented by its width.</summary>
    public ReadOnlySpan<char> HighlightSymbol { get; init; }

    /// <summary>Layered over <see cref="SelectedStyle"/> for the highlight symbol.</summary>
    public Style HighlightSymbolStyle { get; init; }

    public void Render(Rect area, CellBuffer buffer, ref ListState state)
    {
        area = area.Intersect(buffer.Area);
        int count = _source.Count;
        state.Follow(count, area, area);
        if (area.IsEmpty || count == 0)
        {
            return;
        }

        int indent = HighlightSymbol.IsEmpty ? 0 : TextWidth.Of(HighlightSymbol);
        for (int row = 0; row < area.Height; row++)
        {
            int index = state.Offset + row;
            if (index >= count)
            {
                break;
            }

            var line = new Rect(area.X, area.Y + row, area.Width, 1);
            bool selected = index == state.Selected;
            _source.RenderItem(index, new Rect(line.X + indent, line.Y, line.Width - indent, 1), buffer, selected);
            if (selected)
            {
                buffer.SetStyle(line, SelectedStyle);
                if (indent > 0)
                {
                    buffer.SetString(line.X, line.Y, HighlightSymbol, SelectedStyle.Patch(HighlightSymbolStyle), line.Width);
                }
            }
        }
    }
}

/// <summary>A list source over strings, drawn in one style and clipped with an ellipsis.</summary>
public readonly ref struct TextItems : IListSource
{
    private readonly ReadOnlySpan<string> _items;
    private readonly Style _style;

    public TextItems(ReadOnlySpan<string> items, Style style = default)
    {
        _items = items;
        _style = style;
    }

    public int Count => _items.Length;

    public void RenderItem(int index, Rect area, CellBuffer buffer, bool selected) =>
        buffer.SetString(area.X, area.Y, _items[index], _style, area.Width, Overflow.Ellipsis);
}
