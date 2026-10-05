using Tuinet.Widgets;

namespace Tuinet.Samples.Inline;

/// <summary>A fake multi-file download: a few workers, each "fetching" one file at its own speed.</summary>
public sealed class Download(Download.File[] files, int workers)
{
    /// <summary>Status line, one row per worker, total bar.</summary>
    public const int BandHeight = 5;

    public readonly record struct File(string Name, long Bytes, double BytesPerMs);

    private readonly (int File, long StartMs)[] _slots = Enumerable.Repeat((-1, 0L), workers).ToArray();
    private readonly long _total = files.Sum(f => f.Bytes);
    private int _next;
    private int _done;
    private long _doneBytes;
    private long _finishedMs = -1;
    private bool _cancelled;

    public bool IsFinished => _finishedMs >= 0;

    public static File[] SampleFiles()
    {
        var random = new Random(7);
        string[] names = ["kernel-6.12.tar.xz", "fonts-noto.zip", "dotnet-sdk-10.tar.gz", "wallpapers.7z", "llvm-19.tar.xz",
            "dataset-part1.parquet", "dataset-part2.parquet", "manual.pdf"];
        return [.. names.Select(name => new File(name, random.Next(4, 48) * 1_000_000L, random.Next(18, 45) * 1_000.0))];
    }

    /// <summary>
    /// Move time forward. A finished file is printed above the band, and its worker starts the next file at the
    /// moment it finished, so the simulation doesn't depend on how often frames are drawn.
    /// </summary>
    public void Advance(long nowMs, Terminal terminal)
    {
        for (int i = 0; i < _slots.Length; i++)
        {
            while (_slots[i].File >= 0)
            {
                (int file, long start) = _slots[i];
                long end = start + Duration(files[file]);
                if (end > nowMs)
                {
                    break;
                }

                Log(terminal, files[file], end - start);
                _done++;
                _doneBytes += files[file].Bytes;
                _slots[i] = _next < files.Length ? (_next++, end) : (-1, 0);
            }

            if (_slots[i].File < 0 && _next < files.Length)
            {
                _slots[i] = (_next++, nowMs);
            }
        }

        if (_done == files.Length && !IsFinished)
        {
            _finishedMs = nowMs;
        }
    }

    public void Cancel(long nowMs, Terminal terminal)
    {
        _cancelled = true;
        _finishedMs = nowMs;
        terminal.PrintAbove(new StyledText("✗ cancelled", Theme.Red));
    }

    public void Render(CellBuffer buffer, long nowMs)
    {
        int width = buffer.Width;
        Span<char> text = stackalloc char[96];
        if (IsFinished)
        {
            // The last frame stays on screen after exit: keep it to one summary line.
            var summary = new StyledTextBuilder(stackalloc char[96], stackalloc StyledRun[3]);
            summary.Append(_cancelled ? "✗ " : "✓ ", _cancelled ? Theme.Red : Theme.Green);
            text.TryWrite($"{_done} of {files.Length} files · {Mb(_doneBytes)} MB in {_finishedMs / 1000.0:F1} s", out int n);
            summary.Append(text[..n], Theme.Text);
            buffer.SetText(0, 0, summary.Build(), width);
            return;
        }

        long total = _total;
        long received = _doneBytes;
        int active = 0;
        for (int i = 0; i < _slots.Length; i++)
        {
            (int file, long start) = _slots[i];
            if (file >= 0)
            {
                long bytes = Math.Min(Received(file, start, nowMs), files[file].Bytes);
                received += bytes;
                active++;
                RenderFile(buffer, 1 + i, files[file], bytes, nowMs);
            }
        }

        var status = new StyledTextBuilder(stackalloc char[96], stackalloc StyledRun[3]);
        status.Append([Spinner.Frame(Spinner.Dots, nowMs)], Theme.Blue);
        text.TryWrite($" downloading · {active} active · {_done} of {files.Length} done", out int length);
        status.Append(text[..length], Theme.Text);
        buffer.SetText(0, 0, status.Build(), width);

        int barWidth = Math.Max(0, width - 26);
        buffer.SetString(0, 4, "total", Theme.Dim, width);
        buffer.Render(new ProgressBar((double)received / total) { FilledStyle = Theme.Green, EmptyStyle = Theme.Faint }, new Rect(6, 4, barWidth, 1));
        text.TryWrite($"{Mb(received),6} / {Mb(total)} MB", out length);
        buffer.SetString(7 + barWidth, 4, text[..length], Theme.Dim, width - 7 - barWidth);
    }

    private static void RenderFile(CellBuffer buffer, int y, File file, long bytes, long nowMs)
    {
        int width = buffer.Width;
        int nameWidth = Math.Min(22, width / 3);
        buffer.SetString(0, y, file.Name, Theme.Text, nameWidth, Overflow.Ellipsis);
        int barWidth = Math.Max(0, width - nameWidth - 18);   // 18: "  68.8%  41 MB/s" and gaps
        buffer.Render(new ProgressBar((double)bytes / file.Bytes) { FilledStyle = Theme.Blue, EmptyStyle = Theme.Faint },
            new Rect(nameWidth + 1, y, barWidth, 1));
        Span<char> text = stackalloc char[32];
        text.TryWrite($"{100.0 * bytes / file.Bytes,5:F1}%  {file.BytesPerMs / 1000:F0} MB/s", out int n);
        buffer.SetString(nameWidth + 2 + barWidth, y, text[..n], Theme.Dim, width - nameWidth - 2 - barWidth);
    }

    private static void Log(Terminal terminal, File file, long ms)
    {
        var line = new StyledTextBuilder(stackalloc char[96], stackalloc StyledRun[3]);
        line.Append("✓ ", Theme.Green);
        line.Append(file.Name, Theme.Text);
        Span<char> text = stackalloc char[48];
        text.TryWrite($"  {Mb(file.Bytes)} MB in {ms / 1000.0:F1} s", out int n);
        line.Append(text[..n], Theme.Dim);
        terminal.PrintAbove(line.Build());
    }

    private long Received(int file, long startMs, long nowMs) => (long)((nowMs - startMs) * files[file].BytesPerMs);

    private static long Duration(File file) => (long)Math.Ceiling(file.Bytes / file.BytesPerMs);

    private static double Mb(long bytes) => Math.Round(bytes / 1_000_000.0, 1);
}

internal static class Theme
{
    public static readonly Style Text = new(Color.Default, default);
    public static readonly Style Dim = new(Color.BrightBlack, default);
    public static readonly Style Faint = new(Color.Hex(0x3A4252), default);
    public static readonly Style Blue = new(Color.Hex(0x5AA9FF), default);
    public static readonly Style Green = new(Color.Hex(0x34D399), default, Attr.Bold);
    public static readonly Style Red = new(Color.Hex(0xF87171), default, Attr.Bold);
}
