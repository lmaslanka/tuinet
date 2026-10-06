namespace Tuinet.Widgets;

/// <summary>
/// Selected tab and scroll position of a <see cref="Tabs"/> bar. Owned by the app. Render scrolls to keep
/// <see cref="Selected"/> visible whenever it changes (or the bar is resized); in between, <see cref="Offset"/>
/// can scroll on its own, e.g. with the overflow arrows or the wheel.
/// </summary>
public struct TabsState
{
    private int _renderedOffset;
    private int _followed;   // Selected + 1 when render last scrolled to it; 0 before the first render

    public TabsState(int selected = 0) => Selected = selected;

    /// <summary>Selected tab, or -1 for none.</summary>
    public int Selected { get; set; }

    /// <summary>First visible tab when the titles don't fit. Render adjusts it to keep <see cref="Selected"/> visible when that changes.</summary>
    public int Offset { get; set; }

    /// <summary>The row the bar was last rendered into, for mouse hit-testing.</summary>
    public Rect Area { get; private set; }

    /// <summary>The next tab, wrapping to the first.</summary>
    public void Next(int count) => Selected = count == 0 ? -1 : (Selected + 1) % count;

    /// <summary>The previous tab, wrapping to the last.</summary>
    public void Previous(int count) => Selected = count == 0 ? -1 : (Math.Max(Selected, 0) + count - 1) % count;

    /// <summary>Select tab <paramref name="index"/>, clamped to the tabs there are.</summary>
    public void Select(int index, int count) => Selected = count == 0 ? -1 : Math.Clamp(index, 0, count - 1);

    /// <summary>
    /// ←/→ (or h/l) move with wrap-around, Home/End jump to the ends. For when the bar itself has focus;
    /// shortcuts that switch tabs from anywhere (e.g. Alt+1…9) are the app's. Returns whether the key was used.
    /// </summary>
    public bool Handle(KeyEvent key, int count)
    {
        if (key.Is(KeyCode.Left) || key.IsChar('h'))
        {
            Previous(count);
        }
        else if (key.Is(KeyCode.Right) || key.IsChar('l'))
        {
            Next(count);
        }
        else if (key.Is(KeyCode.Home))
        {
            Select(0, count);
        }
        else if (key.Is(KeyCode.End))
        {
            Select(count - 1, count);
        }
        else
        {
            return false;
        }

        return true;
    }

    /// <summary>The offset the bar was last drawn with: hit-testing uses it even if <see cref="Offset"/> changed since.</summary>
    internal readonly int RenderedOffset => _renderedOffset;

    /// <summary>Whether render should scroll to <see cref="Selected"/>: it changed, or the bar was resized.</summary>
    internal readonly bool NeedsFollow(Rect row) => Selected + 1 != _followed || row.Width != Area.Width;

    internal void Rendered(Rect row, int offset)
    {
        Offset = offset;
        Area = row;
        _renderedOffset = offset;
        _followed = Selected + 1;
    }
}

