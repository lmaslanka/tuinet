using System.Text;
using Tuinet;

namespace Tuinet.App.Tests;

public class OptionsDialogTests
{
    [Fact]
    public void Typed_org_appears_in_the_field_bar()
    {
        var dialog = Create(out _, out _);
        Type(dialog, "acme");
        Assert.True(Contains(Paint(dialog), "acme"));
    }

    [Fact]
    public void Label_sits_on_the_top_border()
    {
        var dialog = Create(out _, out _);
        Type(dialog, "acme");
        var buffer = Paint(dialog);
        (int x, int y) = Find(buffer, "Organization");
        Assert.Equal(new Rune('╭'), buffer[x - 3, y].Rune);
        (_, int valueY) = Find(buffer, "acme");
        Assert.Equal(y + 1, valueY);
    }

    [Fact]
    public void Required_mark_sits_on_the_bottom_border()
    {
        Assert.True(Contains(Paint(Create(out _, out _)), "( *)"));
    }

    [Fact]
    public void Focused_outline_uses_cyan_foreground_not_fill()
    {
        var buffer = Paint(Create(out _, out _));
        (int x, int y) = Find(buffer, "Organization");
        Cell corner = buffer[x - 3, y];
        Assert.Equal(new Rune('╭'), corner.Rune);
        Assert.Equal(Color.FromRgb(0, 200, 200), corner.Style.Foreground);
        Assert.NotEqual(Color.FromRgb(0, 200, 200), corner.Style.Background);
    }

    [Fact]
    public void Project_shows_a_chevron_inside_the_box()
    {
        Assert.True(Contains(Paint(Create(out _, out _)), "▾"));
    }

    [Fact]
    public void Pat_is_painted_as_bullets()
    {
        var dialog = Create(out _, out _);
        dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Tab)));
        Type(dialog, "secret");
        var buffer = Paint(dialog);
        Assert.True(Contains(buffer, "••••••"));
        Assert.False(Contains(buffer, "secret"));
    }

    [Fact]
    public void Shift_tab_moves_to_close_button()
    {
        var dialog = Create(out _, out _);
        dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Tab, default, Modifiers.Shift)));
        var buffer = Paint(dialog);
        (int x, int y) = Find(buffer, "Close");
        Assert.Equal(Color.FromRgb(0, 200, 200), buffer[x, y].Style.Background);
    }

    [Fact]
    public void Enter_on_save_persists_and_closes()
    {
        var dialog = Create(out Queue<object> mailbox, out SettingsStore store);
        Type(dialog, "acme");
        dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Tab)));
        Type(dialog, "token");
        dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Enter)));
        Drain(dialog, mailbox);

        dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Enter)));
        dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Down)));
        dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Enter)));
        dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Tab)));
        Assert.False(dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Enter))));

        Settings loaded = store.Load();
        Assert.Equal("acme", loaded.Organization);
        Assert.Equal("token", loaded.Pat);
        Assert.Equal("Beta", loaded.Project);
    }

    [Fact]
    public void Close_dismisses_without_saving()
    {
        var dialog = Create(out _, out SettingsStore store);
        Type(dialog, "acme");
        dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Tab, default, Modifiers.Shift)));
        Assert.False(dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Enter))));
        Assert.Equal("", store.Load().Organization);
    }

    [Fact]
    public void Escape_closes()
    {
        var dialog = Create(out _, out _);
        Assert.False(dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Escape))));
    }

    private static CellBuffer Paint(OptionsDialog dialog)
    {
        var buffer = new CellBuffer(80, 24);
        dialog.Paint(buffer);
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

    private static OptionsDialog Create(out Queue<object> mailbox, out SettingsStore store)
    {
        mailbox = new Queue<object>();
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        store = new SettingsStore(path);
        Queue<object> box = mailbox;
        return new OptionsDialog(new FakeAzure(["Alpha", "Beta"]), store, box.Enqueue, work => work());
    }

    private static void Type(OptionsDialog dialog, string text)
    {
        foreach (Rune rune in text.EnumerateRunes())
            dialog.Handle(Event.FromKey(new KeyEvent(KeyCode.Char, rune)));
    }

    private static void Drain(OptionsDialog dialog, Queue<object> mailbox)
    {
        Assert.True(mailbox.Count > 0, "expected a posted fetch result");
        while (mailbox.Count > 0)
            dialog.Handle(Event.FromMessage(mailbox.Dequeue()));
    }

    private sealed class FakeAzure : IAzureProjects
    {
        private readonly string[] _names;

        public FakeAzure(string[] names) => _names = names;

        public string[] ListProjects(string organization, string pat) => _names;
    }
}
