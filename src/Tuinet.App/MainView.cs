namespace Tuinet;

public sealed class MainView
{
    private readonly GitBranch[] _branches;
    private readonly string _directory;
    private int _sel;
    private int _scroll;

    public MainView(string directory, IGitBranches git)
    {
        _directory = directory;
        _branches = git.ListLocal(directory);
        _sel = Array.FindIndex(_branches, b => b.Current);
        if (_sel < 0)
        {
            _sel = 0;
        }
    }

    public string Title
    {
        get
        {
            string name = System.IO.Path.GetFileName(
                System.IO.Path.GetFullPath(_directory).TrimEnd(System.IO.Path.DirectorySeparatorChar));
            return string.IsNullOrEmpty(name) ? "tuinet" : name;
        }
    }

    public bool Handle(KeyEvent key)
    {
        if (IsOptionsKey(key))
        {
            return true;
        }

        if (key.IsChar('j') || key.Code == KeyCode.Down)
        {
            Move(1);
        }
        else if (key.IsChar('k') || key.Code == KeyCode.Up)
        {
            Move(-1);
        }

        return false;
    }

    public void Paint(CellBuffer buffer)
    {
        int top = 2;
        int vis = Math.Max(1, buffer.Height - 4);
        if (_sel < _scroll)
        {
            _scroll = _sel;
        }

        if (_sel >= _scroll + vis)
        {
            _scroll = _sel - vis + 1;
        }

        if (_branches.Length == 0)
        {
            buffer.Put(2, top, "No local branches.", Theme.Status);
        }
        else
        {
            for (int row = 0; row < vis && _scroll + row < _branches.Length; row++)
            {
                int index = _scroll + row;
                GitBranch branch = _branches[index];
                Style style = index == _sel ? Theme.ListCursor : Theme.Title;
                string mark = branch.Current ? "* " : "  ";
                buffer.Put(2, top + row, mark + branch.Name, style);
            }
        }

        buffer.Put(2, buffer.Height - 2, "j/k move  o options  q quit", Theme.Status);
    }

    private void Move(int delta)
    {
        if (_branches.Length == 0)
        {
            return;
        }

        _sel = Math.Clamp(_sel + delta, 0, _branches.Length - 1);
    }

    private static bool IsOptionsKey(KeyEvent key) =>
        key.Code == KeyCode.Char
        && (key.Rune.Value is 'o' or 'O')
        && (key.Modifiers & Modifiers.Ctrl) == 0;
}
