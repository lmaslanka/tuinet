using System.Runtime.InteropServices;

namespace Tuinet;

internal static class CrashGuard
{
    private static readonly object Gate = new();
    private static ITty? _tty;
    private static PosixSignalRegistration? _sigint;
    private static PosixSignalRegistration? _sigterm;
    private static bool _exitHooked;

    public static void Register(ITty tty)
    {
        lock (Gate)
        {
            _tty = tty;
            if (!_exitHooked)
            {
                AppDomain.CurrentDomain.ProcessExit += (_, _) => Restore();
                _exitHooked = true;
            }

            if (!OperatingSystem.IsWindows())
            {
                _sigint ??= PosixSignalRegistration.Create(PosixSignal.SIGINT, OnSignal);
                _sigterm ??= PosixSignalRegistration.Create(PosixSignal.SIGTERM, OnSignal);
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

    private static void OnSignal(PosixSignalContext ctx) => Restore();

    private static void Restore()
    {
        ITty? tty;
        lock (Gate)
        {
            tty = _tty;
            _tty = null;
        }

        if (tty is null)
        {
            return;
        }

        try
        {
            tty.Write(Vt.ShowCursor);
            tty.Write(Vt.ResetStyle);
            tty.Write(Vt.AltOff);
            tty.Restore();
        }
        catch
        {
        }
    }
}
