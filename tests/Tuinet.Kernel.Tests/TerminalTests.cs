using System.Text;
using Tuinet;

namespace Tuinet.Kernel.Tests;

public class TerminalTests
{
    [Fact]
    public void Enter_switches_to_alt_screen_and_hides_cursor()
    {
        var tty = new FakeTty();
        using var terminal = new Terminal(tty);
        Assert.Contains("\u001b[?1049h", tty.WrittenUtf8);
        Assert.Contains("\u001b[?25l", tty.WrittenUtf8);
    }

    [Fact]
    public void Dispose_restores_cursor_and_leaves_alt_screen()
    {
        var tty = new FakeTty();
        new Terminal(tty).Dispose();
        Assert.Contains("\u001b[?25h", tty.WrittenUtf8);
        Assert.Contains("\u001b[?1049l", tty.WrittenUtf8);
    }

    [Fact]
    public void Second_identical_draw_does_not_rewrite_cells()
    {
        var tty = new FakeTty();
        using var terminal = new Terminal(tty);
        terminal.Draw(b => b.Put(0, 0, new Rune('A')));
        tty.Written.Clear();
        terminal.Draw(b => b.Put(0, 0, new Rune('A')));
        Assert.DoesNotContain("A", tty.WrittenUtf8);
    }

    [Fact]
    public void Poll_reads_j_as_char_j()
    {
        var tty = new FakeTty();
        using var terminal = new Terminal(tty);
        tty.Enqueue("j"u8);
        Assert.True(terminal.Poll(out Event ev, 0));
        Assert.Equal(EventKind.Key, ev.Kind);
        Assert.True(ev.Key.IsChar('j'));
    }

    [Fact]
    public void Posted_message_is_returned_from_poll()
    {
        var tty = new FakeTty();
        using var terminal = new Terminal(tty);
        terminal.Post("loaded");
        Assert.True(terminal.Poll(out Event ev, 0));
        Assert.Equal(EventKind.Message, ev.Kind);
        Assert.Equal("loaded", ev.Message);
    }

    [Fact]
    public void Poll_returns_posted_message_while_blocked()
    {
        var tty = new FakeTty();
        using var terminal = new Terminal(tty);
        Event ev = default;
        bool ok = false;
        var poller = new Thread(() => ok = terminal.Poll(out ev, Timeout.Infinite));
        poller.Start();
        Assert.True(tty.Blocked.Wait(1000));
        terminal.Post("loaded");
        if (!poller.Join(1000))
        {
            tty.Wake();
            poller.Join(1000);
            Assert.Fail("Poll did not unblock");
        }

        Assert.True(ok);
        Assert.Equal(EventKind.Message, ev.Kind);
        Assert.Equal("loaded", ev.Message);
    }

    [Fact]
    public void Poll_returns_resize_while_blocked()
    {
        var tty = new FakeTty();
        using var terminal = new Terminal(tty);
        Event ev = default;
        bool ok = false;
        var poller = new Thread(() => ok = terminal.Poll(out ev, Timeout.Infinite));
        poller.Start();
        Assert.True(tty.Blocked.Wait(1000));
        tty.Width = 100;
        tty.Height = 40;
        tty.Wake();
        if (!poller.Join(1000))
        {
            tty.Wake();
            poller.Join(1000);
            Assert.Fail("Poll did not unblock");
        }

        Assert.True(ok);
        Assert.Equal(EventKind.Resize, ev.Kind);
        Assert.Equal(100, ev.Width);
        Assert.Equal(40, ev.Height);
    }

    [Fact]
    public void Poll_zero_does_not_wait_on_incomplete_escape()
    {
        var tty = new FakeTty();
        using var terminal = new Terminal(tty);
        tty.Enqueue("\u001b"u8);
        Assert.False(terminal.Poll(out _, 0));
        Assert.All(tty.ReadTimeouts, t => Assert.True(t <= 0));
    }

    [Fact]
    public void Hold_j_increments_count_on_each_key()
    {
        var tty = new FakeTty();
        using var terminal = new Terminal(tty);
        int count = 0;
        tty.Enqueue("jjj"u8);
        while (terminal.Poll(out Event ev, 0))
        {
            if (ev.Kind == EventKind.Key && ev.Key.IsChar('j'))
            {
                count++;
            }
            int snapshot = count;
            terminal.Draw(b => b.Put(0, 0, snapshot.ToString()));
        }

        Assert.Equal(3, count);
        Assert.Contains("3", tty.WrittenUtf8);
    }
}
