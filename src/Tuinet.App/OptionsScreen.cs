using System.Text;

namespace Tuinet;

public sealed class OptionsScreen
{
    private const int Org = 0;
    private const int Pat = 1;
    private const int Project = 2;
    private const int Save = 3;
    private const int Close = 4;
    private const int FocusCount = 5;
    private const int LabelWidth = 16;

    private readonly IAzureProjects _azure;
    private readonly SettingsStore _store;
    private readonly Action<object> _post;
    private readonly Action<Action> _background;
    private readonly string _directory;
    private readonly GitBranch[] _branches;
    private readonly Field _org = new();
    private readonly Field _pat = new(masked: true);
    private int _focus;
    private bool _dialogOpen;
    private bool _dropdownOpen;
    private string[] _projects = [];
    private int _projectSel;
    private int _projectScroll;
    private string _status = "tab next  enter confirm  esc close";
    private int _fetchGen;
    private string _selectedProject = "";

    public OptionsScreen(
        IAzureProjects azure,
        SettingsStore store,
        Action<object> post,
        string directory,
        IGitBranches git,
        Action<Action>? background = null)
    {
        _azure = azure;
        _store = store;
        _post = post;
        _directory = directory;
        _branches = git.ListLocal(directory);
        _background = background ?? StartBackground;

        Settings settings = store.Load();

        _org.Set(settings.Organization);
        _pat.Set(settings.Pat);
        _selectedProject = settings.Project;

        if (_org.Length > 0 && _pat.Length > 0)
        {
            StartFetch();
        }
    }

    public bool Handle(Event ev)
    {
        if (ev.Kind == EventKind.Message)
        {
            return HandleMessage(ev.Message);
        }

        if (ev.Kind == EventKind.Resize)
        {
            return true;
        }

        if (ev.Kind != EventKind.Key)
        {
            return true;
        }

        return HandleKey(ev.Key);
    }

    public void Paint(CellBuffer buffer)
    {
        buffer.Fill(new Rect(0, 0, buffer.Width, buffer.Height), Theme.Screen);
        buffer.DrawBox(new Rect(0, 0, buffer.Width, buffer.Height), Title(), Theme.Title);

        if (!_dialogOpen)
        {
            PaintMain(buffer);
            return;
        }

        PaintDialog(buffer);
    }

    private bool HandleMessage(object? message)
    {
        switch (message)
        {
            case ProjectsLoaded loaded when loaded.Generation == _fetchGen:
                _projects = loaded.Names;
                _projectSel = Array.FindIndex(_projects, p =>
                    string.Equals(p, _selectedProject, StringComparison.OrdinalIgnoreCase));
                if (_projectSel < 0)
                {
                    _projectSel = 0;
                }
                _status = _projects.Length == 0 ? "No projects." : $"{_projects.Length} projects";
                break;
            case ProjectsFailed failed when failed.Generation == _fetchGen:
                _projects = [];
                _status = failed.Error;
                _dropdownOpen = false;
                break;
        }

        return true;
    }

    private bool HandleKey(KeyEvent key)
    {
        if (key.IsCtrl('c') || key.IsChar('q'))
        {
            return false;
        }

        if (!_dialogOpen)
        {
            return HandleMain(key);
        }

        if (key.Code == KeyCode.Escape)
        {
            if (_dropdownOpen)
            {
                _dropdownOpen = false;
                return true;
            }

            CloseDialog();
            return true;
        }

        if (_dropdownOpen)
        {
            return HandleDropdown(key);
        }

        return _focus switch
        {
            Org or Pat => HandleText(key),
            Project => HandleProjectClosed(key),
            Save => HandleButton(key, save: true),
            Close => HandleButton(key, save: false),
            _ => true,
        };
    }

    private bool HandleMain(KeyEvent key)
    {
        if (IsOptionsKey(key))
        {
            _dialogOpen = true;
            _dropdownOpen = false;
            return true;
        }

        if (key.Code == KeyCode.Escape)
        {
            return false;
        }

        return true;
    }

    private static bool IsOptionsKey(KeyEvent key) =>
        key.Code == KeyCode.Char
        && (key.Rune.Value is 'o' or 'O')
        && (key.Modifiers & Modifiers.Ctrl) == 0;

    private bool HandleText(KeyEvent key)
    {
        if (key.Code == KeyCode.Tab)
        {
            CycleFocus(TabDelta(key));
            if (_focus == Project)
            {
                StartFetch();
            }
            return true;
        }

        if (key.Code == KeyCode.Enter)
        {
            if (_focus == Pat)
            {
                StartFetch();
            }
            CycleFocus(1);
            return true;
        }

        Field field = _focus == Org ? _org : _pat;
        field.Handle(key);
        return true;
    }

