using Tuinet.Widgets;

namespace Tuinet.Samples.Showcase;

/// <summary>Main screen: a tabbed panel (the 20-item list, stats) with a details panel, plus the edit and progress dialogs.</summary>
public sealed class ShowcaseApp
{
    private readonly Item[] _items = Item.Samples();
    private ListState _list;
    private TabsState _tabs;
    private long _frameBytes;
    private TimeSpan _frameTime;
    private EditDialog? _edit;
    private ProgressDialog? _progress;
    private string _flash = "";
    private string? _copy;
    private int _sortColumn;
    private bool _sortDescending;
    private long _lastClickMs = long.MinValue;
    private int _lastClickRow = -1;

    public IReadOnlyList<Item> Items => _items;
    public int Selected => _list.Selected;
    public int Page => _tabs.Selected;

    public const int ListPage = 0;
    public const int StatsPage = 1;
    private static readonly string[] Pages = ["List", "Stats"];
    public EditDialog? Edit => _edit;
    public bool ProgressOpen => _progress is not null;
    public int SortColumn => _sortColumn;
    public bool SortDescending => _sortDescending;

    /// <summary>Two clicks on the same row within this long open the edit dialog.</summary>
    public const int DoubleClickMs = 400;

    /// <summary>Text to put on the clipboard (the loop sends it with <see cref="Terminal.CopyToClipboard"/>), taken once.</summary>
    public string? TakeCopy()
    {
        string? text = _copy;
        _copy = null;
        return text;
    }

    /// <summary>What the previous frame cost, shown on the stats page (the loop reports it after each <see cref="Terminal.Present"/>).</summary>
    public void RecordFrame(long bytes, TimeSpan time)
    {
        _frameBytes = bytes;
        _frameTime = time;
    }

    /// <summary>The loop redraws on a timer only while something animates; otherwise it sleeps until input.</summary>
    public bool IsAnimating(long nowMs) => _progress?.IsAnimating(nowMs) == true;

