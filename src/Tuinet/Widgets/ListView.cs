namespace Tuinet.Widgets;

/// <summary>Items for a <see cref="ListView{TSource}"/>. Only visible rows are rendered (virtualized).</summary>
public interface IListSource
{
    int Count { get; }

    /// <summary>Draw item <paramref name="index"/> into a one-row <paramref name="area"/>.</summary>
    void RenderItem(int index, Rect area, CellBuffer buffer, bool selected);
}

/// <summary>Selection and scroll position of a list. Owned by the app, updated by input and by render.</summary>
public struct ListState
{
    public ListState(int selected = 0) => Selected = selected;

    /// <summary>Selected index, or -1 for none.</summary>
    public int Selected { get; set; }

    /// <summary>First visible index. Render adjusts it to keep <see cref="Selected"/> visible.</summary>
    public int Offset { get; set; }

    /// <summary>Visible rows at the last render (used for paging).</summary>
    public int Viewport { get; private set; }

    public void Next(int count) => Selected = count == 0 ? -1 : Math.Min(Selected + 1, count - 1);
    public void Previous(int count) => Selected = count == 0 ? -1 : Math.Max(Selected - 1, 0);
    public void First(int count) => Selected = count == 0 ? -1 : 0;
    public void Last(int count) => Selected = count - 1;
    public void PageDown(int count) => Selected = count == 0 ? -1 : Math.Min(Selected + Math.Max(1, Viewport), count - 1);
    public void PageUp(int count) => Selected = count == 0 ? -1 : Math.Max(Selected - Math.Max(1, Viewport), 0);

    internal void Follow(int count, int height)
    {
        Viewport = height;
        if (Selected >= count)
        {
            Selected = count - 1;
        }

        if (Selected >= 0)
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

        Offset = Math.Clamp(Offset, 0, Math.Max(0, count - height));
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

    public void Render(Rect area, CellBuffer buffer, ref ListState state)
    {
        area = area.Intersect(buffer.Area);
        int count = _source.Count;
        state.Follow(count, area.Height);
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
            if (selected && indent > 0)
            {
                buffer.SetString(line.X, line.Y, HighlightSymbol, SelectedStyle, line.Width);
            }

            _source.RenderItem(index, new Rect(line.X + indent, line.Y, line.Width - indent, 1), buffer, selected);
            if (selected)
            {
                buffer.SetStyle(line, SelectedStyle);
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
