using Tuinet.Widgets;

namespace Tuinet.Tests;

/// <summary>Steady-state frames and input must not allocate: no GC pauses on the key→frame path.</summary>
public class AllocationTests
{
    private static readonly string[] Items = [.. Enumerable.Range(0, 200).Select(i => $"item {i}")];

    [Fact]
    public void Frame_with_widgets_allocates_nothing()
    {
        var tty = new NullTty(120, 40);
        using var terminal = new Terminal(tty);
        var list = new ListState();
        var input = new TextInputState("hello");

        for (int i = 0; i < 50; i++)
        {
            Frame(terminal, i, ref list, input);
        }

        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 1000; i++)
        {
            Frame(terminal, i, ref list, input);
        }

        long allocated = GC.GetAllocatedBytesForCurrentThread() - before;
        Assert.Equal(0, allocated);
        Assert.True(tty.Bytes > 0);
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

    private static void Frame(Terminal terminal, int tick, ref ListState list, TextInputState input)
    {
        CellBuffer frame = terminal.BeginFrame();
        Span<Rect> rows = stackalloc Rect[3];
        Layout.Vertical(frame.Area, [Constraint.Length(3), Constraint.Fill(), Constraint.Length(1)], rows);

        var header = new Block { Title = "header", BorderType = BorderType.Rounded };
        frame.Render(header, rows[0]);
        frame.Render(new TextInput { Focused = true }, header.Inner(rows[0]), ref input);

        list.Selected = tick % Items.Length;
        frame.Render(new ListView<TextItems>(new TextItems(Items)) { SelectedStyle = new Style(Color.Black, Color.White) }, rows[1], ref list);

        Span<char> status = stackalloc char[32];
        tick.TryFormat(status, out int written);
        frame.SetString(0, rows[2].Y, status[..written], new Style(Color.Rgb(200, 210, (byte)tick), Color.Default));
        terminal.Present();
    }

    private sealed class NullTty(int width, int height) : ITty
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
