using System.Text;
using Tuinet;

namespace TuinetTests;

public class AppTests
{
    [Fact]
    public void Main_window_has_a_border()
    {
        var app = Create();
        var buffer = Paint(app);
        Assert.Equal(new Rune('┌'), buffer[0, 0].Rune);
        Assert.Equal(new Rune('┐'), buffer[79, 0].Rune);
        Assert.Equal(new Rune('└'), buffer[0, 23].Rune);
        Assert.Equal(new Rune('┘'), buffer[79, 23].Rune);
    }

    [Fact]
    public void Options_are_closed_on_start()
    {
        var app = Create();
        var buffer = Paint(app);
        Assert.False(Contains(buffer, "Organization"));
        Assert.True(Contains(buffer, "main"));
    }

    [Fact]
    public void O_opens_options_over_the_branch_list()
    {
        var app = Create();
        app.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, new Rune('o'))));
        var buffer = Paint(app);
        Assert.True(Contains(buffer, "Organization"));
        Assert.True(Contains(buffer, "main"));
        Assert.True(Contains(buffer, "topic"));
    }

    [Fact]
    public void Escape_closes_options_and_keeps_branches()
    {
        var app = Create();
        app.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, new Rune('o'))));
        app.Handle(Event.FromKey(new KeyEvent(KeyCode.Escape)));
        var buffer = Paint(app);
        Assert.False(Contains(buffer, "Organization"));
        Assert.True(Contains(buffer, "main"));
    }

    [Fact]
    public void O_reopens_options()
    {
        var app = Create();
        app.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, new Rune('o'))));
        app.Handle(Event.FromKey(new KeyEvent(KeyCode.Escape)));
        app.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, new Rune('O'))));
        Assert.True(Contains(Paint(app), "Organization"));
    }

    [Fact]
    public void Q_quits_from_main_and_from_options()
    {
        var app = Create();
        Assert.False(app.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, new Rune('q')))));
        app.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, new Rune('o'))));
        Assert.False(app.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, new Rune('q')))));
    }

    private static CellBuffer Paint(App app)
    {
        var buffer = new CellBuffer(80, 24);
        app.Paint(buffer);
        return buffer;
    }

    private static bool Contains(CellBuffer buffer, string text)
    {
        for (int y = 0; y < buffer.Height; y++)
        {
            if (Row(buffer, y).Contains(text))
            {
                return true;
            }
        }

        return false;
    }

    private static string Row(CellBuffer buffer, int y)
    {
        var builder = new StringBuilder(buffer.Width);
        for (int x = 0; x < buffer.Width; x++)
        {
            builder.Append(buffer[x, y].Rune);
        }

        return builder.ToString();
    }

    private static App Create()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        return new App(
            new FakeAzure([]),
            new SettingsStore(path),
            _ => { },
            ".",
            new FakeGit([new GitBranch("main", true), new GitBranch("topic", false)]),
            work => work());
    }

    private sealed class FakeAzure : IAzureProjects
    {
        private readonly string[] _names;

        public FakeAzure(string[] names) => _names = names;

        public string[] ListProjects(string organization, string pat) => _names;
    }

    private sealed class FakeGit : IGitBranches
    {
        private readonly GitBranch[] _branches;

        public FakeGit(GitBranch[] branches) => _branches = branches;

        public GitBranch[] ListLocal(string directory) => _branches;
    }
}
