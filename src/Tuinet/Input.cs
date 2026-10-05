using System.Text;

namespace Tuinet;

public enum KeyCode : byte
{
    Char,
    Up,
    Down,
    Left,
    Right,
    Enter,
    Escape,
    Backspace,
    Tab,
    Delete,
    Insert,
    Home,
    End,
    PageUp,
    PageDown,
    F1,
    F2,
    F3,
    F4,
    F5,
    F6,
    F7,
    F8,
    F9,
    F10,
    F11,
    F12,
}

[Flags]
public enum Modifiers : byte
{
    None = 0,
    Ctrl = 1,
    Alt = 2,
    Shift = 4,
}

/// <summary>A key press. Printable keys are <see cref="KeyCode.Char"/> with <see cref="Rune"/> set; Ctrl+letter is the lowercase letter with <see cref="Modifiers.Ctrl"/>.</summary>
public readonly struct KeyEvent : IEquatable<KeyEvent>
{
    public KeyEvent(KeyCode code, Rune rune = default, Modifiers modifiers = Modifiers.None)
    {
        Code = code;
        Rune = rune;
        Modifiers = modifiers;
    }

    public KeyCode Code { get; }
    public Rune Rune { get; }
    public Modifiers Modifiers { get; }

    public static KeyEvent Char(char c, Modifiers modifiers = Modifiers.None) => new(KeyCode.Char, new Rune(c), modifiers);

    /// <summary>The unmodified character <paramref name="c"/>.</summary>
    public bool IsChar(char c) => Code == KeyCode.Char && Rune.Value == c && Modifiers == Modifiers.None;

    /// <summary>Ctrl + <paramref name="c"/> (case-insensitive).</summary>
    public bool IsCtrl(char c) =>
        Code == KeyCode.Char && Rune.Value == char.ToLowerInvariant(c) && Modifiers == Modifiers.Ctrl;

    public bool Is(KeyCode code, Modifiers modifiers = Modifiers.None) => Code == code && Modifiers == modifiers;

    public bool Equals(KeyEvent other) => Code == other.Code && Rune == other.Rune && Modifiers == other.Modifiers;
    public override bool Equals(object? obj) => obj is KeyEvent other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(Code, Rune, Modifiers);
    public static bool operator ==(KeyEvent left, KeyEvent right) => left.Equals(right);
    public static bool operator !=(KeyEvent left, KeyEvent right) => !left.Equals(right);

    public override string ToString()
    {
        string mods = (Modifiers.HasFlag(Modifiers.Ctrl) ? "Ctrl+" : "")
            + (Modifiers.HasFlag(Modifiers.Alt) ? "Alt+" : "")
            + (Modifiers.HasFlag(Modifiers.Shift) ? "Shift+" : "");
        return mods + (Code == KeyCode.Char ? Rune.ToString() : Code.ToString());
    }
}

public enum MouseKind : byte
{
    Down,
    Up,
    Drag,
    Move,
    ScrollUp,
    ScrollDown,
    ScrollLeft,
    ScrollRight,
}

public enum MouseButton : byte
{
    None,
    Left,
    Middle,
    Right,
}

/// <summary>A mouse report (requires <see cref="TerminalOptions.Mouse"/>). Coordinates are 0-based cells.</summary>
public readonly record struct MouseEvent(MouseKind Kind, MouseButton Button, int X, int Y, Modifiers Modifiers)
{
    /// <summary>The left button went down. Widgets act on the press, as most terminal apps do.</summary>
    public bool IsClick => Kind == MouseKind.Down && Button == MouseButton.Left;

    /// <summary>A vertical wheel notch.</summary>
    public bool IsWheel => Kind is MouseKind.ScrollUp or MouseKind.ScrollDown;

    /// <summary>-1 for a wheel notch up, +1 down, 0 otherwise.</summary>
    public int WheelDelta => Kind == MouseKind.ScrollUp ? -1 : Kind == MouseKind.ScrollDown ? 1 : 0;

    /// <summary>The pointer is inside <paramref name="area"/>.</summary>
    public bool IsIn(Rect area) => area.Contains(X, Y);

    /// <summary>A click (<see cref="IsClick"/>) inside <paramref name="area"/>, e.g. the rect a button was rendered into.</summary>
    public bool IsClickIn(Rect area) => IsClick && area.Contains(X, Y);
}

public enum EventKind : byte
{
    None,
    Key,
    Resize,
    Message,
    Mouse,
    Paste,
    FocusGained,
    FocusLost,
}

/// <summary>One input event. Only the members matching <see cref="Kind"/> are meaningful.</summary>
public readonly struct Event
{
    private Event(EventKind kind, KeyEvent key = default, MouseEvent mouse = default, Size size = default, object? payload = null)
    {
        Kind = kind;
        Key = key;
        Mouse = mouse;
        Size = size;
        _payload = payload;
    }

    private readonly object? _payload;

    public EventKind Kind { get; }
    public KeyEvent Key { get; }
    public MouseEvent Mouse { get; }

    /// <summary>New terminal size, for <see cref="EventKind.Resize"/>.</summary>
    public Size Size { get; }

    /// <summary>The object passed to <see cref="Terminal.Post"/>, for <see cref="EventKind.Message"/>.</summary>
    public object? Message => Kind == EventKind.Message ? _payload : null;

    /// <summary>Pasted text, for <see cref="EventKind.Paste"/> (requires <see cref="TerminalOptions.BracketedPaste"/>).</summary>
    public string? Paste => Kind == EventKind.Paste ? (string?)_payload : null;

    public static Event FromKey(KeyEvent key) => new(EventKind.Key, key: key);
    public static Event FromKey(KeyCode code, Modifiers modifiers = Modifiers.None) => new(EventKind.Key, key: new KeyEvent(code, default, modifiers));
    public static Event FromChar(char c, Modifiers modifiers = Modifiers.None) => new(EventKind.Key, key: KeyEvent.Char(c, modifiers));
    public static Event FromResize(Size size) => new(EventKind.Resize, size: size);
    public static Event FromMessage(object message) => new(EventKind.Message, payload: message);
    public static Event FromMouse(MouseEvent mouse) => new(EventKind.Mouse, mouse: mouse);
    public static Event FromPaste(string text) => new(EventKind.Paste, payload: text);
    public static Event Focus(bool gained) => new(gained ? EventKind.FocusGained : EventKind.FocusLost);

    public override string ToString() => Kind switch
    {
        EventKind.Key => $"Key {Key}",
        EventKind.Resize => $"Resize {Size.Width}x{Size.Height}",
        EventKind.Mouse => $"Mouse {Mouse}",
        EventKind.Paste => $"Paste {Paste?.Length} chars",
        _ => Kind.ToString(),
    };
}
