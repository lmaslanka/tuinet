using System.Text;
using Tuinet.Widgets;

namespace Tuinet.Samples.Showcase;

/// <summary>
/// Simulated pipeline: stage bars with values, worker cards with effort meters and spinners, and an
/// overall bar. Progress is a pure function of elapsed time, so rendering is deterministic in tests.
/// </summary>
public sealed class ProgressDialog
{
    private static readonly Stage[] Stages =
    [
        new("which file", 0.92, 5.0, "sharp"),
        new("which tool", 0.87, 6.5, "sharp"),
        new("retry or give up", 0.88, 11.0, "sharp"),
    ];

    private static readonly Worker[] Workers =
    [
        new("explorer", "reads the code", 2, 9.0, Theme.Blue),
        new("researcher", "pulls the docs", 2, 4.0, Theme.Blue),
        new("worker", "edits + runs tests", 3, 14.0, Theme.Violet),
    ];

    private long _startMs;

    public ProgressDialog(long nowMs) => _startMs = nowMs;

    /// <summary>True until every bar has finished: the loop keeps redrawing while animating.</summary>
    public bool IsAnimating(long nowMs) => Seconds(nowMs) < Longest();

    public DialogResult Handle(Event ev, long nowMs)
    {
        if (ev.Kind != EventKind.Key)
        {
            return DialogResult.Open;
        }

        if (ev.Key.IsChar('r'))
        {
            _startMs = nowMs;
            return DialogResult.Open;
        }

        return ev.Key.Is(KeyCode.Escape) || ev.Key.IsChar('p') || ev.Key.Is(KeyCode.Enter)
            ? DialogResult.Closed
            : DialogResult.Open;
    }

    public void Render(CellBuffer buffer, long nowMs)
    {
        double t = Seconds(nowMs);
        long elapsedMs = nowMs - _startMs;
        Rect dialog = buffer.Area.Centered(78, 28);
        buffer.Render(new Clear(Theme.Dialog), dialog);
        var frame = new Block
        {
            BorderType = BorderType.Rounded,
            BorderStyle = Theme.Accent(Theme.Faint),
            Title = " PIPELINE · progress ",
            TitleStyle = Theme.Heading(Theme.Text),
            TitleAlignment = Alignment.Center,
            Footer = " r restart · esc close ",
            FooterAlignment = Alignment.Right,
            Style = Theme.Dialog,
        };
        buffer.Render(frame, dialog);
        Rect inner = frame.Inner(dialog).Inset(2, 1);

        Span<Rect> rows = stackalloc Rect[6];
        Layout.Vertical(inner,
        [
            Constraint.Length(1), Constraint.Length(1), Constraint.Length(2 * Stages.Length + 5),
            Constraint.Length(1), Constraint.Length(8), Constraint.Fill(),
        ], rows);

        RenderLegend(buffer, rows[0]);
        RenderStages(buffer, rows[2], t);
        RenderWorkers(buffer, rows[4], t, elapsedMs);
        RenderOverall(buffer, rows[5], t);
    }

    private static void RenderLegend(CellBuffer buffer, Rect row)
    {
        int x = row.X + 1;
        x = Legend(buffer, x, row, Theme.Amber, "main · high");
        x = Legend(buffer, x, row, Theme.Blue, "agents · medium");
        x = Legend(buffer, x, row, Theme.Green, "stages · forks");
        Legend(buffer, x, row, Theme.Violet, "worker · high");
    }

    private static int Legend(CellBuffer buffer, int x, Rect row, Color color, ReadOnlySpan<char> label)
    {
        x = buffer.SetString(x, row.Y, "■ ", Theme.Accent(color), row.Right - x);
        x = buffer.SetString(x, row.Y, label, Theme.Dim, row.Right - x);
        return x + 3;
    }

    private static void RenderStages(CellBuffer buffer, Rect box, double t)
    {
        var block = new Block { BorderStyle = Theme.Accent(Theme.Green) };
        buffer.Render(block, box);
        Rect inner = block.Inner(box).Inset(2, 0);

        buffer.SetString(inner.X, inner.Y, "STAGES", Theme.Heading(Theme.Green), 8);
        buffer.SetString(inner.X + 7, inner.Y, "· fork layer", Theme.Accent(Theme.Green), inner.Width - 7);
        Span<char> count = stackalloc char[24];
        count.TryWrite($"forks {(int)(Math.Min(t, Longest()) * 126.5):N0}", out int countLength);
        buffer.SetString(inner.Right - countLength, inner.Y, count[..countLength], Theme.Heading(Theme.Green), countLength);

        int labelWidth = 20;
        int valueWidth = 12;
        int barWidth = Math.Max(4, inner.Width - labelWidth - valueWidth - 2);
        Span<char> value = stackalloc char[8];
        for (int i = 0; i < Stages.Length; i++)
        {
            Stage stage = Stages[i];
            // A blank row between bars: full-height blocks on adjacent rows would merge into a slab.
            int y = inner.Y + 2 + 2 * i;
            double ratio = Math.Min(1, t / stage.Seconds) * stage.Target;
            bool finished = t >= stage.Seconds;

            buffer.SetString(inner.X, y, stage.Label, Theme.Strong, labelWidth - 1, Overflow.Ellipsis);
            buffer.Render(new ProgressBar(ratio)
            {
                FilledStyle = Theme.Accent(Theme.Green),
                EmptyStyle = Theme.Faded,
            }, new Rect(inner.X + labelWidth, y, barWidth, 1));

            value.TryWrite($"{ratio:0.00}", out int valueLength);
            int x = inner.X + labelWidth + barWidth + 2;
            x = buffer.SetString(x, y, value[..valueLength], Theme.Heading(Theme.Green), inner.Right - x);
            buffer.SetString(x + 1, y, finished ? stage.Verdict : "…", finished ? Theme.Accent(Theme.Green) : Theme.Dim, inner.Right - x - 1);
        }

        int footerY = inner.Y + 2 * Stages.Length + 2;
        int fx = buffer.SetString(inner.X, footerY, "sharp → runs in code", Theme.Accent(Theme.Green), inner.Width);
        buffer.SetString(fx + 4, footerY, "split → model", Theme.Dim, inner.Right - fx - 4);
        buffer.SetString(inner.Right - 7, footerY, "< 0.5 s", Theme.Dim, 7);
    }

