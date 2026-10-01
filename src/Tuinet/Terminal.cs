using System.Collections.Concurrent;

namespace Tuinet;

/// <summary>
/// An immediate-mode terminal session: alternate screen, raw input, double-buffered output.
/// <para>
/// Single-threaded: call <see cref="Poll"/>, <see cref="BeginFrame"/> and <see cref="Present"/>
/// from one thread. <see cref="Post"/> may be called from any thread to wake the loop.
/// </para>
/// <code>
/// using var term = Terminal.Open();
/// while (running)
/// {
///     if (!term.Poll(out Event ev, Timeout.Infinite)) continue;
///     do { running = app.Handle(ev); } while (running &amp;&amp; term.Poll(out ev, 0)); // coalesce bursts
///     CellBuffer frame = term.BeginFrame();
///     app.Render(frame);
///     term.Present();                                                             // one write
/// }
/// </code>
/// </summary>
public sealed class Terminal : IDisposable
{
    private readonly ITty _tty;
    private readonly bool _ownsTty;
    private readonly TerminalOptions _options;
    private readonly VtParser _parser = new();
    private readonly Renderer _renderer;
    private readonly VtBuffer _out;
    private readonly byte[] _readBuf = new byte[4096];
    private readonly ConcurrentQueue<object> _messages = new();
    private readonly byte[] _leave;
    private CellBuffer _front;
    private CellBuffer _back;
    private Size _reportedSize;
    private bool _fullRedraw;
    private bool _inFrame;
    private bool _disposed;

    public Terminal(ITty tty, TerminalOptions? options = null)
        : this(tty, options ?? new TerminalOptions(), ownsTty: false)
    {
    }

    private Terminal(ITty tty, TerminalOptions options, bool ownsTty)
    {
        _tty = tty;
        _ownsTty = ownsTty;
        _options = options;
        _renderer = new Renderer(options.ColorMode ?? ColorMode.TrueColor);

        Size size = Clamp(tty.Size);
        _reportedSize = size;
        _front = new CellBuffer(size.Width, size.Height);
        _back = new CellBuffer(size.Width, size.Height);
        _out = new VtBuffer(size.Width * size.Height * 8 + 4096);
        _leave = BuildLeave(options);

        if (_ownsTty)
        {
            CrashGuard.Register(_tty, _leave);
        }

        Enter();
    }

    /// <summary>Open the process's controlling terminal (stdin/stdout, or /dev/tty when redirected).</summary>
    public static Terminal Open(TerminalOptions? options = null)
    {
        options ??= new TerminalOptions();
        if (options.ColorMode is null)
        {
            options = options with { ColorMode = TerminalOptions.DetectColorMode() };
        }

        ITty tty = OperatingSystem.IsWindows() ? new WindowsTty() : new UnixTty();
        try
        {
            return new Terminal(tty, options, ownsTty: true);
        }
        catch
        {
            tty.Dispose();
            throw;
        }
    }

    public Size Size => _back.Size;
    public int Width => _back.Width;
    public int Height => _back.Height;
    public ColorMode ColorMode => _renderer.Mode;
    public TerminalOptions Options => _options;

    /// <summary>Frames presented so far.</summary>
    public long Frames { get; private set; }

    /// <summary>Total bytes written to the tty (including setup and teardown).</summary>
    public long BytesWritten { get; private set; }

    /// <summary>Bytes the last <see cref="Present"/> wrote.</summary>
    public int LastFrameBytes { get; private set; }

