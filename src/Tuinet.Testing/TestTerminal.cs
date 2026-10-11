using System.Text;

namespace Tuinet.Testing;

/// <summary>
/// Drives an <see cref="IApp"/> the way <see cref="Terminal.Run"/> does, over a real <see cref="Tuinet.Terminal"/> on a
/// <see cref="TestTty"/>, with a clock the test controls. It draws the first frame when created; after that each input
/// call is one burst of events followed by one frame, as when a user acts and the loop catches up. Calls chain:
/// <code>
/// using var term = new TestTerminal(new MyApp(), 80, 24);
/// term.Press("jj").Key(KeyCode.Enter);
/// Assert.Contains("EDIT", term.Text);
/// term.Click("Save");
/// </code>
/// Typed calls (<see cref="Press(string)"/>, <see cref="Key"/>, <see cref="Click(int, int, MouseButton)"/>…) hand
/// events straight to the app; <see cref="Input(string)"/> sends raw bytes through the terminal's input parser.
/// </summary>
public sealed class TestTerminal : IDisposable
{
    public TestTerminal(IApp app, int width = 80, int height = 24, TerminalOptions? options = null)
    {
        ArgumentNullException.ThrowIfNull(app);
        App = app;
        Tty = new TestTty(width, height);
        Terminal = new Terminal(Tty, options);
        Screen = Render();
    }

    public IApp App { get; }

    /// <summary>The fake tty: what was written, how many writes, scripted raw input.</summary>
    public TestTty Tty { get; }

    /// <summary>The terminal the app draws through (frame and byte counters: <see cref="Terminal.LastFrameBytes"/>…).</summary>
    public Terminal Terminal { get; }

    /// <summary>The time passed to <see cref="IApp.Handle"/> and <see cref="IApp.Render"/>, in ms. Setting it doesn't draw; see <see cref="Advance"/>.</summary>
    public long NowMs { get; set; }

    /// <summary>False once <see cref="IApp.Handle"/> returned false; input calls then throw.</summary>
    public bool IsRunning { get; private set; } = true;

    /// <summary>A copy of the last frame drawn (it doesn't change under later frames).</summary>
    public CellBuffer Screen { get; private set; }

    /// <summary>The last frame as plain text, rows joined with '\n'.</summary>
    public string Text => Screen.ToString();

    /// <summary>
    /// Type <paramref name="keys"/>, one key press and frame per character. <c>\n</c> or <c>\r</c> is Enter,
    /// <c>\t</c> Tab, <c>\u001b</c> Escape and <c>\b</c> Backspace. Keys after one that stops the app are dropped.
    /// </summary>
    public TestTerminal Press(string keys)
    {
        ArgumentNullException.ThrowIfNull(keys);
        EnsureRunning();
        foreach (Rune rune in keys.EnumerateRunes())
        {
            if (!IsRunning)
            {
                break;   // keys after the one that stopped the app go nowhere, as in Terminal.Run
            }

            Send(Event.FromKey(rune.Value switch
            {
                '\r' or '\n' => new KeyEvent(KeyCode.Enter),
                '\t' => new KeyEvent(KeyCode.Tab),
                '\u001b' => new KeyEvent(KeyCode.Escape),
                '\b' => new KeyEvent(KeyCode.Backspace),
                _ => new KeyEvent(KeyCode.Char, rune),
            }));
        }

        return this;
    }

    /// <summary>Press <paramref name="c"/> with <paramref name="modifiers"/>, e.g. <c>Press('s', Modifiers.Ctrl)</c>.</summary>
    public TestTerminal Press(char c, Modifiers modifiers) => Send(Event.FromChar(c, modifiers));

    /// <summary>Press a non-character key, e.g. <c>Key(KeyCode.Tab, Modifiers.Shift)</c>.</summary>
    public TestTerminal Key(KeyCode code, Modifiers modifiers = Modifiers.None) => Send(Event.FromKey(code, modifiers));

    /// <summary>A bracketed paste of <paramref name="text"/>.</summary>
    public TestTerminal Paste(string text) => Send(Event.FromPaste(text));

    /// <summary>One mouse report.</summary>
    public TestTerminal Mouse(MouseEvent mouse) => Send(Event.FromMouse(mouse));

    /// <summary>One mouse report of <paramref name="kind"/> at (<paramref name="x"/>, <paramref name="y"/>).</summary>
    public TestTerminal Mouse(MouseKind kind, int x, int y, MouseButton button = MouseButton.None, Modifiers modifiers = Modifiers.None) =>
        Send(Event.FromMouse(new MouseEvent(kind, button, x, y, modifiers)));

    /// <summary>Press and release <paramref name="button"/> at (<paramref name="x"/>, <paramref name="y"/>): two reports, a frame after each.</summary>
    public TestTerminal Click(int x, int y, MouseButton button = MouseButton.Left) =>
        Mouse(MouseKind.Down, x, y, button).Mouse(MouseKind.Up, x, y, button);

