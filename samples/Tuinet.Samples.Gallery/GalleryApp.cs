using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// The gallery: a tab per widget or feature, the live widget on the left and the code that draws it on the right
/// (below it on a narrow screen). [ and ] or Alt+1…9 switch pages, as does a click on a tab; c hides the code.
/// </summary>
public sealed class GalleryApp : IApp
{
    /// <summary>The live panel keeps at least this many columns beside the code; narrower screens stack them.</summary>
    public const int MinLiveWidth = 44;

    private readonly Page[] _pages =
    [
        new LayoutPage(),
        new BlockPage(),
        new TextPage(),
        new FormsPage(),
        new InputPage(),
        new ListPage(),
        new TablePage(),
        new TreePage(),
        new ScrollPage(),
        new ProgressPage(),
        new ChartsPage(),
        new TabsPage(),
        new MenuPage(),
        new PopupPage(),
        new FuzzyPage(),
    ];

    private readonly string[] _titles;
    private TabsState _tabs;
    private int _codeScroll;
    private Rect _codeArea;

    public GalleryApp() => _titles = Array.ConvertAll(_pages, page => page.Title);

    public IReadOnlyList<Page> Pages => _pages;
    public int PageIndex => _tabs.Selected;
    public Page Current => _pages[_tabs.Selected];

    /// <summary>Whether the code panel shows (c toggles it).</summary>
    public bool ShowCode { get; private set; } = true;

    /// <summary>The page of type <typeparamref name="T"/>.</summary>
    public T Get<T>() where T : Page => (T)Array.Find(_pages, page => page is T)!;

    public void Show(int index)
    {
        int before = _tabs.Selected;
        _tabs.Select(index, _pages.Length);
        if (_tabs.Selected != before)
        {
            _codeScroll = 0;
        }
    }

    public void Show<T>() where T : Page => Show(Array.IndexOf(_pages, Get<T>()));

    public bool IsAnimating(long nowMs) => Current.IsAnimating(nowMs);

    /// <summary>Returns false when the app should exit.</summary>
    public bool Handle(Event ev, long nowMs)
    {
        if (ev.Kind == EventKind.Mouse)
        {
            HandleMouse(ev, nowMs);
            return true;
        }

        if (ev.Kind != EventKind.Key)
        {
            Current.Handle(ev, nowMs);   // ticks, pastes
            return true;
        }

        KeyEvent key = ev.Key;
        if (key.IsCtrl('c'))
        {
            return false;
        }

        if (JumpToPage(key))
        {
            return true;
        }

        // A page that's editing text (or has a list open) gets every other key: 'q' and '[' type themselves.
        if (Current.WantsKeys)
        {
            Current.Handle(ev, nowMs);
            return true;
        }

        if (key.IsChar('q'))
        {
            return false;
        }

        if (key.IsChar(']'))
        {
            Show((_tabs.Selected + 1) % _pages.Length);
        }
        else if (key.IsChar('['))
        {
            Show((_tabs.Selected + _pages.Length - 1) % _pages.Length);
        }
        else if (key.IsChar('c'))
        {
            ShowCode = !ShowCode;
        }
        else
        {
            Current.Handle(ev, nowMs);
        }

        return true;
    }

    /// <summary>Alt+1…9 jump to a page from anywhere, even while a page takes the keys.</summary>
    private bool JumpToPage(KeyEvent key)
    {
        if (key.Code != KeyCode.Char || key.Modifiers != Modifiers.Alt || key.Kind == KeyKind.Release
            || (uint)(key.Rune.Value - '1') >= (uint)Math.Min(9, _pages.Length))
        {
            return false;
        }

        Show(key.Rune.Value - '1');
        return true;
    }

    private void HandleMouse(Event ev, long nowMs)
    {
        MouseEvent mouse = ev.Mouse;
        int before = _tabs.Selected;
        if (PageTabs().HandleMouse(mouse, ref _tabs))
        {
            if (_tabs.Selected != before)
            {
                _codeScroll = 0;
            }

            return;
        }

        if (Current.Handle(ev, nowMs))
        {
            return;
        }

        if (mouse.IsWheel && mouse.IsIn(_codeArea))
        {
            _codeScroll += mouse.WheelDelta;   // clamped when drawn
        }
    }

