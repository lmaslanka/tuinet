using System.Text;
using Tuinet;

namespace Tuinet.Kernel.Tests;

internal sealed class FakeTty : ITty
{
    private readonly ManualResetEventSlim _wake = new(false);

    public List<byte> Written { get; } = [];
    public Queue<byte> Incoming { get; } = new();
    public List<int> ReadTimeouts { get; } = [];
    public ManualResetEventSlim Blocked { get; } = new();
    public int Width { get; set; } = 80;
    public int Height { get; set; } = 24;

    public string WrittenUtf8 => Encoding.UTF8.GetString(Written.ToArray());

    public void Enqueue(ReadOnlySpan<byte> data)
    {
        foreach (byte b in data)
            Incoming.Enqueue(b);
        _wake.Set();
    }

    public void Write(ReadOnlySpan<byte> bytes) => Written.AddRange(bytes.ToArray());

    public int Read(Span<byte> buffer, int timeoutMs)
    {
        ReadTimeouts.Add(timeoutMs);
        if (Incoming.Count == 0)
        {
            if (timeoutMs == 0)
            {
                return 0;
            }

            Blocked.Set();
            if (!_wake.Wait(timeoutMs < 0 ? Timeout.Infinite : timeoutMs))
            {
                return 0;
            }

            _wake.Reset();
        }

        if (Incoming.Count == 0)
        {
            return 0;
        }

        int n = 0;
        while (n < buffer.Length && Incoming.Count > 0)
            buffer[n++] = Incoming.Dequeue();
        return n;
    }

    public void Wake() => _wake.Set();

    public void Restore()
    {
    }

    public void Dispose()
    {
        _wake.Set();
        _wake.Dispose();
        Blocked.Dispose();
    }
}
