using Tuinet.Widgets;

namespace Tuinet.Samples.Showcase;

/// <summary>Main screen: a 20-item list with a details panel, plus the edit and progress dialogs.</summary>
public sealed class ShowcaseApp
{
    private readonly Item[] _items = Item.Samples();
    private ListState _list;
    private EditDialog? _edit;
    private ProgressDialog? _progress;
    private string _flash = "";

    public IReadOnlyList<Item> Items => _items;
    public int Selected => _list.Selected;
    public EditDialog? Edit => _edit;
    public bool ProgressOpen => _progress is not null;

    /// <summary>The loop redraws on a timer only while something animates; otherwise it sleeps until input.</summary>
    public bool IsAnimating(long nowMs) => _progress?.IsAnimating(nowMs) == true;

    /// <summary>Returns false when the app should exit.</summary>
    public bool Handle(Event ev, long nowMs)
    {
        if (ev.Kind == EventKind.Key && ev.Key.IsCtrl('c'))
        {
            return false;
        }

        if (_edit is not null)
        {
            DialogResult result = _edit.Handle(ev);
            if (result != DialogResult.Open)
            {
                _flash = result == DialogResult.Saved ? $"saved · {_items[_list.Selected].Name}" : "";
                _edit = null;
            }

            return true;
        }

        if (_progress is not null)
        {
            if (_progress.Handle(ev, nowMs) != DialogResult.Open)
            {
                _progress = null;
            }

            return true;
        }

        if (ev.Kind != EventKind.Key)
        {
            return true;
        }

        KeyEvent key = ev.Key;
        int count = _items.Length;
        if (key.IsChar('q'))
        {
            return false;
        }

        if (key.IsChar('j') || key.Is(KeyCode.Down)) _list.Next(count);
        else if (key.IsChar('k') || key.Is(KeyCode.Up)) _list.Previous(count);
        else if (key.IsChar('g') || key.Is(KeyCode.Home)) _list.First(count);
        else if (key.IsChar('G') || key.Is(KeyCode.End)) _list.Last(count);
        else if (key.Is(KeyCode.PageDown)) _list.PageDown(count);
        else if (key.Is(KeyCode.PageUp)) _list.PageUp(count);
        else if (key.Is(KeyCode.Enter) || key.IsChar('e')) _edit = new EditDialog(_items[_list.Selected], _list.Selected + 1);
        else if (key.IsChar('p')) _progress = new ProgressDialog(nowMs);
        else if (key.IsChar(' ')) _items[_list.Selected].Enabled = !_items[_list.Selected].Enabled;

        _flash = "";
        return true;
    }

    public void Render(CellBuffer buffer, long nowMs)
    {
        buffer.Fill(buffer.Area, Theme.Screen);
        Rect area = buffer.Area.Inset(2, 1);

        Span<Rect> rows = stackalloc Rect[4];
        Layout.Vertical(area, [Constraint.Length(1), Constraint.Length(2), Constraint.Fill(), Constraint.Length(1)], rows);
        buffer.SetString(rows[0].X, rows[0].Y, "TUINET SHOWCASE", Theme.Heading(Theme.Text), rows[0].Width);
        RenderSubtitle(buffer, rows[1]);

        Span<Rect> columns = stackalloc Rect[2];
        Layout.Horizontal(rows[2], [Constraint.Fill(), Constraint.Length(38)], columns, spacing: 3);
        RenderList(buffer, columns[0]);
        RenderDetails(buffer, columns[1]);
        RenderKeys(buffer, rows[3]);

        _edit?.Render(buffer);
        _progress?.Render(buffer, nowMs);
    }

    private static void RenderSubtitle(CellBuffer buffer, Rect row)
    {
        var subtitle = new StyledTextBuilder(stackalloc char[80], stackalloc StyledRun[5]);
        subtitle.Append("immediate mode", Theme.Dim);
        subtitle.Append(" → ", Theme.Faded);
        subtitle.Append("zero allocations per frame", Theme.Dim);
        subtitle.Append(" → ", Theme.Faded);
        subtitle.Append("one write per frame", Theme.Dim);
        buffer.SetText(row.X, row.Y, subtitle.Build(), row.Width);
    }

    private void RenderList(CellBuffer buffer, Rect box)
    {
        Span<char> title = stackalloc char[24];
        title.TryWrite($" ITEMS · {_items.Length} ", out int length);
        var block = new Block
        {
            BorderType = BorderType.Rounded,
            BorderStyle = Theme.Accent(Theme.Amber),
            Title = title[..length],
            TitleStyle = Theme.Heading(Theme.Amber),
        };
        buffer.Render(block, box);
        Rect inner = block.Inner(box).Inset(1, 1);
        int shown = FittingColumns(inner.Width - 1);   // 1 for the highlight symbol
        buffer.Render(new Table<ItemRows>(new ItemRows(_items), ItemColumns.AsSpan(0, shown))
        {
            HeaderStyle = Theme.Heading(Theme.Muted),
            HeaderSeparator = true,
            SeparatorStyle = Theme.Faded,
            ColumnSpacing = ColumnSpacing,
            SortColumn = 0,
            SelectedStyle = Theme.RowSelected,
            HighlightSymbol = "▌",
            HighlightSymbolStyle = Theme.Accent(Theme.Blue),
        }, inner, ref _list);
    }

