using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="Menu"/>: an "Actions" button that opens a menu under itself (Enter, m or a click), and a right-click
/// anywhere on the page that opens it at the pointer. Items have mnemonics (underlined), shortcuts, separators and a
/// disabled Delete. ↑/↓ or the pointer highlight, Enter, a mnemonic or a click runs, Esc or a click outside closes.
/// </summary>
public sealed class MenuPage : Page
{
    private const string Snippet = """
        Rect anchor = _anchor ?? _button;
        Rect box = buffer.Area.PlaceNear(anchor,
            Menu.Measure(Items));
        buffer.Render(new Menu(Items)
        {
            Popup = new Popup
            {
                Block = MenuBorder(),   // rounded, blue, filled
                Shadow = true,
                ShadowStyle = Theme.Shadow,
            },
            SelectedStyle = Theme.RowSelected,
            ShortcutStyle = Theme.Dim,
            DisabledStyle = Theme.Faded,
            MnemonicStyle = Mnemonic,
        }, box, ref _menu);
        // …
        MenuResult result = ev.Kind == EventKind.Key
            ? _menu.Handle(ev.Key, Items)
            : _menu.HandleMouse(ev.Mouse, Items);
        """;

    private static readonly (string, string)[] Keys = [("enter, m", "open"), ("right-click", "menu here")];
    private static readonly (string, string)[] OpenKeys = [("↑/↓", "move"), ("enter", "run"), ("letter", "mnemonic"), ("esc", "close")];

    private static readonly MenuItem[] Items =
    [
        new("&Open", "enter"),
        new("&Copy path", "y"),
        new("&Rename", "f2"),
        MenuItem.Separator,
        new("Sort by &name"),
        new("Sort by &size"),
        MenuItem.Separator,
        new("&Delete", "del", Enabled: false),   // shows the disabled style; its mnemonic does nothing
    ];

    private static readonly Style Mnemonic = new(Theme.Amber, default, Attr.Underline);

    private MenuState _menu;
    private bool _open;
    private Rect? _anchor;   // where the menu opens: a point for a right-click, null for under the button
    private Rect _button;
    private Rect _area;
    private int _ran = -1;
    private bool _cancelled;

    public MenuPage() : base("Menu", "Menu · MenuItem · MenuState", Snippet, Keys)
    {
    }

    public bool IsOpen => _open;
    public int Highlighted => _menu.Selected;

    /// <summary>The item that ran last, or -1.</summary>
    public int Ran => _ran;

    public override bool WantsKeys => _open;

    internal override (string Key, string Action)[] Hints => _open ? OpenKeys : Keys;

    public override bool Handle(Event ev, long nowMs)
    {
        if (_open && ev.Kind is EventKind.Key or EventKind.Mouse)
        {
            MenuResult result = ev.Kind == EventKind.Key
                ? _menu.Handle(ev.Key, Items)
                : _menu.HandleMouse(ev.Mouse, Items);
            if (result is MenuResult.Activated or MenuResult.Cancelled)
            {
                _open = false;
                _ran = result == MenuResult.Activated ? _menu.Selected : _ran;
                _cancelled = result == MenuResult.Cancelled;
                if (ev.Kind == EventKind.Mouse && result == MenuResult.Cancelled)
                {
                    return HandleClosed(ev);   // a right-click elsewhere reopens it there
                }
            }

            return true;
        }

        return HandleClosed(ev);
    }

    private bool HandleClosed(Event ev)
    {
        if (ev.Kind == EventKind.Key && (ev.Key.Is(KeyCode.Enter) || ev.Key.IsChar('m')))
        {
            Open(null);
            return true;
        }

        if (ev.Kind != EventKind.Mouse)
        {
            return false;
        }

        MouseEvent mouse = ev.Mouse;
        if (mouse.IsClickIn(_button))
        {
            Open(null);
            return true;
        }

        if (mouse.Kind == MouseKind.Down && mouse.Button == MouseButton.Right && mouse.IsIn(_area))
        {
            Open(new Rect(mouse.X, mouse.Y, 0, 0));   // a point: the menu opens at the pointer
            return true;
        }

        return false;
    }

    private void Open(Rect? anchor)
    {
        _anchor = anchor;
        _menu.Reset(Items);
        _open = true;
    }

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        // A hint, the button under it (the menu opens below the button), and what ran last at the bottom.
        _area = area;
        buffer.SetMarkup(area.X, area.Y, "[b fg=#38BDF8]right-click[/][dim] anywhere on this side, or[/]", default, area.Width);
        _button = new Rect(area.X, area.Y + 2, Button.WidthOf("Actions ▾"), 1).Intersect(area);
        buffer.Render(new Button("Actions ▾")
        {
            Style = new Style(Theme.Text, Theme.Raised),
            FocusedStyle = new Style(Theme.Bg, Theme.Blue, Attr.Bold),
            Focused = _open,
        }, _button);

        int y = area.Bottom - 1;
        if (y > _button.Y + 1)
        {
            int x = buffer.SetString(area.X, y, "last: ", Theme.Dim, area.Width);
            if (_ran >= 0)
            {
                Label(buffer, x, y, area.Right, Items[_ran].Label);
            }
            else
            {
                buffer.SetString(x, y, _cancelled ? "cancelled" : "nothing yet", Theme.Faded, area.Right - x);
            }
        }

        if (!_open)
        {
            return;
        }

        // Last, so it sits on top of the page and the code panel.
        Rect anchor = _anchor ?? _button;
        Rect box = buffer.Area.PlaceNear(anchor,
            Menu.Measure(Items));
        buffer.Render(new Menu(Items)
        {
            Popup = new Popup
            {
                Block = MenuBorder(),   // rounded, blue, filled
                Shadow = true,
                ShadowStyle = Theme.Shadow,
            },
            SelectedStyle = Theme.RowSelected,
            ShortcutStyle = Theme.Dim,
            DisabledStyle = Theme.Faded,
            MnemonicStyle = Mnemonic,
        }, box, ref _menu);
    }

    private static Block MenuBorder() => new()
    {
        BorderType = BorderType.Rounded,
        BorderStyle = Theme.Accent(Theme.Blue),
        Style = Theme.Dialog,
    };

    /// <summary>A menu label without its '&amp;' marks.</summary>
    private static void Label(CellBuffer buffer, int x, int y, int right, ReadOnlySpan<char> label)
    {
        foreach (Range part in label.Split('&'))
        {
            x = buffer.SetString(x, y, label[part], Theme.Heading(Theme.Green), right - x);
        }
    }
}
