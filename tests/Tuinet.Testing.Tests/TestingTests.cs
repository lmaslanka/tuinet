using System.Text;
namespace Tuinet.Testing.Tests;

/// <summary>The Tuinet.Testing helpers: styled dumps, finding text, snapshots, <see cref="TestTerminal"/>, the allocation check.</summary>
public class TestingTests
{
    private static readonly Style Amber = new(Color.Hex(0xF5A623), Color.Default, Attr.Bold);

    [Fact]
    public void Styled_string_tags_only_styled_runs()
    {
        var buffer = new CellBuffer(12, 2);
        buffer.SetString(0, 0, "ITEMS", Amber);
        buffer.SetString(6, 0, "20");
        buffer.SetString(0, 1, "a", new Style(Color.Red, Color.Indexed(200), Attr.Underline | Attr.Strike));
        buffer.SetString(1, 1, "[x]", new Style(Color.BrightBlue, Color.Default));

        Assert.Equal(
            "[fg=#F5A623 b]ITEMS[/] 20    \n" +
            "[fg=red bg=200 u s]a[/][fg=bright-blue][[x][/]        ",
            buffer.ToStyledString());
    }

    [Fact]
    public void Styled_string_round_trips_through_parse()
    {
        var buffer = new CellBuffer(16, 3);
        buffer.Fill(new Rect(0, 0, 16, 1), new Style(Color.Default, Color.Hex(0x101820)));
        buffer.SetString(1, 0, "日本", Amber);                         // wide glyphs over a filled row
        buffer.SetString(6, 0, "[1/3]", new Style(Color.Green, Color.Default, Attr.Dim | Attr.Italic));
        buffer.SetString(0, 1, "é 👨‍👩‍👧 x", new Style(Color.Indexed(99), Color.Default, Attr.Reverse | Attr.Blink | Attr.Hidden));
        buffer.SetString(0, 2, "plain [[ text");

        string styled = buffer.ToStyledString();
        CellBuffer parsed = Snapshot.Parse(styled);

        Assert.Equal(buffer.Size, parsed.Size);
        Assert.Equal(styled, parsed.ToStyledString());
        for (int y = 0; y < buffer.Height; y++)
        {
            Assert.True(buffer.Row(y).SequenceEqual(parsed.Row(y)), $"row {y} differs");
        }
    }

    [Fact]
    public void Ansi_dump_has_sgr_for_each_style_change()
    {
        var buffer = new CellBuffer(6, 2);
        buffer.SetString(0, 0, "ab", Amber);
        buffer.SetString(2, 0, "c", new Style(Color.BrightRed, Color.Indexed(42)));
        buffer.SetString(0, 1, "d");

        Assert.Equal(
            "\u001b[0;1;38;2;245;166;35mab\u001b[0;91;48;5;42mc\u001b[0m   \u001b[0m\n" +
            "d     \u001b[0m",
            buffer.ToAnsi());
    }

    [Fact]
    public void Find_text_reports_cell_columns_past_wide_glyphs()
    {
        var buffer = new CellBuffer(20, 3);
        buffer.SetString(0, 2, "日本 Save ok", Amber);

        Rect save = buffer.FindText("Save");
        Assert.Equal(new Rect(5, 2, 4, 1), save);
        Assert.Equal(new Rect(0, 2, 5, 1), buffer.FindText("日本 "));
        Assert.Equal(Amber, buffer.StyleOf("ok"));
        Assert.Equal(Amber, buffer.StyleAt(1, 2));                    // the right half of 日
        Assert.False(buffer.TryFindText("Cancel", out _));

        var missing = Assert.Throws<ScreenAssertionException>(() => buffer.FindText("Cancel"));
        Assert.Contains("'Cancel' is not on screen", missing.Message);
        Assert.Contains("日本 Save ok", missing.Message);               // the screen, to see what was there
    }

    [Fact]
    public void Snapshot_next_to_the_source_file_matches()   // tests/Tuinet.Testing.Tests/__snapshots__/testing-example.txt
    {
        var buffer = new CellBuffer(24, 3);
        buffer.SetString(1, 0, "TUINET", Amber);
        buffer.SetString(1, 1, "snapshot [tests]", new Style(Color.Cyan, Color.Default));
        Snapshot.Verify(buffer, "testing-example");
        Snapshot.Verify(buffer, "testing-example-plain", styled: false);
    }

