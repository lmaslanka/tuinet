using Tuinet;
using Tuinet.Samples.Branches;

string directory = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

using var terminal = Terminal.Open(new TerminalOptions { BracketedPaste = true });

var app = new App(
    new AzureDevOpsProjects(),
    SettingsStore.Default(),
    terminal.Post,
    directory,
    new LocalGitBranches());

bool running = true;
while (running)
{
    app.Render(terminal.BeginFrame());
    terminal.Present();

    if (!terminal.Poll(out Event ev, Timeout.Infinite))
    {
        continue;
    }

    // Handle everything already queued, then draw once: a burst of key repeats costs one frame.
    do
    {
        running = app.Handle(ev);
    }
    while (running && terminal.Poll(out ev, 0));
}
