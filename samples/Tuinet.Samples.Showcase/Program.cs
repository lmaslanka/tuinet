// Showcase: a 20-item list, an edit dialog (text boxes, dropdowns, checkboxes, buttons) and an
// animated progress dialog. Nothing is saved to disk.
using System.Diagnostics;
using Tuinet;
using Tuinet.Samples.Showcase;

using var terminal = Terminal.Open(new TerminalOptions { BracketedPaste = true });
var app = new ShowcaseApp();
var clock = Stopwatch.StartNew();

bool running = true;
while (running)
{
    long now = clock.ElapsedMilliseconds;
    app.Render(terminal.BeginFrame(), now);
    terminal.Present();

    // Sleep until input, or wake ~30 times a second while the progress dialog animates.
    if (!terminal.Poll(out Event ev, app.IsAnimating(now) ? 33 : Timeout.Infinite))
    {
        continue;
    }

    do
    {
        running = app.Handle(ev, clock.ElapsedMilliseconds);
    }
    while (running && terminal.Poll(out ev, 0));
}
