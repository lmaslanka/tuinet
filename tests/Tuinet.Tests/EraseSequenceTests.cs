using System.Text;
using Tuinet.Testing;

namespace Tuinet.Tests;

/// <summary>Runs of blanks are erased with EL / ECH when that is fewer bytes than writing spaces.</summary>
public class EraseSequenceTests
{
    private static readonly Style OnBlue = new(Color.Default, Color.Blue);

    [Fact]
    public void Closing_a_dialog_over_a_blank_screen_erases_to_the_end_of_each_row()
    {
        var (prev, cur) = Buffers(20, 3);
        prev.SetString(4, 1, "##########");
        Assert.Equal("\u001b[?2026h\u001b[2;5H\u001b[K\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Trailing_blanks_keep_their_background()
    {
        var (prev, cur) = Buffers(12, 1);
        prev.Fill(prev.Area, OnBlue);
        prev.SetString(0, 0, "hello world!", OnBlue);
        cur.Fill(cur.Area, OnBlue);
        cur.SetString(0, 0, "hello", OnBlue);
        // The pen takes the blanks' background first: EL fills with it (BCE).
        Assert.Equal("\u001b[?2026h\u001b[1;7H\u001b[44m\u001b[K\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Erase_reuses_a_pen_that_already_has_the_background()
    {
        var (prev, cur) = Buffers(12, 1);
        prev.SetString(0, 0, "abcdefghijkl");
        cur.SetString(0, 0, "X", new Style(Color.Red, Color.Blue, Attr.Bold));
        cur.Fill(new Rect(1, 0, 11, 1), new Style(Color.Green, Color.Blue, Attr.Italic));
        // After "X" the pen is red bold on blue: the blanks only need blue, so no new SGR.
        Assert.Equal("\u001b[?2026h\u001b[H\u001b[1;31;44mX\u001b[K\u001b[?2026l", Render(cur, prev));
    }

    [Theory]
    [InlineData(Attr.Reverse, "7")]
    [InlineData(Attr.Underline, "4")]
    [InlineData(Attr.Strike, "9")]
    public void Blanks_that_show_an_attribute_are_written_as_spaces(Attr attr, string sgr)
    {
        var (prev, cur) = Buffers(12, 1);
        prev.SetString(0, 0, "hello world!");
        cur.SetString(0, 0, "hello");
        cur.Fill(new Rect(5, 0, 7, 1), new Style(Color.Default, Color.Default, attr));
        Assert.Equal($"\u001b[?2026h\u001b[1;6H\u001b[{sgr}m       \u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void A_long_run_inside_a_row_is_erased_with_ech()
    {
        var (prev, cur) = Buffers(30, 1);
        prev.SetString(0, 0, new string('a', 30));
        cur.SetString(0, 0, "a" + new string(' ', 20) + new string('a', 9));
        Assert.Equal("\u001b[?2026h\u001b[1;2H\u001b[20X\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Ech_does_not_move_the_cursor_so_the_next_change_jumps_from_its_start()
    {
        var (prev, cur) = Buffers(30, 1);
        prev.SetString(0, 0, new string('a', 30));
        cur.SetString(0, 0, "a" + new string(' ', 20) + "aaaabaaaa");
        Assert.Equal("\u001b[?2026h\u001b[1;2H\u001b[20X\u001b[24Cb\u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Short_runs_stay_spaces()
    {
        var (prev, cur) = Buffers(8, 1);
        prev.SetString(0, 0, "aaaaaaaa");
        cur.SetString(0, 0, "a   aaaa");
        Assert.Equal("\u001b[?2026h\u001b[1;2H   \u001b[?2026l", Render(cur, prev));
    }

    [Fact]
    public void Erase_sequences_can_be_turned_off()
    {
        var (prev, cur) = Buffers(20, 3);
        prev.SetString(4, 1, "##########");
        Assert.Equal("\u001b[?2026h\u001b[2;5H          \u001b[?2026l", Render(cur, prev, eraseSequences: false));

        var tty = new TestTty(20, 3);
        using var terminal = new Terminal(tty, new TerminalOptions { EraseSequences = false });
        terminal.BeginFrame().SetString(4, 1, "##########");
        terminal.Present();
        tty.ClearWritten();
        terminal.BeginFrame();
        terminal.Present();
        Assert.DoesNotContain("\u001b[K", tty.WrittenText);
    }

    private static (CellBuffer Previous, CellBuffer Current) Buffers(int width, int height) =>
        (new CellBuffer(width, height), new CellBuffer(width, height));

    private static string Render(CellBuffer current, CellBuffer previous, bool eraseSequences = true)
    {
        var renderer = new Renderer(ColorMode.TrueColor, scrollRegions: true, eraseSequences);
        renderer.AfterClear();
        var output = new VtBuffer(64);
        renderer.Render(current, previous, output);
        return Encoding.UTF8.GetString(output.Written);
    }
}
