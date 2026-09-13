using System.Text;

namespace Tuinet;

public sealed class OptionsDialog
{
    private const int Org = 0;
    private const int Pat = 1;
    private const int Project = 2;
    private const int Save = 3;
    private const int Close = 4;
    private const int FocusCount = 5;

    private readonly IAzureProjects _azure;
    private readonly SettingsStore _store;
    private readonly Action<object> _post;
    private readonly Action<Action> _background;
    private readonly Field _org = new();
    private readonly Field _pat = new(masked: true);
    private int _focus;
    private bool _dropdownOpen;
    private string[] _projects = [];
    private int _projectSel;
    private int _projectScroll;
    private string _status = "tab next  enter confirm  esc close";
    private int _fetchGen;
    private string _selectedProject = "";

    public OptionsDialog(
        IAzureProjects azure,
        SettingsStore store,
        Action<object> post,
        Action<Action>? background = null)
    {
        _azure = azure;
        _store = store;
        _post = post;
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
            HandleMessage(ev.Message);
            return true;
        }

        if (ev.Kind != EventKind.Key)
        {
            return true;
        }

        return HandleKey(ev.Key);
    }

    public void Prepare() => _dropdownOpen = false;

    public void Paint(CellBuffer buffer)
    {
        Rect dialog = DialogRect(buffer);
        buffer.Fill(dialog, Theme.Screen);
        buffer.DrawBox(dialog, "Options", Theme.Title);

        int y = dialog.Y + 2;
        PaintField(buffer, dialog, y, "Organization", _org, _focus == Org);
        PaintField(buffer, dialog, y + 3, "PAT", _pat, _focus == Pat);
        PaintProject(buffer, dialog, y + 6);

        if (_dropdownOpen)
        {
            PaintDropdown(buffer, dialog, y + 9);
        }

        int buttonsY = dialog.Y + dialog.Height - 3;
        PaintButton(buffer, dialog.X + 3, buttonsY, "Save", _focus == Save);
        PaintButton(buffer, dialog.X + 15, buttonsY, "Close", _focus == Close);
        buffer.Put(dialog.X + 2, dialog.Y + dialog.Height - 2, _status, Theme.Status);
    }

    private void HandleMessage(object? message)
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
    }

    private bool HandleKey(KeyEvent key)
    {
        if (key.Code == KeyCode.Escape)
        {
            if (_dropdownOpen)
            {
                _dropdownOpen = false;
                return true;
            }

            return false;
        }

        if (_dropdownOpen)
        {
            HandleDropdown(key);
            return true;
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

            return false;
        }

        return true;
    }

    private void HandleDropdown(KeyEvent key)
    {
        if (key.Code == KeyCode.Tab)
        {
            _dropdownOpen = false;
            CycleFocus(TabDelta(key));
            return;
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

    private void PaintField(CellBuffer buffer, Rect dialog, int y, string label, Field field, bool focused)
    {
        Rect box = FieldRect(dialog, y);
        PaintOutline(buffer, box, label, focused, required: true);
        if (box.Width < 3)
        {
            return;
        }

        int inner = box.Width - 2;
        int caret = field.Caret;
        int scroll = 0;
        if (caret - scroll >= inner)
        {
            scroll = caret - inner + 1;
        }

        int col = 0;
        int valueY = box.Y + 1;
        int valueX = box.X + 1;
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

            Style glyph = focused && i == caret ? Theme.Caret : Theme.Title;
            buffer.Put(valueX + col, valueY, rune, glyph);
            col += width;
        }

        if (focused && caret >= field.Length && col < inner)
        {
            buffer.Put(valueX + col, valueY, new Rune(' '), Theme.Caret);
        }
    }

    private void PaintProject(CellBuffer buffer, Rect dialog, int y)
    {
        Rect box = FieldRect(dialog, y);
        PaintOutline(buffer, box, "Project", _focus == Project, required: true);
        if (box.Width < 3)
        {
            return;
        }

        int inner = box.Width - 3;
        if (inner < 1)
        {
            inner = 1;
        }

        string text = string.IsNullOrEmpty(_selectedProject) ? "select a project" : _selectedProject;
        int col = 0;
        int valueY = box.Y + 1;
        int valueX = box.X + 1;
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

            buffer.Put(valueX + col, valueY, rune, Theme.Title);
            col += width;
        }

        buffer.Put(box.X + box.Width - 2, valueY, new Rune('▾'), Theme.Title);
    }

    private void PaintDropdown(CellBuffer buffer, Rect dialog, int y)
    {
        if (_projects.Length == 0)
        {
            return;
        }

        Rect box = FieldRect(dialog, y - 3);
        int height = Math.Max(0, dialog.Y + dialog.Height - 3 - y);
        var clip = new Rect(box.X, y, box.Width, height);
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
        buffer.Fill(new Rect(x, y, label.Length + 4, 1), style);
        buffer.Put(x + 2, y, label, style);
    }

    private static Rect DialogRect(CellBuffer buffer)
    {
        int width = Math.Clamp(buffer.Width - 4, 2, 60);
        int height = Math.Clamp(buffer.Height - 4, 2, 18);
        int x = Math.Max(0, (buffer.Width - width) / 2);
        int y = Math.Max(0, (buffer.Height - height) / 2);
        return new Rect(x, y, width, height);
    }

    private static Rect FieldRect(Rect dialog, int y)
    {
        int x = dialog.X + 2;
        int width = Math.Max(4, dialog.Width - 4);
        return new Rect(x, y, width, 3);
    }

    private static void PaintOutline(CellBuffer buffer, Rect box, string label, bool focused, bool required)
    {
        if (box.Width < 2 || box.Height < 3)
        {
            return;
        }

        Style border = focused ? Theme.OutlineFocus : Theme.Label;
        int x0 = box.X;
        int y0 = box.Y;
        int x1 = box.X + box.Width - 1;
        int y1 = box.Y + 2;

        buffer.Put(x0, y0, new Rune('╭'), border);
        buffer.Put(x1, y0, new Rune('╮'), border);
        buffer.Put(x0, y1, new Rune('╰'), border);
        buffer.Put(x1, y1, new Rune('╯'), border);
        buffer.Put(x0, y0 + 1, new Rune('│'), border);
        buffer.Put(x1, y0 + 1, new Rune('│'), border);

        for (int x = x0 + 1; x < x1; x++)
        {
            buffer.Put(x, y0, new Rune('─'), border);
            buffer.Put(x, y1, new Rune('─'), border);
        }

        int col = x0 + 1;
        if (col < x1)
        {
            buffer.Put(col, y0, new Rune('─'), border);
            col++;
        }

        if (col < x1)
        {
            buffer.Put(col, y0, new Rune(' '), border);
            col++;
        }

        foreach (Rune rune in label.EnumerateRunes())
        {
            int width = Cell.WidthOf(rune);
            if (width <= Cell.ZeroWidth)
            {
                continue;
            }

            if (col + width >= x1)
            {
                break;
            }

            buffer.Put(col, y0, rune, border);
            col += width;
        }

        if (col < x1)
        {
            buffer.Put(col, y0, new Rune(' '), border);
            col++;
        }

        while (col < x1)
        {
            buffer.Put(col, y0, new Rune('─'), border);
            col++;
        }

        if (required)
        {
            ReadOnlySpan<char> mark = "( *)";
            int start = x1 - 2 - mark.Length;
            if (start > x0)
            {
                buffer.Put(start, y1, mark, border);
            }
        }
    }
}

public sealed record ProjectsLoaded(int Generation, string[] Names);

public sealed record ProjectsFailed(int Generation, string Error);
