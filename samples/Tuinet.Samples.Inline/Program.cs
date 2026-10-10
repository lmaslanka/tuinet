// Inline: a simulated download drawn in a band under the shell prompt, not on the alternate screen.
// Each finished file is printed above the band with PrintAbove, so it stays in the scrollback; the
// summary line stays on screen after exit. Ctrl+C or q cancels. Nothing touches the network or disk.
using Tuinet;
using Tuinet.Samples.Inline;

using var terminal = Terminal.Open(new TerminalOptions { Inline = new InlineOptions(Download.BandHeight) });
var download = new Download(Download.SampleFiles(), workers: 3);
download.Advance(0, terminal);   // the workers start before the first frame
terminal.Run(new DownloadApp(terminal, download));   // ~30 frames a second; the last frame (the summary) stays

/// <summary>Moves the download forward on every tick or key, and stops when it finishes or is cancelled.</summary>
internal sealed class DownloadApp(Terminal terminal, Download download) : IApp
{
    public bool Handle(Event ev, long nowMs)
    {
        if (ev.Kind == EventKind.Key && (ev.Key.IsCtrl('c') || ev.Key.IsChar('q')))
        {
            download.Cancel(nowMs, terminal);
            return false;
        }

        download.Advance(nowMs, terminal);   // prints finished files above the band: between frames, not in Render
        return !download.IsFinished;
    }

    public void Render(CellBuffer frame, long nowMs) => download.Render(frame, nowMs);

    public bool IsAnimating(long nowMs) => true;
}