    /// <summary>Click the first cell of <paramref name="text"/> on screen (see <see cref="CellBufferTesting.FindText"/>).</summary>
    public TestTerminal Click(string text, MouseButton button = MouseButton.Left)
    {
        Rect at = Screen.FindText(text);
        return Click(at.X, at.Y, button);
    }

    /// <summary>Turn the wheel <paramref name="notches"/> at (<paramref name="x"/>, <paramref name="y"/>): negative up, positive down.</summary>
    public TestTerminal Wheel(int x, int y, int notches)
    {
        MouseKind kind = notches < 0 ? MouseKind.ScrollUp : MouseKind.ScrollDown;
        for (int i = 0; i < Math.Abs(notches); i++)
        {
            Mouse(kind, x, y);
        }

        return this;
    }

    /// <summary>Hand <paramref name="ev"/> to the app, then draw a frame (unless the app stopped).</summary>
    public TestTerminal Send(Event ev)
    {
        EnsureRunning();
        if (Handle(ev))
        {
            Render();
        }

        return this;
    }

    /// <summary>
    /// Feed raw terminal input (escape sequences, UTF-8) through the terminal's parser, hand every event it yields to
    /// the app, then draw a frame. A sequence cut off at the end resolves after <see cref="TerminalOptions.EscapeTimeoutMs"/>
    /// (a lone ESC is the Escape key).
    /// </summary>
    public TestTerminal Input(ReadOnlySpan<byte> bytes)
    {
        EnsureRunning();
        Tty.Enqueue(bytes);
        return Drain();
    }

    /// <inheritdoc cref="Input(ReadOnlySpan{byte})"/>
    public TestTerminal Input(string text) => Input(Encoding.UTF8.GetBytes(text));

    /// <summary>Resize the tty: the app gets the <see cref="EventKind.Resize"/>, then a frame at the new size.</summary>
    public TestTerminal Resize(int width, int height)
    {
        EnsureRunning();
        Tty.Resize(width, height);
        return Drain();
    }

    /// <summary><see cref="Terminal.Post"/> a message: the app gets it as <see cref="EventKind.Message"/>, then a frame.</summary>
    public TestTerminal Post(object message)
    {
        EnsureRunning();
        Terminal.Post(message);
        return Drain();
    }

    /// <summary>
    /// Move the clock <paramref name="ms"/> forward. If the app was animating, or has something due by then
    /// (<see cref="IApp.NextDueMs"/>), it gets one <see cref="EventKind.Tick"/> and a frame, as when
    /// <see cref="Terminal.Run"/> wakes; the animation frames in between are skipped.
    /// </summary>
    public TestTerminal Advance(long ms)
    {
        ArgumentOutOfRangeException.ThrowIfNegative(ms);
        EnsureRunning();
        bool wakes = App.IsAnimating(NowMs) || App.NextDueMs(NowMs) <= NowMs + ms;
        NowMs += ms;
        return wakes ? Send(Event.Tick) : this;
    }

    /// <summary>Draw a frame now through <see cref="Terminal.BeginFrame"/> and <see cref="Terminal.Present"/>; returns <see cref="Screen"/>.</summary>
    public CellBuffer Render()
    {
        CellBuffer frame = Terminal.BeginFrame();
        App.Render(frame, NowMs);
        Terminal.Present();
        var copy = new CellBuffer(frame.Width, frame.Height);
        frame.CopyTo(copy);
        Screen = copy;
        return copy;
    }

    /// <inheritdoc cref="CellBufferTesting.FindText"/>
    public Rect FindText(string text) => Screen.FindText(text);

    public void Dispose()
    {
        Terminal.Dispose();
        Tty.Dispose();
    }

    private TestTerminal Drain()
    {
        // Poll(0) never waits, so it leaves a sequence cut off at the end (a lone ESC) pending. A last poll with a
        // timeout resolves it after TerminalOptions.EscapeTimeoutMs, as on a real terminal; otherwise it costs ~1 ms.
        while (Terminal.Poll(out Event ev, 0) || Terminal.Poll(out ev, 1))
        {
            if (!Handle(ev))
            {
                return this;
            }
        }

        Render();
        return this;
    }

    /// <summary>One event; false when the app stopped (an inline app's last frame is drawn again, as <see cref="Terminal.Run"/> does).</summary>
    private bool Handle(Event ev)
    {
        if (App.Handle(ev, NowMs))
        {
            return true;
        }

        IsRunning = false;
        if (Terminal.Options.Inline is not null)
        {
            Render();
        }

        return false;
    }

    private void EnsureRunning()
    {
        if (!IsRunning)
        {
            throw new InvalidOperationException("The app has stopped: its Handle returned false.");
        }
    }
}
