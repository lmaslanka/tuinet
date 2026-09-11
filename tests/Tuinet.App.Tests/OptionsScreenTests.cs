using System.Text;
using Tuinet;

namespace Tuinet.App.Tests;

public class OptionsScreenTests
{
    [Fact]
    public void Main_window_has_a_border()
    {
        var screen = Create(out _, out _);
        var buffer = Paint(screen);
        Assert.Equal(new Rune('┌'), buffer[0, 0].Rune);
        Assert.Equal(new Rune('┐'), buffer[79, 0].Rune);
        Assert.Equal(new Rune('└'), buffer[0, 23].Rune);
        Assert.Equal(new Rune('┘'), buffer[79, 23].Rune);
    }

    [Fact]
    public void Options_are_closed_on_start()
    {
        var screen = Create(out _, out _);
        Assert.False(Contains(Paint(screen), "Organization:"));
    }

    [Fact]
    public void Local_branches_are_listed_on_main()
    {
        var screen = Create(out _, out _);
        var buffer = Paint(screen);
        Assert.True(Contains(buffer, "main"));
        Assert.True(Contains(buffer, "topic"));
    }

    [Fact]
    public void Typed_org_appears_in_the_field_bar()
    {
        var screen = Create(out _, out _);
        OpenOptions(screen);
        Type(screen, "acme");
        Assert.True(Contains(Paint(screen), "acme"));
    }

    [Fact]
    public void Pat_is_painted_as_bullets()
    {
        var screen = Create(out _, out _);
        OpenOptions(screen);
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Tab)));
        Type(screen, "secret");
        var buffer = Paint(screen);
        Assert.True(Contains(buffer, "••••••"));
        Assert.False(Contains(buffer, "secret"));
    }

    [Fact]
    public void Shift_tab_moves_to_close_button()
    {
        var screen = Create(out _, out _);
        OpenOptions(screen);
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Tab, default, Modifiers.Shift)));
        var buffer = Paint(screen);
        (int x, int y) = Find(buffer, "Close");
        Assert.Equal(Color.FromRgb(0, 200, 200), buffer[x, y].Style.Background);
    }

    [Fact]
    public void Q_on_org_quits()
    {
        var screen = Create(out _, out _);
        OpenOptions(screen);
        Assert.False(screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, new Rune('q')))));
    }

    [Fact]
    public void Enter_on_save_persists_and_closes_dialog()
    {
        var screen = Create(out Queue<object> mailbox, out SettingsStore store);
        OpenOptions(screen);
        Type(screen, "acme");
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Tab)));
        Type(screen, "token");
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Enter)));
        Drain(screen, mailbox);

        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Enter)));
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Down)));
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Enter)));
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Tab)));
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Enter)));

        Settings loaded = store.Load();
        Assert.Equal("acme", loaded.Organization);
        Assert.Equal("token", loaded.Pat);
        Assert.Equal("Beta", loaded.Project);
        Assert.False(Contains(Paint(screen), "Organization:"));
    }

    [Fact]
    public void Close_dismisses_dialog_without_saving()
    {
        var screen = Create(out _, out SettingsStore store);
        OpenOptions(screen);
        Type(screen, "acme");
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Tab, default, Modifiers.Shift)));
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Enter)));

        Assert.False(Contains(Paint(screen), "Organization:"));
        Assert.Equal("", store.Load().Organization);
    }

    [Fact]
    public void O_reopens_options_dialog()
    {
        var screen = Create(out _, out _);
        Assert.False(Contains(Paint(screen), "Organization:"));

        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, new Rune('o'))));
        Assert.True(Contains(Paint(screen), "Organization:"));

        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Tab, default, Modifiers.Shift)));
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Enter)));
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, new Rune('O'))));
        Assert.True(Contains(Paint(screen), "Organization:"));
    }

    private static CellBuffer Paint(OptionsScreen screen)
    {
        var buffer = new CellBuffer(80, 24);
        screen.Paint(buffer);
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

    private static OptionsScreen Create(out Queue<object> mailbox, out SettingsStore store)
    {
        mailbox = new Queue<object>();
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        store = new SettingsStore(path);
        Queue<object> box = mailbox;
        var azure = new FakeAzure(["Alpha", "Beta"]);
        return new OptionsScreen(
            azure,
            store,
            box.Enqueue,
            ".",
            new FakeGit([new GitBranch("main", true), new GitBranch("topic", false)]),
            work => work());
    }

    private static void OpenOptions(OptionsScreen screen) =>
        screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, new Rune('o'))));

    private static void Type(OptionsScreen screen, string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
            screen.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, rune)));
    }

    private static void Drain(OptionsScreen screen, Queue<object> mailbox)
    {
        Assert.True(mailbox.Count > 0, "expected a posted fetch result");
        while (mailbox.Count > 0)
            screen.Handle(Event.FromMessage(mailbox.Dequeue()));
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
