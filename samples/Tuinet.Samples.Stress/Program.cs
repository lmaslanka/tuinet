// Latency and throughput harness.
//   j/k (hold it)  move through 100k items: one frame per key, no backlog
//   n              toggle full-screen color noise, repainted as fast as the terminal accepts it
//   q / Ctrl+C     quit
// The status bar shows frames, bytes and managed allocations since warm-up (or the last resize,
// which reallocates the cell buffers); in steady state allocations stay at 0.
using System.Diagnostics;
using Tuinet;
using Tuinet.Widgets;

const int ItemCount = 100_000;
var highlight = new Style(Color.Rgb(16, 16, 16), Color.Rgb(120, 200, 255));
var status = new Style(Color.Rgb(16, 16, 16), Color.Rgb(200, 200, 200));
var list = new ListState();
bool noise = false;
uint seed = 1;
long startAllocated = -1;
long baselineFrame = 5;
int fpsFrames = 0;
var clock = Stopwatch.StartNew();
double fps = 0;
Span<Rect> rows = stackalloc Rect[2];
Span<char> line = stackalloc char[160];

using var terminal = Terminal.Open();

bool running = true;
while (running)
{
    CellBuffer frame = terminal.BeginFrame();
    Layout.Vertical(frame.Area, [Constraint.Fill(), Constraint.Length(1)], rows);

    if (noise)
    {
        for (int y = rows[0].Y; y < rows[0].Bottom; y++)
        {
            for (int x = 0; x < frame.Width; x++)
            {
                seed = seed * 1664525 + 1013904223;
                frame.SetRune(x, y, new System.Text.Rune('a' + (int)(seed >> 27)), new Style(Color.Rgb((byte)(seed >> 8), (byte)(seed >> 16), (byte)(seed >> 24)), Color.Rgb(10, 10, 10)));
            }
        }
    }
    else
    {
        var block = new Block { Title = " 100k items — hold j ", BorderType = BorderType.Rounded };
        frame.Render(block, rows[0]);
        frame.Render(new ListView<Items>(new Items(ItemCount)) { SelectedStyle = highlight, HighlightSymbol = "▶ " }, block.Inner(rows[0]), ref list);
    }

    // Status bar, formatted into stack memory: no strings per frame.
    long allocated = startAllocated < 0 ? -1 : GC.GetAllocatedBytesForCurrentThread() - startAllocated;
    line.TryWrite($" frame {terminal.Frames}  last {terminal.LastFrameBytes} B  total {terminal.BytesWritten / 1024} KiB  {fps:F0} fps  alloc {allocated} B  gc0 {GC.CollectionCount(0)}  [{terminal.ColorMode}]  j/k n q", out int length);
    frame.Fill(rows[1], status);
    frame.SetString(0, rows[1].Y, line[..length], status, rows[1].Width);
    terminal.Present();

    if (terminal.Frames == baselineFrame)
    {
        // Measure steady state only: ignore warm-up and buffer growth after a resize.
        startAllocated = GC.GetAllocatedBytesForCurrentThread();
    }

    fpsFrames++;
    if (clock.ElapsedMilliseconds >= 500)
    {
        fps = fpsFrames * 1000.0 / clock.ElapsedMilliseconds;
        fpsFrames = 0;
        clock.Restart();
    }

    if (!terminal.Poll(out Event ev, noise ? 0 : Timeout.Infinite))
    {
        continue;
    }

    do
    {
        if (ev.Kind == EventKind.Resize)
        {
            baselineFrame = terminal.Frames + 2;
            startAllocated = -1;
        }

        if (ev.Kind != EventKind.Key)
        {
            continue;
        }

        KeyEvent key = ev.Key;
        if (key.IsChar('q') || key.IsCtrl('c'))
        {
            running = false;
        }
        else if (key.IsChar('j') || key.Is(KeyCode.Down))
        {
            list.Next(ItemCount);
        }
        else if (key.IsChar('k') || key.Is(KeyCode.Up))
        {
            list.Previous(ItemCount);
        }
        else if (key.Is(KeyCode.PageDown))
        {
            list.PageDown(ItemCount);
        }
        else if (key.Is(KeyCode.PageUp))
        {
            list.PageUp(ItemCount);
        }
        else if (key.IsChar('n'))
        {
            noise = !noise;
        }
    }
    while (running && terminal.Poll(out ev, 0));
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
