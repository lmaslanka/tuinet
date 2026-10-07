using Tuinet.Widgets;

namespace Tuinet.Tests;

/// <summary>Steady-state frames and input must not allocate: no GC pauses on the key→frame path.</summary>
public class AllocationTests
{
    private static readonly string[] Items = [.. Enumerable.Range(0, 200).Select(i => $"item {i}")];
    private static readonly string[] Pages = [.. Enumerable.Range(0, 12).Select(i => $"page {i}")];
    private static readonly double[] History = [.. Enumerable.Range(0, 120).Select(i => (double)(i * 37 % 101))];
    private static readonly Bar[] Bars = [new(3, "feature"), new(7.5, "bugfix", new Style(Color.Red, Color.Default)), new(1, "世界"), new(12, "docs")];
    private static readonly TableColumn[] Columns =
    [
        new("name", Constraint.Fill()),
        new("size", Constraint.Length(8), Alignment.Right),
        new("state", Constraint.Length(6), Alignment.Center),
    ];

    [Fact]
    public void Frame_with_widgets_allocates_nothing()
    {
        var tty = new NullTty(120, 40);
        using var terminal = new Terminal(tty);
        var list = new ListState();
        var table = new ListState();
        var input = new TextInputState("hello");
        var tabs = new TabsState();

        for (int i = 0; i < 50; i++)
        {
            Frame(terminal, i, ref list, ref table, ref tabs, input);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            Frame(terminal, i, ref list, ref table, ref tabs, input);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(tty.Bytes > 0);
    }

    [Fact]
    public void Inline_frames_and_print_above_allocate_nothing()
    {
        var tty = new NullTty(120, 40) { Input = "\u001b[30;1R"u8.ToArray() };
        using var terminal = new Terminal(tty, new TerminalOptions { Inline = new InlineOptions(10) });
        var list = new ListState();
        var table = new ListState();
        var input = new TextInputState("hello");
        var tabs = new TabsState();
        for (int i = 0; i < 50; i++)
        {
            Frame(terminal, i, ref list, ref table, ref tabs, input);
            terminal.PrintAbove("downloaded part 17 of 200 · 1.2 MB/s");
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            Frame(terminal, i, ref list, ref table, ref tabs, input);
            terminal.PrintAbove("downloaded part 17 of 200 · 1.2 MB/s");
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
    }

    [Fact]
    public void Text_area_frames_and_caret_keys_allocate_nothing()
    {
        var tty = new NullTty(120, 40);
        using var terminal = new Terminal(tty);
        var notes = new TextAreaState(string.Join('\n', Enumerable.Range(0, 2000).Select(i => $"line {i} 👨‍👩‍👧 世界 with enough words to wrap around the edge")));
        KeyEvent[] keys =
        [
            new(KeyCode.Down), new(KeyCode.Down, default, Modifiers.Shift), new(KeyCode.Right, default, Modifiers.Ctrl),
            new(KeyCode.End), new(KeyCode.Up), new(KeyCode.PageDown), new(KeyCode.Left, default, Modifiers.Shift),
        ];

        for (int i = 0; i < 50; i++)
        {
            notes.Handle(keys[i % keys.Length]);
            TextAreaFrame(terminal, notes);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            notes.Handle(keys[i % keys.Length]);
            TextAreaFrame(terminal, notes);
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(notes.CaretLine > 100);
    }

    private static void TextAreaFrame(Terminal terminal, TextAreaState notes)
    {
        CellBuffer frame = terminal.BeginFrame();
        var box = new Block { Title = "notes", BorderType = BorderType.Rounded };
        frame.Render(box, frame.Area);
        frame.Render(new TextArea { Focused = true, LineNumbers = true }, box.Inner(frame.Area), ref notes);
        terminal.Present();
    }

    [Fact]
    public void Polling_keys_allocates_nothing()
    {
        var tty = new NullTty(80, 24);
        using var terminal = new Terminal(tty);
        tty.Input = "jjjj\u001b[A\u001b[1;5B"u8.ToArray();
        for (int i = 0; i < 100; i++)
        {
            terminal.Poll(out _, 0);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        int keys = 0;
        for (int i = 0; i < 10_000; i++)
        {
            if (terminal.Poll(out Event ev, 0) && ev.Kind == EventKind.Key)
            {
                keys++;
            }
        }

        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.Equal(10_000, keys);
    }

    private static void Frame(Terminal terminal, int tick, ref ListState list, ref ListState table, ref TabsState tabs, TextInputState input)
    {
        CellBuffer frame = terminal.BeginFrame();
        Span<Rect> rows = stackalloc Rect[3];
        Layout.Vertical(frame.Area, [Constraint.Length(3), Constraint.Fill(), Constraint.Length(1)], rows);

        var header = new Block { Title = "header", BorderType = BorderType.Rounded };
        frame.Render(header, rows[0]);
        frame.Render(new TextInput { Focused = true }, header.Inner(rows[0]), ref input);

        Span<Rect> panes = stackalloc Rect[2];
        Layout.Horizontal(rows[1], [Constraint.Fill(), Constraint.Fill()], panes);
        list.Selected = tick % Items.Length;
        frame.Render(new ListView<TextItems>(new TextItems(Items)) { SelectedStyle = new Style(Color.Black, Color.White), Scrollbar = ScrollbarMode.Always }, panes[0], ref list);
        table.Selected = tick % Items.Length;
        frame.Render(new Table<Files>(new Files(Items), Columns)
        {
            HeaderStyle = new Style(Color.Default, Color.Default, Attr.Bold),
            HeaderSeparator = true,
            ColumnSeparator = '│',
            AlternateRowStyle = new Style(Color.Default, Color.Rgb(20, 20, 20)),
            SelectedStyle = new Style(Color.Black, Color.White),
            HighlightSymbol = "> ",
            SortColumn = 1,
            Scrollbar = ScrollbarMode.Always,
        }, panes[1], ref table);

        // A tab bar too narrow for its titles (scrolls to follow the selection), and a shadowed popup on top.
        tabs.Selected = tick % Pages.Length;
        var bar = new Tabs(Pages) { SelectedStyle = new Style(Color.Black, Color.White), DividerStyle = new Style(Color.Blue, Color.Default) };
        frame.Render(bar, new Rect(2, rows[0].Y, 40, 1), ref tabs);
        bar.HandleMouse(new MouseEvent(MouseKind.Down, MouseButton.Left, 10, rows[0].Y, Modifiers.None), ref tabs);
        var popup = new Popup
        {
            Block = new Block { Title = "popup", BorderType = BorderType.Rounded, Style = new Style(Color.Default, Color.Rgb(20, 24, 32)) },
            Shadow = true,
            Padding = 1,
        };
        Rect dialog = frame.Area.Centered(popup.Outer(30, 3));
        frame.Render(popup, dialog);
        Rect inner = popup.Inner(dialog);
        frame.SetString(inner.X, inner.Y, "inside");

        // Charts with value labels formatted on the stack, and a sparkline over a ring buffer's two halves.
        frame.Render(new BarChart(Bars) { ValueFormat = "F1", BarStyle = new Style(Color.Blue, Color.Default) }, new Rect(60, rows[1].Y, 20, 8));
        frame.Render(new BarChart(Bars) { Direction = Direction.Horizontal, Gap = 0 }, new Rect(60, rows[1].Y + 9, 30, 4));
        int head = tick % History.Length;
        frame.Render(new Sparkline(History.AsSpan(head), History.AsSpan(0, head)) { MaxStyle = new Style(Color.Red, Color.Default) }, new Rect(60, rows[1].Y + 14, 40, 3));

        // Styled text: a builder with a formatted number, markup, and a styled paragraph.
        var status = new StyledTextBuilder(stackalloc char[48], stackalloc StyledRun[6]);
        status.Append("frame ", new Style(Color.BrightBlack, Color.Default));
        status.Append(tick, new Style(Color.Green, Color.Default, Attr.Bold));
        frame.SetText(60, rows[2].Y, status.Build(), 20);
        frame.SetMarkup(80, rows[2].Y, "[b fg=#F5A623]q[/] quit  [u]?[/] help", default, 20);
        frame.Render(new Paragraph(status.Build()) { Wrap = TextWrap.Word, Scrollbar = ScrollbarMode.Auto }, new Rect(100, rows[2].Y, 10, 1));

        // Grapheme clusters are interned on first sight; after warm-up they cost nothing.
        frame.SetString(40, rows[2].Y, "👨‍👩‍👧 e\u0301 🇵🇱 ❤️ 👍🏽", default, 20);

        Span<char> counter = stackalloc char[32];
        tick.TryFormat(counter, out int written);
        frame.SetString(0, rows[2].Y, counter[..written], new Style(Color.Rgb(200, 210, (byte)tick), Color.Default));
        terminal.Present();
    }

    /// <summary>A name column straight from the array plus a size formatted into scratch.</summary>
    private readonly struct Files(string[] names) : ITableSource
    {
        public int RowCount => names.Length;

        public ReadOnlySpan<char> Cell(int row, int column, Span<char> scratch, out Style style)
        {
            style = default;
            switch (column)
            {
                case 0:
                    return names[row];
                case 1:
                    (row * 1024).TryFormat(scratch, out int written);
                    return scratch[..written];
                default:
                    style = new Style(row % 3 == 0 ? Color.Red : Color.Green, Color.Default);
                    return row % 3 == 0 ? "dirty" : "ok";
            }
        }
    }

    internal sealed class NullTty(int width, int height) : ITty
    {
        private int _inputPos;

        public byte[] Input { get; set; } = [];
        public long Bytes { get; private set; }
        public Size Size { get; } = new(width, height);

        public void Write(ReadOnlySpan<byte> bytes) => Bytes += bytes.Length;

        public int Read(Span<byte> buffer, int timeoutMs)
        {
            if (Input.Length == 0)
            {
                return 0;
            }

            int n = Math.Min(buffer.Length, Input.Length - _inputPos);
            Input.AsSpan(_inputPos, n).CopyTo(buffer);
            _inputPos = (_inputPos + n) % Input.Length;
            return n;
        }

        public void Wake()
        {
        }

        public void Restore()
        {
        }

        public void Dispose()
        {
        }
    }
}
