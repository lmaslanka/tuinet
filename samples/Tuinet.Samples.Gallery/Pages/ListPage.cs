using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="ListView{TSource}"/> over a custom <see cref="IListSource"/> (a name, and a size right-aligned) with a
/// highlight symbol and a scrollbar. j/k or arrows move, PgUp/PgDn page, Home/End jump; a click selects, the wheel
/// scrolls the view without moving the selection, and the scrollbar can be clicked or dragged.
/// </summary>
public sealed class ListPage() : Page("List", "ListView<T> · ListState", Snippet, Keys)
{
    private const string Snippet = """
        var files = new Files(Names, Sizes);
        buffer.Render(new ListView<Files>(files)
        {
            SelectedStyle = Theme.RowSelected,
            HighlightSymbol = "▌",
            HighlightSymbolStyle = Theme.Accent(Theme.Blue),
            Scrollbar = ScrollbarMode.Auto,
            ScrollbarThumbStyle = Theme.Accent(Theme.Muted),
            ScrollbarTrackStyle = Theme.Faded,
        }, list, ref _list);
        // …
        if (key.IsChar('j') || key.Is(KeyCode.Down))
            _list.Next(count);
        else if (key.IsChar('k') || key.Is(KeyCode.Up))
            _list.Previous(count);
        // …
        return _list.HandleMouse(ev.Mouse, count);
        """;

    private static readonly (string, string)[] Keys = [("j/k", "move"), ("pgup/pgdn", "page"), ("click, wheel", "select, scroll")];

    private static readonly string[] Names =
    [
        "App.cs", "Alarm.cs", "Block.cs", "Button.cs", "Cell.cs", "CellBuffer.cs", "Checkbox.cs", "Color.cs",
        "Dropdown.cs", "EditableText.cs", "FocusRing.cs", "Fuzzy.cs", "Input.cs", "Layout.cs", "ListView.cs", "Menu.cs",
        "Paragraph.cs", "Popup.cs", "ProgressBar.cs", "Rect.cs", "Scrollbar.cs", "Sparkline.cs", "Style.cs",
        "StyledText.cs", "Table.cs", "Tabs.cs", "Terminal.cs", "TerminalOptions.cs", "TextArea.cs", "TextInput.cs",
        "TextWidth.cs", "TreeView.cs",
    ];

    private static readonly long[] Sizes = Array.ConvertAll(Names, name => 1_200L + name.Length * 731L % 9_000 * (name[0] - 'A' + 3));

    private ListState _list;

    public int Selected => _list.Selected;
    public int Offset => _list.Offset;

    public override bool Handle(Event ev, long nowMs)
    {
        int count = Names.Length;
        if (ev.Kind == EventKind.Mouse)
        {
            return _list.HandleMouse(ev.Mouse, count);
        }

        if (ev.Kind != EventKind.Key)
        {
            return false;
        }

        KeyEvent key = ev.Key;
        if (key.IsChar('j') || key.Is(KeyCode.Down))
            _list.Next(count);
        else if (key.IsChar('k') || key.Is(KeyCode.Up))
            _list.Previous(count);
        else if (key.Is(KeyCode.PageDown)) _list.PageDown(count);
        else if (key.Is(KeyCode.PageUp)) _list.PageUp(count);
        else if (key.Is(KeyCode.Home)) _list.First(count);
        else if (key.Is(KeyCode.End)) _list.Last(count);
        else return false;

        return true;
    }

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        var list = new Rect(area.X, area.Y, area.Width, Math.Max(0, area.Height - 2));
        var files = new Files(Names, Sizes);
        buffer.Render(new ListView<Files>(files)
        {
            SelectedStyle = Theme.RowSelected,
            HighlightSymbol = "▌",
            HighlightSymbolStyle = Theme.Accent(Theme.Blue),
            Scrollbar = ScrollbarMode.Auto,
            ScrollbarThumbStyle = Theme.Accent(Theme.Muted),
            ScrollbarTrackStyle = Theme.Faded,
        }, list, ref _list);

        var status = new StyledTextBuilder(stackalloc char[64], stackalloc StyledRun[8]);
        status.Append("item ", Theme.Dim);
        status.Append(_list.Selected + 1, Theme.Strong);
        status.Append(" of ", Theme.Dim);
        status.Append(Names.Length, Theme.Strong);
        status.Append(" · rows ", Theme.Dim);
        status.Append(_list.Offset + 1, Theme.Strong);
        status.Append("–", Theme.Dim);
        status.Append(Math.Min(Names.Length, _list.Offset + _list.Viewport), Theme.Strong);
        if (area.Height > 2)
        {
            buffer.SetText(area.X, area.Bottom - 1, status.Build(), area.Width);
        }
    }

    /// <summary>A file name, and its size right-aligned, formatted on the stack.</summary>
    private readonly struct Files(string[] names, long[] sizes) : IListSource
    {
        public int Count => names.Length;

        public void RenderItem(int index, Rect area, CellBuffer buffer, bool selected)
        {
            Span<char> size = stackalloc char[16];
            sizes[index].TryFormat(size, out int length, "N0");
            int x = area.Right - length - 2;   // "B" and a blank before the scrollbar
            buffer.SetString(area.X, area.Y, names[index], selected ? Theme.Strong : Theme.Body, x - 1 - area.X, Overflow.Ellipsis);
            x = buffer.SetString(x, area.Y, size[..length], Theme.Dim);
            buffer.SetString(x, area.Y, "B", Theme.Faded);
        }
    }
}
