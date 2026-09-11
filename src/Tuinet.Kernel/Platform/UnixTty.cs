using System.Runtime.InteropServices;

namespace Tuinet;

internal sealed class UnixTty : ITty
{
    private const int TcsaNow = 0;
    private readonly int _inFd;
    private readonly int _outFd;
    private readonly int _wakeRead;
    private readonly int _wakeWrite;
    private readonly bool _ownsFd;
    private readonly bool _mac;
    private TermiosLinux _savedLinux;
    private TermiosMac _savedMac;
    private readonly PosixSignalRegistration? _winch;
    private bool _modesRestored;
    private bool _disposed;
    private int _width = 80;
    private int _height = 24;

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
            int fd = LibC.open("/dev/tty", LibC.ORdwr);
            if (fd < 0)
            {
                throw new InvalidOperationException("No tty available.");
            }
            _inFd = fd;
            _outFd = fd;
            _ownsFd = true;
        }

        int[] pipe = new int[2];
        if (LibC.pipe(pipe) != 0)
        {
            if (_ownsFd)
            {
                _ = LibC.close(_inFd);
            }

            throw new IOException("pipe failed", Marshal.GetLastPInvokeError());
        }

        _wakeRead = pipe[0];
        _wakeWrite = pipe[1];
        if (!OperatingSystem.IsWindows())
        {
            _winch = PosixSignalRegistration.Create(PosixSignal.SIGWINCH, _ => Wake());
        }
        EnterRaw();
        RefreshSize();
    }

    public int Width
    {
        get
        {
            RefreshSize();
            return _width;
        }
    }

    public int Height => _height;

    public void Write(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            nint n = LibC.write(_outFd, bytes, bytes.Length);
            if (n < 0)
            {
                throw new IOException("write failed", Marshal.GetLastPInvokeError());
            }
            if (n == 0)
            {
                break;
            }
            bytes = bytes[(int)n..];
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
            return 0;
        }

        if ((fds[1].Revents & LibC.PollIn) != 0)
        {
            Span<byte> drain = stackalloc byte[64];
            _ = LibC.read(_wakeRead, drain, drain.Length);
        }

        if ((fds[0].Revents & LibC.PollIn) == 0)
        {
            return 0;
        }

        nint n = LibC.read(_inFd, buffer, buffer.Length);
        if (n < 0)
        {
            throw new IOException("read failed", Marshal.GetLastPInvokeError());
        }
        return (int)n;
    }

    public void Wake()
    {
        ReadOnlySpan<byte> one = [1];
        _ = LibC.write(_wakeWrite, one, 1);
    }

    public void Restore()
    {
        if (_modesRestored)
        {
            return;
        }

        _modesRestored = true;
        try
        {
            LeaveRaw();
        }
        catch
        {
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
        Restore();
        if (_ownsFd)
        {
            _ = LibC.close(_inFd);
        }

        _ = LibC.close(_wakeRead);
        _ = LibC.close(_wakeWrite);
    }

    private void EnterRaw()
    {
        if (_mac)
        {
            if (LibC.tcgetattrMac(_inFd, out _savedMac) != 0)
            {
                throw new IOException("tcgetattr failed", Marshal.GetLastPInvokeError());
            }
            TermiosMac raw = _savedMac;
            MakeRawMac(ref raw);
            if (LibC.tcsetattrMac(_inFd, TcsaNow, in raw) != 0)
            {
                throw new IOException("tcsetattr failed", Marshal.GetLastPInvokeError());
            }
            return;
        }

        if (LibC.tcgetattr(_inFd, out _savedLinux) != 0)
        {
            throw new IOException("tcgetattr failed", Marshal.GetLastPInvokeError());
        }
        TermiosLinux linux = _savedLinux;
        MakeRawLinux(ref linux);
        if (LibC.tcsetattr(_inFd, TcsaNow, in linux) != 0)
        {
            throw new IOException("tcsetattr failed", Marshal.GetLastPInvokeError());
        }
    }

    private void LeaveRaw()
    {
        if (_mac)
        {
            _ = LibC.tcsetattrMac(_inFd, TcsaNow, in _savedMac);
        }
        else
        {
            _ = LibC.tcsetattr(_inFd, TcsaNow, in _savedLinux);
        }
    }

    private WinSize RefreshSize()
    {
        nuint request = _mac ? 0x40087468u : 0x5413u;
        if (LibC.ioctl(_outFd, request, out WinSize size) == 0 && size.Col > 0 && size.Row > 0)
        {
            _width = size.Col;
            _height = size.Row;
            return size;
        }

        return new WinSize { Col = (ushort)_width, Row = (ushort)_height };
    }

    private static void MakeRawLinux(ref TermiosLinux t)
    {
        t.Iflag &= ~0x5CBu;
        t.Oflag &= ~0x1u;
        t.Lflag &= ~0x804Bu;
        t.Cflag &= ~0x130u;
        t.Cflag |= 0x30u;
        t.Cc[5] = 0;
        t.Cc[6] = 0;
    }

    private static void MakeRawMac(ref TermiosMac t)
    {
        t.Iflag &= ~(nuint)0x3EBu;
        t.Oflag &= ~(nuint)0x1u;
        t.Lflag &= ~(nuint)0x598u;
        t.Cflag &= ~(nuint)0x1300u;
        t.Cflag |= 0x300u;
        t.Cc[17] = 0;
        t.Cc[16] = 0;
    }
}
