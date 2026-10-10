using System.Buffers;
using System.Buffers.Text;
using System.Collections.Concurrent;
using System.Diagnostics;
using System.Text;
using Tuinet.Widgets;

namespace Tuinet;

/// <summary>
/// An immediate-mode terminal session: the alternate screen (or, with <see cref="TerminalOptions.Inline"/>, a
/// band under the prompt), raw input, double-buffered output.
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
/// Or implement <see cref="IApp"/> and let <see cref="Run"/> be that loop.
/// </summary>
public sealed partial class Terminal : IDisposable
{
    private readonly ITty _tty;
    private readonly bool _ownsTty;
    private readonly TerminalOptions _options;
    private readonly VtParser _parser = new();
    private readonly Renderer _renderer;
    private readonly VtBuffer _out;
    private readonly byte[] _readBuf = new byte[4096];
    private readonly ConcurrentQueue<object> _messages = new();
    private readonly InlineOptions? _inline;
    private byte[] _leave;
    private string? _title;
    private bool _leaveResetsCursorShape;
    private CellBuffer _front;
    private CellBuffer _back;
    private CellBuffer? _print;
    private CellBuffer? _blank;
    private Size _screen;
    private Size _reportedScreen;
    private bool _fullRedraw;
    private bool _resumed;
    private bool _inFrame;
    private long _frameStart;
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
        _inline = options.Inline;
        _renderer = new Renderer(options.ColorMode ?? ColorMode.TrueColor, options.ScrollRegions, options.EraseSequences)
        {
            ParkCursor = _inline is not null,
        };

