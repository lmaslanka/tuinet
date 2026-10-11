// Gallery: a page per widget and feature (layout, blocks, text, forms, editors, lists, tables, trees, scrollbars,
// progress, charts, tabs, menus, popups, fuzzy matching), each live and interactive beside the code that draws it.
// [ and ] or Alt+1…9 switch pages, c hides the code, q quits. Nothing is saved to disk. The window title names the
// page, and keys use the kitty keyboard protocol where the terminal has it.
using Tuinet;
using Tuinet.Samples.Gallery;

using var terminal = Terminal.Open(new TerminalOptions
{
    Mouse = true,
    MouseMotion = true,   // menus highlight the item under the pointer
    BracketedPaste = true,
    SuspendOnCtrlZ = true,
    KittyKeyboard = true,
});
terminal.Run(new GalleryLoop(terminal, new GalleryApp()));   // sleeps until input, or ~30 fps while a page animates

/// <summary>Connects the app to the terminal: the window title follows the page.</summary>
internal sealed class GalleryLoop(Terminal terminal, GalleryApp app) : IApp
{
    private readonly char[] _title = new char[64];

    public bool Handle(Event ev, long nowMs) => app.Handle(ev, nowMs);

    public void Render(CellBuffer frame, long nowMs)
    {
        _title.AsSpan().TryWrite($"tuinet gallery · {app.Current.Title}", out int titleLength);
        terminal.SetTitle(_title.AsSpan(0, titleLength));   // sent only when it changes
        app.Render(frame, nowMs);
    }

    public bool IsAnimating(long nowMs) => app.IsAnimating(nowMs);
}
