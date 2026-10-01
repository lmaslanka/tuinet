using Tuinet.Widgets;

namespace Tuinet.Samples.Branches;

public sealed class MainView
{
    private readonly GitBranch[] _branches;
    private ListState _list;

    public MainView(string directory, IGitBranches git)
    {
        _branches = git.ListLocal(directory);
        _list = new ListState(Math.Max(0, Array.FindIndex(_branches, b => b.Current)));

        string name = Path.GetFileName(Path.GetFullPath(directory).TrimEnd(Path.DirectorySeparatorChar));
        Title = string.IsNullOrEmpty(name) ? "tuinet" : name;
    }

    public string Title { get; }

    /// <summary>Returns true when the user asked for the options dialog.</summary>
    public bool Handle(KeyEvent key)
    {
        if (key.IsChar('o') || key.IsChar('O'))
        {
            return true;
        }

        if (key.IsChar('j') || key.Is(KeyCode.Down))
        {
            _list.Next(_branches.Length);
        }
        else if (key.IsChar('k') || key.Is(KeyCode.Up))
        {
            _list.Previous(_branches.Length);
        }
        else if (key.Is(KeyCode.PageDown))
        {
            _list.PageDown(_branches.Length);
        }
        else if (key.Is(KeyCode.PageUp))
        {
            _list.PageUp(_branches.Length);
        }
        else if (key.IsChar('g') || key.Is(KeyCode.Home))
        {
            _list.First(_branches.Length);
        }
        else if (key.IsChar('G') || key.Is(KeyCode.End))
        {
            _list.Last(_branches.Length);
        }

        return false;
    }

    public void Render(CellBuffer buffer, Rect area)
    {
        Rect list = new(area.X + 2, area.Y + 2, area.Width - 4, area.Height - 4);
        if (_branches.Length == 0)
        {
            buffer.SetString(list.X, list.Y, "No local branches.", Theme.Status, list.Width);
        }
        else
        {
            buffer.Render(new ListView<BranchItems>(new BranchItems(_branches)) { SelectedStyle = Theme.ListCursor }, list, ref _list);
        }

        buffer.SetString(area.X + 2, area.Bottom - 2, "j/k move  o options  q quit", Theme.Status, area.Width - 4);
    }

    private readonly struct BranchItems(GitBranch[] branches) : IListSource
    {
        public int Count => branches.Length;

        public void RenderItem(int index, Rect area, CellBuffer buffer, bool selected)
        {
            GitBranch branch = branches[index];
            int x = buffer.SetString(area.X, area.Y, branch.Current ? "* " : "  ", Theme.Title, area.Width);
            buffer.SetString(x, area.Y, branch.Name, Theme.Title, area.Right - x, Overflow.Ellipsis);
        }
    }
}
