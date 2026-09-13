namespace Tuinet;

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

    public bool Handle(Event ev)
    {
        if (ev.Kind == EventKind.Message)
        {
            _options.Handle(ev);
            return true;
        }

        if (ev.Kind == EventKind.Resize)
        {
            return true;
        }

        if (ev.Kind != EventKind.Key)
        {
            return true;
        }

        KeyEvent key = ev.Key;
        if (key.IsCtrl('c') || key.IsChar('q'))
        {
            return false;
        }

        if (_open)
        {
            if (!_options.Handle(ev))
            {
                _open = false;
            }

            return true;
        }

        if (_main.Handle(key))
        {
            _options.Prepare();
            _open = true;
        }

        return true;
    }

    public void Paint(CellBuffer buffer)
    {
        buffer.Fill(new Rect(0, 0, buffer.Width, buffer.Height), Theme.Screen);
        buffer.DrawBox(new Rect(0, 0, buffer.Width, buffer.Height), _main.Title, Theme.Title);
        _main.Paint(buffer);
        if (_open)
        {
            _options.Paint(buffer);
        }
    }
}
