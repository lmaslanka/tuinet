using System.Text;

namespace Tuinet;

public readonly struct KeyEvent
{
    public KeyCode Code { get; }
    public Rune Rune { get; }
    public Modifiers Modifiers { get; }

    public KeyEvent(KeyCode code, Rune rune = default, Modifiers modifiers = Modifiers.None)
    {
        Code = code;
        Rune = rune;
        Modifiers = modifiers;
    }

    public bool IsChar(char c) =>
        Code == KeyCode.Char && Rune == new Rune(c) && Modifiers == Modifiers.None;

    public bool IsCtrl(char c) =>
        Code == KeyCode.Char
        && Rune == new Rune(char.ToLowerInvariant(c))
        && (Modifiers & Modifiers.Ctrl) != 0;
}