    private bool HandleProjectClosed(KeyEvent key)
    {
        if (key.Code == KeyCode.Tab)
        {
            CycleFocus(TabDelta(key));
            return true;
        }

        if (key.Code == KeyCode.Enter)
        {
            if (_projects.Length == 0)
            {
                StartFetch();
            }
            else
            {
                _dropdownOpen = true;
            }
            return true;
        }

        return true;
    }

    private bool HandleButton(KeyEvent key, bool save)
    {
        if (key.Code == KeyCode.Tab)
        {
            CycleFocus(TabDelta(key));
            return true;
        }

        if (key.Code == KeyCode.Enter)
        {
            if (save)
            {
                Persist();
            }

            CloseDialog();
        }

        return true;
    }

    private bool HandleDropdown(KeyEvent key)
    {
        if (key.Code == KeyCode.Tab)
        {
            _dropdownOpen = false;
            CycleFocus(TabDelta(key));
            return true;
        }

        if (key.IsChar('j') || key.Code == KeyCode.Down)
        {
            MoveProject(1);
        }
        else if (key.IsChar('k') || key.Code == KeyCode.Up)
        {
            MoveProject(-1);
        }
        else if (key.Code == KeyCode.Enter)
        {
            PickProject();
        }

        return true;
    }

    private static int TabDelta(KeyEvent key) =>
        (key.Modifiers & Modifiers.Shift) != 0 ? -1 : 1;

    private void CycleFocus(int delta)
    {
        _focus = (_focus + delta) % FocusCount;
        if (_focus < 0)
        {
            _focus += FocusCount;
        }

        if (_focus != Project)
        {
            _dropdownOpen = false;
        }
    }

    private void MoveProject(int delta)
    {
        if (_projects.Length == 0)
        {
            return;
        }
        _projectSel = Math.Clamp(_projectSel + delta, 0, _projects.Length - 1);
    }

    private void PickProject()
    {
        if (_projects.Length == 0)
        {
            return;
        }

        _selectedProject = _projects[_projectSel];
        _dropdownOpen = false;
        _status = $"Selected {_selectedProject}";
    }

    private void Persist()
    {
        _store.Save(new Settings
        {
            Organization = _org.Text,
            Pat = _pat.Text,
            Project = _selectedProject,
        });
        _status = string.IsNullOrEmpty(_selectedProject)
            ? "Saved"
            : $"Saved {_selectedProject}";
    }

    private void CloseDialog()
    {
        _dialogOpen = false;
        _dropdownOpen = false;
    }

    private void StartFetch()
    {
        if (_org.Length == 0 || _pat.Length == 0)
        {
            _status = "Organization and PAT required.";
            return;
        }

        int gen = ++_fetchGen;
        string org = _org.Text;
        string pat = _pat.Text;
        _status = "Loading projects…";
        _background(() =>
        {
            try
            {
                string[] names = _azure.ListProjects(org, pat);
                _post(new ProjectsLoaded(gen, names));
            }
            catch (Exception ex)
            {
                _post(new ProjectsFailed(gen, ex.Message));
            }
        });
    }

    private static void StartBackground(Action work) =>
        new Thread(() => work()) { IsBackground = true }.Start();

    private string Title()
    {
        string name = System.IO.Path.GetFileName(
            System.IO.Path.GetFullPath(_directory).TrimEnd(System.IO.Path.DirectorySeparatorChar));
        return string.IsNullOrEmpty(name) ? "tuinet" : name;
    }

    private void PaintMain(CellBuffer buffer)
    {
        int top = 2;
        int vis = Math.Max(1, buffer.Height - 4);
        int current = Array.FindIndex(_branches, b => b.Current);
        int scroll = 0;
        if (current >= vis)
        {
            scroll = current - vis + 1;
        }

        if (_branches.Length == 0)
        {
            buffer.Put(2, top, "No local branches.", Theme.Status);
        }
        else
        {
            for (int row = 0; row < vis && scroll + row < _branches.Length; row++)
            {
                GitBranch branch = _branches[scroll + row];
                Style style = branch.Current ? Theme.ListCursor : Theme.Title;
                string mark = branch.Current ? "* " : "  ";
                buffer.Put(2, top + row, mark + branch.Name, style);
            }
        }

        buffer.Put(2, buffer.Height - 2, "o options  q quit", Theme.Status);
    }

    private void PaintDialog(CellBuffer buffer)
    {
        Rect dialog = DialogRect(buffer);
        buffer.Fill(dialog, Theme.Screen);
        buffer.DrawBox(dialog, "Options", Theme.Title);

        int y = dialog.Y + 2;
        PaintField(buffer, dialog, y, "Organization:", _org, _focus == Org);
        PaintField(buffer, dialog, y + 2, "PAT:", _pat, _focus == Pat);
        PaintProject(buffer, dialog, y + 4);

        if (_dropdownOpen)
        {
            PaintDropdown(buffer, dialog, y + 5);
        }

        int buttonsY = dialog.Y + dialog.Height - 3;
        PaintButton(buffer, dialog.X + 3, buttonsY, "Save", _focus == Save);
        PaintButton(buffer, dialog.X + 11, buttonsY, "Close", _focus == Close);
        buffer.Put(dialog.X + 2, dialog.Y + dialog.Height - 2, _status, Theme.Status);
    }

