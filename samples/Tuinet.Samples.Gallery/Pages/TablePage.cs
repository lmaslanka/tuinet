using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="Table{TSource}"/>: a process list with right-aligned numbers formatted into the table's scratch span,
/// per-cell colors, column separators, zebra rows and a sort arrow. j/k move; a click on a header sorts by it (again:
/// reverses), as does s; rows take clicks, the wheel and scrollbar drags like a list.
/// </summary>
public sealed class TablePage : Page
{
    private const string Snippet = """
        private Table<Procs> ProcTable() =>
            new(new Procs(_procs), Columns)
            {
                HeaderStyle = Theme.Heading(Theme.Muted),
                HeaderSeparator = true,
                ColumnSeparator = '│',
                SeparatorStyle = Theme.Faded,
                AlternateRowStyle = Zebra,
                SortColumn = _sortColumn,
                SortDescending = _descending,
                SelectedStyle = Theme.RowSelected,
                Scrollbar = ScrollbarMode.Auto,
                ScrollbarThumbStyle = Theme.Accent(Theme.Muted),
                ScrollbarTrackStyle = Theme.Faded,
            };
        // …
        buffer.Render(ProcTable(), table, ref _list);
        // …
        int column =
            ProcTable().HeaderColumnAt(m.X, m.Y, _list);
        """;

    private static readonly (string, string)[] Keys = [("j/k", "move"), ("click header, s", "sort")];

    private static readonly TableColumn[] Columns =
    [
        new("name", Constraint.Fill()),
        new("pid", Constraint.Length(6), Alignment.Right),
        new("cpu %", Constraint.Length(7), Alignment.Right),
        new("mem MB", Constraint.Length(8), Alignment.Right),
    ];

    /// <summary>One row: a process.</summary>
    public readonly record struct Proc(string Name, int Pid, double Cpu, int MemoryMb);

    private readonly Proc[] _procs =
    [
        new("dotnet", 4211, 38.5, 412), new("tuinet-gallery", 5120, 2.1, 38), new("kitty", 1834, 6.4, 187),
        new("Xwayland", 1207, 1.2, 96), new("pipewire", 902, 0.8, 24), new("firefox", 3302, 54.0, 1980),
        new("code", 2780, 12.7, 860), new("rg", 6021, 71.3, 44), new("ssh-agent", 811, 0.0, 6), new("tmux", 1410, 0.3, 12),
        new("dockerd", 1022, 3.9, 154), new("postgres", 1100, 8.2, 230), new("redis-server", 1104, 1.4, 31),
        new("node", 4480, 22.6, 520), new("cargo", 5873, 96.1, 610), new("systemd", 1, 0.1, 18),
        new("NetworkManager", 640, 0.2, 21), new("bash", 4102, 0.0, 5), new("htop", 5002, 1.8, 7),
        new("gpg-agent", 1499, 0.0, 4), new("hyprland", 1188, 4.4, 240), new("waybar", 1301, 0.9, 64),
    ];

    /// <summary>Every other row, one step lighter.</summary>
    private static readonly Style Zebra = new(default, Theme.Panel);

    private ListState _list;
    private int _sortColumn = -1;
    private bool _descending;

    public TablePage() : base("Table", "Table<T> · TableColumn", Snippet, Keys)
    {
    }

    public int SortColumn => _sortColumn;
    public bool Descending => _descending;
    public int Selected => _list.Selected;
    public Proc SelectedProc => _procs[_list.Selected];

    public override bool Handle(Event ev, long nowMs)
    {
        int count = _procs.Length;
        if (ev.Kind == EventKind.Mouse)
        {
            MouseEvent m = ev.Mouse;
            int column = ProcTable().HeaderColumnAt(m.X, m.Y, _list);
            if (m.IsClick && column >= 0)
            {
                SortBy(column);
                return true;
            }

            return _list.HandleMouse(m, count);
        }

        if (ev.Kind != EventKind.Key)
        {
            return false;
        }

        KeyEvent key = ev.Key;
        if (key.IsChar('j') || key.Is(KeyCode.Down)) _list.Next(count);
        else if (key.IsChar('k') || key.Is(KeyCode.Up)) _list.Previous(count);
        else if (key.Is(KeyCode.PageDown)) _list.PageDown(count);
        else if (key.Is(KeyCode.PageUp)) _list.PageUp(count);
        else if (key.IsChar('s')) SortBy((_sortColumn + 1) % Columns.Length);
        else return false;

        return true;
    }

