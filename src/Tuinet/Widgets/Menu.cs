using System.Text;

namespace Tuinet.Widgets;

/// <summary>
/// One row of a <see cref="Menu"/>. An '&amp;' in <see cref="Label"/> marks the next letter as its mnemonic (drawn with
/// <see cref="Menu.MnemonicStyle"/>; pressing it activates the item); "&amp;&amp;" is a literal '&amp;'. Items are values: build
/// the array once and swap one in with <c>with { Enabled = … }</c> to change it.
/// </summary>
public readonly record struct MenuItem(string Label, string Shortcut = "", bool Enabled = true)
{
    /// <summary>A divider line, drawn joined to the menu's border. Never selected.</summary>
    public static MenuItem Separator { get; } = new("", "", false) { IsSeparator = true };

    public bool IsSeparator { get; private init; }

    /// <summary>Whether the item can be selected and activated.</summary>
    internal bool Selectable => Enabled && !IsSeparator;

    /// <summary>The mnemonic letter, lowercased, or '\0' for none.</summary>
    internal char Mnemonic
    {
        get
        {
            ReadOnlySpan<char> label = Label;
            for (int i = 0; i < label.Length - 1; i++)
            {
                if (label[i] == '&')
                {
                    if (label[i + 1] != '&')
                    {
                        return char.ToLowerInvariant(label[i + 1]);
                    }

                    i++;
                }
            }

            return '\0';
        }
    }
}

/// <summary>What a <see cref="MenuState"/> did with an event.</summary>
public enum MenuResult : byte
{
    /// <summary>Not a menu event: route it on.</summary>
    Unhandled,

    /// <summary>Used (the highlight moved, the list scrolled, a disabled item was clicked).</summary>
    Handled,

    /// <summary><see cref="MenuState.Selected"/> was chosen: run it and close the menu.</summary>
    Activated,

    /// <summary>Esc, or a click outside the menu: close it. For a click, the caller can still act on it.</summary>
    Cancelled,
}

/// <summary>The highlighted item and scroll position of a <see cref="Menu"/>, and where it was drawn. Owned by the app.</summary>
public struct MenuState
{
    private ListState _list;

    /// <summary>The highlighted item: never a separator or a disabled item; -1 when none can be selected.</summary>
    public int Selected { get; private set; }

    /// <summary>Where the menu was last drawn (border included, shadow excluded), for mouse hit-testing.</summary>
    public Rect Area { get; private set; }

    /// <summary>Highlight the first selectable item and scroll to the top, e.g. each time the menu opens.</summary>
    public void Reset(ReadOnlySpan<MenuItem> items)
    {
        Selected = Step(items, -1, 1, wrap: false);
        _list = new ListState(Selected);
        Area = default;
    }

    /// <summary>
    /// Up/Down (and k/j) move to the previous or next selectable item, wrapping; Home/End go to the first or last;
    /// Enter or Space activates; Esc cancels; an item's mnemonic letter activates it. j and k always move, so don't
    /// use them as mnemonics. Other keys are <see cref="MenuResult.Unhandled"/>.
    /// </summary>
    public MenuResult Handle(KeyEvent key, ReadOnlySpan<MenuItem> items)
    {
        if (key.Kind == KeyKind.Release)
        {
            return MenuResult.Unhandled;
        }

        if (key.Is(KeyCode.Down) || key.IsChar('j'))
        {
            Select(Step(items, Selected, 1, wrap: true));
        }
        else if (key.Is(KeyCode.Up) || key.IsChar('k'))
        {
            Select(Step(items, Selected, -1, wrap: true));
        }
        else if (key.Is(KeyCode.Home))
        {
            Select(Step(items, -1, 1, wrap: false));
        }
        else if (key.Is(KeyCode.End))
        {
            Select(Step(items, items.Length, -1, wrap: false));
        }
        else if (key.Is(KeyCode.Enter) || key.IsChar(' '))
        {
            return IsSelectable(items, Selected) ? MenuResult.Activated : MenuResult.Handled;
        }
        else if (key.Is(KeyCode.Escape))
        {
            return MenuResult.Cancelled;
        }
        else if (key.Code == KeyCode.Char && (key.Modifiers & ~Modifiers.Shift) == 0 && key.Rune.IsBmp)
        {
            int c = char.ToLowerInvariant((char)key.Rune.Value);
            for (int i = 0; i < items.Length; i++)
            {
                if (items[i].Selectable && items[i].Mnemonic == c)
                {
                    Select(i);
                    return MenuResult.Activated;
                }
            }

            return MenuResult.Unhandled;
        }
        else
        {
            return MenuResult.Unhandled;
        }

        return MenuResult.Handled;
    }

