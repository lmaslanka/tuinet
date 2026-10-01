using System.Text;

namespace Tuinet.Testing;

/// <summary>
/// An in-memory <see cref="ITty"/> for tests: script input with <see cref="Enqueue(ReadOnlySpan{byte})"/>,
/// resize with <see cref="Resize"/>, and inspect what the terminal wrote. Counts writes and bytes so
/// tests can pin the output cost of a frame.
/// </summary>
public sealed class TestTty : ITty
{
    private readonly Lock _gate = new();
    private readonly Queue<byte> _incoming = new();
    private readonly ManualResetEventSlim _signal = new(false);
    private readonly List<byte> _written = [];
    private Size _size;

    public TestTty(int width = 80, int height = 24) => _size = new Size(width, height);

    public Size Size
    {
        get
        {
            lock (_gate)
            {
                return _size;
            }
        }
    }

    /// <summary>Number of <see cref="Write"/> calls (each one is a syscall on a real tty).</summary>
    public int WriteCount { get; private set; }

    /// <summary>Timeouts passed to <see cref="Read"/>, in order.</summary>
    public List<int> ReadTimeouts { get; } = [];

    /// <summary>Set when a <see cref="Read"/> is about to block.</summary>
    public ManualResetEventSlim Blocked { get; } = new(false);

    public bool Restored { get; private set; }

    public byte[] Written
    {
        get
        {
            lock (_gate)
            {
                return [.. _written];
            }
        }
    }

    public string WrittenText => Encoding.UTF8.GetString(Written);

    public void ClearWritten()
    {
        lock (_gate)
        {
            _written.Clear();
            WriteCount = 0;
        }
    }

    public void Enqueue(ReadOnlySpan<byte> input)
    {
        lock (_gate)
        {
            foreach (byte b in input)
            {
                _incoming.Enqueue(b);
            }
        }

        _signal.Set();
    }

    public void Enqueue(string input) => Enqueue(Encoding.UTF8.GetBytes(input));

    public void Resize(int width, int height)
    {
        lock (_gate)
        {
            _size = new Size(width, height);
        }

        _signal.Set();
    }

    public void Write(ReadOnlySpan<byte> bytes)
    {
        lock (_gate)
        {
            _written.AddRange(bytes);
            WriteCount++;
        }
    }

    public int Read(Span<byte> buffer, int timeoutMs)
    {
        ReadTimeouts.Add(timeoutMs);
        if (!HasInput() && timeoutMs != 0)
        {
            Blocked.Set();
            if (!_signal.Wait(timeoutMs < 0 ? Timeout.Infinite : timeoutMs))
            {
                return 0;
            }
        }

        _signal.Reset();
        lock (_gate)
        {
            int n = 0;
            while (n < buffer.Length && _incoming.Count > 0)
            {
                buffer[n++] = _incoming.Dequeue();
            }

            return n;
        }
    }

    public void Wake() => _signal.Set();

    public void Restore() => Restored = true;

    public void Dispose()
    {
        _signal.Set();
    }

    private bool HasInput()
    {
        lock (_gate)
        {
            return _incoming.Count > 0;
        }
    }
}
