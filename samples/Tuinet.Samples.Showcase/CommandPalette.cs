using Tuinet.Widgets;

namespace Tuinet.Samples.Showcase;

/// <summary>Something the app can do, listed in the command palette. Built once, with the app.</summary>
public sealed record Command(string Name, string Shortcut, Action<ShowcaseApp> Run);

/// <summary>
/// Ctrl+P: a query box over every command, fuzzy-filtered as you type, best match first. A pattern, not a library
/// widget: a <see cref="Popup"/>, a <see cref="TextInput"/> and a <see cref="ListView{TSource}"/>, with
/// <see cref="Fuzzy"/> doing the matching. Filtering runs only when the query changes, into preallocated arrays, so
/// neither typing nor drawing allocates.
/// </summary>
public sealed class CommandPalette
{
    private const int MaxQuery = 64;
    private const int MaxRows = 10;

    private readonly Command[] _commands;
    private readonly long[] _keys;     // score and original index packed, so sorting keeps ties in their original order
    private readonly int[] _order;     // indices into _commands, best match first
    private readonly char[] _query = new char[MaxQuery];   // what _order was filtered for
    private TextInputState _input = new();   // not readonly: rendered by ref
    private int _queryLength = -1;
    private int _count;
    private ListState _list;
    private Rect _box;

    public CommandPalette(Command[] commands)
    {
        _commands = commands;
        _keys = new long[commands.Length];
        _order = new int[commands.Length];
    }

    public bool IsOpen { get; private set; }

    /// <summary>The query as typed (allocates; for tests).</summary>
    public string Query => _input.Text;

    /// <summary>Matching commands, best first.</summary>
    public int MatchCount => _count;

    public Command Match(int index) => _commands[_order[index]];

    public void Open()
    {
        IsOpen = true;
        _input.Clear();
        _list = new ListState(0);
        Filter();
    }

    /// <summary>Returns the command to run (the palette has closed), or null. Every event is the palette's while it's open.</summary>
    public Command? Handle(Event ev)
    {
        if (ev.Kind == EventKind.Mouse)
        {
            return HandleMouse(ev.Mouse);
        }

        if (ev.Kind == EventKind.Paste)
        {
            _input.Insert(ev.Paste);
        }
        else if (ev.Kind != EventKind.Key)
        {
            return null;
        }

        KeyEvent key = ev.Key;
        if (key.Is(KeyCode.Escape))
        {
            IsOpen = false;
        }
        else if (key.Is(KeyCode.Enter))
        {
            return Run(_list.Selected);
        }
        else if (key.Is(KeyCode.Down) || key.IsCtrl('n'))
        {
            _list.Next(_count);
        }
        else if (key.Is(KeyCode.Up) || key.IsCtrl('p'))
        {
            _list.Previous(_count);
        }
        else if (key.Is(KeyCode.PageDown))
        {
            _list.PageDown(_count);
        }
        else if (key.Is(KeyCode.PageUp))
        {
            _list.PageUp(_count);
        }
        else
        {
            _input.Handle(key);
        }

        Filter();
        return null;
    }

    private Command? HandleMouse(MouseEvent mouse)
    {
        if (mouse.IsClick && !mouse.IsIn(_box))
        {
            IsOpen = false;
        }
        else if (mouse.IsClick && _list.RowAt(mouse.X, mouse.Y, _count) is int row and >= 0)
        {
            return Run(row);
        }
        else if (!_list.HandleMouse(mouse, _count, wheelRows: 1))
        {
            _input.HandleMouse(mouse);
        }

        return null;
    }

    private Command? Run(int index)
    {
        if ((uint)index >= (uint)_count)
        {
            return null;
        }

        IsOpen = false;
        return Match(index);
    }

    /// <summary>Re-score every command, only if the query changed since the last time.</summary>
    private void Filter()
    {
        Span<char> query = stackalloc char[MaxQuery];
        query = query[.._input.CopyTo(0, query)];
        if (_queryLength >= 0 && query.SequenceEqual(_query.AsSpan(0, _queryLength)))
        {
            return;
        }

        query.CopyTo(_query);
        _queryLength = query.Length;
        _count = 0;
        for (int i = 0; i < _commands.Length; i++)
        {
            int score = Fuzzy.Score(query, _commands[i].Name);
            if (score >= 0)
            {
                _keys[_count] = (long)(int.MaxValue - score) << 32 | (uint)i;
                _order[_count++] = i;
            }
        }

        Array.Sort(_keys, _order, 0, _count);
        _list = new ListState(_count > 0 ? 0 : -1);
    }

