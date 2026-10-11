using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="Tabs"/> in a block's top border, with more titles than fit: the bar scrolls to keep the selected one
/// in view and shows ‹ › where tabs are hidden. ←/→ (or h/l) and Home/End move, as when the bar has focus; a click
/// selects a title, a click on ‹ › or the wheel scrolls the bar. The gallery's own page bar is the same widget.
/// </summary>
public sealed class TabsPage : Page
{
    private const string Snippet = """
        private Tabs Bar() => new(Files)
        {
            Style = Theme.Dim,
            SelectedStyle = SelectedTab,
            Divider = "─",
            DividerStyle = Theme.Accent(Theme.Blue),
            ArrowStyle = Theme.Accent(Theme.Amber),
        };
        // …
        buffer.Render(block, area);
        buffer.Render(Bar(), new Rect(area.X + 2, area.Y,
            area.Width - 4, 1), ref _tabs);
        // …
        EventKind.Key => _tabs.Handle(ev.Key, Files.Length),
        EventKind.Mouse =>
            Bar().HandleMouse(ev.Mouse, ref _tabs),
        """;

    private static readonly (string, string)[] Keys = [("←/→", "switch"), ("home/end", "ends"), ("click, wheel", "select, scroll")];

    private static readonly string[] Files =
    [
        "Program.cs", "GalleryApp.cs", "Page.cs", "CodePanel.cs", "Theme.cs", "LayoutPage.cs", "ListPage.cs",
        "TablePage.cs", "TreePage.cs", "TabsPage.cs", "MenuPage.cs", "PopupPage.cs",
    ];

    private static readonly string[] About =
    [
        "Opens the terminal with mouse, paste and the kitty keyboard, and runs the app.",
        "The tab bar, the code panel and the key hints around each page.",
        "What every page has: a title, a snippet, its keys, render and input.",
        "Draws a snippet with light highlighting, straight from a const.",
        "The showcase's palette, plus the code panel's colors.",
        "Constraints, drawn as dashed boxes with the sizes they got.",
        "A virtualized list with a custom source and a scrollbar.",
        "A table with aligned numbers, zebra rows and click-to-sort.",
        "A folder tree with lazy children and cached rows.",
        "This page: a tab bar that scrolls when its titles don't fit.",
        "A context menu with mnemonics, shortcuts and a disabled item.",
        "A dialog with a drop shadow, and a popup placed by the pointer.",
    ];

    private static readonly Style SelectedTab = new(Theme.Blue, Theme.Raised, Attr.Bold);

    private TabsState _tabs;

    public TabsPage() : base("Tabs", "Tabs · TabsState", Snippet, Keys)
    {
    }

    public int Selected => _tabs.Selected;
    public int Offset => _tabs.Offset;

    public override bool Handle(Event ev, long nowMs) => ev.Kind switch
    {
        EventKind.Key => _tabs.Handle(ev.Key, Files.Length),
        EventKind.Mouse => Bar().HandleMouse(ev.Mouse, ref _tabs),
        _ => false,
    };

    /// <summary>The bar; render and clicks use the same one.</summary>
    private Tabs Bar() => new(Files)
    {
        Style = Theme.Dim,
        SelectedStyle = SelectedTab,
        Divider = "─",
        DividerStyle = Theme.Accent(Theme.Blue),
        ArrowStyle = Theme.Accent(Theme.Amber),
    };

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        area = new Rect(area.X, area.Y + 1, area.Width, Math.Min(area.Height - 1, 11));
        var block = new Block { BorderType = BorderType.Rounded, BorderStyle = Theme.Accent(Theme.Blue) };
        buffer.Render(block, area);
        buffer.Render(Bar(), new Rect(area.X + 2, area.Y,
            area.Width - 4, 1), ref _tabs);

        // The selected file: its name, where it is in the bar, and what it holds.
        Rect inner = block.Inner(area).Inset(2, 1);
        if (inner.IsEmpty)
        {
            return;
        }

        string file = Files[_tabs.Selected];
        buffer.SetString(inner.X, inner.Y, file, Theme.Heading(Theme.Text), inner.Width, Overflow.Ellipsis);
        var position = new StyledTextBuilder(stackalloc char[48], stackalloc StyledRun[6]);
        position.Append("tab ", Theme.Dim);
        position.Append(_tabs.Selected + 1, Theme.Strong);
        position.Append(" of ", Theme.Dim);
        position.Append(Files.Length, Theme.Strong);
        position.Append(" · first shown ", Theme.Dim);
        position.Append(_tabs.Offset + 1, Theme.Strong);
        buffer.SetText(inner.X, inner.Y + 1, position.Build(), inner.Width, Overflow.Ellipsis);
        buffer.Render(new Paragraph(About[_tabs.Selected], Theme.Body) { Wrap = TextWrap.Word },
            new Rect(inner.X, inner.Y + 3, inner.Width, inner.Height - 3));
    }
}
