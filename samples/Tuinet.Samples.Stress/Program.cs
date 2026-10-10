// Latency and throughput harness.
//   j/k (hold it)  move through 100k items: one frame per key, no backlog
//   n              toggle full-screen color noise, repainted as fast as the terminal accepts it
//   q / Ctrl+C     quit
// The status bar shows frames, bytes and managed allocations since warm-up (or the last resize,
// which reallocates the cell buffers); in steady state allocations stay at 0. At its right end, a
// sparkline of the last frame times (render + diff + write).
using System.Diagnostics;
using Tuinet;
using Tuinet.Widgets;

using var terminal = Terminal.Open();
var app = new StressApp(terminal);
terminal.Run(app, frameMs: 0);   // while the noise is on, frames go out as fast as the terminal takes them

internal sealed class StressApp(Terminal terminal) : IApp
{
    private const int ItemCount = 100_000;
    private static readonly Style Highlight = new(Color.Rgb(16, 16, 16), Color.Rgb(120, 200, 255));
    private static readonly Style Status = new(Color.Rgb(16, 16, 16), Color.Rgb(200, 200, 200));
    private static readonly Style SparklineStyle = new(Color.Rgb(20, 90, 170), default);
    private readonly double[] _frameTimes = new double[60];   // µs, a ring: oldest at _frameHead
    private readonly Stopwatch _clock = Stopwatch.StartNew();
    private int _frameHead;
    private ListState _list;
    private bool _noise;
    private uint _seed = 1;
    private long _startAllocated = -1;
    private long _baselineFrame = 5;
    private int _fpsFrames;
    private double _fps;

    public bool IsAnimating(long nowMs) => _noise;

    public void Render(CellBuffer frame, long nowMs)
    {
        if (terminal.Frames > 0)
        {
            _frameTimes[_frameHead] = terminal.LastFrameTime.TotalMicroseconds;   // the previous frame
            _frameHead = (_frameHead + 1) % _frameTimes.Length;
        }

        if (terminal.Frames == _baselineFrame)
        {
            // Measure steady state only: ignore warm-up and buffer growth after a resize.
            _startAllocated = GC.GetAllocatedBytesForCurrentThread();
        }

        _fpsFrames++;
        if (_clock.ElapsedMilliseconds >= 500)
        {
            _fps = _fpsFrames * 1000.0 / _clock.ElapsedMilliseconds;
            _fpsFrames = 0;
            _clock.Restart();
        }

        Span<Rect> rows = stackalloc Rect[2];
        Layout.Vertical(frame.Area, [Constraint.Fill(), Constraint.Length(1)], rows);
        if (_noise)
        {
            for (int y = rows[0].Y; y < rows[0].Bottom; y++)
            {
                for (int x = 0; x < frame.Width; x++)
                {
                    _seed = _seed * 1664525 + 1013904223;
                    frame.SetRune(x, y, new System.Text.Rune('a' + (int)(_seed >> 27)), new Style(Color.Rgb((byte)(_seed >> 8), (byte)(_seed >> 16), (byte)(_seed >> 24)), Color.Rgb(10, 10, 10)));
                }
            }
        }
        else
        {
            var block = new Block { Title = " 100k items — hold j ", BorderType = BorderType.Rounded };
            frame.Render(block, rows[0]);
            frame.Render(new ListView<Items>(new Items(ItemCount)) { SelectedStyle = Highlight, HighlightSymbol = "▶ " }, block.Inner(rows[0]), ref _list);
        }

        // Status bar, formatted into stack memory: no strings per frame.
        Span<char> line = stackalloc char[160];
        long allocated = _startAllocated < 0 ? -1 : GC.GetAllocatedBytesForCurrentThread() - _startAllocated;
        line.TryWrite($" frame {terminal.Frames}  last {terminal.LastFrameBytes} B  total {terminal.BytesWritten / 1024} KiB  {_fps:F0} fps  alloc {allocated} B  gc0 {GC.CollectionCount(0)}  [{terminal.ColorMode}]  j/k n q", out int length);
        frame.Fill(rows[1], Status);
        int sparkWidth = Math.Min(_frameTimes.Length, rows[1].Width / 4);
        frame.SetString(0, rows[1].Y, line[..length], Status, rows[1].Width - sparkWidth - 1);
        var times = new Rect(rows[1].Right - sparkWidth, rows[1].Y, sparkWidth, 1);
        frame.Render(new Sparkline(_frameTimes.AsSpan(_frameHead), _frameTimes.AsSpan(0, _frameHead)) { Style = SparklineStyle }, times);
    }

    public bool Handle(Event ev, long nowMs)
    {
        if (ev.Kind == EventKind.Resize)
        {
            _baselineFrame = terminal.Frames + 2;
            _startAllocated = -1;
        }

        if (ev.Kind != EventKind.Key)
        {
            return true;
        }

        KeyEvent key = ev.Key;
        if (key.IsChar('q') || key.IsCtrl('c'))
        {
            return false;
        }

        if (key.IsChar('j') || key.Is(KeyCode.Down))
        {
            _list.Next(ItemCount);
        }
        else if (key.IsChar('k') || key.Is(KeyCode.Up))
        {
            _list.Previous(ItemCount);
        }
        else if (key.Is(KeyCode.PageDown))
        {
            _list.PageDown(ItemCount);
        }
        else if (key.Is(KeyCode.PageUp))
        {
            _list.PageUp(ItemCount);
        }
        else if (key.IsChar('n'))
        {
            _noise = !_noise;
        }

        return true;
    }
}

/// <summary>Virtualized items: text is formatted on the stack only for visible rows.</summary>
internal readonly struct Items(int count) : IListSource
{
    public int Count => count;

    public void RenderItem(int index, Rect area, CellBuffer buffer, bool selected)
    {
        Span<char> text = stackalloc char[48];
        text.TryWrite($"item {index:D6}  0x{index * 2654435761u:X8}", out int length);
        buffer.SetString(area.X, area.Y, text[..length], default, area.Width);
    }
}
