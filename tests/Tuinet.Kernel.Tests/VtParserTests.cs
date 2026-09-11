using System.Text;
using Tuinet;

namespace Tuinet.Kernel.Tests;

public class VtParserTests
{
    [Fact]
    public void Letter_j_is_char_j()
    {
        var parser = new VtParser();
        parser.Feed("j"u8);
        Assert.True(parser.TryTake(out KeyEvent key));
        Assert.True(key.IsChar('j'));
    }

    [Fact]
    public void Csi_A_is_up()
    {
        var parser = new VtParser();
        parser.Feed("\u001b[A"u8);
        Assert.True(parser.TryTake(out KeyEvent key));
        Assert.Equal(KeyCode.Up, key.Code);
    }

    [Fact]
    public void Byte_3_is_ctrl_c()
    {
        var parser = new VtParser();
        parser.Feed([(byte)3]);
        Assert.True(parser.TryTake(out KeyEvent key));
        Assert.True(key.IsCtrl('c'));
    }

    [Fact]
    public void Application_cursor_up_is_up()
    {
        var parser = new VtParser();
        parser.Feed("\u001bOA"u8);
        Assert.True(parser.TryTake(out KeyEvent key));
        Assert.Equal(KeyCode.Up, key.Code);
    }

    [Fact]
    public void Csi_Z_is_shift_tab()
    {
        var parser = new VtParser();
        parser.Feed("\u001b[Z"u8);
        Assert.True(parser.TryTake(out KeyEvent key));
        Assert.Equal(KeyCode.Tab, key.Code);
        Assert.Equal(Modifiers.Shift, key.Modifiers);
    }

    [Fact]
    public void Lone_escape_flushes_as_escape()
    {
        var parser = new VtParser();
        parser.Feed("\u001b"u8);
        Assert.True(parser.IsIncomplete);
        parser.FlushIncomplete();
        Assert.True(parser.TryTake(out KeyEvent key));
        Assert.Equal(KeyCode.Escape, key.Code);
    }
}
