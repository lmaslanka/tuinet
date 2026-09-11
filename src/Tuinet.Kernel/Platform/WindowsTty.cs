using System.Runtime.InteropServices;

namespace Tuinet;

internal sealed class WindowsTty : ITty
{
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const uint EnableProcessedInput = 0x1;
    private const uint EnableLineInput = 0x2;
    private const uint EnableEchoInput = 0x4;
    private const uint EnableVirtualTerminalInput = 0x200;
    private const uint EnableVirtualTerminalProcessing = 0x4;
    private const uint DisableNewlineAutoReturn = 0x8;
    private const uint WaitTimeout = 258;
    private const uint Infinite = 0xFFFFFFFF;

    private readonly IntPtr _in;
    private readonly IntPtr _out;
    private readonly IntPtr _wake;
    private readonly uint _savedIn;
    private readonly uint _savedOut;
    private bool _modesRestored;
    private bool _disposed;
    private int _width = 80;
    private int _height = 24;

    public WindowsTty()
    {
        _in = Kernel32.GetStdHandle(StdInputHandle);
        _out = Kernel32.GetStdHandle(StdOutputHandle);
        if (_in == IntPtr.Zero || _in == (IntPtr)(-1) || _out == IntPtr.Zero || _out == (IntPtr)(-1))
        {
            throw new InvalidOperationException("No console handles.");
        }

        if (!Kernel32.GetConsoleMode(_in, out _savedIn) || !Kernel32.GetConsoleMode(_out, out _savedOut))
        {
            throw new IOException("GetConsoleMode failed", Marshal.GetLastPInvokeError());
        }

        uint inMode = (_savedIn & ~(EnableEchoInput | EnableLineInput | EnableProcessedInput)) | EnableVirtualTerminalInput;
        uint outMode = _savedOut | EnableVirtualTerminalProcessing | DisableNewlineAutoReturn;
        if (!Kernel32.SetConsoleMode(_in, inMode) || !Kernel32.SetConsoleMode(_out, outMode))
        {
            throw new IOException("SetConsoleMode failed", Marshal.GetLastPInvokeError());
        }

        _wake = Kernel32.CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);
        if (_wake == IntPtr.Zero)
        {
            throw new IOException("CreateEvent failed", Marshal.GetLastPInvokeError());
        }

        RefreshSize();
    }

    public int Width
    {
        get
        {
            RefreshSize();
            return _width;
        }
    }

    public int Height => _height;

    public void Write(ReadOnlySpan<byte> bytes)
    {
        while (!bytes.IsEmpty)
        {
            if (!Kernel32.WriteFile(_out, bytes, bytes.Length, out int written, IntPtr.Zero))
            {
                throw new IOException("WriteFile failed", Marshal.GetLastPInvokeError());
            }
            if (written <= 0)
            {
                break;
            }
            bytes = bytes[written..];
        }
    }

    public int Read(Span<byte> buffer, int timeoutMs)
    {
        uint wait = timeoutMs < 0 ? Infinite : (uint)timeoutMs;
        Span<IntPtr> handles = [_in, _wake];
        uint result = Kernel32.WaitForMultipleObjects(2, handles, false, wait);
        if (result == WaitTimeout || result != 0)
        {
            return 0;
        }

        if (!Kernel32.ReadFile(_in, buffer, buffer.Length, out int read, IntPtr.Zero))
        {
            throw new IOException("ReadFile failed", Marshal.GetLastPInvokeError());
        }
        return read;
    }

    public void Wake() => _ = Kernel32.SetEvent(_wake);

    public void Restore()
    {
        if (_modesRestored)
        {
            return;
        }

        _modesRestored = true;
        _ = Kernel32.SetConsoleMode(_in, _savedIn);
        _ = Kernel32.SetConsoleMode(_out, _savedOut);
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Restore();
        _ = Kernel32.CloseHandle(_wake);
    }

    private (int width, int height) RefreshSize()
    {
        if (Kernel32.GetConsoleScreenBufferInfo(_out, out ConsoleScreenBufferInfo info))
        {
            int width = info.Window.Right - info.Window.Left + 1;
            int height = info.Window.Bottom - info.Window.Top + 1;
            if (width > 0 && height > 0)
            {
                _width = width;
                _height = height;
            }
        }

        return (_width, _height);
    }
}

internal static partial class Kernel32
{
    [LibraryImport("kernel32")]
    public static partial IntPtr GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteFile(IntPtr hFile, ReadOnlySpan<byte> buffer, int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadFile(IntPtr hFile, Span<byte> buffer, int nNumberOfBytesToRead, out int lpNumberOfBytesRead, IntPtr lpOverlapped);

    [LibraryImport("kernel32", SetLastError = true)]
    public static partial uint WaitForMultipleObjects(
        uint nCount,
        ReadOnlySpan<IntPtr> lpHandles,
        [MarshalAs(UnmanagedType.Bool)] bool bWaitAll,
        uint dwMilliseconds);

    [LibraryImport("kernel32", SetLastError = true)]
    public static partial IntPtr CreateEventW(
        IntPtr lpEventAttributes,
        [MarshalAs(UnmanagedType.Bool)] bool bManualReset,
        [MarshalAs(UnmanagedType.Bool)] bool bInitialState,
        IntPtr lpName);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetEvent(IntPtr hEvent);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool CloseHandle(IntPtr hObject);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetConsoleScreenBufferInfo(IntPtr hConsoleOutput, out ConsoleScreenBufferInfo lpConsoleScreenBufferInfo);
}

[StructLayout(LayoutKind.Sequential)]
internal struct Coord
{
    public short X;
    public short Y;
}

[StructLayout(LayoutKind.Sequential)]
internal struct SmallRect
{
    public short Left;
    public short Top;
    public short Right;
    public short Bottom;
}

[StructLayout(LayoutKind.Sequential)]
internal struct ConsoleScreenBufferInfo
{
    public Coord Size;
    public Coord CursorPosition;
    public ushort Attributes;
    public SmallRect Window;
    public Coord MaximumWindowSize;
}
