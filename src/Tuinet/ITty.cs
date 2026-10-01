namespace Tuinet;

/// <summary>
/// The platform seam: raw byte I/O with a terminal. Implement it to run a <see cref="Terminal"/>
/// over anything (a pty, a socket, a test double).
/// </summary>
public interface ITty : IDisposable
{
    /// <summary>Current size. Must be cheap: called on every poll. Backends refresh it on resize notification.</summary>
    Size Size { get; }

    /// <summary>Write all bytes.</summary>
    void Write(ReadOnlySpan<byte> bytes);

    /// <summary>
    /// Wait up to <paramref name="timeoutMs"/> (-1 = forever, 0 = don't wait) for input and read it.
    /// Returns 0 on timeout, on <see cref="Wake"/>, or when the size may have changed.
    /// </summary>
    int Read(Span<byte> buffer, int timeoutMs);

    /// <summary>Make a blocked <see cref="Read"/> return now. Callable from any thread.</summary>
    void Wake();

    /// <summary>Restore the terminal's original modes (termios / console modes). Idempotent; must not throw.</summary>
    void Restore();
}
