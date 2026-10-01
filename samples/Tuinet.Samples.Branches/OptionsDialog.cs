using System.Text;
using Tuinet.Widgets;

namespace Tuinet.Samples.Branches;

/// <summary>Azure DevOps settings form: organization, PAT, project picker, Save / Close.</summary>
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
    private TextInputState _org = new();
    private TextInputState _pat = new(mask: '•');
    private ListState _dropdown;
    private int _focus;
    private bool _dropdownOpen;
    private string[] _projects = [];
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

        if (!_org.IsEmpty && !_pat.IsEmpty)
        {
            StartFetch();
        }
    }

    /// <summary>Returns false when the dialog should close.</summary>
    public bool Handle(Event ev)
    {
        switch (ev.Kind)
        {
            case EventKind.Message:
                HandleMessage(ev.Message);
                return true;
            case EventKind.Paste when FocusedField() is { } field:
                field.Insert(ev.Paste);
                return true;
            case EventKind.Key:
                return HandleKey(ev.Key);
            default:
                return true;
        }
    }

    public void Prepare() => _dropdownOpen = false;

    public void Render(CellBuffer buffer)
    {
        Rect dialog = buffer.Area.Centered(Math.Min(buffer.Width - 4, 60), Math.Min(buffer.Height - 4, 18));
        buffer.Render(new Clear(Theme.Screen), dialog);
        buffer.Render(new Block { Title = "Options", BorderStyle = Theme.Title }, dialog);

        int y = dialog.Y + 2;
        RenderInput(buffer, FieldRect(dialog, y), " Organization ", ref _org, _focus == Org);
        RenderInput(buffer, FieldRect(dialog, y + 3), " PAT ", ref _pat, _focus == Pat);
        RenderProject(buffer, FieldRect(dialog, y + 6));

        int buttonsY = dialog.Bottom - 3;
        buffer.Render(new Button("Save") { Style = Theme.FieldIdle, FocusedStyle = Theme.FieldFocus, Focused = _focus == Save }, new Rect(dialog.X + 3, buttonsY, 10, 1));
        buffer.Render(new Button("Close") { Style = Theme.FieldIdle, FocusedStyle = Theme.FieldFocus, Focused = _focus == Close }, new Rect(dialog.X + 15, buttonsY, 10, 1));
        buffer.SetString(dialog.X + 2, dialog.Bottom - 2, _status, Theme.Status, dialog.Width - 4, Overflow.Ellipsis);

        if (_dropdownOpen && _projects.Length > 0)
        {
            Rect list = new(dialog.X + 2, y + 9, dialog.Width - 4, Math.Max(0, buttonsY - (y + 9)));
            buffer.Render(new Clear(Theme.FieldIdle), list);
            buffer.Render(new ListView<TextItems>(new TextItems(_projects, Theme.FieldIdle)) { SelectedStyle = Theme.ListCursor }, list.Inset(1, 0), ref _dropdown);
        }
    }

    private static Rect FieldRect(Rect dialog, int y) => new(dialog.X + 2, y, Math.Max(4, dialog.Width - 4), 3);

    private static Block Outline(ReadOnlySpan<char> label, bool focused) => new()
    {
        BorderType = BorderType.Rounded,
        BorderStyle = focused ? Theme.OutlineFocus : Theme.Label,
        Title = label,
        Footer = "( *)",
        FooterAlignment = Alignment.Right,
    };

    private static void RenderInput(CellBuffer buffer, Rect box, ReadOnlySpan<char> label, ref TextInputState state, bool focused)
    {
        Block outline = Outline(label, focused);
        buffer.Render(outline, box);
        buffer.Render(new TextInput { Style = Theme.Title, Focused = focused }, outline.Inner(box), ref state);
    }

    private void RenderProject(CellBuffer buffer, Rect box)
    {
        Block outline = Outline(" Project ", _focus == Project);
        buffer.Render(outline, box);
        Rect inner = outline.Inner(box);
        string text = _selectedProject.Length == 0 ? "select a project" : _selectedProject;
        buffer.SetString(inner.X, inner.Y, text, Theme.Title, inner.Width - 2, Overflow.Ellipsis);
        buffer.SetRune(inner.Right - 1, inner.Y, new Rune('▾'), Theme.Title);
    }

    private TextInputState? FocusedField() => _focus switch
    {
        Org => _org,
        Pat => _pat,
        _ => null,
    };

    private void HandleMessage(object? message)
    {
        switch (message)
        {
            case ProjectsLoaded loaded when loaded.Generation == _fetchGen:
                _projects = loaded.Names;
                _dropdown = new ListState(Math.Max(0, Array.FindIndex(_projects, p =>
                    string.Equals(p, _selectedProject, StringComparison.OrdinalIgnoreCase))));
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
        if (key.Is(KeyCode.Escape))
        {
            if (_dropdownOpen)
            {
                _dropdownOpen = false;
                return true;
            }

            return false;
        }

        if (key.Code == KeyCode.Tab)
        {
            _dropdownOpen = false;
            CycleFocus((key.Modifiers & Modifiers.Shift) != 0 ? -1 : 1);
            if (_focus == Project && _projects.Length == 0)
            {
                StartFetch();
            }

            return true;
        }

        if (_dropdownOpen)
        {
            HandleDropdown(key);
            return true;
        }

        switch (_focus)
        {
            case Org or Pat when key.Is(KeyCode.Enter):
                if (_focus == Pat)
                {
                    StartFetch();
                }

                CycleFocus(1);
                return true;
            case Org or Pat:
                FocusedField()!.Handle(key);
                return true;
            case Project when key.Is(KeyCode.Enter):
                if (_projects.Length == 0)
                {
                    StartFetch();
                }
                else
                {
                    _dropdownOpen = true;
                }

                return true;
            case Save or Close when key.Is(KeyCode.Enter):
                if (_focus == Save)
                {
                    Persist();
                }

                return false;
            default:
                return true;
        }
    }

    private void HandleDropdown(KeyEvent key)
    {
        if (key.IsChar('j') || key.Is(KeyCode.Down))
        {
            _dropdown.Next(_projects.Length);
        }
        else if (key.IsChar('k') || key.Is(KeyCode.Up))
        {
            _dropdown.Previous(_projects.Length);
        }
        else if (key.Is(KeyCode.Enter) && _dropdown.Selected >= 0 && _projects.Length > 0)
        {
            _selectedProject = _projects[_dropdown.Selected];
            _dropdownOpen = false;
            _status = $"Selected {_selectedProject}";
        }
    }

    private void CycleFocus(int delta) => _focus = (_focus + delta + FocusCount) % FocusCount;

    private void Persist()
    {
        _store.Save(new Settings
        {
            Organization = _org.Text,
            Pat = _pat.Text,
            Project = _selectedProject,
        });
        _status = _selectedProject.Length == 0 ? "Saved" : $"Saved {_selectedProject}";
    }

    private void StartFetch()
    {
        if (_org.IsEmpty || _pat.IsEmpty)
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
                _post(new ProjectsLoaded(gen, _azure.ListProjects(org, pat)));
            }
            catch (Exception ex)
            {
                _post(new ProjectsFailed(gen, ex.Message));
            }
        });
    }

    private static void StartBackground(Action work) =>
        new Thread(() => work()) { IsBackground = true }.Start();
}

public sealed record ProjectsLoaded(int Generation, string[] Names);

public sealed record ProjectsFailed(int Generation, string Error);
