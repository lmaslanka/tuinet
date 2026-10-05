using System.Runtime.InteropServices;

namespace Tuinet;

/// <summary>
/// termios raw mode on stdin/stdout (or /dev/tty when redirected). Input waits in poll(2) on the
/// tty plus a self-pipe for <see cref="Wake"/>. Size is cached and refreshed only after SIGWINCH.
/// <para>
/// Job control: raw mode turns ISIG off, so Ctrl+Z is a key; <see cref="Suspend"/> stops the process
/// itself. A SIGTSTP from outside is caught and reported as <see cref="TtySignals.StopRequested"/> so the
/// terminal can leave the screen first; a SIGCONT after a stop we didn't make (SIGSTOP) is reported as
/// <see cref="TtySignals.Continued"/>.
/// </para>
/// </summary>
[System.Runtime.Versioning.UnsupportedOSPlatform("windows")]
internal sealed class UnixTty : ITty
{
    private const int TcsaNow = 0;

    private readonly int _inFd;
    private readonly int _outFd;
    private readonly int _wakeRead;
    private readonly int _wakeWrite;
    private readonly bool _ownsFd;
    private readonly bool _mac;
    private readonly PosixSignalRegistration? _winch;
    private readonly PosixSignalRegistration? _cont;
    private PosixSignalRegistration? _tstp;
    private TermiosLinux _savedLinux;
    private TermiosMac _savedMac;
    private Size _size = new(80, 24);
    private volatile bool _resized;
    private int _wakePending;
    private int _signals;
    private int _selfStop;
    private int _restored;
    private bool _disposed;

