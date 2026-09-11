using System.Buffers;
using System.Collections.Concurrent;

namespace Tuinet;

public sealed class Terminal : IDisposable
{
    private readonly ITty _tty;
    private readonly bool _ownsTty;
    private readonly CellBuffer _front;
    private readonly CellBuffer _back;
    private readonly VtParser _parser = new();
    private readonly ArrayBufferWriter<byte> _out = new(4096);
    private readonly byte[] _readBuf = new byte[256];
    private readonly ConcurrentQueue<object> _messages = new();
    private bool _disposed;

    public Terminal(ITty tty)
        : this(tty, ownsTty: false)
    {
    }

    private Terminal(ITty tty, bool ownsTty)
    {
        _tty = tty;
        _ownsTty = ownsTty;
        _front = new CellBuffer(tty.Width, tty.Height);
        _back = new CellBuffer(tty.Width, tty.Height);
        if (_ownsTty)
        {
            CrashGuard.Register(_tty);
        }
        Enter();
    }

    public int Width => _back.Width;
    public int Height => _back.Height;

    public static Terminal Open()
    {
        ITty tty = OperatingSystem.IsWindows() ? new WindowsTty() : new UnixTty();
        return new Terminal(tty, ownsTty: true);
    }

    public void Post(object message)
    {
        _messages.Enqueue(message);
        _tty.Wake();
    }

    public void Draw(Action<CellBuffer> paint)
    {
        EnsureSize();
        _back.Clear();
        paint(_back);
        _out.Clear();
        Diff.Write(_back, _front, _out);
        Flush();
        _front.CopyFrom(_back);
    }

    public bool Poll(out Event ev, int timeoutMs)
    {
        if (_messages.TryDequeue(out object? message))
        {
            ev = Event.FromMessage(message);
            return true;
        }

        if (_parser.TryTake(out KeyEvent key))
        {
            ev = Event.FromKey(key);
            return true;
        }

        if (SizeChanged())
        {
            ApplyResize();
            ev = Event.FromResize(Width, Height);
            return true;
        }

        if (_parser.IsIncomplete)
        {
            FinishIncomplete(timeoutMs);
            if (_parser.TryTake(out key))
            {
                ev = Event.FromKey(key);
                return true;
            }
        }

        int n = _tty.Read(_readBuf, timeoutMs);
        if (n > 0)
        {
            _parser.Feed(_readBuf.AsSpan(0, n));
            if (_parser.IsIncomplete)
            {
                FinishIncomplete(timeoutMs);
            }

            if (_parser.TryTake(out key))
            {
                ev = Event.FromKey(key);
                return true;
            }
        }

        if (SizeChanged())
        {
            ApplyResize();
            ev = Event.FromResize(Width, Height);
            return true;
        }

        if (_messages.TryDequeue(out message))
        {
            ev = Event.FromMessage(message);
            return true;
        }

        ev = default;
        return false;
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
            Leave();
        }
        catch
        {
        }

        if (_ownsTty)
        {
            CrashGuard.Unregister(_tty);
            _tty.Dispose();
        }
    }

    private void Enter()
    {
        _out.Clear();
        Utf8Write.Bytes(_out, Vt.AltOn);
        Utf8Write.Bytes(_out, Vt.HideCursor);
        Utf8Write.Bytes(_out, Vt.Clear);
        Utf8Write.Bytes(_out, Vt.ResetStyle);
        Flush();
    }

    private void Leave()
    {
        _out.Clear();
        Utf8Write.Bytes(_out, Vt.ShowCursor);
        Utf8Write.Bytes(_out, Vt.ResetStyle);
        Utf8Write.Bytes(_out, Vt.AltOff);
        Flush();
    }

    private void Flush()
    {
        if (_out.WrittenCount == 0)
        {
            return;
        }
        _tty.Write(_out.WrittenSpan);
        _out.Clear();
    }

    private bool SizeChanged() => _tty.Width != _back.Width || _tty.Height != _back.Height;

    private void EnsureSize()
    {
        if (SizeChanged())
        {
            ApplyResize();
        }
    }

    private void ApplyResize()
    {
        int width = Math.Max(1, _tty.Width);
        int height = Math.Max(1, _tty.Height);
        _back.Resize(width, height);
        _front.Resize(width, height);
    }

    private void FinishIncomplete(int timeoutMs)
    {
        while (_parser.IsIncomplete)
        {
            int n = _tty.Read(_readBuf, 0);
            if (n > 0)
            {
                _parser.Feed(_readBuf.AsSpan(0, n));
                continue;
            }

            if (timeoutMs == 0)
            {
                break;
            }

            n = _tty.Read(_readBuf, 10);
            if (n > 0)
            {
                _parser.Feed(_readBuf.AsSpan(0, n));
                continue;
            }

            _parser.FlushIncomplete();
            break;
        }
    }
}