    public void Render(CellBuffer buffer, long nowMs)
    {
        buffer.Fill(buffer.Area, Theme.Screen);
        Rect area = buffer.Area.Inset(2, 1);

        // Blank rows between the header, the tabs, the panels and the hints, unless the screen is short.
        int gap = area.Height >= 30 ? 1 : 0;
        Span<Rect> rows = stackalloc Rect[4];
        Layout.Vertical(area, [Constraint.Length(1), Constraint.Length(1), Constraint.Fill(), Constraint.Length(1)], rows, gap);
        RenderHeader(buffer, rows[0]);
        buffer.Render(PageTabs(), rows[1], ref _tabs);

        // The live widget on the left and its code on the right, or the code below it (a third of the height, scrolling
        // with the wheel) when it would squeeze the widget.
        Page page = Current;
        Rect live = rows[2];
        _codeArea = default;
        if (ShowCode)
        {
            Span<Rect> panels = stackalloc Rect[2];
            int codeWidth = page.CodeWidth + CodePanel.Chrome;
            if (live.Width - codeWidth - 2 >= MinLiveWidth)
            {
                Layout.Horizontal(live, [Constraint.Fill(), Constraint.Length(codeWidth)], panels, spacing: 2);
            }
            else
            {
                int codeHeight = Math.Min(page.CodeLines + 2, Math.Max(5, live.Height / 3));
                Layout.Vertical(live, [Constraint.Fill(), Constraint.Length(codeHeight)], panels);
            }

            (live, _codeArea) = (panels[0], panels[1]);
            CodePanel.Render(buffer, _codeArea, page, ref _codeScroll);
        }

        RenderHints(buffer, rows[3], page);

        var panel = new Block
        {
            BorderType = BorderType.Rounded,
            BorderStyle = Theme.Accent(Theme.Amber),
            Title = page.Heading,
            TitleStyle = Theme.Heading(Theme.Amber),
        };
        buffer.Render(panel, live);

        // Last, so the page's popups and menus sit on top of everything else.
        page.Render(buffer, panel.Inner(live).Inset(1, 0), nowMs);
    }

    /// <summary>The page tabs; render and clicks use the same one. No dividers: all fifteen fit in 106 columns.</summary>
    private Tabs PageTabs() => new(_titles)
    {
        Style = Theme.Dim,
        SelectedStyle = new Style(Theme.Amber, Theme.Raised, Attr.Bold),
        Divider = "",
        ArrowStyle = Theme.Accent(Theme.Amber),
    };

    private void RenderHeader(CellBuffer buffer, Rect row)
    {
        var title = new StyledTextBuilder(stackalloc char[48], stackalloc StyledRun[6]);
        title.Append("TUINET GALLERY", Theme.Heading(Theme.Text));
        title.Append("  ", Theme.Dim);
        title.Append(_tabs.Selected + 1, Theme.Heading(Theme.Amber));
        title.Append(" / ", Theme.Faded);
        title.Append(_pages.Length, Theme.Dim);
        int end = buffer.SetText(row.X, row.Y, title.Build(), row.Width);

        const string Subtitle = "every widget, live, beside the code that draws it";
        int x = row.Right - Subtitle.Length;
        if (x > end + 2)
        {
            buffer.SetString(x, row.Y, Subtitle, Theme.Dim);
        }
    }

    private static readonly (string Key, string Action)[] GlobalHints = [("[ ]", "pages"), ("c", "code"), ("q", "quit")];
    private static readonly (string Key, string Action)[] CapturedHints = [("alt+1…9", "pages"), ("ctrl+c", "quit")];

    private static void RenderHints(CellBuffer buffer, Rect row, Page page)
    {
        var keys = new StyledTextBuilder(stackalloc char[160], stackalloc StyledRun[48]);
        Append(ref keys, page.Hints);
        Append(ref keys, page.WantsKeys ? CapturedHints : GlobalHints);
        buffer.SetText(row.X, row.Y, keys.Build(), row.Width, Overflow.Ellipsis);
    }

    private static void Append(ref StyledTextBuilder keys, (string Key, string Action)[] hints)
    {
        foreach ((string key, string action) in hints)
        {
            if (keys.Length > 0)
            {
                keys.Append("  ·  ", Theme.Faded);
            }

            keys.Append(key, Theme.Heading(Theme.Blue));
            keys.Append(" ");
            keys.Append(action, Theme.Dim);
        }
    }
}