    /// <summary>Returns false when the app should exit.</summary>
    public bool Handle(Event ev, long nowMs)
    {
        // Ctrl+C quits, unless it copies text selected in the edit dialog.
        if (ev.Kind == EventKind.Key && ev.Key.IsCtrl('c') && _edit?.HasSelection != true)
        {
            return false;
        }

        if (_edit is not null)
        {
            DialogResult result = _edit.Handle(ev);
            _copy = _edit.TakeCopy() ?? _copy;
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

        if (ev.Kind == EventKind.Mouse)
        {
            HandleMouse(ev.Mouse, nowMs);
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

        if (SwitchPage(key)) { }
        else if (key.IsChar('p')) _progress = new ProgressDialog(nowMs);
        else if (Page == ListPage)
        {
            if (key.IsChar('j') || key.Is(KeyCode.Down)) _list.Next(count);
            else if (key.IsChar('k') || key.Is(KeyCode.Up)) _list.Previous(count);
            else if (key.IsChar('g') || key.Is(KeyCode.Home)) _list.First(count);
            else if (key.IsChar('G') || key.Is(KeyCode.End)) _list.Last(count);
            else if (key.Is(KeyCode.PageDown)) _list.PageDown(count);
            else if (key.Is(KeyCode.PageUp)) _list.PageUp(count);
            else if (key.Is(KeyCode.Enter) || key.IsChar('e')) _edit = new EditDialog(_items[_list.Selected]);
            else if (key.IsChar(' ')) _items[_list.Selected].Enabled = !_items[_list.Selected].Enabled;
            else if (key.IsChar('y'))
            {
                _copy = _items[_list.Selected].Name;
                _flash = $"copied · {_copy}";
                return true;
            }
        }

        _flash = "";
        return true;
    }

    /// <summary>] and [ step through the tabs (wrapping); Alt+1…9 jump to one.</summary>
    private bool SwitchPage(KeyEvent key)
    {
        if (key.IsChar(']'))
        {
            _tabs.Next(Pages.Length);
        }
        else if (key.IsChar('['))
        {
            _tabs.Previous(Pages.Length);
        }
        else if (key.Code == KeyCode.Char && key.Modifiers == Modifiers.Alt && key.Kind != KeyKind.Release
            && (uint)(key.Rune.Value - '1') < (uint)Pages.Length)
        {
            _tabs.Select(key.Rune.Value - '1', Pages.Length);
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>A header click sorts by that column (again: reverses); a row click selects; a double-click edits.</summary>
    private void HandleMouse(MouseEvent mouse, long nowMs)
    {
        if (PageTabs().HandleMouse(mouse, ref _tabs) || Page != ListPage)
        {
            return;   // the list isn't drawn on other pages, so it must not hit-test its last position
        }

        int column = mouse.IsClick ? ItemTable(_list.Area.Width).HeaderColumnAt(mouse.X, mouse.Y, _list) : -1;
        if (column >= 0)
        {
            Sort(column, column == _sortColumn ? !_sortDescending : false);
            return;
        }

        // Only clicks on rows count toward a double-click, not ones on the scrollbar.
        bool onRow = mouse.IsClick && _list.RowAt(mouse.X, mouse.Y, _items.Length) >= 0;
        if (!_list.HandleMouse(mouse, _items.Length) || !onRow)
        {
            return;
        }

        _flash = "";
        if (_list.Selected == _lastClickRow && nowMs - _lastClickMs <= DoubleClickMs)
        {
            _edit = new EditDialog(_items[_list.Selected]);
            _lastClickRow = -1;
            return;
        }

        _lastClickRow = _list.Selected;
        _lastClickMs = nowMs;
    }

    /// <summary>Sort the items by <paramref name="column"/>, keeping the same item selected.</summary>
    public void Sort(int column, bool descending)
    {
        Item selected = _items[_list.Selected];
        _sortColumn = column;
        _sortDescending = descending;
        Array.Sort(_items, (a, b) =>
        {
            int order = column switch
            {
                1 => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase),
                2 => a.Kind.CompareTo(b.Kind),
                3 => a.Priority.CompareTo(b.Priority),
                4 => string.Compare(a.Owner, b.Owner, StringComparison.OrdinalIgnoreCase),
                _ => 0,
            };
            order = order != 0 ? order : a.Number.CompareTo(b.Number);
            return descending ? -order : order;
        });
        _list.Selected = Array.IndexOf(_items, selected);
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
        RenderPanel(buffer, columns[0]);
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
        int end = buffer.SetText(row.X, row.Y, subtitle.Build(), row.Width);

        // A hyperlink (OSC 8): Ctrl+click (or click) opens it in terminals that support links.
        const string Repo = "github ↗";
        int x = row.Right - TextWidth.Of(Repo);
        if (x > end + 1)
        {
            buffer.SetString(x, row.Y, Repo, Theme.Accent(Theme.Blue).With(Attr.Underline));
            buffer.SetLink(new Rect(x, row.Y, row.Right - x, 1), "https://github.com/lmaslanka/tuinet");
        }
    }

    /// <summary>The left panel: tabs in its top border, the count on the right of it, and the selected page inside.</summary>
    private void RenderPanel(CellBuffer buffer, Rect box)
    {
        Span<char> title = stackalloc char[24];
        title.TryWrite($" ITEMS · {_items.Length} ", out int length);
        var block = new Block
        {
            BorderType = BorderType.Rounded,
            BorderStyle = Theme.Accent(Theme.Amber),
            Title = title[..length],
            TitleStyle = Theme.Heading(Theme.Amber),
            TitleAlignment = Alignment.Right,
        };
        buffer.Render(block, box);

        // Between the corner and the title, leaving a ─ on each side.
        var bar = new Rect(box.X + 2, box.Y, box.Width - 5 - TextWidth.Of(title[..length]), 1);
        buffer.Render(PageTabs(), bar, ref _tabs);

        Rect inner = block.Inner(box).Inset(1, 1);
        if (Page == StatsPage)
        {
            RenderStats(buffer, inner);
        }
        else
        {
            buffer.Render(ItemTable(inner.Width), inner, ref _list);
        }
    }

    /// <summary>The page tabs; render and clicks use the same one.</summary>
    private static Tabs PageTabs() => new(Pages)
    {
        Style = Theme.Dim,
        SelectedStyle = new Style(Theme.Amber, Theme.Raised, Attr.Bold),
        Divider = "─",
        DividerStyle = Theme.Accent(Theme.Amber),
        ArrowStyle = Theme.Accent(Theme.Amber),
    };

    /// <summary>Counts per kind, priority and flag, and what the previous frame cost.</summary>
    private void RenderStats(CellBuffer buffer, Rect area)
    {
        Span<int> kinds = stackalloc int[Item.Kinds.Length];
        Span<int> priorities = stackalloc int[Item.Priorities.Length];
        int enabled = 0;
        int notify = 0;
        foreach (Item item in _items)
        {
            kinds[item.Kind]++;
            priorities[item.Priority]++;
            enabled += item.Enabled ? 1 : 0;
            notify += item.Notify ? 1 : 0;
        }

        Span<char> value = stackalloc char[48];
        int y = area.Y;
        y = Heading(buffer, area, y, "BY KIND", Theme.Blue);
        for (int k = 0; k < kinds.Length; k++)
        {
            value.TryWrite($"{kinds[k],3}", out int written);
            y = Stat(buffer, area, y, "● ", Theme.KindColor(k), Item.Kinds[k], value[..written]);
        }

        y = Heading(buffer, area, y + 1, "BY PRIORITY", Theme.Violet);
        for (int p = 0; p < priorities.Length; p++)
        {
            value.TryWrite($"{priorities[p],3}", out int written);
            y = Stat(buffer, area, y, "▲ ", Theme.PriorityColor(p), Item.Priorities[p], value[..written]);
        }

        y = Heading(buffer, area, y + 1, "FLAGS", Theme.Coral);
        value.TryWrite($"{enabled,3} of {_items.Length}", out int length);
        y = Stat(buffer, area, y, "■ ", Theme.Green, "enabled", value[..length]);
        value.TryWrite($"{notify,3} of {_items.Length}", out length);
        y = Stat(buffer, area, y, "■ ", Theme.Green, "notify", value[..length]);

        y = Heading(buffer, area, y + 1, "PREVIOUS FRAME", Theme.Green);
        value.TryWrite($"{_frameBytes,3} B", out length);
        y = Stat(buffer, area, y, "→ ", Theme.Muted, "written", value[..length]);
        value.TryWrite($"{_frameTime.TotalMicroseconds,3:F0} µs", out length);
        Stat(buffer, area, y, "→ ", Theme.Muted, "render + diff + write", value[..length]);
    }

    private static int Heading(CellBuffer buffer, Rect area, int y, ReadOnlySpan<char> text, Color color)
    {
        if (y < area.Bottom)
        {
            buffer.SetString(area.X, y, text, Theme.Heading(color), area.Width);
        }

        return y + 1;
    }

    private static int Stat(CellBuffer buffer, Rect area, int y, ReadOnlySpan<char> glyph, Color color, ReadOnlySpan<char> label, ReadOnlySpan<char> value)
    {
        if (y < area.Bottom)
        {
            const int ValueColumn = 26;
            int valueX = area.X + ValueColumn;
            int x = buffer.SetString(area.X, y, glyph, Theme.Accent(color), area.Width);
            buffer.SetString(x, y, label, Theme.Body, Math.Min(area.Right, valueX - 1) - x, Overflow.Ellipsis);
            if (valueX < area.Right)
            {
                buffer.SetString(valueX, y, value, Theme.Strong, area.Right - valueX);
            }
        }

        return y + 1;
    }

    /// <summary>The item table for an area <paramref name="width"/> wide; render and header clicks use the same one.</summary>
    private Table<ItemRows> ItemTable(int width) =>
        new(new ItemRows(_items), ItemColumns.AsSpan(0, FittingColumns(width - 2)))   // the highlight symbol and the scrollbar
        {
            HeaderStyle = Theme.Heading(Theme.Muted),
            HeaderSeparator = true,
            SeparatorStyle = Theme.Faded,
            ColumnSpacing = ColumnSpacing,
            SortColumn = _sortColumn,
            SortDescending = _sortDescending,
            SelectedStyle = Theme.RowSelected,
            HighlightSymbol = "▌",
            HighlightSymbolStyle = Theme.Accent(Theme.Blue),
            Scrollbar = ScrollbarMode.Auto,
            ScrollbarThumbStyle = Theme.Accent(Theme.Muted),
            ScrollbarTrackStyle = Theme.Faded,
        };

    private void RenderDetails(CellBuffer buffer, Rect area)
    {
        Item item = _items[_list.Selected];
        int y = area.Y;

        Span<char> heading = stackalloc char[24];
        heading.TryWrite($"SELECTED · {item.Number:D2}", out int length);
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
        [("j/k", "move"), ("enter", "edit"), ("space", "toggle"), ("y", "copy"), ("[ ]", "tabs"), ("p", "progress"), ("q", "quit")];

    private static void RenderKeys(CellBuffer buffer, Rect row)
    {
        var keys = new StyledTextBuilder(stackalloc char[112], stackalloc StyledRun[32]);
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
                    scratch.TryWrite($"{item.Number:D2}", out written);
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
