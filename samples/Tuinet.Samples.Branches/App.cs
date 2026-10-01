using Tuinet.Widgets;

namespace Tuinet.Samples.Branches;

/// <summary>Routes input between the branch list and the options overlay; quits on q / Ctrl+C.</summary>
public sealed class App
{
    private readonly MainView _main;
    private readonly OptionsDialog _options;
    private bool _open;

    public App(
        IAzureProjects azure,
        SettingsStore store,
        Action<object> post,
        string directory,
        IGitBranches git,
        Action<Action>? background = null)
    {
        _main = new MainView(directory, git);
        _options = new OptionsDialog(azure, store, post, background);
    }

    /// <summary>Returns false when the app should exit.</summary>
    public bool Handle(Event ev)
    {
        switch (ev.Kind)
        {
            case EventKind.Message:
                _options.Handle(ev);
                return true;
            case EventKind.Paste when _open:
                _options.Handle(ev);
                return true;
            case EventKind.Key:
                break;
            default:
                return true;
        }

        KeyEvent key = ev.Key;
        if (key.IsCtrl('c') || key.IsChar('q'))
        {
            return false;
        }

        if (_open)
        {
            _open = _options.Handle(ev);
            return true;
        }

        if (_main.Handle(key))
        {
            _options.Prepare();
            _open = true;
        }

        return true;
    }

    public void Render(CellBuffer buffer)
    {
        Rect area = buffer.Area;
        buffer.Fill(area, Theme.Screen);
        buffer.Render(new Block { Title = _main.Title, BorderStyle = Theme.Title }, area);
        _main.Render(buffer, area);
        if (_open)
        {
            _options.Render(buffer);
        }
    }
}
