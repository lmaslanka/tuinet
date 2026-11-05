namespace Tuinet.Widgets;

/// <summary>
/// One entry of a <see cref="KeyHints"/> row: "<paramref name="Key"/> <paramref name="Action"/>". When the row is
/// too narrow, hints with a lower <paramref name="Priority"/> are dropped first.
/// </summary>
public readonly record struct KeyHint(string Key, string Action, int Priority = 0);

/// <summary>
/// A row of key hints ("j/k move  ·  enter edit  ·  q quit"). When they don't fit, whole hints are dropped, never cut:
/// the lowest <see cref="KeyHint.Priority"/> first (ties: from the end), keeping the order of the rest; a hint wider
/// than the row on its own is never shown. <see cref="HintAt"/> recomputes the same fit, so a click on a hint can run
/// the same action as its key. Keep the hint array static: building it is the only cost, and it is the app's.
/// <code>
/// static readonly KeyHint[] Hints = [new("j/k", "move", 2), new("enter", "edit", 1), new("?", "help"), new("q", "quit", 3)];
/// buffer.Render(new KeyHints(Hints) { KeyStyle = key, ActionStyle = dim }, row);
/// </code>
/// </summary>
public readonly ref struct KeyHints : IWidget
{
    private readonly ReadOnlySpan<KeyHint> _hints;

    public KeyHints(ReadOnlySpan<KeyHint> hints) => _hints = hints;

    public Style KeyStyle { get; init; }

    public Style ActionStyle { get; init; }

    /// <summary>Drawn between hints; empty for none.</summary>
    public ReadOnlySpan<char> Separator { get; init; } = "  ·  ";

    public Style SeparatorStyle { get; init; }

    /// <summary>Columns every hint needs, separators included.</summary>
    public int Width
    {
        get
        {
            int width = 0;
            for (int i = 0; i < _hints.Length; i++)
            {
                width += HintWidth(i) + (i > 0 ? SeparatorWidth : 0);
            }

            return width;
        }
    }

    /// <summary>Columns the hints that fit in <paramref name="width"/> take, e.g. to right-align them.</summary>
    public int Measure(int width)
    {
        Fit(width, out _, out _, out int used);
        return used;
    }

    /// <summary>Draws the hints that fit on the first row of <paramref name="area"/>; the rest of the row is left as it is.</summary>
    public void Render(Rect area, CellBuffer buffer)
    {
        if (area.Width <= 0 || area.Height <= 0)
        {
            return;
        }

        Fit(area.Width, out int cutPriority, out int cutIndex, out _);
        int x = area.X;
        bool first = true;
        for (int i = 0; i < _hints.Length; i++)
        {
            if (!Kept(i, area.Width, cutPriority, cutIndex))
            {
                continue;
            }

            if (!first)
            {
                x = buffer.SetString(x, area.Y, Separator, SeparatorStyle);
            }

            first = false;
            KeyHint hint = _hints[i];
            x = buffer.SetString(x, area.Y, hint.Key, KeyStyle);
            if (!string.IsNullOrEmpty(hint.Action))
            {
                x = buffer.SetString(x, area.Y, " ");   // over whatever is underneath, e.g. a status bar's fill
                x = buffer.SetString(x, area.Y, hint.Action, ActionStyle);
            }
        }
    }

    /// <summary>
    /// The index of the hint drawn at cell (<paramref name="x"/>, <paramref name="y"/>) when rendered into
    /// <paramref name="area"/>, or -1 (separators and empty space).
    /// </summary>
    public int HintAt(int x, int y, Rect area)
    {
        if (y != area.Y || area.Height <= 0 || x < area.X || x >= area.Right)
        {
            return -1;
        }

        Fit(area.Width, out int cutPriority, out int cutIndex, out _);
        int left = area.X;
        bool first = true;
        for (int i = 0; i < _hints.Length && left <= x; i++)
        {
            if (!Kept(i, area.Width, cutPriority, cutIndex))
            {
                continue;
            }

            left += first ? 0 : SeparatorWidth;
            first = false;
            int right = left + HintWidth(i);
            if (x >= left && x < right)
            {
                return i;
            }

            left = right;
        }

        return -1;
    }

    private int SeparatorWidth => Separator.IsEmpty ? 0 : TextWidth.Of(Separator);

    private int HintWidth(int index)
    {
        KeyHint hint = _hints[index];
        int action = string.IsNullOrEmpty(hint.Action) ? 0 : 1 + TextWidth.Of(hint.Action);
        return TextWidth.Of(hint.Key) + action;
    }

    /// <summary>
    /// Hint <paramref name="index"/> is shown: it fits on its own, and it comes after the cut in drop order (lowest
    /// priority first, then from the end). The cut is the last hint dropped; nothing is cut before the first drop.
    /// </summary>
    private bool Kept(int index, int width, int cutPriority, int cutIndex)
    {
        int priority = _hints[index].Priority;
        bool dropped = priority < cutPriority || (priority == cutPriority && index >= cutIndex);
        return !dropped && HintWidth(index) <= width;
    }

    /// <summary>
    /// Drops hints in priority order until the rest fit in <paramref name="width"/>. The dropped ones are always a prefix
    /// of the drop order, so the result is two numbers rather than a flag per hint: render and hit-testing share it.
    /// </summary>
    private void Fit(int width, out int cutPriority, out int cutIndex, out int used)
    {
        cutPriority = int.MinValue;
        cutIndex = int.MaxValue;
        int separator = SeparatorWidth;
        int count = 0;
        used = 0;
        for (int i = 0; i < _hints.Length; i++)
        {
            if (Kept(i, width, cutPriority, cutIndex))
            {
                used += HintWidth(i) + (count > 0 ? separator : 0);
                count++;
            }
        }

        while (used > width && count > 0)
        {
            // The next to drop: the lowest priority among those kept, the last of them on a tie.
            int next = -1;
            for (int i = 0; i < _hints.Length; i++)
            {
                if (Kept(i, width, cutPriority, cutIndex) && (next < 0 || _hints[i].Priority <= _hints[next].Priority))
                {
                    next = i;
                }
            }

            cutPriority = _hints[next].Priority;
            cutIndex = next;
            count--;
            used -= HintWidth(next) + (count > 0 ? separator : 0);
        }
    }
}
