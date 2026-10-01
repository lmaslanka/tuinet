using Tuinet;

namespace Tuinet.Benchmarks;

/// <summary>Discards output (counting bytes) so benchmarks measure the library, not a terminal.</summary>
internal sealed class NullTty(int width, int height) : ITty
{
    public long Bytes { get; private set; }
    public Size Size { get; } = new(width, height);
    public void Write(ReadOnlySpan<byte> bytes) => Bytes += bytes.Length;
    public int Read(Span<byte> buffer, int timeoutMs) => 0;
    public void Wake() { }
    public void Restore() { }
    public void Dispose() { }
}