    [Fact]
    public void Snapshot_mismatch_writes_received_and_matching_cleans_it_up()
    {
        string directory = TempDirectory();
        var buffer = new CellBuffer(8, 2);
        buffer.SetString(0, 0, "one");
        string expected = Path.Combine(directory, "screen.txt");
        string received = Path.Combine(directory, "screen.received.txt");

        var missing = Assert.Throws<ScreenAssertionException>(() => Snapshot.Verify(buffer, "screen", directory: directory));
        Assert.Contains("No snapshot 'screen' yet", missing.Message);
        Assert.Equal("one\n", File.ReadAllText(received));            // trailing spaces and the blank last row trimmed
        Assert.False(File.Exists(expected));

        File.Move(received, expected);                                // accept by renaming
        Snapshot.Verify(buffer, "screen", directory: directory);

        buffer.SetString(0, 1, "two", Amber);
        var differs = Assert.Throws<ScreenAssertionException>(() => Snapshot.Verify(buffer, "screen", directory: directory));
        Assert.Contains("line 2", differs.Message);
        Assert.Contains("[fg=#F5A623 b]two[/]", differs.Message);
        Assert.True(File.Exists(received));

        File.WriteAllText(expected, "one   \r\n[fg=#F5A623 b]two[/]\r\n");   // CRLF and trailing spaces don't count
        Snapshot.Verify(buffer, "screen", directory: directory);
        Assert.False(File.Exists(received));
    }

    [Fact]
    public void Text_snapshots_compare_any_string()
    {
        string directory = TempDirectory();
        File.WriteAllText(Path.Combine(directory, "log.txt"), "a\nb\n");
        Snapshot.Verify("a\nb", "log", directory);
        Assert.Throws<ScreenAssertionException>(() => Snapshot.Verify("a\nc", "log", directory));
        Assert.Throws<ArgumentException>(() => Snapshot.Verify("a", "bad/name", directory));
    }

    [Fact]
    public void Test_terminal_draws_the_first_frame_and_one_per_input()
    {
        using var term = new TestTerminal(new Counter(), 30, 5);
        Assert.Equal(1, term.Terminal.Frames);
        Assert.StartsWith("count 0", term.Text);

        term.Press("jjk");
        Assert.Equal(4, term.Terminal.Frames);                        // one frame per key, as when typed
        Assert.StartsWith("count 1", term.Text);
        Assert.Contains("last k", term.Screen.RowText(1));
        Assert.True(term.Tty.Written.Length > 0);                     // drawn through a real terminal
        Assert.True(term.Terminal.LastFrameBytes > 0);

        CellBuffer before = term.Screen;
        term.Press('s', Modifiers.Ctrl).Key(KeyCode.Up, Modifiers.Shift);
        Assert.Contains("last Shift+Up", term.Screen.RowText(1));
        Assert.Contains("last k", before.RowText(1));                // an old screen is a copy: later frames don't touch it
        Assert.Equal(["j", "j", "k", "Ctrl+s", "Shift+Up"], ((Counter)term.App).Keys);
    }

    [Fact]
    public void Test_terminal_maps_control_characters_to_keys()
    {
        using var term = new TestTerminal(new Counter());
        term.Press("a\n\t\u001b\b");
        Assert.Equal(["a", "Enter", "Tab", "Escape", "Backspace"], ((Counter)term.App).Keys);
    }

    [Fact]
    public void Test_terminal_clicks_text_on_screen()
    {
        var app = new Counter();
        using var term = new TestTerminal(app, 30, 5);
        term.Click("[+]");
        Assert.StartsWith("count 1", term.Text);
        Assert.Equal([new MouseEvent(MouseKind.Down, MouseButton.Left, 10, 0, Modifiers.None),
            new MouseEvent(MouseKind.Up, MouseButton.Left, 10, 0, Modifiers.None)], app.Mice);

        term.Wheel(3, 2, -2).Mouse(MouseKind.Drag, 4, 4, MouseButton.Left);
        Assert.Equal([MouseKind.ScrollUp, MouseKind.ScrollUp, MouseKind.Drag], app.Mice[2..].Select(m => m.Kind));
        Assert.Throws<ScreenAssertionException>(() => term.Click("nope"));
    }

    [Fact]
    public void Test_terminal_input_goes_through_the_parser()
    {
        var app = new Counter();
        using var term = new TestTerminal(app, 30, 5);
        term.Input("j\u001b[A\u001b[1;5Cé");
        Assert.Equal(["j", "Up", "Ctrl+Right", "é"], app.Keys);
        Assert.Equal(2, term.Terminal.Frames);                        // one burst, one frame

        term.Input("\u001b");                                          // a lone ESC resolves after the escape timeout
        Assert.Equal("Escape", app.Keys[^1]);
        term.Input(Encoding.UTF8.GetBytes("\u001b[200~pasted\u001b[201~"));
        Assert.Equal("pasted", app.Pastes[^1]);
    }

