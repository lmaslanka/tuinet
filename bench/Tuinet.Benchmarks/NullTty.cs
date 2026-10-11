using Tuinet;

namespace Tuinet.Benchmarks;

/// <summary>Discards output (counting bytes) so benchmarks measure the library, not a terminal.</summary>
internal sealed class NullTty(int width, int height) : ITty
{
    public long Bytes { get; private set; }
    public Size Size { get; } = new(width, height);

    /// <summary>Input for the next read (a reply to a startup query), delivered once.</summary>
    public byte[] Input { get; set; } = [];

    public void Write(ReadOnlySpan<byte> bytes) => Bytes += bytes.Length;

    public int Read(Span<byte> buffer, int timeoutMs)
    {
        int n = Input.Length;
        Input.CopyTo(buffer);
        Input = [];
        return n;
    }

    public void Wake() { }
    public void Restore() { }
    public void Dispose() { }

    /// <summary>A terminal that names itself as kitty in reply to XTVERSION (so REP is used), or says nothing.</summary>
    public static Terminal Open(int width, int height, bool repeat)
    {
        var tty = new NullTty(width, height) { Input = repeat ? "\u001bP>|kitty(0.36.4)\u001b\\"u8.ToArray() : [] };
        var terminal = new Terminal(tty);
        terminal.Poll(out _, 0);
        if (terminal.RepeatSequencesActive != repeat)
        {
            throw new InvalidOperationException("REP detection failed");
        }

        return terminal;
    }
}
