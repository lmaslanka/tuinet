using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tuinet;

internal static unsafe partial class LibC
{
    public const int ORdwr = 2;
    public const int OCloexecLinux = 0x80000;
    public const int OCloexecMac = 0x1000000;
    public const short PollIn = 0x1;
    public const short PollOut = 0x4;
    public const short PollErr = 0x8;
    public const short PollHup = 0x10;
    public const short PollNval = 0x20;
    public const int EINTR = 4;
    public const int VTimeLinux = 5;
    public const int VMinLinux = 6;
    public const int VMinMac = 16;
    public const int VTimeMac = 17;

    private const nuint TiocgwinszLinux = 0x5413;
    private const nuint TiocgwinszMac = 0x40087468;

    public static bool IsEAgain(int errno) => errno == (OperatingSystem.IsMacOS() ? 35 : 11);

    /// <summary>
    /// TIOCGWINSZ. ioctl is variadic: on Apple arm64 variadic arguments go on the stack, so the
    /// pointer is passed as the 9th argument (after 8 register slots) to land where va_arg reads it.
    /// </summary>
    public static bool GetWinSize(int fd, bool mac, out WinSize size)
    {
        WinSize result = default;
        int rc = !mac
            ? ioctl(fd, TiocgwinszLinux, &result)
            : RuntimeInformation.ProcessArchitecture == Architecture.Arm64
                ? ioctlArm64Variadic(fd, TiocgwinszMac, 0, 0, 0, 0, 0, 0, &result)
                : ioctl(fd, TiocgwinszMac, &result);
        size = result;
        return rc == 0;
    }

    [LibraryImport("libc", SetLastError = true)]
    public static partial int isatty(int fd);

    [LibraryImport("libc", SetLastError = true, StringMarshalling = StringMarshalling.Utf8)]
    public static partial int open(string path, int flags);

    [LibraryImport("libc")]
    public static partial int close(int fd);

    [LibraryImport("libc", SetLastError = true)]
    public static partial nint read(int fd, Span<byte> buf, nint count);

    [LibraryImport("libc", SetLastError = true)]
    public static partial nint write(int fd, ReadOnlySpan<byte> buf, nint count);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int poll(ref PollFd fds, nuint nfds, int timeout);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int pipe(ref int pipefd);

    [LibraryImport("libc", SetLastError = true)]
    private static partial int ioctl(int fd, nuint request, WinSize* winsize);

    [LibraryImport("libc", EntryPoint = "ioctl", SetLastError = true)]
    private static partial int ioctlArm64Variadic(int fd, nuint request, nint x2, nint x3, nint x4, nint x5, nint x6, nint x7, WinSize* winsize);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int tcgetattr(int fd, out TermiosLinux termios);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int tcsetattr(int fd, int optionalActions, in TermiosLinux termios);

    [LibraryImport("libc", EntryPoint = "tcgetattr", SetLastError = true)]
    public static partial int tcgetattrMac(int fd, out TermiosMac termios);

    [LibraryImport("libc", EntryPoint = "tcsetattr", SetLastError = true)]
    public static partial int tcsetattrMac(int fd, int optionalActions, in TermiosMac termios);
}

[StructLayout(LayoutKind.Sequential)]
internal struct PollFd
{
    public int Fd;
    public short Events;
    public short Revents;
}

[StructLayout(LayoutKind.Sequential)]
internal struct WinSize
{
    public ushort Row;
    public ushort Col;
    public ushort XPixel;
    public ushort YPixel;
}

[InlineArray(32)]
internal struct Cc32
{
    private byte _element0;
}

[InlineArray(20)]
internal struct Cc20
{
    private byte _element0;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TermiosLinux
{
    public uint Iflag;
    public uint Oflag;
    public uint Cflag;
    public uint Lflag;
    public byte Line;
    public Cc32 Cc;
    public uint Ispeed;
    public uint Ospeed;
}

[StructLayout(LayoutKind.Sequential)]
internal struct TermiosMac
{
    public nuint Iflag;
    public nuint Oflag;
    public nuint Cflag;
    public nuint Lflag;
    public Cc20 Cc;
    public nuint Ispeed;
    public nuint Ospeed;
}