    /// <summary>
    /// Start a frame: returns the cleared back buffer to draw into. Nothing reaches the terminal
    /// until <see cref="Present"/>.
    /// </summary>
    public CellBuffer BeginFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SyncSize();
        _back.Clear();
        _inFrame = true;
        return _back;
    }

    /// <summary>Diff the frame against what is on screen and write the difference in one write.</summary>
    public void Present()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_inFrame)
        {
            throw new InvalidOperationException("Call BeginFrame before Present.");
        }

        _inFrame = false;
        _out.Clear();
        if (_fullRedraw)
        {
            _out.Reserve(16);
            _out.Bytes("\u001b[0m\u001b[2J"u8);
            _front.Clear();
            _renderer.AfterClear();
            _fullRedraw = false;
        }

        _renderer.Render(_back, _front, _out);
        LastFrameBytes = _out.Length;
        Flush();
        (_front, _back) = (_back, _front);
        Frames++;
    }

    /// <summary>Repaint everything on the next <see cref="Present"/> (e.g. after another program drew on the screen).</summary>
    public void Invalidate() => _fullRedraw = true;

    /// <summary>Queue a message for the loop and wake a blocked <see cref="Poll"/>. Thread-safe.</summary>
    public void Post(object message)
    {
        ArgumentNullException.ThrowIfNull(message);
        _messages.Enqueue(message);
        _tty.Wake();
    }

    /// <summary>
    /// Wait up to <paramref name="timeoutMs"/> (-1 = forever, 0 = don't wait) for the next event:
    /// input, a resize, or a posted message. Returns false on timeout.
    /// </summary>
    public bool Poll(out Event ev, int timeoutMs)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        long deadline = timeoutMs < 0 ? long.MaxValue : Environment.TickCount64 + timeoutMs;
        while (true)
        {
            if (TryNext(out ev))
            {
                return true;
            }

            if (_parser.IsIncomplete)
            {
                FinishIncomplete(allowWait: timeoutMs != 0);
                if (_parser.Pending > 0)
                {
                    continue;
                }
            }

            int remaining = timeoutMs < 0 ? -1 : (int)Math.Max(0, deadline - Environment.TickCount64);
            int n = _tty.Read(_readBuf, remaining);
            if (n > 0)
            {
                _parser.Feed(_readBuf.AsSpan(0, n));
                continue;
            }

            if (timeoutMs >= 0 && Environment.TickCount64 >= deadline)
            {
                return TryNext(out ev);
            }
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        try
        {
            Write(_leave);
        }
        catch
        {
            // The tty may already be gone; restoring modes below is what matters.
        }

        if (_ownsTty)
        {
            CrashGuard.Unregister(_tty);
            _tty.Dispose();
        }
    }

    private bool TryNext(out Event ev)
    {
        if (_parser.TryTake(out ev))
        {
            return true;
        }

        Size size = Clamp(_tty.Size);
        if (size != _reportedSize)
        {
            _reportedSize = size;
            ev = Event.FromResize(size);
            return true;
        }

        if (_messages.TryDequeue(out object? message))
        {
            ev = Event.FromMessage(message);
            return true;
        }

        ev = default;
        return false;
    }

    private void FinishIncomplete(bool allowWait)
    {
        while (_parser.IsIncomplete)
        {
            int n = _tty.Read(_readBuf, 0);
            if (n == 0 && allowWait)
            {
                n = _tty.Read(_readBuf, _options.EscapeTimeoutMs);
            }

            if (n > 0)
            {
                _parser.Feed(_readBuf.AsSpan(0, n));
                continue;
            }

            if (allowWait)
            {
                _parser.FlushIncomplete();
            }

            return;
        }
    }

    private void SyncSize()
    {
        Size size = Clamp(_tty.Size);
        if (size == _back.Size)
        {
            return;
        }

        _back.Resize(size.Width, size.Height);
        _front.Resize(size.Width, size.Height);
        _fullRedraw = true;
    }

    private void Enter()
    {
        _out.Clear();
        _out.Reserve(128);
        _out.Bytes("\u001b[?1049h\u001b[?25l\u001b[?7l\u001b[0m\u001b[2J"u8);
        if (_options.Mouse)
        {
            _out.Bytes(_options.MouseMotion ? "\u001b[?1003h\u001b[?1006h"u8 : "\u001b[?1002h\u001b[?1006h"u8);
        }

        if (_options.BracketedPaste)
        {
            _out.Bytes("\u001b[?2004h"u8);
        }

        if (_options.FocusEvents)
        {
            _out.Bytes("\u001b[?1004h"u8);
        }

        _renderer.AfterClear();
        Flush();
    }

    private static byte[] BuildLeave(TerminalOptions options)
    {
        var leave = new List<byte>(64);
        if (options.Mouse)
        {
            leave.AddRange("\u001b[?1006l\u001b[?1003l\u001b[?1002l"u8);
        }

        if (options.BracketedPaste)
        {
            leave.AddRange("\u001b[?2004l"u8);
        }

        if (options.FocusEvents)
        {
            leave.AddRange("\u001b[?1004l"u8);
        }

        leave.AddRange("\u001b[0m\u001b[?7h\u001b[?25h\u001b[?1049l"u8);
        return [.. leave];
    }

    private void Flush()
    {
        if (_out.Length == 0)
        {
            return;
        }

        Write(_out.Written);
        _out.Clear();
    }

    private void Write(ReadOnlySpan<byte> bytes)
    {
        _tty.Write(bytes);
        BytesWritten += bytes.Length;
    }

    private static Size Clamp(Size size) => new(Math.Max(1, size.Width), Math.Max(1, size.Height));
}
