// Showcase: a tabbed panel (a 20-item list, stats charts), an edit dialog (text boxes, dropdowns, checkboxes,
// buttons) and an animated progress dialog, both drawn as shadowed popups, a context menu (right-click or 'm')
// and a command palette (Ctrl+P) over every action. Nothing is saved to disk. The window title follows the
// selection, 'y' copies the selected item's name to the clipboard, and keys use the kitty keyboard protocol
// where the terminal has it.
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
terminal.Run(new ShowcaseLoop(terminal, new ShowcaseApp()));   // sleeps until input, or ~30 fps while the progress dialog animates

/// <summary>Connects the app to the terminal: the window title, the clipboard, and the frame costs for the stats page.</summary>
internal sealed class ShowcaseLoop(Terminal terminal, ShowcaseApp app) : IApp
{
    private readonly char[] _title = new char[96];

    public bool Handle(Event ev, long nowMs)
    {
        bool running = app.Handle(ev, nowMs);
        if (app.TakeCopy() is string text)
        {
            terminal.CopyToClipboard(text);
        }

        return running;
    }

    public void Render(CellBuffer frame, long nowMs)
    {
        if (terminal.Frames > 0)
        {
            app.RecordFrame(terminal.LastFrameBytes, terminal.LastFrameTime);   // the previous frame, shown on the stats page
        }

        _title.AsSpan().TryWrite($"tuinet showcase · {app.Items[app.Selected].Name}", out int titleLength);
        terminal.SetTitle(_title.AsSpan(0, titleLength));   // sent only when it changes
        app.LeftRightMargins = terminal.LeftRightMarginsActive;   // the terminal's reply arrives with the first input
        app.Render(frame, nowMs);
    }

    public bool IsAnimating(long nowMs) => app.IsAnimating(nowMs);

    public long NextDueMs(long nowMs) => app.NextDueMs;
}
