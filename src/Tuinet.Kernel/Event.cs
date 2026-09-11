namespace Tuinet;

public readonly struct Event
{
    public EventKind Kind { get; }
    public KeyEvent Key { get; }
    public int Width { get; }
    public int Height { get; }
    public object? Message { get; }

    public static Event FromKey(KeyEvent key) => new(EventKind.Key, key, 0, 0, null);

    public static Event FromResize(int width, int height) =>
        new(EventKind.Resize, default, width, height, null);

    public static Event FromMessage(object message) =>
        new(EventKind.Message, default, 0, 0, message);

    private Event(EventKind kind, KeyEvent key, int width, int height, object? message)
    {
        Kind = kind;
        Key = key;
        Width = width;
        Height = height;
        Message = message;
    }
}