    private void RenderDetails(CellBuffer buffer, Rect area)
    {
        Item item = _items[_list.Selected];
        int y = area.Y;

        Span<char> heading = stackalloc char[24];
        heading.TryWrite($"SELECTED · {_list.Selected + 1:D2}", out int length);
        buffer.SetString(area.X, y++, heading[..length], Theme.Heading(Theme.Green), area.Width);
        buffer.SetString(area.X, y++, item.Name, Theme.Strong, area.Width, Overflow.Ellipsis);
        int lines = Paragraph.LineCount(item.Description, area.Width, TextWrap.Word);
        buffer.Render(new Paragraph(item.Description, Theme.Dim) { Wrap = TextWrap.Word }, new Rect(area.X, y, area.Width, lines));
        y += lines + 1;

        y = Field(buffer, area, y, "1 · OWNER", Theme.Amber, item.Owner, Theme.Body);
        y = Field(buffer, area, y, "2 · KIND", Theme.Blue, Item.Kinds[item.Kind], Theme.Accent(Theme.KindColor(item.Kind)));
        y = Field(buffer, area, y, "3 · PRIORITY", Theme.Violet, Item.Priorities[item.Priority], Theme.Accent(Theme.PriorityColor(item.Priority)));

        buffer.SetString(area.X, y++, "4 · FLAGS", Theme.Heading(Theme.Coral), area.Width);
        int x = Flag(buffer, area.X, y, area.Right, "enabled", item.Enabled);
        Flag(buffer, x + 2, y, area.Right, "notify", item.Notify);
        y += 2;

        if (_flash.Length > 0 && y < area.Bottom)
        {
            buffer.SetString(area.X, y, "✓ ", Theme.Heading(Theme.Green), area.Width);
            buffer.SetString(area.X + 2, y, _flash, Theme.Accent(Theme.Green), area.Width - 2, Overflow.Ellipsis);
        }
    }

    private static int Field(CellBuffer buffer, Rect area, int y, ReadOnlySpan<char> label, Color color, ReadOnlySpan<char> value, Style valueStyle)
    {
        buffer.SetString(area.X, y, label, Theme.Heading(color), area.Width);
        buffer.SetString(area.X, y + 1, value.IsEmpty ? "—" : value, valueStyle, area.Width, Overflow.Ellipsis);
        return y + 3;
    }

    private static int Flag(CellBuffer buffer, int x, int y, int right, ReadOnlySpan<char> label, bool on)
    {
        x = buffer.SetString(x, y, on ? "■ " : "□ ", on ? Theme.Accent(Theme.Green) : Theme.Faded, right - x);
        return buffer.SetString(x, y, label, on ? Theme.Body : Theme.Dim, right - x);
    }

    private static readonly (string Key, string Action)[] KeyHints =
        [("j/k", "move"), ("enter", "edit"), ("space", "toggle"), ("p", "progress"), ("q", "quit")];

    private static void RenderKeys(CellBuffer buffer, Rect row)
    {
        var keys = new StyledTextBuilder(stackalloc char[96], stackalloc StyledRun[24]);
        foreach ((string key, string action) in KeyHints)
        {
            if (keys.Length > 0)
            {
                keys.Append("  ·  ", Theme.Faded);
            }

            keys.Append(key, Theme.Heading(Theme.Blue));
            keys.Append(" ");
            keys.Append(action, Theme.Dim);
        }

        buffer.SetText(row.X, row.Y, keys.Build(), row.Width);
    }

    private const int ColumnSpacing = 2;

    private static readonly TableColumn[] ItemColumns =
    [
        new("#", Constraint.Length(4), Alignment.Right),
        new("name", Constraint.Min(14)),       // the only growing column
        new("kind", Constraint.Length(7)),
        new("priority", Constraint.Length(10)),
        new("owner", Constraint.Length(9)),
    ];

    /// <summary>How many leading columns fit at their set widths: narrow screens drop whole columns from the end.</summary>
    private static int FittingColumns(int width)
    {
        int used = -ColumnSpacing;
        for (int i = 0; i < ItemColumns.Length; i++)
        {
            used += ColumnSpacing + ItemColumns[i].Width.Value;
            if (used > width)
            {
                return Math.Max(1, i);
            }
        }

        return ItemColumns.Length;
    }

    /// <summary>Columns of <see cref="ItemColumns"/>; disabled items are dimmed.</summary>
    private readonly struct ItemRows(Item[] items) : ITableSource
    {
        public int RowCount => items.Length;

        public ReadOnlySpan<char> Cell(int row, int column, Span<char> scratch, out Style style)
        {
            Item item = items[row];
            int written;
            switch (column)
            {
                case 0:
                    style = Theme.Faded;
                    scratch.TryWrite($"{row + 1:D2}", out written);
                    return scratch[..written];
                case 1:
                    style = item.Enabled ? Theme.Body : Theme.Faded;
                    return item.Name;
                case 2:
                    style = Theme.Accent(Theme.KindColor(item.Kind));
                    return Item.Kinds[item.Kind];
                case 3:
                    style = Theme.Accent(Theme.PriorityColor(item.Priority));
                    scratch.TryWrite($"▲ {Item.Priorities[item.Priority]}", out written);
                    return scratch[..written];
                default:
                    style = Theme.Dim;
                    return item.Owner;
            }
        }
    }
}
