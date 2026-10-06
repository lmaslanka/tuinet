using System.Text;

namespace Tuinet.Widgets;

/// <summary>
/// Editable single-line text: a rune buffer with a caret, emacs-style editing keys, and optional
/// masking. The caret moves and deletes by grapheme cluster (an emoji sequence or a letter with its
/// accents is one step). Owned by the app; allocates only when it grows or when <see cref="Text"/> is
/// read after an edit.
/// </summary>
public sealed class TextInputState
{
    private Rune[] _runes = new Rune[16];
    private int _length;
    private string? _text;

    public TextInputState(string? text = null, char? mask = null)
    {
        Mask = mask;
        if (text is not null)
        {
            Set(text);
        }
    }

    /// <summary>Character drawn instead of each grapheme cluster (e.g. '•' for passwords), or null.</summary>
    public char? Mask { get; set; }

    /// <summary>Caret position, in runes (always at a grapheme cluster boundary).</summary>
    public int Caret { get; private set; }

    public int Length => _length;
    public bool IsEmpty => _length == 0;
    public ReadOnlySpan<Rune> Runes => _runes.AsSpan(0, _length);

    /// <summary>First visible rune; maintained by <see cref="TextInput"/> to keep the caret in view.</summary>
    public int Scroll { get; internal set; }

    /// <summary>The row the input was last rendered on, for mouse hit-testing.</summary>
    public Rect Area { get; private set; }

    /// <summary>First rune shown at the last render (0 when unfocused, which shows the beginning).</summary>
    private int _shownScroll;

    public string Text => _text ??= Build();

    public void Set(ReadOnlySpan<char> text)
    {
        _length = 0;
        Insert(text);
        Caret = _length;
    }

    public void Clear()
    {
        _length = 0;
        Caret = 0;
        Scroll = 0;
        _text = string.Empty;
    }

    /// <summary>Insert text at the caret (e.g. a paste). Control characters are dropped; marks and joiners are kept.</summary>
    public void Insert(ReadOnlySpan<char> text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            Insert(rune);
        }
    }

    public void Insert(Rune rune)
    {
        if (Rune.IsControl(rune))
        {
            return;
        }

        if (_length == _runes.Length)
        {
            Array.Resize(ref _runes, _runes.Length * 2);
        }

        Array.Copy(_runes, Caret, _runes, Caret + 1, _length - Caret);
        _runes[Caret++] = rune;
        _length++;
        _text = null;
    }

    /// <summary>Apply an editing key. Returns false if the key is not an editing key (e.g. Tab, Enter, Up).</summary>
    public bool Handle(KeyEvent key)
    {
        Modifiers mods = key.Modifiers;
        bool word = (mods & (Modifiers.Ctrl | Modifiers.Alt)) != 0;
        switch (key.Code)
        {
            case KeyCode.Char when mods is Modifiers.None or Modifiers.Shift:
                Insert(key.Rune);
                return true;
            case KeyCode.Char when mods == Modifiers.Ctrl:
                switch (key.Rune.Value)
                {
                    case 'a': Caret = 0; return true;
                    case 'e': Caret = _length; return true;
                    case 'b': MoveTo(ClusterStart(Caret)); return true;
                    case 'f': MoveTo(ClusterEnd(Caret)); return true;
                    case 'u': Remove(0, Caret); return true;
                    case 'k': Remove(Caret, _length); return true;
                    case 'w': Remove(WordStart(Caret), Caret); return true;
                    case 'd': Remove(Caret, ClusterEnd(Caret)); return true;
                    default: return false;
                }

            case KeyCode.Char when mods == Modifiers.Alt:
                switch (key.Rune.Value)
                {
                    case 'b': MoveTo(WordStart(Caret)); return true;
                    case 'f': MoveTo(WordEnd(Caret)); return true;
                    case 'd': Remove(Caret, WordEnd(Caret)); return true;
                    default: return false;
                }

            case KeyCode.Backspace:
                Remove(word ? WordStart(Caret) : ClusterStart(Caret), Caret);
                return true;
            case KeyCode.Delete:
                Remove(Caret, word ? WordEnd(Caret) : ClusterEnd(Caret));
                return true;
            case KeyCode.Left:
                MoveTo(word ? WordStart(Caret) : ClusterStart(Caret));
                return true;
            case KeyCode.Right:
                MoveTo(word ? WordEnd(Caret) : ClusterEnd(Caret));
                return true;
            case KeyCode.Home:
                Caret = 0;
                return true;
            case KeyCode.End:
                Caret = _length;
                return true;
            default:
                return false;
        }
    }

    /// <summary>
    /// A click on the input moves the caret to the clicked cluster (or the end, past the text), as the last
    /// render showed it, so clicking an unfocused input works too. Returns whether the event was used.
    /// </summary>
    public bool HandleMouse(MouseEvent ev)
    {
        if (!ev.IsClickIn(Area))
        {
            return false;
        }

        Span<char> chars = stackalloc char[Graphemes.MaxChars + 2];
        int column = ev.X - Area.X;
        int x = 0;
        int i = Math.Min(_shownScroll, _length);
        while (i < _length)
        {
            int width = TextInput.ClusterWidth(this, i, chars, out int end);
            if (column < x + width)
            {
                break;
            }

            x += width;
            i = end;
        }

        Caret = i;
        Scroll = Math.Min(_shownScroll, i);
        return true;
    }

    internal void Rendered(Rect row, int scroll)
    {
        Area = row;
        _shownScroll = scroll;
    }

    /// <summary>The rune drawn at <paramref name="index"/> (the mask when masked).</summary>
    public Rune DisplayRune(int index) => Mask is char mask ? new Rune(mask) : _runes[index];

    /// <summary>
    /// Encode the grapheme cluster starting at rune <paramref name="start"/> into <paramref name="chars"/>
    /// (at most <see cref="Graphemes.MaxChars"/> + 2 chars are used). Returns its length in chars;
    /// <paramref name="end"/> is the rune index after it.
    /// </summary>
    internal int Cluster(int start, Span<char> chars, out int end)
    {
        int n = 0;
        end = start;
        while (end < _length && n + 2 <= chars.Length)
        {
            int before = n;
            n += _runes[end].EncodeToUtf16(chars[n..]);
            end++;
            if (end - start >= 2 && Graphemes.Length(chars[..n]) <= before)
            {
                // The rune just added starts the next cluster.
                end--;
                return before;
            }
        }

        return n;
    }

    /// <summary>Rune index after the grapheme cluster that starts at <paramref name="start"/>.</summary>
    internal int ClusterEnd(int start)
    {
        if (start >= _length)
        {
            return _length;
        }

        Span<char> chars = stackalloc char[Graphemes.MaxChars + 2];
        Cluster(start, chars, out int end);
        return end;
    }

    /// <summary>Rune index where the grapheme cluster before <paramref name="index"/> starts.</summary>
    internal int ClusterStart(int index)
    {
        int start = 0;
        while (start < index)
        {
            int end = ClusterEnd(start);
            if (end >= index)
            {
                return start;
            }

            start = end;
        }

        return start;
    }

    private void MoveTo(int caret) => Caret = Math.Clamp(caret, 0, _length);

    private void Remove(int start, int end)
    {
        if (end <= start)
        {
            return;
        }

        Array.Copy(_runes, end, _runes, start, _length - end);
        _length -= end - start;
        Caret = start;
        _text = null;
    }

    private int WordStart(int from)
    {
        int i = from;
        while (i > 0 && Rune.IsWhiteSpace(_runes[i - 1])) i--;
        while (i > 0 && !Rune.IsWhiteSpace(_runes[i - 1])) i--;
        return i;
    }

    private int WordEnd(int from)
    {
        int i = from;
        while (i < _length && Rune.IsWhiteSpace(_runes[i])) i++;
        while (i < _length && !Rune.IsWhiteSpace(_runes[i])) i++;
        return i;
    }

    private string Build()
    {
        var builder = new StringBuilder(_length);
        foreach (Rune rune in Runes)
        {
            builder.Append(rune);
        }

        return builder.ToString();
    }
}

