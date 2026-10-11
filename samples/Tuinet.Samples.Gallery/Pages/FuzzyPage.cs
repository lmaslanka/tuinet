using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="Fuzzy"/>: the library's public names filtered by what's typed, best match first, matched chars
/// highlighted, each with its score: the heart of a command palette (the showcase has a whole one, Ctrl+P). / or Enter
/// (or a click on the box) starts typing, Esc stops; ↑/↓ move through the matches. The list is filtered only when the
/// query changes, into arrays kept for it, so typing allocates nothing.
/// </summary>
public sealed class FuzzyPage : Page
{
    private const string Snippet = """
        Span<char> query = stackalloc char[MaxQuery];
        query = query[.._input.CopyTo(0, query)];
        // …
        for (int i = 0; i < Names.Length; i++)
        {
            int score = Fuzzy.Score(query, Names[i]);
            if (score >= 0)
            {
                _keys[_count] = (long)(int.MaxValue - score)
                    << 32 | (uint)i;
                _order[_count++] = i;
            }
        }

        Array.Sort(_keys, _order, 0, _count);
        // …
        Span<int> matched = stackalloc int[query.Length];
        int score = Fuzzy.Score(query, name, matched);
        """;

    private static readonly (string, string)[] Keys = [("/", "type"), ("j/k", "move")];
    private static readonly (string, string)[] TypingKeys = [("esc", "done"), ("↑/↓", "move")];

    private const int MaxQuery = 32;

    private static readonly string[] Names =
    [
        "Block", "Paragraph", "StyledTextBuilder", "Markup.Parse", "CellBuffer.SetMarkup", "CellBuffer.SetLink",
        "Button", "Checkbox", "TextInput", "TextInputState", "TextArea", "TextAreaState", "Dropdown<T>", "DropdownState",
        "ListView<T>", "ListState", "Table<T>", "TableColumn", "Scrollbar", "Scrollbar.PositionAt", "ProgressBar",
        "Spinner.Frame", "Sparkline", "BarChart", "Tabs", "TabsState", "Menu", "MenuItem", "MenuState", "Popup",
        "TreeView<T>", "TreeState", "Layout.Horizontal", "Layout.Vertical", "Constraint.Fill", "Rect.PlaceNear",
        "Rect.Centered", "FocusRing", "Fuzzy.Score", "Alarm", "Terminal.Run", "Terminal.CopyToClipboard",
        "Terminal.SetTitle", "TerminalOptions", "TextWidth.Of",
    ];

    private readonly long[] _keys = new long[Names.Length];   // score and index packed: best first, ties in order
    private readonly int[] _order = new int[Names.Length];    // indices into Names, best match first
    private readonly char[] _query = new char[MaxQuery];      // what _order was filtered for
    private TextInputState _input = new();
    private ListState _list;
    private int _queryLength = -1;
    private int _count;
    private bool _typing;

    public FuzzyPage() : base("Fuzzy", "Fuzzy.Score", Snippet, Keys) => Filter();

    public bool Typing => _typing;
    public int MatchCount => _count;
    public string Match(int index) => Names[_order[index]];
    public int Selected => _list.Selected;

    public override bool WantsKeys => _typing;

    internal override (string Key, string Action)[] Hints => _typing ? TypingKeys : Keys;

    public override bool Handle(Event ev, long nowMs)
    {
        bool used = ev.Kind switch
        {
            EventKind.Key => HandleKey(ev.Key),
            EventKind.Mouse => HandleMouse(ev.Mouse),
            EventKind.Paste when _typing => Paste(ev.Paste),
            _ => false,
        };
        Filter();
        return used;
    }

    private bool Paste(string? text)
    {
        _input.Insert(text);
        return true;
    }

    private bool HandleKey(KeyEvent key)
    {
        if (key.Is(KeyCode.Down) || (!_typing && key.IsChar('j'))) _list.Next(_count);
        else if (key.Is(KeyCode.Up) || (!_typing && key.IsChar('k'))) _list.Previous(_count);
        else if (_typing && (key.Is(KeyCode.Escape) || key.Is(KeyCode.Enter))) _typing = false;
        else if (_typing) _input.Handle(key);
        else if (key.IsChar('/') || key.Is(KeyCode.Enter)) _typing = true;
        else return false;

        return true;
    }

