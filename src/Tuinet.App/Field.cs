using System.Text;

namespace Tuinet;

public sealed class Field
{
    private readonly List<Rune> _runes = [];
    private string? _text;

    public Field(bool masked = false) => Masked = masked;

    public bool Masked { get; }
    public int Caret { get; private set; }
    public int Length => _runes.Count;

    public string Text => _text ??= BuildText();

    public string Display => Masked ? new string('•', _runes.Count) : Text;

    public Rune DisplayRune(int index) => Masked ? new Rune('•') : _runes[index];

    public void Set(string value)
    {
        _runes.Clear();
        foreach (Rune rune in value.EnumerateRunes())
        {
            _runes.Add(rune);
        }

        Caret = _runes.Count;
        Invalidate();
    }

    public void Handle(KeyEvent key)
    {
        switch (key.Code)
        {
            case KeyCode.Char:
                Insert(key.Rune);
                break;
            case KeyCode.Backspace:
                Backspace();
                break;
            case KeyCode.Delete:
                Delete();
                break;
            case KeyCode.Left:
                if (Caret > 0)
                {
                    Caret--;
                }
                break;
            case KeyCode.Right:
                if (Caret < _runes.Count)
                {
                    Caret++;
                }
                break;
            case KeyCode.Home:
                Caret = 0;
                break;
            case KeyCode.End:
                Caret = _runes.Count;
                break;
        }
    }

    private void Insert(Rune rune)
    {
        if (rune.Value < 32)
        {
            return;
        }

        _runes.Insert(Caret, rune);
        Caret++;
        Invalidate();
    }

    private void Backspace()
    {
        if (Caret == 0)
        {
            return;
        }

        Caret--;
        _runes.RemoveAt(Caret);
        Invalidate();
    }

    private void Delete()
    {
        if (Caret >= _runes.Count)
        {
            return;
        }

        _runes.RemoveAt(Caret);
        Invalidate();
    }

    private void Invalidate() => _text = null;

    private string BuildText()
    {
        var builder = new StringBuilder(_runes.Count);
        foreach (Rune rune in _runes)
        {
            builder.Append(rune);
        }

        return builder.ToString();
    }
}
