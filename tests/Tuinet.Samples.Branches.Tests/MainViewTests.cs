using System.Text;
using Tuinet;
using Tuinet.Samples.Branches;

namespace Tuinet.Samples.Branches.Tests;

public class MainViewTests
{
    [Fact]
    public void Local_branches_are_listed()
    {
        var view = Create();
        var buffer = Paint(view);
        Assert.True(Contains(buffer, "main"));
        Assert.True(Contains(buffer, "topic"));
    }

    [Fact]
    public void Jk_moves_the_branch_cursor()
    {
        var view = Create();
        Color cursor = Color.Rgb(200, 200, 200);

        var buffer = Paint(view);
        (_, int mainY) = Find(buffer, "main");
        (_, int topicY) = Find(buffer, "topic");
        Assert.Equal(cursor, buffer[2, mainY].Style.Bg);
        Assert.NotEqual(cursor, buffer[2, topicY].Style.Bg);

        view.Handle(new KeyEvent(KeyCode.Char, new Rune('j')));
        buffer = Paint(view);
        (_, mainY) = Find(buffer, "main");
        (_, topicY) = Find(buffer, "topic");
        Assert.Equal(cursor, buffer[2, topicY].Style.Bg);
        Assert.NotEqual(cursor, buffer[2, mainY].Style.Bg);

        view.Handle(new KeyEvent(KeyCode.Char, new Rune('k')));
        buffer = Paint(view);
        (_, mainY) = Find(buffer, "main");
        Assert.Equal(cursor, buffer[2, mainY].Style.Bg);
    }

    [Fact]
    public void O_requests_options()
    {
        var view = Create();
        Assert.True(view.Handle(new KeyEvent(KeyCode.Char, new Rune('o'))));
        Assert.True(view.Handle(new KeyEvent(KeyCode.Char, new Rune('O'))));
        Assert.False(view.Handle(new KeyEvent(KeyCode.Char, new Rune('j'))));
    }

    private static MainView Create() =>
        new(".", new FakeGit([new GitBranch("main", true), new GitBranch("topic", false)]));

    private static CellBuffer Paint(MainView view)
    {
        var buffer = new CellBuffer(80, 24);
        view.Render(buffer, buffer.Area);
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

    private static (int x, int y) Find(CellBuffer buffer, string text)
    {
        for (int y = 0; y < buffer.Height; y++)
        {
            int x = Row(buffer, y).IndexOf(text, StringComparison.Ordinal);
            if (x >= 0)
            {
                return (x, y);
            }
        }

        throw new InvalidOperationException($"text '{text}' not found in buffer");
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

    private sealed class FakeGit : IGitBranches
    {
        private readonly GitBranch[] _branches;

        public FakeGit(GitBranch[] branches) => _branches = branches;

        public GitBranch[] ListLocal(string directory) => _branches;
    }
}
