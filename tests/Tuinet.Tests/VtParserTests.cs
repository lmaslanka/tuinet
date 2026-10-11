using System.Text;

namespace Tuinet.Tests;

public class VtParserTests
{
    [Fact]
    public void Letter_j_is_char_j() => Assert.True(Key("j").IsChar('j'));

    [Fact]
    public void Csi_A_is_up() => Assert.True(Key("\u001b[A").Is(KeyCode.Up));

    [Fact]
    public void Application_cursor_up_is_up() => Assert.True(Key("\u001bOA").Is(KeyCode.Up));

    [Fact]
    public void Byte_3_is_ctrl_c() => Assert.True(Key([3]).IsCtrl('c'));

    [Fact]
    public void Nul_is_ctrl_space() => Assert.Equal(KeyEvent.Char(' ', Modifiers.Ctrl), Key([0]));

    [Fact]
    public void Byte_1c_is_ctrl_backslash() => Assert.Equal(KeyEvent.Char('\\', Modifiers.Ctrl), Key([0x1C]));

    [Fact]
    public void Csi_Z_is_shift_tab() => Assert.True(Key("\u001b[Z").Is(KeyCode.Tab, Modifiers.Shift));

    [Fact]
    public void Esc_prefix_is_alt() => Assert.Equal(KeyEvent.Char('x', Modifiers.Alt), Key("\u001bx"));

    [Fact]
    public void Esc_prefix_with_utf8_is_alt() => Assert.Equal(new KeyEvent(KeyCode.Char, new Rune('é'), Modifiers.Alt), Key("\u001bé"));

    [Fact]
    public void Alt_backspace() => Assert.True(Key("\u001b\u007f").Is(KeyCode.Backspace, Modifiers.Alt));

    [Fact]
    public void Ctrl_up_from_modifier_param() => Assert.True(Key("\u001b[1;5A").Is(KeyCode.Up, Modifiers.Ctrl));

    [Fact]
    public void Shift_alt_right_from_modifier_param() =>
        Assert.True(Key("\u001b[1;4C").Is(KeyCode.Right, Modifiers.Shift | Modifiers.Alt));

    [Theory]
    [InlineData("\u001b[2~", KeyCode.Insert)]
    [InlineData("\u001b[3~", KeyCode.Delete)]
    [InlineData("\u001b[5~", KeyCode.PageUp)]
    [InlineData("\u001b[6~", KeyCode.PageDown)]
    [InlineData("\u001b[1~", KeyCode.Home)]
    [InlineData("\u001b[4~", KeyCode.End)]
    [InlineData("\u001bOP", KeyCode.F1)]
    [InlineData("\u001b[15~", KeyCode.F5)]
    [InlineData("\u001b[17~", KeyCode.F6)]
    [InlineData("\u001b[24~", KeyCode.F12)]
    public void Tilde_and_function_keys(string input, KeyCode expected) => Assert.True(Key(input).Is(expected));

    [Fact]
    public void Ctrl_delete() => Assert.True(Key("\u001b[3;5~").Is(KeyCode.Delete, Modifiers.Ctrl));

    [Fact]
    public void Kitty_csi_u_key() => Assert.Equal(KeyEvent.Char('a', Modifiers.Ctrl), Key("\u001b[97;5u"));

    [Fact]
    public void Utf8_split_across_feeds()
    {
        var parser = new VtParser();
        byte[] bytes = Encoding.UTF8.GetBytes("你");
        parser.Feed(bytes.AsSpan(0, 1));
        Assert.True(parser.IsIncomplete);
        parser.Feed(bytes.AsSpan(1));
        Assert.True(parser.TryTake(out Event ev));
        Assert.Equal(new Rune(0x4F60), ev.Key.Rune);
    }

    [Fact]
    public void Invalid_utf8_is_dropped_and_parsing_recovers()
    {
        var parser = new VtParser();
        parser.Feed([0xE4, (byte)'a', 0xFF, (byte)'b']);
        Assert.True(parser.TryTake(out Event a));
        Assert.True(a.Key.IsChar('a'));
        Assert.True(parser.TryTake(out Event b));
        Assert.True(b.Key.IsChar('b'));
        Assert.False(parser.TryTake(out _));
    }

