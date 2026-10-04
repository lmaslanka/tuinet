using System.Text;
using Tuinet.Testing;

namespace Tuinet.Tests;

public class TerminalTests
{
    [Fact]
    public void Enter_switches_to_alt_screen_hides_cursor_and_disables_autowrap()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        Assert.Contains("\u001b[?1049h", tty.WrittenText);
        Assert.Contains("\u001b[?2027h", tty.WrittenText);   // grapheme cluster mode
        Assert.Contains("\u001b[?25l", tty.WrittenText);
        Assert.Contains("\u001b[?7l", tty.WrittenText);
    }

    [Fact]
    public void Dispose_restores_cursor_autowrap_and_leaves_alt_screen()
    {
        var tty = new TestTty();
        new Terminal(tty).Dispose();
        string text = tty.WrittenText;
        Assert.EndsWith("\u001b[0m\u001b[?2027l\u001b[?7h\u001b[?25h\u001b[?1049l", text);
    }

    [Fact]
    public void Options_enable_and_disable_input_modes()
    {
        var tty = new TestTty();
        var options = new TerminalOptions { Mouse = true, BracketedPaste = true, FocusEvents = true };
        new Terminal(tty, options).Dispose();
        string text = tty.WrittenText;
        Assert.Contains("\u001b[?1002h\u001b[?1006h", text);
        Assert.Contains("\u001b[?2004h", text);
        Assert.Contains("\u001b[?1004h", text);
        Assert.Contains("\u001b[?1006l", text);
        Assert.Contains("\u001b[?2004l", text);
        Assert.Contains("\u001b[?1004l", text);
    }

    [Fact]
    public void Present_writes_once_per_frame()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        tty.ClearWritten();
        terminal.BeginFrame().SetString(0, 0, "hello");
        terminal.Present();
        Assert.Equal(1, tty.WriteCount);
        Assert.Contains("hello", tty.WrittenText);
    }

    [Fact]
    public void Identical_frame_writes_nothing()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        terminal.BeginFrame().SetRune(0, 0, new Rune('A'));
        terminal.Present();
        tty.ClearWritten();
        terminal.BeginFrame().SetRune(0, 0, new Rune('A'));
        terminal.Present();
        Assert.Equal(0, terminal.LastFrameBytes);
        Assert.Equal(0, tty.WriteCount);
    }

    [Fact]
    public void One_cell_change_costs_a_cursor_move_and_a_glyph()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        terminal.BeginFrame();
        terminal.Present();
        tty.ClearWritten();
        terminal.BeginFrame().SetRune(9, 4, new Rune('x'));
        terminal.Present();
        Assert.Equal("\u001b[?2026h\u001b[5;10Hx\u001b[?2026l", tty.WrittenText);
    }

    [Fact]
    public void Present_without_begin_frame_throws()
    {
        using var terminal = new Terminal(new TestTty());
        Assert.Throws<InvalidOperationException>(terminal.Present);
    }

    [Fact]
    public void Resize_clears_the_screen_and_repaints_everything()
    {
        var tty = new TestTty(20, 5);
        using var terminal = new Terminal(tty);
        terminal.BeginFrame().SetString(0, 0, "keep");
        terminal.Present();

        tty.Resize(30, 6);
        Assert.True(terminal.Poll(out Event ev, 0));
        Assert.Equal(EventKind.Resize, ev.Kind);
        Assert.Equal(new Size(30, 6), ev.Size);

        tty.ClearWritten();
        CellBuffer frame = terminal.BeginFrame();
        Assert.Equal(new Size(30, 6), frame.Size);
        frame.SetString(0, 0, "keep");
        terminal.Present();
        Assert.StartsWith("\u001b[0m\u001b[2J", tty.WrittenText);
        Assert.Contains("keep", tty.WrittenText);
    }

    [Fact]
    public void Invalidate_repaints_unchanged_content()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        terminal.BeginFrame().SetString(0, 0, "same");
        terminal.Present();
        terminal.Invalidate();
        tty.ClearWritten();
        terminal.BeginFrame().SetString(0, 0, "same");
        terminal.Present();
        Assert.Contains("\u001b[2J", tty.WrittenText);
        Assert.Contains("same", tty.WrittenText);
    }

    [Fact]
    public void Cursor_is_shown_at_the_requested_cell_and_hidden_again()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        terminal.BeginFrame().SetCursor(3, 2);
        terminal.Present();
        Assert.EndsWith("\u001b[3;4H\u001b[?25h", tty.WrittenText);

        tty.ClearWritten();
        terminal.BeginFrame();
        terminal.Present();
        Assert.Equal("\u001b[?25l", tty.WrittenText);
    }

    [Fact]
    public void Poll_reads_j_as_char_j()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        tty.Enqueue("j");
        Assert.True(terminal.Poll(out Event ev, 0));
        Assert.Equal(EventKind.Key, ev.Kind);
        Assert.True(ev.Key.IsChar('j'));
    }

    [Fact]
    public void Posted_message_is_returned_from_poll()
    {
        using var terminal = new Terminal(new TestTty());
        terminal.Post("loaded");
        Assert.True(terminal.Poll(out Event ev, 0));
        Assert.Equal(EventKind.Message, ev.Kind);
        Assert.Equal("loaded", ev.Message);
    }

    [Fact]
    public void Poll_returns_posted_message_while_blocked()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        Event ev = default;
        bool ok = false;
        var poller = new Thread(() => ok = terminal.Poll(out ev, Timeout.Infinite));
        poller.Start();
        Assert.True(tty.Blocked.Wait(1000));
        terminal.Post("loaded");
        Assert.True(poller.Join(2000), "Poll did not unblock");
        Assert.True(ok);
        Assert.Equal("loaded", ev.Message);
    }

    [Fact]
    public void Poll_returns_resize_while_blocked()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        Event ev = default;
        var poller = new Thread(() => terminal.Poll(out ev, Timeout.Infinite));
        poller.Start();
        Assert.True(tty.Blocked.Wait(1000));
        tty.Resize(100, 40);
        Assert.True(poller.Join(2000), "Poll did not unblock");
        Assert.Equal(EventKind.Resize, ev.Kind);
        Assert.Equal(new Size(100, 40), ev.Size);
    }

    [Fact]
    public void Poll_zero_never_waits_on_an_incomplete_escape()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        tty.Enqueue("\u001b");
        Assert.False(terminal.Poll(out _, 0));
        Assert.All(tty.ReadTimeouts, t => Assert.Equal(0, t));
    }

    [Fact]
    public void Lone_escape_becomes_the_escape_key_after_the_timeout()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty, new TerminalOptions { EscapeTimeoutMs = 1 });
        tty.Enqueue("\u001b");
        Assert.True(terminal.Poll(out Event ev, 1000));
        Assert.True(ev.Key.Is(KeyCode.Escape));
    }

    [Fact]
    public void Poll_times_out_without_input()
    {
        using var terminal = new Terminal(new TestTty());
        Assert.False(terminal.Poll(out _, 10));
    }

    [Fact]
    public void Burst_of_keys_coalesces_into_one_frame()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        tty.Enqueue("jjj");
        int count = 0;
        Assert.True(terminal.Poll(out Event ev, Timeout.Infinite));
        do
        {
            if (ev.Key.IsChar('j'))
            {
                count++;
            }
        }
        while (terminal.Poll(out ev, 0));

        long frames = terminal.Frames;
        Span<char> text = stackalloc char[11];
        count.TryFormat(text, out int written);
        terminal.BeginFrame().SetString(0, 0, text[..written]);
        terminal.Present();

        Assert.Equal(3, count);
        Assert.Equal(frames + 1, terminal.Frames);
        Assert.Contains("3", tty.WrittenText);
    }

    [Fact]
    public void Bracketed_paste_arrives_as_one_event()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty, new TerminalOptions { BracketedPaste = true });
        tty.Enqueue("\u001b[200~line one\r\nline two\u001b[201~");
        Assert.True(terminal.Poll(out Event ev, 0));
        Assert.Equal(EventKind.Paste, ev.Kind);
        Assert.Equal("line one\nline two", ev.Paste);
        Assert.False(terminal.Poll(out _, 0));
    }

    [Fact]
    public void Control_characters_in_text_never_reach_the_terminal()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        tty.ClearWritten();
        terminal.BeginFrame().SetString(0, 0, "a\u001b]0;pwned\u0007b\u009b31mc\u0000\u007f\r\nd");
        terminal.Present();

        byte[] written = tty.Written;
        Assert.DoesNotContain((byte)0x07, written);
        Assert.DoesNotContain((byte)0x00, written);
        Assert.DoesNotContain((byte)0x7F, written);
        Assert.DoesNotContain((byte)'\r', written);
        Assert.DoesNotContain("\u001b]", tty.WrittenText);
        for (int i = 0; i + 1 < written.Length; i++)
        {
            Assert.False(written[i] == 0xC2 && written[i + 1] is >= 0x80 and <= 0x9F, "C1 control emitted");
        }

        Assert.Contains("a]0;pwnedb31mcd", tty.WrittenText);
    }

    [Fact]
    public void Color_mode_downsamples_rgb()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty, new TerminalOptions { ColorMode = ColorMode.Indexed256 });
        terminal.BeginFrame().SetString(0, 0, "x", new Style(Color.Rgb(255, 0, 0), Color.Default));
        terminal.Present();
        Assert.Contains("38;5;196m", tty.WrittenText);
        Assert.DoesNotContain("38;2;", tty.WrittenText);
    }
}