    /// <summary>A box at the top center: the query, a separator, then the matches.</summary>
    public void Render(CellBuffer buffer)
    {
        if (!IsOpen)
        {
            return;
        }

        int rows = Math.Clamp(_count, 1, MaxRows);
        int width = Math.Min(64, buffer.Width - 4);
        _box = new Rect((buffer.Width - width) / 2, 2, width, rows + 4).Intersect(buffer.Area);
        var popup = new Popup
        {
            Block = new Block
            {
                BorderType = BorderType.Rounded,
                BorderStyle = Theme.Accent(Theme.Violet),
                Title = " COMMANDS ",
                TitleStyle = Theme.Heading(Theme.Violet),
                StyledFooter = new StyledText(" ↑↓ choose · enter run · esc close ", Theme.Dim),
                FooterAlignment = Alignment.Right,
                Style = Theme.Dialog,
            },
            Shadow = true,
            ShadowStyle = Theme.Shadow,
        };
        buffer.Render(popup, new Rect(_box.X, _box.Y, _box.Width + 2, _box.Height + 1));
        Rect inner = popup.Inner(new Rect(_box.X, _box.Y, _box.Width + 2, _box.Height + 1)).Inset(1, 0);
        if (inner.Height < 3)
        {
            return;
        }

        buffer.SetString(inner.X, inner.Y, "› ", Theme.Heading(Theme.Violet));
        buffer.Render(new TextInput { Focused = true, Placeholder = "type to filter commands", PlaceholderStyle = Theme.Faded },
            new Rect(inner.X + 2, inner.Y, inner.Width - 2, 1), ref _input);

        // A separator joined to the border.
        Style border = Theme.Dialog.Patch(Theme.Accent(Theme.Violet));
        buffer.SetRune(new Rect(_box.X + 1, inner.Y + 1, _box.Width - 2, 1), new System.Text.Rune('─'), border);
        buffer.SetRune(_box.X, inner.Y + 1, new System.Text.Rune('├'), border);
        buffer.SetRune(_box.Right - 1, inner.Y + 1, new System.Text.Rune('┤'), border);

        var list = new Rect(inner.X, inner.Y + 2, inner.Width, inner.Height - 2);
        if (_count == 0)
        {
            buffer.SetString(list.X, list.Y, "no matching commands", Theme.Dim, list.Width);
            return;
        }

        buffer.Render(new ListView<Matches>(new Matches(_commands, _order.AsSpan(0, _count), _query.AsSpan(0, _queryLength)))
        {
            SelectedStyle = Theme.RowSelected,
            Scrollbar = ScrollbarMode.Auto,
            ScrollbarThumbStyle = Theme.Accent(Theme.Muted),
            ScrollbarTrackStyle = Theme.Faded,
        }, list, ref _list);
    }

    /// <summary>A command's name with the matched chars highlighted, and its shortcut on the right.</summary>
    private readonly ref struct Matches(Command[] commands, ReadOnlySpan<int> order, ReadOnlySpan<char> query) : IListSource
    {
        private readonly ReadOnlySpan<int> _order = order;
        private readonly ReadOnlySpan<char> _query = query;

        public int Count => _order.Length;

        public void RenderItem(int index, Rect area, CellBuffer buffer, bool selected)
        {
            Command command = commands[_order[index]];
            int right = area.Right - 1 - TextWidth.Of(command.Shortcut);   // a column clear of the scrollbar
            buffer.SetString(right, area.Y, command.Shortcut, Theme.Dim);

            // Scored again for the visible rows only, to get the matched positions.
            Span<int> matched = stackalloc int[_query.Length];
            Fuzzy.Score(_query, command.Name, matched);
            var name = new StyledTextBuilder(stackalloc char[96], stackalloc StyledRun[2 * MaxQuery + 1]);
            ReadOnlySpan<char> text = command.Name;
            int at = 0;
            foreach (int m in matched)
            {
                name.Append(text[at..m], Theme.Body);
                name.Append(text.Slice(m, 1), Theme.Heading(Theme.Amber));
                at = m + 1;
            }

            name.Append(text[at..], Theme.Body);
            buffer.SetText(area.X, area.Y, name.Build(), right - 1 - area.X, Overflow.Ellipsis);
        }
    }
}