    [Fact]
    public void Lone_escape_flushes_as_escape()
    {
        var parser = new VtParser();
        parser.Feed("\u001b"u8);
        Assert.True(parser.IsIncomplete);
        parser.FlushIncomplete();
        Assert.True(parser.TryTake(out Event ev));
        Assert.True(ev.Key.Is(KeyCode.Escape));
    }

    [Fact]
    public void Esc_bracket_flushes_as_alt_bracket()
    {
        var parser = new VtParser();
        parser.Feed("\u001b["u8);
        parser.FlushIncomplete();
        Assert.True(parser.TryTake(out Event ev));
        Assert.Equal(KeyEvent.Char('[', Modifiers.Alt), ev.Key);
    }

    [Fact]
    public void Sgr_mouse_press_release_and_wheel()
    {
        var parser = new VtParser();
        parser.Feed("\u001b[<0;10;5M\u001b[<0;10;5m\u001b[<64;1;1M\u001b[<34;3;4M\u001b[<35;3;4M\u001b[<16;2;2M"u8);
        Assert.Equal(new MouseEvent(MouseKind.Down, MouseButton.Left, 9, 4, Modifiers.None), Mouse(parser));
        Assert.Equal(new MouseEvent(MouseKind.Up, MouseButton.Left, 9, 4, Modifiers.None), Mouse(parser));
        Assert.Equal(MouseKind.ScrollUp, Mouse(parser).Kind);
        Assert.Equal(new MouseEvent(MouseKind.Drag, MouseButton.Right, 2, 3, Modifiers.None), Mouse(parser));
        Assert.Equal(new MouseEvent(MouseKind.Move, MouseButton.None, 2, 3, Modifiers.None), Mouse(parser));
        Assert.Equal(Modifiers.Ctrl, Mouse(parser).Modifiers);
    }

    [Fact]
    public void Focus_reports()
    {
        var parser = new VtParser();
        parser.Feed("\u001b[I\u001b[O"u8);
        Assert.True(parser.TryTake(out Event a));
        Assert.Equal(EventKind.FocusGained, a.Kind);
        Assert.True(parser.TryTake(out Event b));
        Assert.Equal(EventKind.FocusLost, b.Kind);
    }

    [Fact]
    public void Bracketed_paste_split_across_feeds()
    {
        var parser = new VtParser { BracketedPaste = true };
        parser.Feed("\u001b[200~hel"u8);
        Assert.False(parser.IsIncomplete);
        Assert.False(parser.TryTake(out _));
        parser.Feed("lo\u001b[20"u8);
        parser.Feed("1~x"u8);
        Assert.True(parser.TryTake(out Event paste));
        Assert.Equal("hello", paste.Paste);
        Assert.True(parser.TryTake(out Event key));
        Assert.True(key.Key.IsChar('x'));
    }

    [Fact]
    public void Paste_does_not_interpret_escape_sequences()
    {
        var parser = new VtParser { BracketedPaste = true };
        parser.Feed("\u001b[200~\u001b[A\u0003\u001b[201~"u8);
        Assert.True(parser.TryTake(out Event paste));
        Assert.Equal("\u001b[A\u0003", paste.Paste);
        Assert.False(parser.TryTake(out _));
    }

    [Fact]
    public void Paste_with_a_false_start_of_the_end_marker_keeps_it_as_text()
    {
        var parser = new VtParser { BracketedPaste = true };
        parser.Feed("\u001b[200~a\u001b[20\u001bb\u001b[201~"u8);
        Assert.True(parser.TryTake(out Event paste));
        Assert.Equal("a\u001b[20\u001bb", paste.Paste);
        Assert.False(parser.TryTake(out _));
    }

    [Fact]
    public void Paste_start_is_ignored_when_bracketed_paste_is_off()
    {
        // A terminal left in mode 2004 by a previous program must not wedge an app that never asked for it.
        var parser = new VtParser();
        parser.Feed("\u001b[200~hi\u001b[201~"u8);
        Assert.False(parser.InPaste);
        Assert.True(parser.TryTake(out Event h));
        Assert.True(h.Key.IsChar('h'));
        Assert.True(parser.TryTake(out Event i));
        Assert.True(i.Key.IsChar('i'));
        Assert.False(parser.TryTake(out _));
    }

