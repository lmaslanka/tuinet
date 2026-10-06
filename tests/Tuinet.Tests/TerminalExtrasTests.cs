using System.Text;
using Tuinet.Testing;
using Tuinet.Widgets;

namespace Tuinet.Tests;

/// <summary>Window title, clipboard, cursor shape, hyperlinks and the kitty keyboard protocol.</summary>
public class TerminalExtrasTests
{
    private const string Esc = "\u001b";
    private const string St = "\u001b\\";

    // ---- Window title ----

    [Fact]
    public void Title_saves_the_terminal_title_once_sends_only_changes_and_restores_it_on_exit()
    {
        var tty = new TestTty();
        var terminal = new Terminal(tty);
        tty.ClearWritten();

        terminal.SetTitle("my\u0007app\u001b");
        Assert.Equal($"{Esc}[22;0t{Esc}]2;myapp{St}", tty.WrittenText);   // control characters dropped

        tty.ClearWritten();
        terminal.SetTitle("my\u0007app\u001b");
        Assert.Equal(0, tty.WriteCount);

        terminal.SetTitle("ünï 文");
        Assert.Equal($"{Esc}]2;ünï 文{St}", tty.WrittenText);

        tty.ClearWritten();
        terminal.Dispose();
        Assert.Contains($"{Esc}[23;0t", tty.WrittenText);
    }

    [Fact]
    public void Without_a_title_nothing_is_saved_or_restored()
    {
        var tty = new TestTty();
        new Terminal(tty).Dispose();
        Assert.DoesNotContain("t", tty.WrittenText.Replace("?", ""), StringComparison.Ordinal);
    }

