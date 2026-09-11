using Tuinet;

string directory = args.Length > 0 ? args[0] : Directory.GetCurrentDirectory();
using var terminal = Terminal.Open();
var screen = new OptionsScreen(
    new AzureDevOpsProjects(),
    SettingsStore.Default(),
    terminal.Post,
    directory,
    new LocalGitBranches());
terminal.Draw(screen.Paint);

while (true)
{
    if (!terminal.Poll(out Event ev, Timeout.Infinite))
    {
        continue;
    }

    if (!screen.Handle(ev))
    {
        break;
    }

    terminal.Draw(screen.Paint);
}