    /// <summary>
    /// A click on an item activates it; the pointer moving over one highlights it (needs
    /// <see cref="TerminalOptions.MouseMotion"/>, or a held button); the wheel scrolls a menu that doesn't fit. A
    /// press anywhere outside <see cref="Area"/> is <see cref="MenuResult.Cancelled"/>, and the caller can still act
    /// on it (e.g. a right-click elsewhere opens the menu there).
    /// </summary>
    public MenuResult HandleMouse(MouseEvent ev, ReadOnlySpan<MenuItem> items)
    {
        if (!ev.IsIn(Area))
        {
            return ev.Kind == MouseKind.Down ? MenuResult.Cancelled : MenuResult.Unhandled;
        }

        if (ev.IsWheel)
        {
            _list.Scroll(ev.WheelDelta, items.Length);
            return MenuResult.Handled;
        }

        int row = _list.RowAt(ev.X, ev.Y, items.Length);
        if (row < 0 || !items[row].Selectable)
        {
            return MenuResult.Handled;
        }

        if (ev.IsClick)
        {
            Select(row);
            return MenuResult.Activated;
        }

        if (ev.Kind is MouseKind.Move or MouseKind.Drag)
        {
            Selected = row;   // no scrolling: the pointer is already on it
            _list.Selected = row;
        }

        return MenuResult.Handled;
    }

    /// <summary>Called by render: keeps <see cref="Selected"/> valid and in view of the <paramref name="body"/> rows.</summary>
    internal void Follow(ReadOnlySpan<MenuItem> items, Rect area, Rect body)
    {
        if (!IsSelectable(items, Selected))
        {
            Selected = Step(items, Math.Clamp(Selected, 0, items.Length) - 1, 1, wrap: true);
        }

        _list.Selected = Selected;
        _list.Follow(items.Length, area, body);
        Area = area;
    }

    internal readonly int Offset => _list.Offset;

    private void Select(int index)
    {
        if (index >= 0)
        {
            Selected = index;
            _list.Selected = index;
        }
    }

    private static bool IsSelectable(ReadOnlySpan<MenuItem> items, int index) => (uint)index < (uint)items.Length && items[index].Selectable;

    /// <summary>The next selectable item from <paramref name="from"/> in direction <paramref name="step"/>, or -1.</summary>
    private static int Step(ReadOnlySpan<MenuItem> items, int from, int step, bool wrap)
    {
        int n = items.Length;
        for (int k = 1; k <= n; k++)
        {
            int i = from + step * k;
            if (wrap)
            {
                i = ((i % n) + n) % n;
            }
            else if ((uint)i >= (uint)n)
            {
                return -1;
            }

            if (items[i].Selectable)
            {
                return i;
            }
        }

        return -1;
    }
}