        _screen = Clamp(tty.Size);
        _reportedScreen = _screen;
        Size size = FrameSize(_screen);
        _front = new CellBuffer(size.Width, size.Height);
        _back = new CellBuffer(size.Width, size.Height);
        _out = new VtBuffer(size.Width * size.Height * 8 + 4096);
        _leave = [];
        UpdateLeave();
        Enter();
    }

    private static ReadOnlySpan<byte> LeftRightMarginsQuery => "\u001b[?69$p"u8;

    /// <summary>Longest text <see cref="CopyToClipboard"/> sends, in UTF-8 bytes (terminals cap OSC 52 around here).</summary>
    public const int MaxClipboardBytes = 74_000;

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
    /// How long the last frame took, from <see cref="BeginFrame"/> to the end of <see cref="Present"/>: drawing,
    /// diffing and the write.
    /// </summary>
    public TimeSpan LastFrameTime { get; private set; }

    /// <summary>
    /// With <see cref="TerminalOptions.KittyKeyboard"/>: the terminal confirmed the kitty keyboard protocol. The
    /// reply arrives with input, so this turns true during the first polls; it stays false on terminals without it.
    /// </summary>
    public bool KittyKeyboardActive => _options.KittyKeyboard && _parser.KittyFlags > 0;

    /// <summary>
    /// With <see cref="TerminalOptions.ScrollRegions"/>: the terminal reported left/right margins (DECLRMM), so a band
    /// narrower than the screen that scrolls (a list beside a panel) is moved by the terminal too. The reply arrives with
    /// input, so this turns true during the first polls; it stays false on terminals without them.
    /// </summary>
    public bool LeftRightMarginsActive => _options.ScrollRegions && _parser.LeftRightMargins;

    /// <summary>
    /// Set the window (or tab) title (OSC 2). Control characters are dropped. The terminal's own title comes back on
    /// exit where it supports the title stack (<c>CSI 22 t</c> / <c>CSI 23 t</c>). Cheap to call every frame: only a
    /// change is sent.
    /// </summary>
    public void SetTitle(ReadOnlySpan<char> title)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_title is not null && title.SequenceEqual(_title))
        {
            return;
        }

        bool first = _title is null;
        _title = title.ToString();
        _out.Clear();
        if (first)
        {
            _out.Reserve(8);
            _out.Bytes("\u001b[22;0t"u8);
        }

        WriteTitle();
        Flush();
        if (first)
        {
            UpdateLeave();
        }
    }

    /// <summary>
    /// Copy <paramref name="text"/> to the system clipboard through the terminal (OSC 52), which also works over SSH.
    /// Returns false, sending nothing, when it is longer than <see cref="MaxClipboardBytes"/> in UTF-8. Terminals may
    /// ignore it or ask the user first (it's often off by default, e.g. in tmux without <c>set-clipboard on</c>);
    /// there is no reply saying whether it worked.
    /// </summary>
    public bool CopyToClipboard(ReadOnlySpan<char> text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        int length = Encoding.UTF8.GetByteCount(text);
        if (length > MaxClipboardBytes)
        {
            return false;
        }

        byte[] utf8 = ArrayPool<byte>.Shared.Rent(Math.Max(1, length));
        byte[] base64 = ArrayPool<byte>.Shared.Rent(Base64.GetMaxEncodedToUtf8Length(length));
        try
        {
            Encoding.UTF8.GetBytes(text, utf8);
            Base64.EncodeToUtf8(utf8.AsSpan(0, length), base64, out _, out int encoded);
            _out.Clear();
            _out.Reserve(encoded + 16);
            _out.Bytes("\u001b]52;c;"u8);
            _out.Bytes(base64.AsSpan(0, encoded));
            _out.Bytes("\u001b\\"u8);
            Flush();
        }
        finally
        {
            ArrayPool<byte>.Shared.Return(utf8);
            ArrayPool<byte>.Shared.Return(base64);
        }

        return true;
    }

    /// <summary>
    /// Start a frame: returns the cleared back buffer to draw into. Nothing reaches the terminal
    /// until <see cref="Present"/>.
    /// </summary>
    public CellBuffer BeginFrame()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        SyncSize();
        _frameStart = Stopwatch.GetTimestamp();
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
        _renderer.Open(_out);
        if (_fullRedraw)
        {
            if (_inline is null)
            {
                _out.Reserve(16);
                _out.Bytes("\u001b[0m\u001b[2J"u8);
                _renderer.AfterClear();
            }
            else
            {
                PlaceBand(_renderer.Top);
            }

            _front.Clear();
            _fullRedraw = false;
        }

        _renderer.LeftRightMargins = LeftRightMarginsActive;
        _renderer.Frame(_back, _front);
        LastFrameBytes = _out.Length;
        Flush();
        (_front, _back) = (_back, _front);
        Frames++;
        LastFrameTime = Stopwatch.GetElapsedTime(_frameStart);
        if (_renderer.CursorShapeUsed && !_leaveResetsCursorShape)
        {
            UpdateLeave();
        }
    }

    /// <summary>
    /// Inline mode: print <paramref name="text"/> above the band, as ordinary terminal output that stays in the
    /// scrollback (logs over a progress bar). Long lines wrap; a trailing newline is ignored. The band moves
    /// down (the screen scrolls once it reaches the bottom) and is repainted, all in one write.
    /// </summary>
    public void PrintAbove(StyledText text)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (_inline is null)
        {
            throw new InvalidOperationException("PrintAbove needs inline mode (TerminalOptions.Inline).");
        }

        if (!_inFrame)
        {
            SyncSize();
        }

        if (text.Text.EndsWith('\n'))
        {
            int end = text.Text.Length - 1;
            text = text.Slice(0, end > 0 && text.Text[end - 1] == '\r' ? end - 1 : end);
        }

        int width = _screen.Width;
        int lines = Math.Max(1, Paragraph.LineCount(text.Text, width, TextWrap.Char));
        _print = Fresh(_print, width, lines);
        _print.Render(new Paragraph(text) { Wrap = TextWrap.Char }, _print.Area);

        _out.Clear();
        _renderer.Open(_out);
        _renderer.EraseBelow(0);

        // Rows go where the band was, then below; on the last screen row the screen scrolls up first.
        int screenHeight = _screen.Height;
        int row = _renderer.Top;
        for (int i = 0; i < lines; i++)
        {
            if (row == screenHeight)
            {
                _renderer.ScrollScreen(screenHeight, 1);
                row--;
            }

            _renderer.PrintRow(_print.Row(i), row - _renderer.Top);
            row++;
        }

        PlaceBand(row);
        _blank = Fresh(_blank, _front.Width, _front.Height);
        _renderer.LeftRightMargins = LeftRightMarginsActive;
        _renderer.Frame(_front, _blank);
        Flush();
    }

    /// <summary>Repaint everything on the next <see cref="Present"/> (e.g. after another program drew on the screen).</summary>
    public void Invalidate() => _fullRedraw = true;

    /// <summary>
    /// Leave the screen, restore the terminal and stop the process (Unix job control, as Ctrl+Z does in a
    /// shell command). Returns once the process is continued (e.g. <c>fg</c>), back on the alternate screen;
    /// the next <see cref="Poll"/> returns a <see cref="EventKind.Resize"/> event so the app draws a frame,
    /// which repaints everything. Returns false, doing nothing, where suspending isn't supported (Windows).
    /// <code>if (ev.Key.IsCtrl('z')) term.Suspend();</code>
    /// See also <see cref="TerminalOptions.SuspendOnCtrlZ"/>.
    /// </summary>
    public bool Suspend()
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        if (!_tty.CanSuspend)
        {
            return false;
        }

        Write(_leave);
        _tty.Suspend();
        Resume();
        return true;
    }

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
            if (_inline is not null)
            {
                // Keep the last frame; the shell continues on the line after its last non-blank row.
                _out.Clear();
                _renderer.Open(_out);
                _renderer.LineAfter(LastContentRow(_front));
                Flush();
            }

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
        TtySignals signals = _tty.TakeSignals();
        if (signals != TtySignals.None)
        {
            if ((signals & TtySignals.StopRequested) != 0)
            {
                Suspend();
            }
            else
            {
                Resume();
            }
        }

        while (_parser.TryTake(out ev))
        {
            if (!(_options.SuspendOnCtrlZ && ev.Kind == EventKind.Key && ev.Key.IsCtrl('z') && Suspend()))
            {
                return true;
            }
        }

        Size screen = Clamp(_tty.Size);
        if (screen != _reportedScreen || _resumed)
        {
            _reportedScreen = screen;
            _resumed = false;
            ev = Event.FromResize(FrameSize(screen));
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

    /// <summary>Back from a stop: the screen is the shell's, so enter again and repaint everything.</summary>
    private void Resume()
    {
        _renderer.ForgetCursorShape();
        Enter();
        Invalidate();
        _resumed = true;
    }

    private void SyncSize()
    {
        Size screen = Clamp(_tty.Size);
        if (screen == _screen)
        {
            return;
        }

        _screen = screen;
        Size size = FrameSize(screen);
        _back.Resize(size.Width, size.Height);
        _front.Resize(size.Width, size.Height);
        if (_inline is not null)
        {
            // The terminal may have moved the band (reflow, or rows pushed into the scrollback): ask where the
            // cursor is now, and so where the band is.
            int cursorRow = _renderer.CursorRow;
            int top = _renderer.Top;
            if (cursorRow >= 0 && QueryCursor(out int row, out _))
            {
                top = row - cursorRow;
            }

            _renderer.Top = Math.Clamp(top, 0, screen.Height - 1);
        }

        _fullRedraw = true;
    }

    private Size FrameSize(Size screen) =>
        _inline is null ? screen : new Size(screen.Width, Math.Min(_inline.Height, screen.Height));

    /// <summary>
    /// Inline: put the band at screen row <paramref name="top"/>, scrolling the screen up if it doesn't fit
    /// below, and erase it (and everything under it).
    /// </summary>
    private void PlaceBand(int top)
    {
        int screenHeight = _screen.Height;
        int height = _back.Height;
        if (top + height > screenHeight)
        {
            _renderer.ScrollScreen(screenHeight, top + height - screenHeight);
            top = screenHeight - height;
        }

        _renderer.Top = top;
        _renderer.EraseBelow(0);
    }

    /// <summary>
    /// Ask the terminal where the cursor is (<c>CSI 6n</c>) and wait for the reply. Input that arrives
    /// meanwhile is kept for <see cref="Poll"/>.
    /// </summary>
    private bool QueryCursor(out int row, out int column)
    {
        _out.Clear();
        _out.Reserve(8);
        _out.Bytes("\u001b[6n"u8);
        Flush();
        _parser.ExpectCursorReport = true;
        long deadline = Environment.TickCount64 + _inline!.CursorReportTimeoutMs;
        while (!_parser.TryTakeCursorReport(out row, out column))
        {
            // On a timeout the flag stays set, so a late reply is swallowed instead of becoming F3.
            int remaining = (int)(deadline - Environment.TickCount64);
            if (remaining <= 0)
            {
                return false;
            }

            int n = _tty.Read(_readBuf, remaining);
            if (n > 0)
            {
                _parser.Feed(_readBuf.AsSpan(0, n));
            }
        }

        return true;
    }

    private static CellBuffer Fresh(CellBuffer? buffer, int width, int height)
    {
        if (buffer is null)
        {
            return new CellBuffer(width, height);
        }

        buffer.Resize(width, height);
        buffer.Clear();
        return buffer;
    }

    private static int LastContentRow(CellBuffer buffer)
    {
        for (int y = buffer.Height - 1; y >= 0; y--)
        {
            foreach (Cell cell in buffer.Row(y))
            {
                if (!cell.Equals(Cell.Empty))
                {
                    return y;
                }
            }
        }

        return -1;
    }

    private void Enter()
    {
        _out.Clear();
        _out.Reserve(128);
        // ?2027: grapheme cluster mode, so terminals that know it size clusters the way CellBuffer does.
        _out.Bytes(_inline is null
            ? "\u001b[?1049h\u001b[?25l\u001b[?7l\u001b[?2027h"u8
            : "\u001b[?25l\u001b[?7l\u001b[?2027h"u8);
        if (_inline is null)
        {
            if (_options.ScrollRegions)
            {
                // Ask whether the terminal has left/right margins (DECRQM 69), before the clear: one that can't
                // parse the query may print its last byte.
                _out.Bytes(LeftRightMarginsQuery);
            }

            _out.Bytes("\u001b[0m\u001b[2J"u8);
        }

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

        if (_options.KittyKeyboard)
        {
            // Push our flags (1: disambiguate, 2: event types), then ask what the terminal made of them.
            _out.Bytes(_options.KeyReleaseEvents ? "\u001b[>3u\u001b[?u"u8 : "\u001b[>1u\u001b[?u"u8);
        }

        if (_title is not null)
        {
            // Back from a suspend, which popped the title: save the shell's again and put ours back.
            _out.Bytes("\u001b[22;0t"u8);
            WriteTitle();
        }

        if (_inline is null)
        {
            _renderer.AfterClear();
            Flush();
            return;
        }

        // The band starts on the cursor's line, or the next one if that line has text; with no reply to the
        // cursor query, at the bottom of the screen.
        Flush();
        int start = QueryCursor(out int row, out int column) ? (column > 0 ? row + 1 : row) : _screen.Height;
        _out.Clear();
        _renderer.Open(_out);
        _renderer.Forget();
        _renderer.Top = 0;
        PlaceBand(start);
        if (_options.ScrollRegions)
        {
            // In the band, just erased: a terminal that can't parse the query may print its last byte, so erase again.
            _out.Reserve(16);
            _out.Bytes(LeftRightMarginsQuery);
            _out.Bytes("\r\u001b[K"u8);
        }

        _renderer.Close();
        _front.Clear();
        Flush();
    }

    /// <summary>
    /// Rebuild what leaving writes (dispose, suspend, crash): the cursor shape reset and the title restore are in it
    /// only once the app has used them, so apps that don't pay nothing.
    /// </summary>
    private void UpdateLeave()
    {
        _leaveResetsCursorShape = _renderer.CursorShapeUsed;
        _leave = BuildLeave(_options, _leaveResetsCursorShape, _title is not null);
        if (_ownsTty)
        {
            CrashGuard.Register(_tty, _leave);
        }
    }

    /// <summary>OSC 2 with <see cref="_title"/>, control characters dropped, into <see cref="_out"/>.</summary>
    private void WriteTitle()
    {
        const int MaxChars = 512;
        ReadOnlySpan<char> title = _title.AsSpan(0, Math.Min(_title!.Length, MaxChars));
        _out.Reserve(title.Length * 3 + 16);
        _out.Bytes("\u001b]2;"u8);
        foreach (Rune rune in title.EnumerateRunes())
        {
            if (rune.Value >= 0x20 && rune.Value is not (>= 0x7F and <= 0x9F) && rune != Rune.ReplacementChar)
            {
                _out.Rune(rune);
            }
        }

        _out.Bytes("\u001b\\"u8);
    }

    private static byte[] BuildLeave(TerminalOptions options, bool resetCursorShape, bool restoreTitle)
    {
        var leave = new List<byte>(64);
        if (options.KittyKeyboard)
        {
            leave.AddRange("\u001b[<u"u8);
        }

        if (resetCursorShape)
        {
            leave.AddRange("\u001b[0 q"u8);
        }

        if (restoreTitle)
        {
            leave.AddRange("\u001b[23;0t"u8);
        }

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

        leave.AddRange("\u001b[0m\u001b[?2027l\u001b[?7h\u001b[?25h"u8);
        if (options.Inline is null)
        {
            leave.AddRange("\u001b[?1049l"u8);
        }

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