    [Fact]
    public void Suspend_restores_the_shell_title_and_resume_puts_the_app_title_back()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        terminal.SetTitle("app");
        tty.ClearWritten();
        Assert.True(terminal.Suspend());
        string text = tty.WrittenText;
        int pop = text.IndexOf($"{Esc}[23;0t", StringComparison.Ordinal);
        int push = text.IndexOf($"{Esc}[22;0t", StringComparison.Ordinal);
        Assert.True(pop >= 0 && push > pop, text);
        Assert.Contains($"{Esc}]2;app{St}", text[push..]);
    }

    // ---- Clipboard ----

    [Fact]
    public void Copy_to_clipboard_sends_base64_utf8()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        tty.ClearWritten();
        Assert.True(terminal.CopyToClipboard("héllo ✓"));
        string base64 = Convert.ToBase64String(Encoding.UTF8.GetBytes("héllo ✓"));
        Assert.Equal($"{Esc}]52;c;{base64}{St}", tty.WrittenText);
    }

    [Fact]
    public void Copy_to_clipboard_refuses_text_past_the_cap()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        tty.ClearWritten();
        Assert.True(terminal.CopyToClipboard(new string('x', Terminal.MaxClipboardBytes)));
        tty.ClearWritten();
        Assert.False(terminal.CopyToClipboard(new string('é', Terminal.MaxClipboardBytes / 2 + 1)));   // 2 bytes each
        Assert.Equal(0, tty.WriteCount);
    }

    // ---- Cursor shape ----

    [Fact]
    public void Cursor_shape_is_sent_on_change_only_and_reset_on_exit()
    {
        var tty = new TestTty();
        var terminal = new Terminal(tty);
        terminal.BeginFrame().SetCursor(2, 1);
        terminal.Present();
        Assert.DoesNotContain(" q", tty.WrittenText);

        tty.ClearWritten();
        terminal.BeginFrame().SetCursor(2, 1, CursorShape.Bar);
        terminal.Present();
        Assert.Equal($"{Esc}[6 q", tty.WrittenText);

        tty.ClearWritten();
        terminal.BeginFrame().SetCursor(3, 1, CursorShape.Bar);
        terminal.Present();
        Assert.DoesNotContain(" q", tty.WrittenText);

        tty.ClearWritten();
        terminal.Dispose();
        Assert.Contains($"{Esc}[0 q", tty.WrittenText);
    }

    [Fact]
    public void Exit_leaves_the_cursor_shape_alone_when_the_app_never_set_one()
    {
        var tty = new TestTty();
        var terminal = new Terminal(tty);
        terminal.BeginFrame().SetCursor(0, 0);
        terminal.Present();
        terminal.Dispose();
        Assert.DoesNotContain(" q", tty.WrittenText);
    }

    [Fact]
    public void Cursor_shape_is_sent_again_after_a_suspend()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        terminal.BeginFrame().SetCursor(0, 0, CursorShape.Underline);
        terminal.Present();
        terminal.Suspend();
        tty.ClearWritten();
        terminal.BeginFrame().SetCursor(0, 0, CursorShape.Underline);
        terminal.Present();
        Assert.Contains($"{Esc}[4 q", tty.WrittenText);
    }

    [Fact]
    public void Focused_text_input_shows_a_blinking_bar()
    {
        var buffer = new CellBuffer(10, 1);
        var state = new TextInputState("hi");
        buffer.Render(new TextInput { Focused = true }, buffer.Area, ref state);
        Assert.Equal(CursorShape.BlinkingBar, buffer.CursorShape);

        buffer.Clear();
        Assert.Equal(CursorShape.Default, buffer.CursorShape);
        buffer.Render(new TextInput { Focused = true, CursorShape = CursorShape.Block }, buffer.Area, ref state);
        Assert.Equal(CursorShape.Block, buffer.CursorShape);
    }

    // ---- Hyperlinks ----

    [Fact]
    public void Set_link_marks_cells_and_text_written_over_them_replaces_it()
    {
        var buffer = new CellBuffer(10, 2);
        buffer.SetString(0, 0, "docs here", new Style(Color.Blue, default));
        Assert.True(buffer.SetLink(new Rect(0, 0, 4, 1), "https://example.com/docs"));
        Assert.Equal("https://example.com/docs", buffer[0, 0].Link);
        Assert.Equal("https://example.com/docs", buffer[3, 0].Link);
        Assert.Null(buffer[4, 0].Link);
        Assert.Equal("d", buffer[0, 0].Text);
        Assert.Equal(1, buffer[0, 0].Width);

        buffer.SetStyle(new Rect(0, 0, 10, 1), new Style(default, default, Attr.Underline));
        Assert.Equal("https://example.com/docs", buffer[1, 0].Link);   // restyling keeps it

        buffer.SetString(2, 0, "X");
        Assert.Null(buffer[2, 0].Link);                                 // new text replaces it
        Assert.Equal("https://example.com/docs", buffer[1, 0].Link);

        Assert.True(buffer.SetLink(new Rect(0, 0, 10, 1), ""));
        Assert.Null(buffer[0, 0].Link);
    }

    [Fact]
    public void Set_link_takes_wide_glyphs_whole()
    {
        var buffer = new CellBuffer(6, 1);
        buffer.SetString(0, 0, "你好吗");
        buffer.SetLink(new Rect(1, 0, 2, 1), "https://x.example");
        Assert.Equal("https://x.example", buffer[0, 0].Link);
        Assert.Equal("https://x.example", buffer[3, 0].Link);
        Assert.Null(buffer[4, 0].Link);
    }

    [Theory]
    [InlineData("https://x.example/a\u001b]8;;evil", null)]          // an escape could end the OSC early: refused
    [InlineData("https://x.example/\u0085", null)]
    [InlineData("https://x.example/a b", "https://x.example/a%20b")]
    [InlineData("https://zażółć.example/ü", "https://za%C5%BC%C3%B3%C5%82%C4%87.example/%C3%BC")]
    public void Urls_are_sanitized(string url, string? stored)
    {
        var buffer = new CellBuffer(4, 1);
        Assert.Equal(stored is not null, buffer.SetLink(buffer.Area, url));
        Assert.Equal(stored, buffer[0, 0].Link);
    }

    [Fact]
    public void Linked_cells_are_wrapped_in_osc_8_and_the_link_closes_at_the_end()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty);
        terminal.BeginFrame();
        terminal.Present();

        tty.ClearWritten();
        CellBuffer frame = terminal.BeginFrame();
        frame.SetString(0, 0, "ab cd");
        frame.SetLink(new Rect(0, 0, 2, 1), "https://x.io");
        frame.SetLink(new Rect(3, 0, 2, 1), "https://y.io");
        terminal.Present();
        // The clean space between is jumped, not written, so the first link needn't close before the second opens.
        Assert.Equal($"{Esc}[?2026h{Esc}[H{Esc}]8;;https://x.io{St}ab{Esc}[C{Esc}]8;;https://y.io{St}cd{Esc}]8;;{St}{Esc}[?2026l",
            tty.WrittenText);

        // Only the links change: the cells are written again, unlinked.
        tty.ClearWritten();
        terminal.BeginFrame().SetString(0, 0, "ab cd");
        terminal.Present();
        Assert.Equal($"{Esc}[?2026h\rab cd{Esc}[?2026l", tty.WrittenText);
    }

    [Fact]
    public void Linked_blanks_are_written_not_erased()
    {
        var tty = new TestTty(30, 2);
        using var terminal = new Terminal(tty);
        terminal.BeginFrame();
        terminal.Present();
        tty.ClearWritten();
        CellBuffer frame = terminal.BeginFrame();
        frame.Fill(new Rect(0, 0, 30, 1), new Style(default, Color.Blue));
        frame.SetLink(new Rect(0, 0, 30, 1), "https://x.io");
        terminal.Present();
        Assert.Contains(new string(' ', 30), tty.WrittenText);
        Assert.DoesNotContain($"{Esc}[K", tty.WrittenText);
    }

    [Fact]
    public void Links_and_shapes_cost_no_allocations_per_frame()
    {
        using var terminal = new Terminal(new AllocationTests.NullTty(40, 4));
        for (int i = 0; i < 50; i++)
        {
            Frame(terminal, i);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            Frame(terminal, i);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        static void Frame(Terminal terminal, int i)
        {
            CellBuffer frame = terminal.BeginFrame();
            frame.SetString(0, i % 4, "open the docs");
            frame.SetLink(new Rect(9, i % 4, 4, 1), (i & 1) == 0 ? "https://a.example" : "https://b.example");
            frame.SetCursor(i % 40, 0, (i & 1) == 0 ? CursorShape.Bar : CursorShape.Block);
            terminal.Present();
        }
    }

    // ---- Kitty keyboard ----

    [Fact]
    public void Kitty_keyboard_is_pushed_on_enter_queried_and_popped_on_exit()
    {
        var tty = new TestTty();
        new Terminal(tty, new TerminalOptions { KittyKeyboard = true }).Dispose();
        string text = tty.WrittenText;
        Assert.Contains($"{Esc}[>1u{Esc}[?u", text);
        Assert.Contains($"{Esc}[<u", text);

        tty = new TestTty();
        new Terminal(tty, new TerminalOptions { KittyKeyboard = true, KeyReleaseEvents = true }).Dispose();
        Assert.Contains($"{Esc}[>3u", tty.WrittenText);

        tty = new TestTty();
        new Terminal(tty).Dispose();
        Assert.DoesNotContain("u", tty.WrittenText.Replace("?", ""), StringComparison.Ordinal);
    }

    [Fact]
    public void Kitty_keyboard_is_active_once_the_terminal_replies()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty, new TerminalOptions { KittyKeyboard = true });
        Assert.False(terminal.KittyKeyboardActive);
        tty.Enqueue($"{Esc}[?1u");
        Assert.False(terminal.Poll(out _, 0));   // the reply is not an event
        Assert.True(terminal.KittyKeyboardActive);
    }

    [Fact]
    public void Kitty_keys_tell_apart_what_legacy_encoding_merges_and_esc_needs_no_timeout()
    {
        var tty = new TestTty();
        using var terminal = new Terminal(tty, new TerminalOptions { KittyKeyboard = true });
        tty.Enqueue($"{Esc}[105;5u\t{Esc}[109;5u\r{Esc}[27u");
        Assert.True(terminal.Poll(out Event ev, 0));
        Assert.True(ev.Key.IsCtrl('i'));
        Assert.True(terminal.Poll(out ev, 0));
        Assert.True(ev.Key.Is(KeyCode.Tab));
        Assert.True(terminal.Poll(out ev, 0));
        Assert.True(ev.Key.IsCtrl('m'));
        Assert.True(terminal.Poll(out ev, 0));
        Assert.True(ev.Key.Is(KeyCode.Enter));
        Assert.True(terminal.Poll(out ev, 0));
        Assert.True(ev.Key.Is(KeyCode.Escape));
        Assert.DoesNotContain(new TerminalOptions().EscapeTimeoutMs, tty.ReadTimeouts);
    }

    [Theory]
    [InlineData("\u001b[97;1:3u", KeyCode.Char, 'a', Modifiers.None, KeyKind.Release)]
    [InlineData("\u001b[97;5:2u", KeyCode.Char, 'a', Modifiers.Ctrl, KeyKind.Repeat)]
    [InlineData("\u001b[97;1:1u", KeyCode.Char, 'a', Modifiers.None, KeyKind.Press)]
    [InlineData("\u001b[1;1:3A", KeyCode.Up, '\0', Modifiers.None, KeyKind.Release)]
    [InlineData("\u001b[5;3:2~", KeyCode.PageUp, '\0', Modifiers.Alt, KeyKind.Repeat)]
    [InlineData("\u001b[97:65;2u", KeyCode.Char, 'a', Modifiers.Shift, KeyKind.Press)]     // alternate key ignored
    [InlineData("\u001b[57399u", KeyCode.Char, '0', Modifiers.None, KeyKind.Press)]        // keypad 0
    [InlineData("\u001b[57413;5u", KeyCode.Char, '+', Modifiers.Ctrl, KeyKind.Press)]      // keypad +
    [InlineData("\u001b[57414u", KeyCode.Enter, '\0', Modifiers.None, KeyKind.Press)]      // keypad Enter
    [InlineData("\u001b[57419u", KeyCode.Up, '\0', Modifiers.None, KeyKind.Press)]         // keypad Up
    public void Kitty_event_types_and_keypad_keys(string input, KeyCode code, char c, Modifiers mods, KeyKind kind)
    {
        var parser = new VtParser();
        parser.Feed(Encoding.UTF8.GetBytes(input));
        Assert.True(parser.TryTake(out Event ev));
        Assert.Equal(new KeyEvent(code, c == '\0' ? default : new Rune(c), mods, kind), ev.Key);
        Assert.False(parser.TryTake(out _));
    }

    [Fact]
    public void Releases_never_match_key_helpers()
    {
        var release = new KeyEvent(KeyCode.Char, new Rune('q'), Modifiers.None, KeyKind.Release);
        Assert.False(release.IsChar('q'));
        Assert.False(new KeyEvent(KeyCode.Char, new Rune('s'), Modifiers.Ctrl, KeyKind.Release).IsCtrl('s'));
        Assert.False(new KeyEvent(KeyCode.Enter, default, Modifiers.None, KeyKind.Release).Is(KeyCode.Enter));
        Assert.True(new KeyEvent(KeyCode.Char, new Rune('q'), Modifiers.None, KeyKind.Repeat).IsChar('q'));
        Assert.Equal("q (Release)", release.ToString());
    }

    [Fact]
    public void Unknown_functional_keys_and_the_flags_reply_make_no_events()
    {
        var parser = new VtParser();
        parser.Feed(Encoding.UTF8.GetBytes($"{Esc}[57376u{Esc}[57441;2u{Esc}[?3u"));   // F13, left shift, reply
        Assert.False(parser.TryTake(out _));
        Assert.Equal(3, parser.KittyFlags);
    }
}