/// <summary>
/// A popup list of actions: labels with mnemonics, right-aligned shortcuts, separators joined to the border and
/// disabled items. Size it with <see cref="Measure"/> and place it with <see cref="Rect.PlaceNear"/>, by the row it
/// belongs to or at a 0×0 point at the pointer; render it last so it sits on top. The area is the box: a
/// <see cref="Popup.Shadow"/> hangs off it. A menu taller than its area scrolls to keep the highlight in view.
/// </summary>
/// <example><code>
/// Rect box = frame.Area.PlaceNear(new Rect(mouse.X, mouse.Y, 0, 0), Menu.Measure(items));
/// frame.Render(new Menu(items) { Popup = popup, SelectedStyle = highlight }, box, ref menu);
/// </code></example>
public readonly ref struct Menu : IStatefulWidget<MenuState>
{
    private const int ShortcutGap = 2;
    private readonly ReadOnlySpan<MenuItem> _items;

    public Menu(ReadOnlySpan<MenuItem> items) => _items = items;

    /// <summary>The border, fill and shadow. Its <see cref="Popup.Padding"/> is ignored: rows have one blank column on each side.</summary>
    public Popup Popup { get; init; } = new();

    /// <summary>Layered over the highlighted row.</summary>
    public Style SelectedStyle { get; init; } = new(Color.Default, Color.Default, Attr.Reverse);

    public Style ShortcutStyle { get; init; } = new(Color.Default, Color.Default, Attr.Dim);
    public Style DisabledStyle { get; init; } = new(Color.Default, Color.Default, Attr.Dim);

    /// <summary>Layered over each item's mnemonic letter.</summary>
    public Style MnemonicStyle { get; init; } = new(Color.Default, Color.Default, Attr.Underline);

    /// <summary>The box for <paramref name="items"/>: a border, a blank column each side, the widest label, a gap and the widest shortcut.</summary>
    public static Size Measure(ReadOnlySpan<MenuItem> items)
    {
        int label = 0;
        int shortcut = 0;
        foreach (MenuItem item in items)
        {
            label = Math.Max(label, LabelWidth(item.Label));
            shortcut = Math.Max(shortcut, TextWidth.Of(item.Shortcut));
        }

        return new Size(4 + label + (shortcut > 0 ? ShortcutGap + shortcut : 0), items.Length + 2);
    }

    public void Render(Rect area, CellBuffer buffer, ref MenuState state)
    {
        Popup popup = Popup with { Padding = 0 };
        Rect outer = popup.Shadow ? new Rect(area.X, area.Y, area.Width + 2, area.Height + 1) : area;
        Rect box = popup.Frame(outer);
        Rect body = popup.Block.Inner(box).Intersect(buffer.Area);
        state.Follow(_items, box, body);
        buffer.Render(popup, outer);
        if (body.IsEmpty)
        {
            return;
        }

        bool scrolls = _items.Length > body.Height;
        for (int row = 0; row < body.Height; row++)
        {
            int index = state.Offset + row;
            if (index >= _items.Length)
            {
                break;
            }

            var line = new Rect(body.X, body.Y + row, body.Width, 1);
            MenuItem item = _items[index];
            if (item.IsSeparator)
            {
                RenderSeparator(buffer, box, line, popup.Block);
                continue;
            }

            Style style = item.Enabled ? popup.Block.Style : popup.Block.Style.Patch(DisabledStyle);
            int right = line.Right - 1;
            int shortcutWidth = TextWidth.Of(item.Shortcut);
            if (shortcutWidth > 0)
            {
                right -= shortcutWidth;
                Style shortcutStyle = item.Enabled ? style.Patch(ShortcutStyle) : style;
                buffer.SetString(right, line.Y, item.Shortcut, shortcutStyle, line.Right - 1 - right);
                right -= ShortcutGap;
            }

            RenderLabel(buffer, line.X + 1, line.Y, right, item, style);
            if (index == state.Selected)
            {
                buffer.SetStyle(line, SelectedStyle);
            }
        }

        if (scrolls)
        {
            // In the blank column on the right.
            buffer.Render(new Scrollbar(_items.Length, body.Height, state.Offset) { ThumbStyle = popup.Block.BorderStyle, TrackChar = ' ' },
                new Rect(body.Right - 1, body.Y, 1, body.Height));
        }
    }

    /// <summary>The label without its '&amp;' marks, the mnemonic letter in <see cref="MnemonicStyle"/>, cut before <paramref name="right"/>.</summary>
    private void RenderLabel(CellBuffer buffer, int x, int y, int right, MenuItem item, Style style)
    {
        ReadOnlySpan<char> label = item.Label;
        bool marked = false;
        while (!label.IsEmpty && x < right)
        {
            int amp = label.IndexOf('&');
            if (amp < 0 || amp == label.Length - 1)
            {
                buffer.SetString(x, y, label, style, right - x);
                return;
            }

            x = buffer.SetString(x, y, label[..amp], style, right - x);
            label = label[(amp + 1)..];
            if (label[0] == '&')
            {
                x = buffer.SetString(x, y, "&", style, right - x);
            }
            else
            {
                x = buffer.SetString(x, y, label[..1], marked ? style : style.Patch(MnemonicStyle), right - x);
                marked = true;
            }

            label = label[1..];
        }
    }

    /// <summary>A line across the row, joined to the left and right borders with tees that match them.</summary>
    private static void RenderSeparator(CellBuffer buffer, Rect box, Rect line, Block block)
    {
        (char h, char left, char right) = block.BorderType switch
        {
            BorderType.Double => ('─', '╟', '╢'),
            BorderType.Thick => ('─', '┠', '┨'),
            BorderType.Dashed => ('┄', '├', '┤'),
            _ => ('─', '├', '┤'),
        };

        Style style = block.Style.Patch(block.BorderStyle);
        buffer.SetRune(line, new Rune(h), style);
        if ((block.Borders & Borders.Left) != 0 && line.X > box.X)
        {
            buffer.SetRune(box.X, line.Y, new Rune(left), style);
        }

        if ((block.Borders & Borders.Right) != 0 && line.Right < box.Right)
        {
            buffer.SetRune(box.Right - 1, line.Y, new Rune(right), style);
        }
    }

    /// <summary>Columns <paramref name="label"/> takes on screen: its '&amp;' marks aren't drawn.</summary>
    private static int LabelWidth(ReadOnlySpan<char> label)
    {
        int width = TextWidth.Of(label);
        for (int i = 0; i < label.Length - 1; i++)
        {
            if (label[i] == '&')
            {
                width--;
                i++;   // "&&" draws one '&'
            }
        }

        return width;
    }
}
