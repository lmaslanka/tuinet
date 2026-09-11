using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace Tuinet;

internal static partial class LibC
{
    public const int ORdwr = 2;
    public const short PollIn = 1;

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
    public static partial int pipe([Out] int[] pipefd);

    [LibraryImport("libc", SetLastError = true)]
    public static partial int ioctl(int fd, nuint request, out WinSize winsize);

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