    [Fact]
    public void Paste_without_an_end_marker_is_delivered_when_flushed()
    {
        var parser = new VtParser { BracketedPaste = true };
        parser.Feed("\u001b[200~abc"u8);
        Assert.True(parser.InPaste);
        Assert.False(parser.IsIncomplete);                             // not subject to the escape timeout
        parser.FlushIncomplete();
        Assert.False(parser.InPaste);
        Assert.True(parser.TryTake(out Event paste));
        Assert.Equal("abc", paste.Paste);
        parser.Feed("q"u8);
        Assert.True(parser.TryTake(out Event key));
        Assert.True(key.Key.IsChar('q'));
    }

    [Fact]
    public void Empty_paste_without_an_end_marker_flushes_to_nothing()
    {
        var parser = new VtParser { BracketedPaste = true };
        parser.Feed("\u001b[200~"u8);
        parser.FlushIncomplete();
        Assert.False(parser.InPaste);
        Assert.False(parser.TryTake(out _));
    }

    [Fact]
    public void Paste_is_cut_at_the_cap_and_the_rest_is_dropped()
    {
        var parser = new VtParser { BracketedPaste = true };
        parser.Feed("\u001b[200~"u8);
        byte[] chunk = new byte[1 << 20];
        chunk.AsSpan().Fill((byte)'a');
        for (int fed = 0; fed < Terminal.MaxPasteBytes + chunk.Length; fed += chunk.Length)
        {
            parser.Feed(chunk);
        }

        Assert.True(parser.InPaste);
        Assert.False(parser.TryTake(out _));
        parser.Feed("\u001b[201~x"u8);                                 // the end marker still ends it past the cap
        Assert.True(parser.TryTake(out Event paste));
        Assert.Equal(Terminal.MaxPasteBytes, paste.Paste!.Length);
        Assert.True(parser.TryTake(out Event key));
        Assert.True(key.Key.IsChar('x'));
    }

    [Fact]
    public void Paste_cut_at_the_cap_does_not_split_a_character()
    {
        var parser = new VtParser { BracketedPaste = true };
        parser.Feed("\u001b[200~"u8);
        byte[] chunk = new byte[1 << 20];
        chunk.AsSpan().Fill((byte)'a');
        for (int fed = 0; fed + chunk.Length < Terminal.MaxPasteBytes; fed += chunk.Length)
        {
            parser.Feed(chunk);
        }

        parser.Feed(chunk.AsSpan(0, (1 << 20) - 1));                   // MaxPasteBytes - 1 bytes so far
        parser.Feed("你好\u001b[201~"u8);                                // 3-byte characters straddle the cap
        Assert.True(parser.TryTake(out Event paste));
        Assert.Equal(Terminal.MaxPasteBytes - 1, paste.Paste!.Length);
        Assert.EndsWith("aa", paste.Paste);
    }

    [Fact]
    public void Unknown_csi_is_ignored()
    {
        var parser = new VtParser();
        parser.Feed("\u001b[?1;2cj"u8);
        Assert.True(parser.TryTake(out Event ev));
        Assert.True(ev.Key.IsChar('j'));
    }

    [Fact]
    public void Cursor_report_is_taken_only_while_expected()
    {
        var parser = new VtParser { ExpectCursorReport = true };
        parser.Feed("\u001b[12;"u8);
        parser.Feed("40R\u001b[1;2R"u8);                              // split reply, then Shift+F3
        Assert.True(parser.TryTakeCursorReport(out int row, out int column));
        Assert.Equal((11, 39), (row, column));
        Assert.False(parser.ExpectCursorReport);
        Assert.False(parser.TryTakeCursorReport(out _, out _));
        Assert.True(parser.TryTake(out Event ev));
        Assert.True(ev.Key.Is(KeyCode.F3, Modifiers.Shift));
    }

    [Fact]
    public void Csi_R_without_a_query_is_f3() => Assert.True(Key("\u001b[1;5R").Is(KeyCode.F3, Modifiers.Ctrl));

    [Fact]
    public void Cursor_report_is_clamped_to_the_largest_screen()
    {
        var parser = new VtParser { ExpectCursorReport = true };
        parser.Feed("\u001b[1000000;99999999999R"u8);
        Assert.True(parser.TryTakeCursorReport(out int row, out int column));
        Assert.Equal((Terminal.MaxHeight - 1, Terminal.MaxWidth - 1), (row, column));
        Assert.False(parser.TryTake(out _));
    }

