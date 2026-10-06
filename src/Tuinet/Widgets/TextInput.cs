namespace Tuinet.Widgets;

/// <summary>
/// Editable single-line text: emacs-style editing keys, Shift+movement selection, undo/redo and optional masking,
/// on the shared <see cref="EditableText"/> core. The caret moves and deletes by grapheme cluster (an emoji sequence
/// or a letter with its accents is one step). Line breaks and other control characters are dropped. Owned by the app.
/// </summary>
public sealed class TextInputState : EditableText
{
    /// <summary>First char shown at the last render (0 when unfocused, which shows the beginning).</summary>
    private int _shownScroll;
    private bool _dragging;

    /// <summary>Text with the caret at its end.</summary>
    public TextInputState(string? text = null, char? mask = null)
        : base(multiline: false, caretAtEndOnSet: true, text)
    {
        Mask = mask;
    }

    /// <summary>Character drawn instead of each grapheme cluster (e.g. '•' for passwords), or null.</summary>
    public char? Mask { get; set; }

    /// <summary>First visible char; maintained by <see cref="TextInput"/> to keep the caret in view.</summary>
    public int Scroll { get; internal set; }

    /// <summary>The row the input was last rendered on, for mouse hit-testing.</summary>
    public Rect Area { get; private set; }

    /// <summary>
    /// Apply an editing key. Returns false if the key is not an editing key (e.g. Tab, Enter, Up). On top of the
    /// shared keys (see <see cref="EditableText"/>): Home/End (Shift selects), Ctrl+A/E/B/F, Ctrl+U/K/W/D and
    /// Alt+B/F/D.
    /// </summary>
    public bool Handle(KeyEvent key)
    {
        if (key.Kind == KeyKind.Release)
        {
            return false;
        }

        Modifiers mods = key.Modifiers;
        switch (key.Code)
        {
            case KeyCode.Char when mods == Modifiers.Ctrl:
                switch (key.Rune.Value)
                {
                    case 'a': MoveTo(0); return true;
                    case 'e': MoveTo(Length); return true;
                    case 'b': MoveTo(PrevCluster(Caret)); return true;
                    case 'f': MoveTo(NextCluster(Caret)); return true;
                    case 'u': Delete(0, Caret); return true;
                    case 'k': Delete(Caret, Length); return true;
                    case 'w': Delete(WordStart(Caret), Caret); return true;
                    case 'd': Delete(Caret, NextCluster(Caret)); return true;
                }

                break;
            case KeyCode.Char when mods == Modifiers.Alt:
                switch (key.Rune.Value)
                {
                    case 'b': MoveTo(WordStart(Caret)); return true;
                    case 'f': MoveTo(WordEnd(Caret)); return true;
                    case 'd': Delete(Caret, WordEnd(Caret)); return true;
                    default: return false;
                }

            case KeyCode.Home:
                MoveTo(0, (mods & Modifiers.Shift) != 0);
                return true;
            case KeyCode.End:
                MoveTo(Length, (mods & Modifiers.Shift) != 0);
                return true;
        }

        return HandleCommon(key);
    }

    /// <summary>
    /// A click on the input moves the caret to the clicked cluster (or the end, past the text), as the last render
    /// showed it, so clicking an unfocused input works too. Shift+click selects to there, and dragging selects.
    /// Returns whether the event was used.
    /// </summary>
    public bool HandleMouse(MouseEvent ev)
    {
        if (_dragging && ev.Kind == MouseKind.Drag)
        {
            MoveTo(OffsetAt(ev.X), extend: true);
            return true;
        }

        if (ev.Kind == MouseKind.Up && _dragging)
        {
            _dragging = false;
            return true;
        }

        if (!ev.IsClickIn(Area))
        {
            return false;
        }

        MoveTo(OffsetAt(ev.X), extend: (ev.Modifiers & Modifiers.Shift) != 0);
        Scroll = Math.Min(_shownScroll, Caret);
        _dragging = true;
        return true;
    }

    /// <summary>The cluster boundary at screen column <paramref name="x"/>, as the last render laid the text out.</summary>
    private int OffsetAt(int x)
    {
        ReadOnlySpan<char> text = Slice(0, Length);
        int column = x - Area.X;
        int at = 0;
        int i = Math.Min(_shownScroll, text.Length);
        while (i < text.Length)
        {
            int width = TextInput.ClusterWidth(text, i, Mask, out int end);
            if (column < at + width)
            {
                break;
            }

            at += width;
            i = end;
        }

        return i;
    }