    [Fact]
    public void Test_terminal_resize_and_post_reach_the_app()
    {
        var app = new Counter();
        using var term = new TestTerminal(app, 30, 5);
        term.Resize(40, 8);
        Assert.Equal(new Size(40, 8), app.Resized);
        Assert.Equal(new Size(40, 8), term.Screen.Size);

        term.Post("hello");
        Assert.Contains("msg hello", term.Text);
        term.Paste("clip");
        Assert.Equal("clip", app.Pastes[^1]);
    }

    [Fact]
    public void Advance_ticks_only_when_the_app_would_wake()
    {
        var app = new Counter();
        using var term = new TestTerminal(app);
        long frames = term.Terminal.Frames;
        term.Advance(1000);
        Assert.Equal(1000, term.NowMs);
        Assert.Equal(frames, term.Terminal.Frames);                   // idle: Run would sleep through it
        Assert.Equal(0, app.Ticks);

        app.DueMs = 1500;
        term.Advance(400);
        Assert.Equal(0, app.Ticks);
        term.Advance(100);
        Assert.Equal(1, app.Ticks);
        Assert.Equal(frames + 1, term.Terminal.Frames);
        Assert.Contains("now 1500", term.Text);

        app.DueMs = long.MaxValue;
        app.Animating = true;
        term.Advance(33);
        Assert.Equal(2, app.Ticks);
        Assert.Contains("now 1533", term.Text);
    }

    [Fact]
    public void Input_after_the_app_stops_throws()
    {
        var app = new Counter();
        using var term = new TestTerminal(app);
        long frames = term.Terminal.Frames;
        term.Press("qj");
        Assert.False(term.IsRunning);
        Assert.Equal(["q"], app.Keys);                                // the rest of the input is not handled
        Assert.Equal(frames, term.Terminal.Frames);                   // and no frame after stopping
        Assert.Throws<InvalidOperationException>(() => term.Press("j"));
    }

    [Fact]
    public void Allocation_check_passes_a_step_that_allocates_nothing()
    {
        var buffer = new CellBuffer(20, 2);
        Allocations.AssertNone(i =>
        {
            buffer.Clear();
            buffer.SetString(0, i % 2, "frame");
        });
    }

    [Fact]
    public void Allocation_check_fails_a_step_that_allocates_every_time()
    {
        var kept = new List<object>();
        var failed = Assert.Throws<ScreenAssertionException>(() => Allocations.AssertNone(i => kept.Add(new object()), steps: 10, rounds: 2));
        Assert.StartsWith("Every round allocated:", failed.Message);
        Assert.Contains("per 10 steps", failed.Message);
    }

    private static string TempDirectory()
    {
        string directory = Path.Combine(Path.GetTempPath(), "tuinet-snapshots-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    /// <summary>Counts j/k, shows the last key and message, and records everything it gets.</summary>
    private sealed class Counter : IApp
    {
        public List<string> Keys { get; } = [];
        public List<MouseEvent> Mice { get; } = [];
        public List<string> Pastes { get; } = [];
        public Size Resized { get; private set; }
        public int Ticks { get; private set; }
        public bool Animating { get; set; }
        public long DueMs { get; set; } = long.MaxValue;
        private int _count;
        private string _message = "";

        public bool Handle(Event ev, long nowMs)
        {
            switch (ev.Kind)
            {
                case EventKind.Key:
                    Keys.Add(ev.Key.ToString());
                    if (ev.Key.IsChar('q'))
                    {
                        return false;
                    }

                    _count += ev.Key.IsChar('j') ? 1 : ev.Key.IsChar('k') ? -1 : 0;
                    break;
                case EventKind.Mouse:
                    Mice.Add(ev.Mouse);
                    if (ev.Mouse.IsClickIn(new Rect(10, 0, 3, 1)))
                    {
                        _count++;
                    }

                    break;
                case EventKind.Paste:
                    Pastes.Add(ev.Paste!);
                    break;
                case EventKind.Resize:
                    Resized = ev.Size;
                    break;
                case EventKind.Message:
                    _message = (string)ev.Message!;
                    break;
                case EventKind.Tick:
                    Ticks++;
                    break;
            }

            return true;
        }

        public void Render(CellBuffer frame, long nowMs)
        {
            frame.SetString(0, 0, $"count {_count}");
            frame.SetString(10, 0, "[+]");
            frame.SetString(0, 1, $"last {(Keys.Count > 0 ? Keys[^1] : "-")}");
            frame.SetString(0, 2, $"now {nowMs} msg {_message}");
        }

        public bool IsAnimating(long nowMs) => Animating;

        public long NextDueMs(long nowMs) => DueMs;
    }
}