    [Theory]
    [InlineData(0x80)]
    [InlineData(0x85)]   // NEL
    [InlineData(0x9B)]   // CSI
    [InlineData(0x9F)]
    public void Kitty_c1_code_points_are_not_keys(int codePoint)
    {
        var parser = new VtParser();
        parser.Feed(Encoding.ASCII.GetBytes($"\u001b[{codePoint};5uz"));
        Assert.True(parser.TryTake(out Event ev));
        Assert.True(ev.Key.IsChar('z'), $"U+{codePoint:X4} was delivered as a key");
        Assert.False(parser.TryTake(out _));
    }

    [Fact]
    public void Kitty_code_point_after_c1_is_a_key() => Assert.Equal(new Rune(0xA0), Key("\u001b[160u").Rune);

    [Fact]
    public void Utf8_c1_code_points_are_not_keys()
    {
        var parser = new VtParser();
        parser.Feed([0xC2, 0x85, (byte)'a', 0xC2, 0x9B, (byte)'b', 0xC2, 0xA0]);   // NEL, then CSI, then NBSP
        Assert.True(parser.TryTake(out Event a));
        Assert.True(a.Key.IsChar('a'));
        Assert.True(parser.TryTake(out Event b));
        Assert.True(b.Key.IsChar('b'));
        Assert.True(parser.TryTake(out Event nbsp));
        Assert.Equal(new Rune(0xA0), nbsp.Key.Rune);
        Assert.False(parser.TryTake(out _));
    }

    [Fact]
    public void Alt_prefix_does_not_outlive_a_dropped_c1()
    {
        var parser = new VtParser();
        parser.Feed([0x1B, 0xC2, 0x85, (byte)'a']);
        Assert.True(parser.TryTake(out Event a));
        Assert.Equal(KeyEvent.Char('a'), a.Key);
        Assert.False(parser.TryTake(out _));
    }

    [Theory]
    [InlineData("\u001bP>|kitty(0.3")]            // reply cut mid-payload
    [InlineData("\u001bP>|kitty(0.36.4)\u001b")]   // reply cut inside ST
    [InlineData("\u001bP>")]                       // reply cut right after the introducer
    public void Unterminated_terminal_reply_is_dropped_not_typed(string input)
    {
        var parser = new VtParser();
        parser.Feed(Encoding.UTF8.GetBytes(input));
        Assert.True(parser.IsIncomplete);
        parser.FlushIncomplete();
        Assert.False(parser.IsIncomplete);
        Assert.False(parser.TryTake(out _));
        Assert.False(parser.RepeatSequences);
        parser.Feed("x"u8);
        Assert.True(parser.TryTake(out Event ev));
        Assert.Equal(KeyEvent.Char('x'), ev.Key);
    }

    [Fact]
    public void Esc_P_alone_flushes_as_alt_shift_p()
    {
        var parser = new VtParser();
        parser.Feed("\u001bP"u8);
        parser.FlushIncomplete();
        Assert.True(parser.TryTake(out Event ev));
        Assert.Equal(KeyEvent.Char('P', Modifiers.Alt), ev.Key);
        Assert.False(parser.TryTake(out _));
    }

    [Fact]
    public void Esc_P_followed_by_a_key_is_alt_shift_p_and_the_key()
    {
        var parser = new VtParser();
        parser.Feed("\u001bPx"u8);
        Assert.True(parser.TryTake(out Event p));
        Assert.Equal(KeyEvent.Char('P', Modifiers.Alt), p.Key);
        Assert.True(parser.TryTake(out Event x));
        Assert.Equal(KeyEvent.Char('x'), x.Key);
    }

    private static KeyEvent Key(string input) => Key(Encoding.UTF8.GetBytes(input));

    private static KeyEvent Key(byte[] input)
    {
        var parser = new VtParser();
        parser.Feed(input);
        Assert.True(parser.TryTake(out Event ev), "no event");
        Assert.Equal(EventKind.Key, ev.Kind);
        Assert.False(parser.TryTake(out _), "more than one event");
        return ev.Key;
    }

    private static MouseEvent Mouse(VtParser parser)
    {
        Assert.True(parser.TryTake(out Event ev));
        Assert.Equal(EventKind.Mouse, ev.Kind);
        return ev.Mouse;
    }
}
