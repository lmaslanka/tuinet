namespace Tuinet;

/// <summary>
/// Which of <see cref="Count"/> controls has focus, as an index that wraps around: Tab and Shift+Tab move it, and a
/// click on a control focuses it. A plain value kept in your state; widgets still take <c>Focused = ring.Is(i)</c>.
/// <code>
/// var focus = new FocusRing(3);                        // name, notify, Save
/// if (ev.Kind == EventKind.Key &amp;&amp; focus.Handle(ev.Key)) return;
/// frame.Render(new Button("Save") { Focused = focus.Is(2) }, saveArea);
/// </code>
/// </summary>
public struct FocusRing
{
    private int _current;

    public FocusRing(int count, int current = 0)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(count);
        Count = count;
        if (count > 0)
        {
            Current = current;
        }
    }

    /// <summary>Number of focusable controls.</summary>
    public int Count { get; }

    /// <summary>The focused control: 0 to <see cref="Count"/> - 1 (0 when <see cref="Count"/> is 0).</summary>
    public int Current
    {
        readonly get => _current;
        set
        {
            ArgumentOutOfRangeException.ThrowIfNegative(value);
            ArgumentOutOfRangeException.ThrowIfGreaterThanOrEqual(value, Count);
            _current = value;
        }
    }

    /// <summary>Whether control <paramref name="index"/> has focus.</summary>
    public readonly bool Is(int index) => Count > 0 && index == _current;

    public void Next() => Move(1);

    public void Previous() => Move(-1);

    /// <summary>Move focus <paramref name="delta"/> controls forward (or back, when negative), wrapping around.</summary>
    public void Move(int delta)
    {
        if (Count > 0)
        {
            _current = (int)(((long)_current + delta) % Count + Count) % Count;
        }
    }

    /// <summary>Tab focuses the next control and Shift+Tab the previous one. Returns false for any other key, or when empty.</summary>
    public bool Handle(KeyEvent key)
    {
        if (Count == 0)
        {
            return false;
        }

        if (key.Is(KeyCode.Tab))
        {
            Next();
            return true;
        }

        if (key.Is(KeyCode.Tab, Modifiers.Shift))
        {
            Previous();
            return true;
        }

        return false;
    }

    /// <summary>
    /// A click inside <c>areas[i]</c>, where control <c>i</c> was drawn, focuses it. Returns false for anything else
    /// (focus is unchanged), so the caller can go on to use the clicked control.
    /// </summary>
    public bool HandleMouse(MouseEvent mouse, ReadOnlySpan<Rect> areas)
    {
        if (!mouse.IsClick)
        {
            return false;
        }

        int n = Math.Min(areas.Length, Count);
        for (int i = 0; i < n; i++)
        {
            if (mouse.IsIn(areas[i]))
            {
                _current = i;
                return true;
            }
        }

        return false;
    }
}
