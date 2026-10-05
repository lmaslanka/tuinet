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

    /// <summary>Whether <see cref="Suspend"/> can stop the process (Unix job control). False by default.</summary>
    bool CanSuspend => false;

    /// <summary>
    /// Restore the original modes, stop the process until it is continued (e.g. the shell's <c>fg</c>), then
    /// re-enter raw mode. Called only when <see cref="CanSuspend"/>; <see cref="Terminal"/> writes the
    /// leave and enter sequences around it.
    /// </summary>
    void Suspend()
    {
    }

    /// <summary>
    /// Job-control signals from outside the app since the last call; called on every poll, so it must be
    /// cheap. Before reporting <see cref="TtySignals.Continued"/>, re-apply raw mode.
    /// </summary>
    TtySignals TakeSignals() => TtySignals.None;
}

/// <summary>Job-control signals an <see cref="ITty"/> reports to <see cref="Terminal"/>.</summary>
[Flags]
public enum TtySignals : byte
{
    None = 0,

    /// <summary>Something asked the app to stop (SIGTSTP, e.g. <c>kill -TSTP</c>); the terminal suspends on the next poll.</summary>
    StopRequested = 1,

    /// <summary>The process continued after a stop it didn't make itself (e.g. SIGSTOP then <c>fg</c>); the terminal re-enters and repaints.</summary>
    Continued = 2,
}
