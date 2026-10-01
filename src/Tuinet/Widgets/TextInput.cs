using System.Text;

namespace Tuinet.Widgets;

/// <summary>
/// Editable single-line text: a rune buffer with a caret, emacs-style editing keys, and optional
/// masking. Owned by the app; allocates only when it grows or when <see cref="Text"/> is read after an edit.
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

    /// <summary>Character drawn instead of each rune (e.g. '•' for passwords), or null.</summary>
    public char? Mask { get; set; }

    /// <summary>Caret position, in runes.</summary>
    public int Caret { get; private set; }

    public int Length => _length;
    public bool IsEmpty => _length == 0;
    public ReadOnlySpan<Rune> Runes => _runes.AsSpan(0, _length);

    /// <summary>First visible rune; maintained by <see cref="TextInput"/> to keep the caret in view.</summary>
    public int Scroll { get; internal set; }

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

    /// <summary>Insert text at the caret (e.g. a paste). Control characters are dropped.</summary>
    public void Insert(ReadOnlySpan<char> text)
    {
        foreach (Rune rune in text.EnumerateRunes())
        {
            Insert(rune);
        }
    }

    public void Insert(Rune rune)
    {
        if (TextWidth.Of(rune) == 0)
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
                    case 'b': MoveTo(Caret - 1); return true;
                    case 'f': MoveTo(Caret + 1); return true;
                    case 'u': Remove(0, Caret); return true;
                    case 'k': Remove(Caret, _length); return true;
                    case 'w': Remove(WordStart(Caret), Caret); return true;
                    case 'd': Remove(Caret, Math.Min(Caret + 1, _length)); return true;
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
                Remove(word ? WordStart(Caret) : Math.Max(0, Caret - 1), Caret);
                return true;
            case KeyCode.Delete:
                Remove(Caret, word ? WordEnd(Caret) : Math.Min(Caret + 1, _length));
                return true;
            case KeyCode.Left:
                MoveTo(word ? WordStart(Caret) : Caret - 1);
                return true;
            case KeyCode.Right:
                MoveTo(word ? WordEnd(Caret) : Caret + 1);
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

    /// <summary>The rune drawn at <paramref name="index"/> (the mask when masked).</summary>
    public Rune DisplayRune(int index) => Mask is char mask ? new Rune(mask) : _runes[index];

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
    public TextInput()
    {
    }

    /// <summary>Fills the input row when not default.</summary>
    public Style Style { get; init; }

    /// <summary>Shown while the input is empty.</summary>
    public ReadOnlySpan<char> Placeholder { get; init; }

    public Style PlaceholderStyle { get; init; }

    public bool Focused { get; init; }

    public void Render(Rect area, CellBuffer buffer, ref TextInputState state)
    {
        area = area.Intersect(buffer.Area);
        if (area.IsEmpty)
        {
            return;
        }

        var row = new Rect(area.X, area.Y, area.Width, 1);
        if (!Style.Equals(default))
        {
            buffer.Fill(row, Style);
        }

        if (state.IsEmpty && !Placeholder.IsEmpty)
        {
            buffer.SetString(row.X, row.Y, Placeholder, Style.Patch(PlaceholderStyle), row.Width, Overflow.Ellipsis);
        }

        int width = row.Width;
        int caret = state.Caret;
        int scroll = Math.Min(state.Scroll, caret);

        // Keep the caret cell on screen: columns from scroll to caret, plus one for the caret itself.
        int columns = 1;
        for (int i = scroll; i < caret; i++)
        {
            columns += TextWidth.Of(state.DisplayRune(i));
        }

        while (columns > width && scroll < caret)
        {
            columns -= TextWidth.Of(state.DisplayRune(scroll));
            scroll++;
        }

        state.Scroll = scroll;

        int x = row.X;
        int caretX = row.X;
        for (int i = scroll; i < state.Length; i++)
        {
            if (i == caret)
            {
                caretX = x;
            }

            Rune rune = state.DisplayRune(i);
            if (x + TextWidth.Of(rune) > row.Right)
            {
                break;
            }

            x += buffer.SetRune(x, row.Y, rune, Style);
        }

        if (caret >= state.Length)
        {
            caretX = x;
        }

        if (Focused)
        {
            buffer.SetCursor(Math.Min(caretX, row.Right - 1), row.Y);
        }
    }
}
