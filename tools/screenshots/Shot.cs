namespace Tuinet.Screenshots;

/// <summary>
/// One screenshot: a fresh app, the input that brings it to the state to show, the frame size, and the time the
/// frame is drawn at (for animations). Written to <c>{Name}.png</c>, with <see cref="Title"/> in the title bar.
/// </summary>
public sealed record Shot(string Name, string Title, int Width, int Height, Func<IApp> App, Action<IApp> Setup, long NowMs = 0)
{
    /// <summary>Create the app, set it up and draw one frame.</summary>
    public CellBuffer Render()
    {
        IApp app = App();
        Setup(app);
        var buffer = new CellBuffer(Width, Height);
        app.Render(buffer, NowMs);
        return buffer;
    }

    /// <summary>Setup that sends <paramref name="events"/> in order, all at time 0.</summary>
    public static Action<IApp> Input(params Event[] events) => app =>
    {
        foreach (Event ev in events)
        {
            app.Handle(ev, 0);
        }
    };

    /// <summary>Setup that types <paramref name="keys"/>, one key event per char.</summary>
    public static Action<IApp> Keys(string keys) => Input([.. keys.Select(c => Event.FromChar(c))]);

    public static Event Key(KeyCode code, Modifiers modifiers = Modifiers.None) => Event.FromKey(code, modifiers);

    public static Event Char(char c, Modifiers modifiers = Modifiers.None) => Event.FromChar(c, modifiers);
}

/// <summary>An <see cref="IApp"/> made of two delegates, for apps that don't implement it themselves.</summary>
public sealed class DelegateApp(Func<Event, long, bool> handle, Action<CellBuffer, long> render) : IApp
{
    public bool Handle(Event ev, long nowMs) => handle(ev, nowMs);

    public void Render(CellBuffer frame, long nowMs) => render(frame, nowMs);
}