    internal void Rendered(Rect row, int scroll)
    {
        Area = row;
        _shownScroll = scroll;
    }

    private protected override void OnReset() => Scroll = 0;
}

/// <summary>
/// Draws a <see cref="TextInputState"/> on the first row of its area, scrolled to keep the caret visible. When
/// <see cref="Focused"/>, places the real terminal cursor at the caret and shows the selection.
/// </summary>
public readonly ref struct TextInput : IStatefulWidget<TextInputState>
{
    /// <summary>Columns of the cluster starting at <paramref name="start"/> (1 per cluster when masked).</summary>
    internal static int ClusterWidth(ReadOnlySpan<char> text, int start, char? mask, out int end)
    {
        end = start + EditableText.ClusterLength(text[start..]);
        return mask is char m ? TextWidth.Of(new System.Text.Rune(m)) : TextWidth.Of(text[start..end]);
    }

    public TextInput()
    {
    }

    /// <summary>Text style; the row is erased with it (default colors keep the background underneath).</summary>
    public Style Style { get; init; }

    /// <summary>Shown while the input is empty.</summary>
    public ReadOnlySpan<char> Placeholder { get; init; }

    public Style PlaceholderStyle { get; init; }

    /// <summary>Layered over <see cref="Style"/> for selected text; reverse video by default.</summary>
    public Style SelectionStyle { get; init; } = new(Color.Default, Color.Default, Attr.Reverse);

    /// <summary>Shows the terminal cursor at the caret, and the selection.</summary>
    public bool Focused { get; init; }

    /// <summary>Shape of the caret while <see cref="Focused"/>: a blinking bar, as in GUI text fields.</summary>
    public CursorShape CursorShape { get; init; } = CursorShape.BlinkingBar;

    public void Render(Rect area, CellBuffer buffer, ref TextInputState state)
    {
        area = area.Intersect(buffer.Area);
        if (area.IsEmpty)
        {
            return;
        }

        var row = new Rect(area.X, area.Y, area.Width, 1);
        buffer.Erase(row, Style);

        if (state.IsEmpty && !Placeholder.IsEmpty)
        {
            buffer.SetString(row.X, row.Y, Placeholder, Style.Patch(PlaceholderStyle), row.Width, Overflow.Ellipsis);
        }

        ReadOnlySpan<char> text = state.Slice(0, state.Length);
        char? mask = state.Mask;

        // Unfocused inputs show their beginning; focused ones scroll to keep the caret visible.
        int caret = Focused ? state.Caret : 0;
        int scroll = Focused ? Math.Min(state.Scroll, caret) : 0;

        // Keep the caret cell on screen: columns from scroll to caret, plus one for the caret itself.
        int columns = 1;
        for (int i = scroll; i < caret;)
        {
            columns += ClusterWidth(text, i, mask, out i);
        }

        while (columns > row.Width && scroll < caret)
        {
            columns -= ClusterWidth(text, scroll, mask, out scroll);
        }

        if (Focused)
        {
            state.Scroll = scroll;
        }

        state.Rendered(row, scroll);

        int selectionStart = Focused ? state.SelectionStart : 0;
        int selectionEnd = Focused ? state.SelectionEnd : 0;
        Style selected = Style.Patch(SelectionStyle);
        Span<char> maskChar = [mask ?? ' '];
        int x = row.X;
        int caretX = row.X;
        for (int i = scroll; i < text.Length;)
        {
            if (i <= caret)
            {
                caretX = x;
            }

            int start = i;
            int width = ClusterWidth(text, start, mask, out i);
            if (x + width > row.Right)
            {
                break;
            }

            ReadOnlySpan<char> cluster = mask is null ? text[start..i] : maskChar;
            bool inSelection = start >= selectionStart && start < selectionEnd;
            x = buffer.SetString(x, row.Y, cluster, inSelection ? selected : Style, row.Right - x);
        }

        if (caret >= text.Length)
        {
            caretX = x;
        }

        if (Focused)
        {
            buffer.SetCursor(Math.Min(caretX, row.Right - 1), row.Y, CursorShape);
        }
    }
}
