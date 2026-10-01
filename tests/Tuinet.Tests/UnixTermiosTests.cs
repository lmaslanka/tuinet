using System.Runtime.InteropServices;
using Tuinet;

namespace Tuinet.Tests;

public class UnixTermiosTests
{
    [Fact]
    public void Linux_termios_matches_glibc_layout()
    {
        if (!OperatingSystem.IsLinux())
        {
            return;
        }

        Assert.Equal(60, Marshal.SizeOf<TermiosLinux>());
        Assert.Equal(16, Marshal.OffsetOf<TermiosLinux>(nameof(TermiosLinux.Line)).ToInt32());
        Assert.Equal(52, Marshal.OffsetOf<TermiosLinux>(nameof(TermiosLinux.Ispeed)).ToInt32());
    }
}