    private bool HandleMouse(MouseEvent mouse)
    {
        if (mouse.IsClickIn(_input.Area))
        {
            _typing = true;
            return _input.HandleMouse(mouse);
        }

        if (_typing && mouse.Kind is MouseKind.Drag or MouseKind.Up)
        {
            return _input.HandleMouse(mouse);
        }

        return _list.HandleMouse(mouse, _count);
    }

    /// <summary>Score every name again, only if the query changed since the last time.</summary>
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
        for (int i = 0; i < Names.Length; i++)
        {
            int score = Fuzzy.Score(query, Names[i]);
            if (score >= 0)
            {
                _keys[_count] = (long)(int.MaxValue - score)
                    << 32 | (uint)i;
                _order[_count++] = i;
            }
        }

        Array.Sort(_keys, _order, 0, _count);
        _list = new ListState(_count > 0 ? 0 : -1);
    }

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        // The query box, then the matches under a rule.
        Style prompt = Theme.Heading(_typing ? Theme.Green : Theme.Violet);
        int x = buffer.SetString(area.X, area.Y, "› ", prompt, area.Width);
        buffer.Render(new TextInput
        {
            Focused = _typing,
            Style = Theme.Field,
            Placeholder = "/ to filter, e.g. tv or sbpos",
            PlaceholderStyle = Theme.Faded,
        }, new Rect(x, area.Y, area.Right - x, 1).Intersect(area), ref _input);

        var count = new StyledTextBuilder(stackalloc char[40], stackalloc StyledRun[4]);
        count.Append(_count, Theme.Strong);
        count.Append(" of ", Theme.Dim);
        count.Append(Names.Length, Theme.Strong);
        count.Append(" names match", Theme.Dim);
        if (area.Height > 2)
        {
            buffer.SetText(area.X, area.Y + 2, count.Build(), area.Width);
        }

        var list = new Rect(area.X, area.Y + 4, area.Width, Math.Max(0, area.Height - 4));
        buffer.Render(new ListView<Matches>(new Matches(Names, _order.AsSpan(0, _count), _query.AsSpan(0, _queryLength)))
        {
            SelectedStyle = Theme.RowSelected,
            HighlightSymbol = "▌",
            HighlightSymbolStyle = Theme.Accent(Theme.Violet),
            Scrollbar = ScrollbarMode.Auto,
            ScrollbarThumbStyle = Theme.Accent(Theme.Muted),
            ScrollbarTrackStyle = Theme.Faded,
        }, list, ref _list);
    }

    /// <summary>A name with the matched chars in amber, and its score on the right.</summary>
    private readonly ref struct Matches(string[] names, ReadOnlySpan<int> order, ReadOnlySpan<char> query) : IListSource
    {
        private readonly ReadOnlySpan<int> _order = order;
        private readonly ReadOnlySpan<char> _query = query;

        public int Count => _order.Length;

        public void RenderItem(int index, Rect area, CellBuffer buffer, bool selected)
        {
            string name = names[_order[index]];
            ReadOnlySpan<char> query = _query;

            // Scored again for the visible rows only, to get the matched positions.
            Span<int> matched = stackalloc int[query.Length];
            int score = Fuzzy.Score(query, name, matched);
            Span<char> text = stackalloc char[12];
            int length = 0;
            if (!query.IsEmpty)   // an empty query matches everything with 0
            {
                score.TryFormat(text, out length);
            }

            int right = area.Right - 1 - length;
            buffer.SetString(right, area.Y, text[..length], Theme.Faded);

            var label = new StyledTextBuilder(stackalloc char[64], stackalloc StyledRun[2 * MaxQuery + 1]);
            int at = 0;
            foreach (int m in matched)
            {
                label.Append(name.AsSpan(at, m - at), Theme.Body);
                label.Append(name.AsSpan(m, 1), Theme.Heading(Theme.Amber));
                at = m + 1;
            }

            label.Append(name.AsSpan(at), Theme.Body);
            buffer.SetText(area.X, area.Y, label.Build(), right - 1 - area.X, Overflow.Ellipsis);
        }
    }
}
