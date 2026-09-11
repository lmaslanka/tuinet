using System.Text;
using Tuinet;

namespace Tuinet.App.Tests;

public class FieldTests
{
    [Fact]
    public void Insert_appends_at_caret()
    {
        var field = new Field();
        field.Handle(new KeyEvent(KeyCode.Char, new Rune('a')));
        field.Handle(new KeyEvent(KeyCode.Char, new Rune('b')));
        Assert.Equal("ab", field.Text);
        Assert.Equal(2, field.Caret);
    }

    [Fact]
    public void Backspace_deletes_before_caret()
    {
        var field = new Field();
        field.Set("ab");
        field.Handle(new KeyEvent(KeyCode.Backspace));
        Assert.Equal("a", field.Text);
    }

    [Fact]
    public void Masked_display_is_bullets_not_secret()
    {
        var field = new Field(masked: true);
        field.Set("secret");
        Assert.Equal("secret", field.Text);
        Assert.Equal("••••••", field.Display);
    }
}
