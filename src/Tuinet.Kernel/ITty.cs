namespace Tuinet;

public interface ITty : IDisposable
{
    int Width { get; }
    int Height { get; }
    void Write(ReadOnlySpan<byte> bytes);
    int Read(Span<byte> buffer, int timeoutMs);
    void Wake();
    void Restore();
}
