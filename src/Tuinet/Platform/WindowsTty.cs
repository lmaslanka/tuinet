using System.Runtime.InteropServices;
using System.Text;

namespace Tuinet;

/// <summary>
/// Windows console in VT mode. Input is read as console input records (never <c>ReadFile</c>, which
/// blocks until a key arrives even when the handle was signaled by focus/mouse/resize records); the
/// UTF-16 key characters, which carry VT sequences in VT input mode, are re-encoded as UTF-8 so
/// the shared parser sees the same bytes as on Unix. Output is UTF-8 via code page 65001.
/// </summary>
internal sealed unsafe class WindowsTty : ITty
{
    private const int StdInputHandle = -10;
    private const int StdOutputHandle = -11;
    private const uint EnableProcessedInput = 0x1;
    private const uint EnableLineInput = 0x2;
    private const uint EnableEchoInput = 0x4;
    private const uint EnableWindowInput = 0x8;
    private const uint EnableQuickEditMode = 0x40;
    private const uint EnableExtendedFlags = 0x80;
    private const uint EnableVirtualTerminalInput = 0x200;
    private const uint EnableVirtualTerminalProcessing = 0x4;
    private const uint DisableNewlineAutoReturn = 0x8;
    private const uint WaitObject0 = 0;
    private const uint Infinite = 0xFFFFFFFF;
    private const uint Utf8CodePage = 65001;
    private const ushort KeyEventType = 0x1;
    private const ushort WindowBufferSizeEventType = 0x4;

    private readonly IntPtr _in;
    private readonly IntPtr _out;
    private readonly IntPtr _wake;
    private readonly uint _savedIn;
    private readonly uint _savedOut;
    private readonly uint _savedInputCp;
    private readonly uint _savedOutputCp;
    private readonly InputRecord[] _records = new InputRecord[128];
    private char _highSurrogate;
    private Size _size = new(80, 24);
    private volatile bool _resized = true;
    private int _restored;
    private bool _disposed;

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

        _savedInputCp = Kernel32.GetConsoleCP();
        _savedOutputCp = Kernel32.GetConsoleOutputCP();

        uint inMode = (_savedIn & ~(EnableEchoInput | EnableLineInput | EnableProcessedInput | EnableQuickEditMode))
            | EnableVirtualTerminalInput | EnableWindowInput | EnableExtendedFlags;
        uint outMode = _savedOut | EnableVirtualTerminalProcessing | DisableNewlineAutoReturn;
        if (!Kernel32.SetConsoleMode(_in, inMode) || !Kernel32.SetConsoleMode(_out, outMode))
        {
            int error = Marshal.GetLastPInvokeError();
            Restore();
            throw new IOException("SetConsoleMode failed", error);
        }

        _ = Kernel32.SetConsoleCP(Utf8CodePage);
        _ = Kernel32.SetConsoleOutputCP(Utf8CodePage);