/// <summary>
/// Draws a <see cref="TextInputState"/> on the first row of its area, scrolled to keep the caret
/// visible. When <see cref="Focused"/>, places the real terminal cursor at the caret.
/// </summary>
public readonly ref struct TextInput : IStatefulWidget<TextInputState>
{
    /// <summary>Columns of the cluster starting at rune <paramref name="start"/> (1 per cluster when masked).</summary>
    internal static int ClusterWidth(TextInputState state, int start, Span<char> chars, out int end)
    {
        int n = state.Cluster(start, chars, out end);
        return state.Mask is char mask ? TextWidth.Of(new Rune(mask)) : TextWidth.Of(chars[..n]);
    }

    public TextInput()
    {
    }

    /// <summary>Text style; the row is erased with it (default colors keep the background underneath).</summary>
    public Style Style { get; init; }

    /// <summary>Shown while the input is empty.</summary>
    public ReadOnlySpan<char> Placeholder { get; init; }

    public Style PlaceholderStyle { get; init; }

    /// <summary>Shows the terminal cursor at the caret.</summary>
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

        int width = row.Width;

        // Unfocused inputs show their beginning; focused ones scroll to keep the caret visible.
        int caret = Focused ? state.Caret : 0;
        int scroll = Focused ? Math.Min(state.Scroll, caret) : 0;
        Span<char> chars = stackalloc char[Graphemes.MaxChars + 2];

        // Keep the caret cell on screen: columns from scroll to caret, plus one for the caret itself.
        int columns = 1;
        for (int i = scroll; i < caret;)
        {
            columns += ClusterWidth(state, i, chars, out i);
        }

        while (columns > width && scroll < caret)
        {
            columns -= ClusterWidth(state, scroll, chars, out scroll);
        }

        if (Focused)
        {
            state.Scroll = scroll;
        }

        state.Rendered(row, scroll);

        int x = row.X;
        int caretX = row.X;
        for (int i = scroll; i < state.Length;)
        {
            if (i == caret)
            {
                caretX = x;
            }

            int start = i;
            int n = state.Cluster(start, chars, out i);
            ReadOnlySpan<char> cluster = state.Mask is char mask ? [mask] : chars[..n];
            if (x + TextWidth.Of(cluster) > row.Right)
            {
                break;
            }

            x = buffer.SetString(x, row.Y, cluster, Style, row.Right - x);
        }

        if (caret >= state.Length)
        {
            caretX = x;
        }

        if (Focused)
        {
            buffer.SetCursor(Math.Min(caretX, row.Right - 1), row.Y, CursorShape);
        }
    }
}
