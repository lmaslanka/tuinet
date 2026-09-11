namespace Tuinet;

internal static class Vt
{
    public static ReadOnlySpan<byte> SyncStart => "\u001b[?2026h"u8;
    public static ReadOnlySpan<byte> SyncEnd => "\u001b[?2026l"u8;
    public static ReadOnlySpan<byte> AltOn => "\u001b[?1049h"u8;
    public static ReadOnlySpan<byte> AltOff => "\u001b[?1049l"u8;
    public static ReadOnlySpan<byte> HideCursor => "\u001b[?25l"u8;
    public static ReadOnlySpan<byte> ShowCursor => "\u001b[?25h"u8;
    public static ReadOnlySpan<byte> Clear => "\u001b[2J"u8;
    public static ReadOnlySpan<byte> ResetStyle => "\u001b[0m"u8;
}