    /// <summary>Sort by <paramref name="column"/> (again: reverse), keeping the same process selected.</summary>
    public void SortBy(int column)
    {
        _descending = column == _sortColumn && !_descending;
        _sortColumn = column;
        Proc selected = _procs[Math.Max(0, _list.Selected)];
        Array.Sort(_procs, column switch
        {
            0 => ByName,
            1 => ByPid,
            2 => ByCpu,
            _ => ByMemory,
        });
        if (_descending)
        {
            Array.Reverse(_procs);
        }

        _list.Selected = Array.IndexOf(_procs, selected);
    }

    private static readonly Comparison<Proc> ByName = (a, b) => string.Compare(a.Name, b.Name, StringComparison.OrdinalIgnoreCase);
    private static readonly Comparison<Proc> ByPid = (a, b) => a.Pid.CompareTo(b.Pid);
    private static readonly Comparison<Proc> ByCpu = (a, b) => a.Cpu.CompareTo(b.Cpu);
    private static readonly Comparison<Proc> ByMemory = (a, b) => a.MemoryMb.CompareTo(b.MemoryMb);

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        var table = new Rect(area.X, area.Y, area.Width, Math.Max(0, area.Height - 2));
        buffer.Render(ProcTable(), table, ref _list);

        Proc proc = _procs[_list.Selected];
        var status = new StyledTextBuilder(stackalloc char[64], stackalloc StyledRun[6]);
        status.Append(proc.Name, Theme.Strong);
        status.Append(" · pid ", Theme.Dim);
        status.Append(proc.Pid, Theme.Body);
        status.Append(" · ", Theme.Dim);
        status.Append(proc.Cpu, Theme.Body, "F1");
        status.Append("% cpu", Theme.Dim);
        if (area.Height > 2)
        {
            buffer.SetText(area.X, area.Bottom - 1, status.Build(), area.Width, Overflow.Ellipsis);
        }
    }

    /// <summary>The table; render and header clicks use the same one.</summary>
    private Table<Procs> ProcTable() =>
        new(new Procs(_procs), Columns)
        {
            HeaderStyle = Theme.Heading(Theme.Muted),
            HeaderSeparator = true,
            ColumnSeparator = '│',
            SeparatorStyle = Theme.Faded,
            AlternateRowStyle = Zebra,
            SortColumn = _sortColumn,
            SortDescending = _descending,
            SelectedStyle = Theme.RowSelected,
            Scrollbar = ScrollbarMode.Auto,
            ScrollbarThumbStyle = Theme.Accent(Theme.Muted),
            ScrollbarTrackStyle = Theme.Faded,
        };

    /// <summary>The cells: numbers are formatted into the table's scratch span; busy processes are colored.</summary>
    private readonly struct Procs(Proc[] procs) : ITableSource
    {
        public int RowCount => procs.Length;

        public ReadOnlySpan<char> Cell(int row, int column, Span<char> scratch, out Style style)
        {
            Proc proc = procs[row];
            int written;
            switch (column)
            {
                case 0:
                    style = Theme.Body;
                    return proc.Name;
                case 1:
                    style = Theme.Dim;
                    proc.Pid.TryFormat(scratch, out written);
                    return scratch[..written];
                case 2:
                    style = Theme.Accent(proc.Cpu >= 50 ? Theme.Coral : proc.Cpu >= 10 ? Theme.Amber : Theme.Green);
                    proc.Cpu.TryFormat(scratch, out written, "F1");
                    return scratch[..written];
                default:
                    style = Theme.Accent(proc.MemoryMb >= 500 ? Theme.Violet : Theme.Text);
                    proc.MemoryMb.TryFormat(scratch, out written, "N0");
                    return scratch[..written];
            }
        }
    }
}
