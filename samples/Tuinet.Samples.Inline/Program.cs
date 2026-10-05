// Inline: a simulated download drawn in a band under the shell prompt, not on the alternate screen.
// Each finished file is printed above the band with PrintAbove, so it stays in the scrollback; the
// summary line stays on screen after exit. Ctrl+C or q cancels. Nothing touches the network or disk.
using System.Diagnostics;
using Tuinet;
using Tuinet.Samples.Inline;

using var terminal = Terminal.Open(new TerminalOptions { Inline = new InlineOptions(Download.BandHeight) });
var download = new Download(Download.SampleFiles(), workers: 3);
var clock = Stopwatch.StartNew();

while (!download.IsFinished)
{
    long now = clock.ElapsedMilliseconds;
    download.Advance(now, terminal);
    download.Render(terminal.BeginFrame(), now);
    terminal.Present();
    if (download.IsFinished)
    {
        break;
    }

    // Redraw ~30 times a second; keys in between.
    if (!terminal.Poll(out Event ev, 33))
    {
        continue;
    }

    do
    {
        if (ev.Kind == EventKind.Key && (ev.Key.IsCtrl('c') || ev.Key.IsChar('q')))
        {
            download.Cancel(clock.ElapsedMilliseconds, terminal);
            download.Render(terminal.BeginFrame(), clock.ElapsedMilliseconds);
            terminal.Present();
            break;
        }
    }
    while (terminal.Poll(out ev, 0));
}
