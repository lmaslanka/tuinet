using Tuinet.Widgets;

namespace Tuinet.Samples.Showcase;

/// <summary>Main screen: a tabbed panel (the 20-item list, stats) with a details panel, plus the edit and progress dialogs.</summary>
public sealed class ShowcaseApp
{
    private readonly Item[] _items = Item.Samples();
    private ListState _list;
    private TabsState _tabs;
    private readonly Ring _frameBytes = new(FrameHistory);
    private readonly Ring _frameMicroseconds = new(FrameHistory);
    private readonly Bar[] _kindBars = new Bar[Item.Kinds.Length];
    private readonly Bar[] _priorityBars = new Bar[Item.Priorities.Length];
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

    /// <summary>Frames kept for the stats page's sparklines.</summary>
    public const int FrameHistory = 120;

    /// <summary>Five bars of 7 columns with 1-column gaps: every kind's name fits under its bar.</summary>
    private const int KindChartWidth = 39;

    /// <summary>What a frame cost, charted on the stats page (the loop reports it after each <see cref="Terminal.Present"/>).</summary>
    public void RecordFrame(long bytes, TimeSpan time)
    {
        _frameBytes.Add(bytes);
        _frameMicroseconds.Add(time.TotalMicroseconds);
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

    /// <summary>Charts of the items per kind and priority, the flags, and what recent frames cost.</summary>
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

        for (int k = 0; k < kinds.Length; k++)
        {
            _kindBars[k] = new Bar(kinds[k], Item.Kinds[k], Theme.Accent(Theme.KindColor(k)));
        }

        for (int p = 0; p < priorities.Length; p++)
        {
            _priorityBars[p] = new Bar(priorities[p], Item.Priorities[p], Theme.Accent(Theme.PriorityColor(p)));
        }

        // Top to bottom; on a short screen the sections at the bottom are cut.
        Span<Rect> top = stackalloc Rect[2];
        Layout.Horizontal(new Rect(area.X, area.Y, area.Width, 6), [Constraint.Length(KindChartWidth), Constraint.Fill()], top, spacing: 3);
        Heading(buffer, top[0], top[0].Y, "BY KIND", Theme.Blue);
        buffer.Render(new BarChart(_kindBars)
        {
            Max = _items.Length / 2.0,   // a kind with half the items fills the chart
            BarWidth = 7,
            LabelStyle = Theme.Dim,
            ValueStyle = Theme.Strong,
        }, Clip(top[0].X, top[0].Y + 1, top[0].Width, 5, area));

        Span<char> value = stackalloc char[48];
        int y = Heading(buffer, top[1], top[1].Y, "FLAGS", Theme.Coral);
        value.TryWrite($"{enabled,2} of {_items.Length}", out int length);
        y = Flag(buffer, top[1], y, "enabled", value[..length]);
        value.TryWrite($"{notify,2} of {_items.Length}", out length);
        Flag(buffer, top[1], y, "notify", value[..length]);

        y = Heading(buffer, area, area.Y + 7, "BY PRIORITY", Theme.Violet);
        buffer.Render(new BarChart(_priorityBars)
        {
            Direction = Direction.Horizontal,
            Max = _items.Length,         // each bar is its share of all items
            Gap = 0,
            LabelStyle = Theme.Dim,
            ValueStyle = Theme.Strong,
        }, Clip(area.X, y, area.Width, _priorityBars.Length, area));
        y += _priorityBars.Length + 1;

        // The two sparklines share what's left, each under a heading.
        int rest = Math.Max(2, area.Bottom - y - 3);
        int sparkline = (rest + 1) / 2;
        value.TryWrite($"last {_frameBytes.Last:F0} B", out length);
        ChartHeading(buffer, Clip(area.X, y, area.Width, 1, area), "BYTES PER FRAME", Theme.Green, _frameBytes.Count > 0 ? value[..length] : "");
        buffer.Render(new Sparkline(_frameBytes.Older, _frameBytes.Newer)
        {
            Style = Theme.Accent(Theme.Green),
            MaxStyle = Theme.Accent(Theme.Amber),
        }, Clip(area.X, y + 1, area.Width, sparkline, area));
        y += sparkline + 2;
        sparkline = rest / 2;

        value.TryWrite($"last {_frameMicroseconds.Last:F0} µs", out length);
        ChartHeading(buffer, Clip(area.X, y, area.Width, 1, area), "RENDER + DIFF + WRITE", Theme.Blue, _frameMicroseconds.Count > 0 ? value[..length] : "");
        buffer.Render(new Sparkline(_frameMicroseconds.Older, _frameMicroseconds.Newer)
        {
            Style = Theme.Accent(Theme.Blue),
            MaxStyle = Theme.Accent(Theme.Amber),
        }, Clip(area.X, y + 1, area.Width, sparkline, area));
    }

    private static Rect Clip(int x, int y, int width, int height, Rect area) => new Rect(x, y, width, height).Intersect(area);

    /// <summary>A chart's heading, with <paramref name="detail"/> on the right when it fits.</summary>
    private static void ChartHeading(CellBuffer buffer, Rect row, ReadOnlySpan<char> title, Color color, ReadOnlySpan<char> detail)
    {
        if (row.IsEmpty)
        {
            return;
        }

        int end = buffer.SetString(row.X, row.Y, title, Theme.Heading(color), row.Width);
        int x = row.Right - TextWidth.Of(detail);
        if (x > end + 1)
        {
            buffer.SetString(x, row.Y, detail, Theme.Dim);
        }
    }

    private static int Flag(CellBuffer buffer, Rect area, int y, ReadOnlySpan<char> label, ReadOnlySpan<char> value)
    {
        if (y < area.Bottom)
        {
            int x = buffer.SetString(area.X, y, "■ ", Theme.Accent(Theme.Green), area.Width);
            x = buffer.SetString(x, y, label, Theme.Body, area.Right - x);
            int valueX = Math.Max(x + 1, area.Right - TextWidth.Of(value));
            buffer.SetString(valueX, y, value, Theme.Strong, area.Right - valueX);
        }

        return y + 1;
    }

    private static int Heading(CellBuffer buffer, Rect area, int y, ReadOnlySpan<char> text, Color color)
    {
        if (y < area.Bottom)
        {
            buffer.SetString(area.X, y, text, Theme.Heading(color), area.Width);
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
