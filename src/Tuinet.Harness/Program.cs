using Tuinet;

string directory = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();

using var terminal = Terminal.Open();

var app = new App(
    new AzureDevOpsProjects(),
    SettingsStore.Default(),
    terminal.Post,
    directory,
    new LocalGitBranches());

terminal.Draw(app.Paint);

while (true)
{
    if (!terminal.Poll(out Event ev, Timeout.Infinite))
    {
        continue;
    }

    if (!app.Handle(ev))
    {
        break;
    }

    terminal.Draw(app.Paint);
}