/// <summary>
/// A one-row tab bar: titles with padding, separated by <see cref="Divider"/>. When they don't fit, it scrolls
/// to keep the selected tab visible and shows ‹ › where tabs are hidden. Only the tabs are drawn; the rest of the
/// row is left as it is, so the bar can sit in a <see cref="Block"/>'s top border.
/// </summary>
public readonly ref struct Tabs : IStatefulWidget<TabsState>
{
    private const int NoHit = -1;
    private const int LeftArrow = -2;
    private const int RightArrow = -3;

    private readonly ReadOnlySpan<string> _titles;

    public Tabs(ReadOnlySpan<string> titles) => _titles = titles;

    /// <summary>Unselected titles and their padding.</summary>
    public Style Style { get; init; }

    /// <summary>Layered over <see cref="Style"/> for the selected title and its padding.</summary>
    public Style SelectedStyle { get; init; }

    /// <summary>Drawn between titles; empty for none.</summary>
    public ReadOnlySpan<char> Divider { get; init; } = "│";

    public Style DividerStyle { get; init; }

    /// <summary>Blank cells on each side of a title.</summary>
    public int Padding { get; init; } = 1;

    /// <summary>The ‹ and › overflow markers.</summary>
    public Style ArrowStyle { get; init; }

    /// <summary>Columns the whole bar needs to show every title.</summary>
    public int Width
    {
        get
        {
            int width = 0;
            for (int i = 0; i < _titles.Length; i++)
            {
                width += TabWidth(i) + (i > 0 ? DividerWidth : 0);
            }

            return width;
        }
    }

    private int DividerWidth => Divider.IsEmpty ? 0 : TextWidth.Of(Divider);

    private int TabWidth(int index) => 2 * Math.Max(0, Padding) + TextWidth.Of(_titles[index]);

    public void Render(Rect area, CellBuffer buffer, ref TabsState state)
    {
        area = area.Intersect(buffer.Area);
        var row = new Rect(area.X, area.Y, area.Width, Math.Min(area.Height, 1));
        int count = _titles.Length;
        if (state.Selected >= count)
        {
            state.Selected = count - 1;
        }

        if (row.IsEmpty || count == 0)
        {
            state.Rendered(row, 0);
            return;
        }

        bool overflow = Width > row.Width;
        Rect strip = Strip(row, overflow);
        int offset = state.Offset;
        if (state.NeedsFollow(row) && state.Selected >= 0)
        {
            if (state.Selected < offset)
            {
                offset = state.Selected;
            }

            while (offset < state.Selected && !Fits(offset, state.Selected, strip.Width))
            {
                offset++;
            }
        }

        int maxOffset = MaxOffset(strip.Width);
        offset = Math.Clamp(offset, 0, maxOffset);
        state.Rendered(row, offset);

        int x = strip.X;
        int divider = DividerWidth;
        int padding = Math.Max(0, Padding);
        for (int i = offset; i < count && x < strip.Right; i++)
        {
            if (i > offset && divider > 0)
            {
                x = buffer.SetString(x, row.Y, Divider, DividerStyle, strip.Right - x);
            }

            int width = Math.Min(TabWidth(i), strip.Right - x);
            if (width <= 0)
            {
                break;
            }

            Style style = i == state.Selected ? Style.Patch(SelectedStyle) : Style;
            buffer.Erase(new Rect(x, row.Y, width, 1), style);
            buffer.SetString(x + padding, row.Y, _titles[i], style, Math.Max(0, width - padding));   // a cut tab loses its right padding first
            x += width;
        }

        if (overflow)
        {
            buffer.SetString(row.X, row.Y, offset > 0 ? "‹" : " ", ArrowStyle, 1);
            buffer.SetString(row.Right - 1, row.Y, offset < maxOffset ? "›" : " ", ArrowStyle, 1);
        }
    }

    /// <summary>The tab drawn at cell (<paramref name="x"/>, <paramref name="y"/>) at the last render, or -1 (dividers, arrows, empty space).</summary>
    public int TabAt(int x, int y, in TabsState state)
    {
        int hit = Hit(x, y, state);
        return hit >= 0 ? hit : -1;
    }

    /// <summary>
    /// A click on a title selects it; a click on ‹ or › scrolls by one tab; the wheel over the bar scrolls it.
    /// Returns whether the event was used (false for events elsewhere), so the caller can route it on.
    /// </summary>
    public bool HandleMouse(MouseEvent ev, ref TabsState state)
    {
        if (!ev.IsIn(state.Area))
        {
            return false;
        }

        int scroll = ev.Kind switch
        {
            MouseKind.ScrollUp or MouseKind.ScrollLeft => -1,
            MouseKind.ScrollDown or MouseKind.ScrollRight => 1,
            _ => 0,
        };
        if (scroll == 0 && ev.IsClick)
        {
            int hit = Hit(ev.X, ev.Y, state);
            if (hit >= 0)
            {
                state.Selected = hit;
                return true;
            }

            scroll = hit == LeftArrow ? -1 : hit == RightArrow ? 1 : 0;
        }

        if (scroll == 0)
        {
            return false;
        }

        bool overflow = Width > state.Area.Width;
        state.Offset = Math.Clamp(state.RenderedOffset + scroll, 0, overflow ? MaxOffset(Strip(state.Area, true).Width) : 0);
        return true;
    }

    private int Hit(int x, int y, in TabsState state)
    {
        Rect row = state.Area;
        if (!row.Contains(x, y) || _titles.Length == 0)
        {
            return NoHit;
        }

        bool overflow = Width > row.Width;
        Rect strip = Strip(row, overflow);
        int offset = state.RenderedOffset;
        if (overflow && x == row.X)
        {
            return offset > 0 ? LeftArrow : NoHit;
        }

        if (overflow && x == row.Right - 1)
        {
            return offset < MaxOffset(strip.Width) ? RightArrow : NoHit;
        }

        int left = strip.X;
        int divider = DividerWidth;
        for (int i = offset; i < _titles.Length && left < strip.Right; i++)
        {
            int right = Math.Min(left + TabWidth(i), strip.Right);
            if (x >= left && x < right)
            {
                return i;
            }

            left = right + divider;
        }

        return NoHit;
    }

    /// <summary>Where the titles go: the whole row, or between the arrow cells when they overflow.</summary>
    private static Rect Strip(Rect row, bool overflow) =>
        overflow ? new Rect(row.X + 1, row.Y, row.Width - 2, row.Height) : row;

    /// <summary>Tabs <paramref name="first"/>…<paramref name="last"/> fit whole in <paramref name="width"/> columns.</summary>
    private bool Fits(int first, int last, int width)
    {
        int used = -DividerWidth;
        for (int i = first; i <= last; i++)
        {
            used += DividerWidth + TabWidth(i);
        }

        return used <= width;
    }

    /// <summary>The smallest offset from which every remaining tab fits whole (no point scrolling further).</summary>
    private int MaxOffset(int width)
    {
        int used = -DividerWidth;
        for (int i = _titles.Length - 1; i >= 0; i--)
        {
            used += DividerWidth + TabWidth(i);
            if (used > width)
            {
                return Math.Min(i + 1, _titles.Length - 1);
            }
        }

        return 0;
    }
}