    private void PaintField(CellBuffer buffer, Rect dialog, int y, string label, Field field, bool focused)
    {
        buffer.Put(dialog.X + 2, y, label, Theme.Label);
        Rect bar = Bar(dialog, y);
        if (bar.Width < 3)
        {
            return;
        }

        Style style = focused ? Theme.FieldFocus : Theme.FieldIdle;
        buffer.Fill(bar, style);

        int inner = bar.Width - 2;
        if (inner < 1)
        {
            inner = 1;
        }

        int caret = field.Caret;
        int scroll = 0;
        if (caret - scroll >= inner)
        {
            scroll = caret - inner + 1;
        }

        int col = 0;
        for (int i = scroll; i < field.Length; i++)
        {
            Rune rune = field.DisplayRune(i);
            int width = Cell.WidthOf(rune);
            if (width <= Cell.ZeroWidth)
            {
                continue;
            }

            if (col + width > inner)
            {
                break;
            }

            Style glyph = focused && i == caret ? Theme.Caret : style;
            buffer.Put(bar.X + 1 + col, y, rune, glyph);
            col += width;
        }

        if (focused && caret >= field.Length && col < inner)
        {
            buffer.Put(bar.X + 1 + col, y, new Rune(' '), Theme.Caret);
        }
    }

    private void PaintProject(CellBuffer buffer, Rect dialog, int y)
    {
        buffer.Put(dialog.X + 2, y, "Project:", Theme.Label);
        Rect bar = Bar(dialog, y);
        if (bar.Width < 3)
        {
            return;
        }

        bool focused = _focus == Project;
        Style style = focused ? Theme.FieldFocus : Theme.FieldIdle;
        buffer.Fill(bar, style);

        int inner = bar.Width - 3;
        if (inner < 1)
        {
            inner = 1;
        }

        string text = string.IsNullOrEmpty(_selectedProject) ? "select a project" : _selectedProject;
        int col = 0;
        foreach (Rune rune in text.EnumerateRunes())
        {
            int width = Cell.WidthOf(rune);
            if (width <= Cell.ZeroWidth)
            {
                continue;
            }

            if (col + width > inner)
            {
                break;
            }

            buffer.Put(bar.X + 1 + col, y, rune, style);
            col += width;
        }

        buffer.Put(bar.X + bar.Width - 1, y, new Rune('▾'), style);
    }

    private void PaintDropdown(CellBuffer buffer, Rect dialog, int y)
    {
        if (_projects.Length == 0)
        {
            return;
        }

        Rect bar = Bar(dialog, y - 1);
        int height = Math.Max(0, dialog.Y + dialog.Height - 3 - y);
        var clip = new Rect(bar.X, y, bar.Width, height);
        if (clip.Width < 1 || clip.Height < 1)
        {
            return;
        }

        if (_projectSel < _projectScroll)
        {
            _projectScroll = _projectSel;
        }

        if (_projectSel >= _projectScroll + clip.Height)
        {
            _projectScroll = _projectSel - clip.Height + 1;
        }

        buffer.Fill(clip, Theme.FieldIdle);
        for (int row = 0; row < clip.Height; row++)
        {
            int index = _projectScroll + row;
            if (index >= _projects.Length)
            {
                break;
            }
            Style style = index == _projectSel ? Theme.ListCursor : Theme.FieldIdle;
            buffer.Put(clip, 1, row, _projects[index], style);
        }
    }

    private static void PaintButton(CellBuffer buffer, int x, int y, string label, bool focused)
    {
        Style style = focused ? Theme.FieldFocus : Theme.FieldIdle;
        buffer.Fill(new Rect(x, y, label.Length + 2, 1), style);
        buffer.Put(x + 1, y, label, style);
    }

    private static Rect DialogRect(CellBuffer buffer)
    {
        int width = Math.Clamp(buffer.Width - 4, 2, 60);
        int height = Math.Clamp(buffer.Height - 4, 2, 14);
        int x = Math.Max(0, (buffer.Width - width) / 2);
        int y = Math.Max(0, (buffer.Height - height) / 2);
        return new Rect(x, y, width, height);
    }

    private static Rect Bar(Rect dialog, int y)
    {
        int x = dialog.X + 2 + LabelWidth;
        int width = Math.Max(2, dialog.X + dialog.Width - 2 - x);
        return new Rect(x, y, width, 1);
    }
}

public sealed record ProjectsLoaded(int Generation, string[] Names);

public sealed record ProjectsFailed(int Generation, string Error);