        _wake = Kernel32.CreateEventW(IntPtr.Zero, false, false, IntPtr.Zero);
        if (_wake == IntPtr.Zero)
        {
            int error = Marshal.GetLastPInvokeError();
            Restore();
            throw new IOException("CreateEvent failed", error);
        }
    }

    public Size Size
    {
        get
        {
            if (_resized)
            {
                _resized = false;
                RefreshSize();
            }

            return _size;
        }
    }

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
        long deadline = timeoutMs < 0 ? long.MaxValue : Environment.TickCount64 + timeoutMs;
        Span<IntPtr> handles = [_in, _wake];
        while (true)
        {
            uint wait = timeoutMs < 0 ? Infinite : (uint)Math.Max(0, deadline - Environment.TickCount64);
            uint result = Kernel32.WaitForMultipleObjects(2, handles, false, wait);
            if (result != WaitObject0)
            {
                return 0;
            }

            if (!Kernel32.GetNumberOfConsoleInputEvents(_in, out uint available) || available == 0)
            {
                continue;
            }

            int max = Math.Min(_records.Length, Math.Max(1, buffer.Length / 8));
            uint read;
            bool ok;
            fixed (InputRecord* records = _records)
            {
                ok = Kernel32.ReadConsoleInputW(_in, records, (uint)max, out read);
            }

            if (!ok)
            {
                throw new IOException("ReadConsoleInput failed", Marshal.GetLastPInvokeError());
            }

            int n = Translate(_records.AsSpan(0, (int)read), buffer);
            if (n > 0 || _resized)
            {
                return n;
            }

            if (timeoutMs >= 0 && Environment.TickCount64 >= deadline)
            {
                return 0;
            }
        }
    }

    public void Wake() => _ = Kernel32.SetEvent(_wake);

    public void Restore()
    {
        if (Interlocked.Exchange(ref _restored, 1) != 0)
        {
            return;
        }

        _ = Kernel32.SetConsoleMode(_in, _savedIn);
        _ = Kernel32.SetConsoleMode(_out, _savedOut);
        if (_savedInputCp != 0)
        {
            _ = Kernel32.SetConsoleCP(_savedInputCp);
        }

        if (_savedOutputCp != 0)
        {
            _ = Kernel32.SetConsoleOutputCP(_savedOutputCp);
        }
    }

    public void Dispose()
    {
        if (_disposed)
        {
            return;
        }

        _disposed = true;
        Restore();
        if (_wake != IntPtr.Zero)
        {
            _ = Kernel32.CloseHandle(_wake);
        }
    }

    /// <summary>Key-down characters → UTF-8; resize records set the resize flag; everything else is dropped.</summary>
    private int Translate(ReadOnlySpan<InputRecord> records, Span<byte> buffer)
    {
        int n = 0;
        foreach (ref readonly InputRecord record in records)
        {
            if (record.EventType == WindowBufferSizeEventType)
            {
                _resized = true;
                continue;
            }

            if (record.EventType != KeyEventType || record.Key.KeyDown == 0 || record.Key.UnicodeChar == 0)
            {
                continue;
            }

            char c = (char)record.Key.UnicodeChar;
            if (char.IsHighSurrogate(c))
            {
                _highSurrogate = c;
                continue;
            }

            Rune rune;
            if (char.IsLowSurrogate(c))
            {
                if (_highSurrogate == 0)
                {
                    continue;
                }

                rune = new Rune(_highSurrogate, c);
                _highSurrogate = '\0';
            }
            else
            {
                rune = new Rune(c);
            }

            int repeat = Math.Max((int)record.Key.RepeatCount, 1);
            for (int i = 0; i < repeat && n + 4 <= buffer.Length; i++)
            {
                n += rune.EncodeToUtf8(buffer[n..]);
            }
        }

        return n;
    }

    private void RefreshSize()
    {
        if (Kernel32.GetConsoleScreenBufferInfo(_out, out ConsoleScreenBufferInfo info))
        {
            int width = info.Window.Right - info.Window.Left + 1;
            int height = info.Window.Bottom - info.Window.Top + 1;
            if (width > 0 && height > 0)
            {
                _size = new Size(width, height);
            }
        }
    }
}

internal static unsafe partial class Kernel32
{
    [LibraryImport("kernel32")]
    public static partial IntPtr GetStdHandle(int nStdHandle);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetConsoleMode(IntPtr hConsoleHandle, out uint lpMode);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetConsoleMode(IntPtr hConsoleHandle, uint dwMode);

    [LibraryImport("kernel32")]
    public static partial uint GetConsoleCP();

    [LibraryImport("kernel32")]
    public static partial uint GetConsoleOutputCP();

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetConsoleCP(uint wCodePageID);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool SetConsoleOutputCP(uint wCodePageID);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool WriteFile(IntPtr hFile, ReadOnlySpan<byte> buffer, int nNumberOfBytesToWrite, out int lpNumberOfBytesWritten, IntPtr lpOverlapped);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool GetNumberOfConsoleInputEvents(IntPtr hConsoleInput, out uint lpcNumberOfEvents);

    [LibraryImport("kernel32", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    public static partial bool ReadConsoleInputW(IntPtr hConsoleInput, InputRecord* lpBuffer, uint nLength, out uint lpNumberOfEventsRead);

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

/// <summary>INPUT_RECORD: a 2-byte event type, padding, then a 16-byte union (KEY_EVENT_RECORD layout here).</summary>
[StructLayout(LayoutKind.Explicit, Size = 20)]
internal struct InputRecord
{
    [FieldOffset(0)]
    public ushort EventType;

    [FieldOffset(4)]
    public KeyEventRecord Key;
}

[StructLayout(LayoutKind.Sequential)]
internal struct KeyEventRecord
{
    public int KeyDown;
    public ushort RepeatCount;
    public ushort VirtualKeyCode;
    public ushort VirtualScanCode;
    public ushort UnicodeChar;
    public uint ControlKeyState;
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
