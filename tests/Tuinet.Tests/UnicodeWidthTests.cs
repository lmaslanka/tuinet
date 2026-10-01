using System.Text;

namespace Tuinet.Tests;

public class UnicodeWidthTests
{
    [Theory]
    [InlineData('A', 1)]
    [InlineData(' ', 1)]
    [InlineData('~', 1)]
    [InlineData(0x00E9, 1)]   // é
    [InlineData(0x2500, 1)]   // ─ box drawing
    [InlineData(0x2026, 1)]   // … (ambiguous → narrow)
    [InlineData(0x4F60, 2)]   // 你
    [InlineData(0xAC00, 2)]   // 가
    [InlineData(0xFF21, 2)]   // fullwidth A
    [InlineData(0x1F680, 2)]  // 🚀
    [InlineData(0x1FA70, 2)]  // 🩰
    [InlineData(0x1F600, 2)]  // 😀
    [InlineData(0x20000, 2)]  // CJK ext B
    [InlineData(0x0301, 0)]   // combining acute
    [InlineData(0x200D, 0)]   // ZWJ
    [InlineData(0xFE0F, 0)]   // VS16
    [InlineData(0x1160, 0)]   // Hangul jungseong
    [InlineData(0x0000, 0)]
    [InlineData(0x001B, 0)]
    [InlineData(0x007F, 0)]
    [InlineData(0x009B, 0)]   // C1 CSI
    [InlineData(0x00AD, 1)]   // soft hyphen
    public void Width_matches_unicode(int codePoint, int expected)
    {
        Assert.Equal(expected, TextWidth.Of(new Rune(codePoint)));
    }

    [Fact]
    public void Text_width_sums_columns()
    {
        Assert.Equal(8, TextWidth.Of("ab你好🚀\u0301"));
    }
}
