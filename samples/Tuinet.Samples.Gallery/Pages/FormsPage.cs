using Tuinet.Widgets;

namespace Tuinet.Samples.Gallery;

/// <summary>
/// <see cref="Checkbox"/> (one row and boxed), <see cref="Dropdown{TSource}"/> and <see cref="Button"/>, with a
/// <see cref="FocusRing"/>: Tab and Shift+Tab move focus, space or Enter uses the focused control, a click focuses and
/// uses one. Save writes a summary of the form; Reset puts it back.
/// </summary>
public sealed class FormsPage() : Page("Forms", "Checkbox · Dropdown · Button", Snippet, Keys)
{
    private const string Snippet = """
        buffer.Render(new Checkbox("notify me", _notify)
        {
            Focused = _focus.Is(Notify),
            FocusedStyle = Theme.Focused,
            CheckedStyle = Theme.Accent(Theme.Green),
        }, _targets[Notify]);
        // …
        buffer.Render(dropdown, _targets[Pick], ref _priority);
        // …
        dropdown.RenderPopup(_targets[Pick], buffer,
            ref _priority);
        // …
        buffer.Render(new Button(label)
        {
            Style = ButtonStyle,
            FocusedStyle = ButtonFocused,
            Focused = _focus.Is(control),
        }, _targets[control]);
        // …
        if (_focus.Handle(key)) return true;   // Tab, Shift+Tab
        """;

    private static readonly (string, string)[] Keys = [("tab", "focus"), ("space", "use"), ("enter", "open / press")];

    public const int Notify = 0;
    public const int Dark = 1;
    public const int Pick = 2;
    public const int Save = 3;
    public const int Reset = 4;

    private static readonly string[] Priorities = ["low", "medium", "high", "critical"];

    private readonly Rect[] _targets = new Rect[5];   // where each control was drawn, for clicks
    private readonly char[] _status = new char[64];   // written once per Save, not per frame
    private int _statusLength;
    private FocusRing _focus = new(5);
    private DropdownState _priority = new(selected: 1);
    private bool _notify = true;
    private bool _dark;

    public int Focus => _focus.Current;
    public bool NotifyChecked => _notify;
    public int Priority => _priority.Selected;
    public string Status => new(_status, 0, _statusLength);

    /// <summary>An open list takes every key until it closes.</summary>
    public override bool WantsKeys => _priority.IsOpen;

    public override bool Handle(Event ev, long nowMs)
    {
        if (ev.Kind == EventKind.Mouse)
        {
            return HandleMouse(ev.Mouse);
        }

        if (ev.Kind != EventKind.Key)
        {
            return false;
        }

        KeyEvent key = ev.Key;
        if (_priority.IsOpen)
        {
            _priority.Handle(key, Priorities.Length);
            return true;
        }

        if (_focus.Handle(key)) return true;   // Tab, Shift+Tab

        if (_focus.Is(Pick))
        {
            return _priority.Handle(key, Priorities.Length);
        }

        if (key.IsChar(' ') || key.Is(KeyCode.Enter))
        {
            Use(_focus.Current);
            return true;
        }

        return false;
    }

    /// <summary>A click focuses a control and uses it; with the list open, a click picks from it (or closes it).</summary>
    private bool HandleMouse(MouseEvent mouse)
    {
        if (_priority.IsOpen && _priority.HandleMouse(mouse, Priorities.Length))
        {
            return true;
        }

        if (!_focus.HandleMouse(mouse, _targets))
        {
            return false;
        }

        if (_focus.Is(Pick))
        {
            _priority.HandleMouse(mouse, Priorities.Length);
        }
        else
        {
            Use(_focus.Current);
        }

        return true;
    }

    private void Use(int control)
    {
        switch (control)
        {
            case Notify:
                _notify = !_notify;
                break;
            case Dark:
                _dark = !_dark;
                break;
            case Save:
                _status.AsSpan().TryWrite(
                    $"saved · notify {(_notify ? "on" : "off")} · dark {(_dark ? "on" : "off")} · {Priorities[_priority.Selected]}",
                    out _statusLength);
                break;
            case Reset:
                (_notify, _dark, _priority, _statusLength) = (true, false, new DropdownState(selected: 1), 0);
                break;
        }
    }

    public override void Render(CellBuffer buffer, Rect area, long nowMs)
    {
        // A column of controls: the checkbox, the boxed one (3 rows), the dropdown, then the buttons side by side.
        int x = area.X + 10;
        _targets[Notify] = Clip(new Rect(x, area.Y, 20, 1), area);
        _targets[Dark] = Clip(new Rect(x, area.Y + 2, 20, 3), area);
        _targets[Pick] = Clip(new Rect(x, area.Y + 6, 16, 1), area);
        _targets[Save] = Clip(new Rect(x, area.Y + 8, Button.WidthOf("Save"), 1), area);
        _targets[Reset] = Clip(new Rect(_targets[Save].Right + 2, area.Y + 8, Button.WidthOf("Reset"), 1), area);
        Label(buffer, area, area.Y, "alerts");
        Label(buffer, area, area.Y + 3, "theme");
        Label(buffer, area, area.Y + 6, "priority");

        buffer.Render(new Checkbox("notify me", _notify)
        {
            Focused = _focus.Is(Notify),
            FocusedStyle = Theme.Focused,
            CheckedStyle = Theme.Accent(Theme.Green),
        }, _targets[Notify]);
        buffer.Render(new Checkbox("dark mode", _dark)
        {
            Boxed = true,
            Focused = _focus.Is(Dark),
            BorderStyle = Theme.Faded,
            FocusedStyle = Theme.Heading(Theme.Blue),
            CheckedStyle = Theme.Accent(Theme.Green),
        }, _targets[Dark]);

        var dropdown = new Dropdown<TextItems>(
            new TextItems(Priorities))
        {
            Style = _focus.Is(Pick) ? Theme.Focused : Theme.Field,
            SelectedStyle = Theme.RowSelected,
            PopupStyle = Theme.Dialog,
            PopupShadow = true,
            PopupShadowStyle = Theme.Shadow,
        };
        buffer.Render(dropdown, _targets[Pick], ref _priority);
        RenderButton(buffer, "Save", Save);
        RenderButton(buffer, "Reset", Reset);

        int y = area.Y + 10;
        if (_statusLength > 0 && y < area.Bottom)
        {
            int end = buffer.SetString(area.X, y, "✓ ", Theme.Heading(Theme.Green), area.Width);
            buffer.SetString(end, y, _status.AsSpan(0, _statusLength), Theme.Accent(Theme.Green), area.Right - end, Overflow.Ellipsis);
        }

        dropdown.RenderPopup(_targets[Pick], buffer, ref _priority);
    }

    private static readonly Style ButtonStyle = new(Theme.Text, Theme.Raised);
    private static readonly Style ButtonFocused = new(Theme.Bg, Theme.Green, Attr.Bold);

    private void RenderButton(CellBuffer buffer, string label, int control) =>
        buffer.Render(new Button(label)
        {
            Style = ButtonStyle,
            FocusedStyle = ButtonFocused,
            Focused = _focus.Is(control),
        }, _targets[control]);

    private static void Label(CellBuffer buffer, Rect area, int y, ReadOnlySpan<char> text)
    {
        if (y < area.Bottom)
        {
            buffer.SetString(area.X, y, text, Theme.Dim, 9);
        }
    }

    private static Rect Clip(Rect rect, Rect area) => rect.Intersect(area);
}