    private static void RenderWorkers(CellBuffer buffer, Rect area, double t, long elapsedMs)
    {
        Span<Rect> cards = stackalloc Rect[Workers.Length];
        Layout.Horizontal(area, [Constraint.Fill(), Constraint.Fill(), Constraint.Fill()], cards, spacing: 2);
        Span<char> status = stackalloc char[16];
        for (int i = 0; i < Workers.Length; i++)
        {
            Worker worker = Workers[i];
            Rect card = cards[i];
            var block = new Block { BorderStyle = Theme.Accent(worker.Color) };
            buffer.Render(block, card);
            Rect inner = block.Inner(card).Inset(1, 0);
            double ratio = Math.Min(1, t / worker.Seconds);
            bool done = ratio >= 1;

            Centered(buffer, inner, 0, worker.Name, Theme.Strong);
            Centered(buffer, inner, 1, "Opus 5.5", Theme.Accent(worker.Color));

            // "effort ■■□□ med": a segmented bar is a ProgressBar with block glyphs and no smoothing.
            int effortX = inner.X + Math.Max(0, (inner.Width - 15) / 2);
            int x = buffer.SetString(effortX, inner.Y + 2, "effort ", Theme.Dim, inner.Right - effortX);
            buffer.Render(new ProgressBar(worker.Effort / 4.0)
            {
                FilledChar = '■',
                EmptyChar = '□',
                FilledStyle = Theme.Accent(worker.Color),
                EmptyStyle = Theme.Accent(worker.Color),
            }, new Rect(x, inner.Y + 2, 4, 1));
            buffer.SetString(x + 5, inner.Y + 2, worker.Effort >= 3 ? "high" : "med", Theme.Accent(worker.Color), inner.Right - x - 5);

            Centered(buffer, inner, 3, worker.Task, Theme.Strong);
            buffer.Render(new ProgressBar(ratio)
            {
                FilledStyle = Theme.Accent(worker.Color),
                EmptyStyle = Theme.Faded,
            }, new Rect(inner.X + 1, inner.Y + 4, inner.Width - 2, 1));

            int length;
            if (done)
            {
                status.TryWrite($"✓ done", out length);
            }
            else
            {
                status.TryWrite($"{Spinner.Frame(Spinner.Line, elapsedMs + i * 130, 120)} running", out length);
            }

            Centered(buffer, inner, 5, status[..length], done ? Theme.Heading(Theme.Green) : Theme.Strong);
        }
    }

    private static void RenderOverall(CellBuffer buffer, Rect area, double t)
    {
        if (area.Height < 2)
        {
            return;
        }

        double total = 0;
        foreach (Stage stage in Stages)
        {
            total += Math.Min(1, t / stage.Seconds);
        }

        foreach (Worker worker in Workers)
        {
            total += Math.Min(1, t / worker.Seconds);
        }

        double ratio = total / (Stages.Length + Workers.Length);
        int y = area.Y + 1;
        int x = buffer.SetString(area.X, y, "overall ", Theme.Heading(Theme.Amber), area.Width);
        Span<char> percent = stackalloc char[8];
        percent.TryWrite($"{ratio * 100,5:0.0}%", out int length);
        int barWidth = Math.Max(4, area.Right - x - length - 1);
        buffer.Render(new ProgressBar(ratio)
        {
            FilledStyle = Theme.Accent(Theme.Amber),
            EmptyStyle = Theme.Faded,
        }, new Rect(x, y, barWidth, 1));
        buffer.SetString(x + barWidth + 1, y, percent[..length], Theme.Heading(Theme.Amber), length);
    }

    private static void Centered(CellBuffer buffer, Rect inner, int row, ReadOnlySpan<char> text, Style style)
    {
        int width = Math.Min(TextWidth.Of(text), inner.Width);
        buffer.SetString(inner.X + (inner.Width - width) / 2, inner.Y + row, text, style, inner.Width, Overflow.Ellipsis);
    }

    private double Seconds(long nowMs) => Math.Max(0, nowMs - _startMs) / 1000.0;

    private static double Longest()
    {
        double longest = 0;
        foreach (Stage stage in Stages)
        {
            longest = Math.Max(longest, stage.Seconds);
        }

        foreach (Worker worker in Workers)
        {
            longest = Math.Max(longest, worker.Seconds);
        }

        return longest;
    }

    private readonly record struct Stage(string Label, double Target, double Seconds, string Verdict);

    private readonly record struct Worker(string Name, string Task, int Effort, double Seconds, Color Color);
}
