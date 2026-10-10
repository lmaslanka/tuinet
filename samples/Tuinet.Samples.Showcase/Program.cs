// Showcase: a tabbed panel (a 20-item list, stats charts), an edit dialog (text boxes, dropdowns, checkboxes,
// buttons) and an animated progress dialog, both drawn as shadowed popups, a context menu (right-click or 'm')
// and a command palette (Ctrl+P) over every action. Nothing is saved to disk. The window title follows the
// selection, 'y' copies the selected item's name to the clipboard, and keys use the kitty keyboard protocol
// where the terminal has it.
using System.Diagnostics;
using Tuinet;
using Tuinet.Samples.Showcase;

using var terminal = Terminal.Open(new TerminalOptions
{
    Mouse = true,
    MouseMotion = true,   // the context menu highlights the item under the pointer
    BracketedPaste = true,
    SuspendOnCtrlZ = true,
    KittyKeyboard = true,
});
var app = new ShowcaseApp();
var clock = Stopwatch.StartNew();
var title = new char[96];

bool running = true;
while (running)
{
    long now = clock.ElapsedMilliseconds;
    title.AsSpan().TryWrite($"tuinet showcase · {app.Items[app.Selected].Name}", out int titleLength);
    terminal.SetTitle(title.AsSpan(0, titleLength));   // sent only when it changes
    app.LeftRightMargins = terminal.LeftRightMarginsActive;   // the terminal's reply arrives with the first input
    long frameStart = Stopwatch.GetTimestamp();
    app.Render(terminal.BeginFrame(), now);
    terminal.Present();
    app.RecordFrame(terminal.LastFrameBytes, Stopwatch.GetElapsedTime(frameStart));   // shown on the stats page

    // Sleep until input, or wake ~30 times a second while the progress dialog animates.
    if (!terminal.Poll(out Event ev, app.IsAnimating(now) ? 33 : Timeout.Infinite))
    {
        continue;
    }

    do
    {
        running = app.Handle(ev, clock.ElapsedMilliseconds);
        if (app.TakeCopy() is string text)
        {
            terminal.CopyToClipboard(text);
        }
    }
    while (running && terminal.Poll(out ev, 0));
}
