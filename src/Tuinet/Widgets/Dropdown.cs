using System.Text;

namespace Tuinet.Widgets;

/// <summary>Committed choice, open flag and highlighted row of a <see cref="Dropdown{TSource}"/>.</summary>
public struct DropdownState
{
    public DropdownState(int selected) => Selected = selected;

    /// <summary>The chosen item, or -1 for none.</summary>
    public int Selected { get; set; }

    public bool IsOpen { get; private set; }

    /// <summary>Highlight inside the open list.</summary>
    public ListState List;

    /// <summary>Where the closed field was last rendered, for mouse hit-testing.</summary>
    public Rect Field { get; private set; }

    /// <summary>Where the open list was last rendered (border included), or empty while closed.</summary>
    public Rect Popup { get; private set; }

    public void Open()
    {
        IsOpen = true;
        List.Selected = Math.Max(0, Selected);
        List.Reveal();
    }

    public void Close() => IsOpen = false;

    /// <summary>
    /// Closed: Enter, Space or Down opens. Open: Up/Down (or k/j) move, Enter commits, Esc cancels.
    /// Returns false for keys it does not use (e.g. Tab while closed), so the caller can handle them.
    /// </summary>
    public bool Handle(KeyEvent key, int count)
    {
        if (!IsOpen)
        {
            if (key.Is(KeyCode.Enter) || key.IsChar(' ') || key.Is(KeyCode.Down))
            {
                Open();
                return true;
            }

            return false;
        }

        if (key.Is(KeyCode.Down) || key.IsChar('j'))
        {
            List.Next(count);
        }
        else if (key.Is(KeyCode.Up) || key.IsChar('k'))
        {
            List.Previous(count);
        }
        else if (key.Is(KeyCode.Home))
        {
            List.First(count);
        }
        else if (key.Is(KeyCode.End))
        {
            List.Last(count);
        }
        else if (key.Is(KeyCode.Enter) || key.IsChar(' '))
        {
            Selected = List.Selected;
            IsOpen = false;
        }
        else if (key.Is(KeyCode.Escape))
        {
            IsOpen = false;
        }
        else if (key.Code == KeyCode.Tab)
        {
            IsOpen = false;
            return false;
        }

        return true;
    }

    /// <summary>
    /// Closed: a click on the field opens. Open: a click on an item commits it, the wheel scrolls the list, a
    /// click on the field or anywhere else cancels. Returns whether the event was used; a click elsewhere closes
    /// the list but returns false, so the caller can still act on it (e.g. focus what was clicked).
    /// </summary>
    public bool HandleMouse(MouseEvent ev, int count)
    {
        if (!IsOpen)
        {
            if (!ev.IsClickIn(Field))
            {
                return false;
            }

            Open();
            return true;
        }

        if (ev.IsIn(Popup))
        {
            int row = ev.IsClick ? List.RowAt(ev.X, ev.Y, count) : -1;
            if (ev.IsWheel)
            {
                List.Scroll(ev.WheelDelta * 3, count);
            }
            else if (row >= 0)
            {
                Selected = row;
                IsOpen = false;
            }

            return true;
        }

        if (!ev.IsClick)
        {
            return false;
        }

        IsOpen = false;
        return ev.IsIn(Field);
    }

    internal void Rendered(Rect field)
    {
        Field = field;
        Popup = default;
    }

    internal void RenderedPopup(Rect popup) => Popup = popup;
}

/// <summary>
/// A select box: <see cref="Render"/> draws the closed field (selected item + '▾'); after drawing
/// everything else, call <see cref="RenderPopup"/> so the open list sits on top.
/// </summary>
public readonly ref struct Dropdown<TSource> : IStatefulWidget<DropdownState>
    where TSource : IListSource, allows ref struct
{
    private readonly TSource _source;

    public Dropdown(TSource source) => _source = source;

    public Style Style { get; init; }
    public ReadOnlySpan<char> Placeholder { get; init; }
    public Style PlaceholderStyle { get; init; }
    public Style PopupStyle { get; init; }
    public Style SelectedStyle { get; init; }
    /// <summary>Draw a border around the open list.</summary>
    public bool PopupBordered { get; init; }

    public BorderType PopupBorder { get; init; }
    public Style PopupBorderStyle { get; init; }

    /// <summary>Most rows the open list shows before it scrolls.</summary>
    public int MaxVisible { get; init; } = 8;

    public void Render(Rect area, CellBuffer buffer, ref DropdownState state)
    {
        if (area.IsEmpty)
        {
            return;
        }

        var row = new Rect(area.X, area.Y, area.Width, 1);
        state.Rendered(row);
        buffer.Erase(row, Style);

        var text = new Rect(row.X, row.Y, Math.Max(0, row.Width - 2), 1);
        if (state.Selected >= 0 && state.Selected < _source.Count)
        {
            _source.RenderItem(state.Selected, text, buffer, selected: false);
        }
        else if (!Placeholder.IsEmpty)
        {
            buffer.SetString(text.X, text.Y, Placeholder, Style.Patch(PlaceholderStyle), text.Width, Overflow.Ellipsis);
        }

        buffer.SetRune(row.Right - 1, row.Y, new Rune(state.IsOpen ? '▴' : '▾'), Style);
    }

    /// <summary>Draw the open list under <paramref name="anchor"/> (above it if there is no room).</summary>
    public void RenderPopup(Rect anchor, CellBuffer buffer, ref DropdownState state)
    {
        if (!state.IsOpen || _source.Count == 0)
        {
            return;
        }

        int border = PopupBordered ? 2 : 0;
        int height = Math.Min(_source.Count, Math.Max(1, MaxVisible)) + border;
        int below = buffer.Height - anchor.Bottom;
        int y = below >= height || below >= anchor.Y ? anchor.Bottom : Math.Max(0, anchor.Y - height);
        var popup = new Rect(anchor.X, y, anchor.Width, Math.Min(height, Math.Max(below, anchor.Y)));
        state.RenderedPopup(popup);

        buffer.Render(new Clear(PopupStyle), popup);
        Rect inner = popup;
        if (border > 0)
        {
            var block = new Block { BorderType = PopupBorder, BorderStyle = PopupBorderStyle };
            buffer.Render(block, popup);
            inner = block.Inner(popup);
        }

        buffer.Render(new ListView<TSource>(_source) { SelectedStyle = SelectedStyle }, inner, ref state.List);
    }
}