    public UnixTty()
    {
        _mac = OperatingSystem.IsMacOS();
        if (LibC.isatty(0) == 1 && LibC.isatty(1) == 1)
        {
            _inFd = 0;
            _outFd = 1;
        }
        else
        {
            int fd = LibC.open("/dev/tty", LibC.ORdwr | (_mac ? LibC.OCloexecMac : LibC.OCloexecLinux));
            if (fd < 0)
            {
                throw new InvalidOperationException("No terminal available (stdin/stdout are not a tty and /dev/tty cannot be opened).");
            }

            _inFd = fd;
            _outFd = fd;
            _ownsFd = true;
        }

        Span<int> pipe = stackalloc int[2];
        if (LibC.pipe(ref pipe[0]) != 0)
        {
            int errno = Marshal.GetLastPInvokeError();
            CloseOwned();
            throw new IOException($"pipe failed (errno {errno})");
        }

        _wakeRead = pipe[0];
        _wakeWrite = pipe[1];

        try
        {
            SaveModes();
            EnterRaw();
        }
        catch
        {
            CloseOwned();
            _ = LibC.close(_wakeRead);
            _ = LibC.close(_wakeWrite);
            throw;
        }

        RefreshSize();
        _winch = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, ctx =>
        {
            ctx.Cancel = true;
            _resized = true;
            Wake();
        });
        // Cancel skips the runtime's own SIGCONT handling, which re-applies the termios it captured at the
        // first signal registration (raw, by then), writes keypad_xmit, and makes it re-apply raw at exit.
        _cont = PosixSignalRegistration.Create(PosixSignal.SIGCONT, ctx =>
        {
            ctx.Cancel = true;
            if (Interlocked.Exchange(ref _selfStop, 0) == 0)
            {
                Raise(TtySignals.Continued);
            }
        });
        _tstp = CreateTstp();
    }

    public bool CanSuspend => true;

    public Size Size
    {
        get
        {
            if (_resized)
            {
                _resized = false;
                RefreshSize();
            }

            return _size;
        }
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            nint n = LibC.write(_outFd, bytes, bytes.Length);
            if (n > 0)
            {
                bytes = bytes[(int)n..];
                continue;
            }

            int errno = Marshal.GetLastPInvokeError();
            if (n < 0 && errno == LibC.EINTR)
            {
                continue;
            }

            if (n < 0 && LibC.IsEAgain(errno))
            {
                var fd = new PollFd { Fd = _outFd, Events = LibC.PollOut };
                _ = LibC.poll(ref fd, 1, -1);
                continue;
            }

            throw new IOException($"write to terminal failed (errno {errno})");
        }
    }

    public int Read(Span<byte> buffer, int timeoutMs)
    {
        Span<PollFd> fds = stackalloc PollFd[2];
        fds[0] = new PollFd { Fd = _inFd, Events = LibC.PollIn };
        fds[1] = new PollFd { Fd = _wakeRead, Events = LibC.PollIn };
        int ready = LibC.poll(ref fds[0], 2, timeoutMs);
        if (ready <= 0)
        {
            // Timeout, or EINTR (e.g. SIGWINCH): either way the caller re-checks size and messages.
            return 0;
        }

        if ((fds[1].Revents & LibC.PollIn) != 0)
        {
            Volatile.Write(ref _wakePending, 0);
            Span<byte> drain = stackalloc byte[64];
            _ = LibC.read(_wakeRead, drain, drain.Length);
        }

        short revents = fds[0].Revents;
        if ((revents & LibC.PollIn) == 0)
        {
            if ((revents & (LibC.PollHup | LibC.PollErr | LibC.PollNval)) != 0)
            {
                throw new EndOfStreamException("The terminal was closed.");
            }

            return 0;
        }

        while (true)
        {
            nint n = LibC.read(_inFd, buffer, buffer.Length);
            if (n > 0)
            {
                return (int)n;
            }

            if (n == 0)
            {
                throw new EndOfStreamException("The terminal was closed.");
            }

            int errno = Marshal.GetLastPInvokeError();
            if (errno == LibC.EINTR)
            {
                continue;
            }

            if (LibC.IsEAgain(errno))
            {
                return 0;
            }

            throw new IOException($"read from terminal failed (errno {errno})");
        }
    }

    /// <summary>At most one byte is ever in flight, so the pipe cannot fill and block a poster.</summary>
    public void Wake()
    {
        if (Interlocked.Exchange(ref _wakePending, 1) == 0)
        {
            ReadOnlySpan<byte> one = [1];
            _ = LibC.write(_wakeWrite, one, 1);
        }
    }

    /// <summary>
    /// The runtime handles a signal on its own thread while any registration exists, so raise() would return
    /// before the stop. Drop ours first: with none, SIGTSTP stops the process synchronously, and kill()
    /// returns after SIGCONT. Signalling the process group stops a pipeline (app | tee) as Ctrl+Z would.
    /// </summary>
    public void Suspend()
    {
        _tstp?.Dispose();
        _tstp = null;
        Restore();
        Volatile.Write(ref _selfStop, 1);
        if (LibC.kill(0, LibC.SigTstp) != 0)
        {
            Volatile.Write(ref _selfStop, 0);
        }

        // Continued. Raw mode is rebuilt from the modes saved at startup, which Restore still needs.
        EnterRaw();
        Volatile.Write(ref _restored, 0);
        _resized = true;
        _tstp = CreateTstp();
    }

    public TtySignals TakeSignals()
    {
        if (Volatile.Read(ref _signals) == 0)
        {
            return TtySignals.None;
        }

        var signals = (TtySignals)Interlocked.Exchange(ref _signals, 0);
        if ((signals & TtySignals.Continued) != 0)
        {
            // The shell restored the modes it saved when the job stopped (raw, since we couldn't leave); re-apply
            // raw mode anyway, but never save these as the originals.
            EnterRaw();
            Volatile.Write(ref _restored, 0);
            _resized = true;
        }

        return signals;
    }

    public void Restore()
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0)
        {
            return;
        }

        if (_mac)
        {
            _ = LibC.tcsetattrMac(_inFd, TcsaNow, in _savedMac);
        }
        else
        {
            _ = LibC.tcsetattr(_inFd, TcsaNow, in _savedLinux);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        _winch?.Dispose();
        _cont?.Dispose();
        _tstp?.Dispose();
        Restore();
        CloseOwned();
        _ = LibC.close(_wakeRead);
        _ = LibC.close(_wakeWrite);
    }

    private PosixSignalRegistration CreateTstp() => PosixSignalRegistration.Create(PosixSignal.SIGTSTP, ctx =>
    {
        ctx.Cancel = true;
        Raise(TtySignals.StopRequested);
    });

    private void Raise(TtySignals signal)
    {
        Interlocked.Or(ref _signals, (int)signal);
        Wake();
    }

    private void CloseOwned()
    {
        if (_ownsFd)
        {
            _ = LibC.close(_inFd);
        }
    }

    private void SaveModes()
    {
        int rc = _mac ? LibC.tcgetattrMac(_inFd, out _savedMac) : LibC.tcgetattr(_inFd, out _savedLinux);
        if (rc != 0)
        {
            throw new IOException($"tcgetattr failed (errno {Marshal.GetLastPInvokeError()})");
        }
    }

    private void EnterRaw()
    {
        if (_mac)
        {
            TermiosMac raw = _savedMac;
            MakeRawMac(ref raw);
            if (LibC.tcsetattrMac(_inFd, TcsaNow, in raw) != 0)
            {
                throw new IOException($"tcsetattr failed (errno {Marshal.GetLastPInvokeError()})");
            }

            return;
        }

        TermiosLinux linux = _savedLinux;
        MakeRawLinux(ref linux);
        if (LibC.tcsetattr(_inFd, TcsaNow, in linux) != 0)
        {
            throw new IOException($"tcsetattr failed (errno {Marshal.GetLastPInvokeError()})");
        }
    }

    private void RefreshSize()
    {
        if (LibC.GetWinSize(_outFd, _mac, out WinSize size) && size.Col > 0 && size.Row > 0)
        {
            _size = new Size(size.Col, size.Row);
        }
    }

    // cfmakeraw(3): IGNBRK|BRKINT|PARMRK|ISTRIP|INLCR|IGNCR|ICRNL|IXON off, OPOST off,
    // ECHO|ECHONL|ICANON|ISIG|IEXTEN off, CSIZE|PARENB off, CS8 on; VMIN=VTIME=0 (poll decides).
    private static void MakeRawLinux(ref TermiosLinux t)
    {
        t.Iflag &= ~0x5EBu;
        t.Oflag &= ~0x1u;
        t.Lflag &= ~0x804Bu;
        t.Cflag &= ~0x130u;
        t.Cflag |= 0x30u;
        t.Cc[LibC.VTimeLinux] = 0;
        t.Cc[LibC.VMinLinux] = 0;
    }

    private static void MakeRawMac(ref TermiosMac t)
    {
        t.Iflag &= ~(nuint)0x3EBu;
        t.Oflag &= ~(nuint)0x1u;
        t.Lflag &= ~(nuint)0x598u;
        t.Cflag &= ~(nuint)0x1300u;
        t.Cflag |= 0x300u;
        t.Cc[LibC.VTimeMac] = 0;
        t.Cc[LibC.VMinMac] = 0;
    }
}
