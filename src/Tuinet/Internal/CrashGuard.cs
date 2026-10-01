using System.Runtime.InteropServices;

namespace Tuinet;

/// <summary>
/// Restores the terminal when the process dies without disposing the <see cref="Terminal"/>:
/// SIGINT/SIGTERM/SIGHUP/SIGQUIT (console close and Ctrl+Break on Windows), unhandled exceptions
/// (before the runtime prints the trace onto the alternate screen), and process exit.
/// </summary>
internal static class CrashGuard
{
    private static readonly Lock Gate = new();
    private static readonly List<PosixSignalRegistration> Signals = [];
    private static ITty? _tty;
    private static byte[] _leave = [];
    private static bool _hooked;

    public static void Register(ITty tty, byte[] leave)
    {
        lock (Gate)
        {
            _tty = tty;
            _leave = leave;
            if (_hooked)
            {
                return;
            }

            _hooked = true;
            AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
            AppDomain.CurrentDomain.UnhandledException += (_, _) => Restore();
            foreach (PosixSignal signal in (ReadOnlySpan<PosixSignal>)[PosixSignal.SIGINT, PosixSignal.SIGTERM, PosixSignal.SIGHUP, PosixSignal.SIGQUIT])
            {
                try
                {
                    Signals.Add(PosixSignalRegistration.Create(signal, _ => Restore()));
                }
                catch (PlatformNotSupportedException)
                {
                }
            }
        }
    }

    public static void Unregister(ITty tty)
    {
        lock (Gate)
        {
            if (ReferenceEquals(_tty, tty))
            {
                _tty = null;
            }
        }
    }

    private static void Restore()
    {
        ITty? tty;
        byte[] leave;
        lock (Gate)
        {
            tty = _tty;
            leave = _leave;
            _tty = null;
        }

        if (tty is null)
        {
            return;
        }

        try
        {
            tty.Write(leave);
        }
        catch
        {
        }

        tty.Restore();
    }
}
