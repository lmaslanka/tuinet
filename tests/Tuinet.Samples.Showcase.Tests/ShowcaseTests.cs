using Tuinet;
using Tuinet.Samples.Showcase;

namespace Tuinet.Samples.Showcase.Tests;

public class ShowcaseTests
{
    [Fact]
    public void Main_screen_lists_twenty_items_with_details()
    {
        var app = new ShowcaseApp();
        string screen = Render(app).ToString();
        Assert.Equal(20, app.Items.Count);
        Assert.Contains("ITEMS · 20", screen);
        Assert.Contains("▲ #  name", screen);
        Assert.Contains("01  parse & validate inte…  feature  ▲ low", screen);
        Assert.Contains("20  telemetry export", screen);
        Assert.Contains("SELECTED · 01", screen);
    }

    [Fact]
    public void Shows_the_title()   // the README's testing example, kept honest
    {
        var buffer = new CellBuffer(80, 24);
        new ShowcaseApp().Render(buffer, nowMs: 0);
        Assert.Contains("ITEMS · 20", buffer.ToString());
        Assert.Equal(Color.Hex(0xF5A623), buffer[2, 4].Style.Fg);   // the list's amber border
    }

    [Fact]
    public void J_and_k_move_the_selection()
    {
        var app = new ShowcaseApp();
        Press(app, 'j', 'j', 'j', 'k');
        Assert.Equal(2, app.Selected);
        Assert.Contains("SELECTED · 03", Render(app).ToString());
    }

    [Fact]
    public void Enter_opens_the_edit_form_with_all_controls()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);
        string screen = Render(app).ToString();
        foreach (string label in (string[])["EDIT ITEM · 01", "name", "owner", "description", "kind", "priority", "enabled", "notify on finish", "Save", "Close"])
        {
            Assert.Contains(label, screen);
        }
    }

    [Fact]
    public void Focused_text_box_shows_the_real_cursor()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);
        CellBuffer buffer = Render(app);
        Assert.True(buffer.CursorVisible);
        Assert.Contains("parse & validate intent", buffer.RowText(buffer.CursorY));
    }

    [Fact]
    public void Editing_all_controls_and_saving_updates_the_item()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);
        Key(app, KeyCode.Char, 'u', Modifiers.Ctrl);          // clear name
        Type(app, "renamed");
        Key(app, KeyCode.Tab);
        Key(app, KeyCode.Char, 'u', Modifiers.Ctrl);
        Type(app, "zoe");
        Key(app, KeyCode.Tab);                                  // description: keep
        Key(app, KeyCode.Tab);                                  // kind dropdown
        Key(app, KeyCode.Enter);
        Key(app, KeyCode.Down);
        Key(app, KeyCode.Enter);                                // feature → bugfix
        Key(app, KeyCode.Tab);                                  // priority dropdown
        Key(app, KeyCode.Enter);
        Key(app, KeyCode.End);
        Key(app, KeyCode.Enter);                                // → critical
        Key(app, KeyCode.Tab);
        Key(app, KeyCode.Char, ' ');                            // enabled off
        Key(app, KeyCode.Tab);
        Key(app, KeyCode.Enter);                                // notify toggled
        Key(app, KeyCode.Tab);
        Key(app, KeyCode.Enter);                                // Save

        Item item = app.Items[0];
        Assert.Null(app.Edit);
        Assert.Equal("renamed", item.Name);
        Assert.Equal("zoe", item.Owner);
        Assert.Equal(1, item.Kind);
        Assert.Equal(3, item.Priority);
        Assert.False(item.Enabled);
        Assert.False(item.Notify);
        Assert.Contains("saved · renamed", Render(app).ToString());
    }

    [Fact]
    public void Close_and_escape_discard_changes()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);
        Type(app, "XYZ");
        Key(app, KeyCode.Escape);
        Assert.Null(app.Edit);
        Assert.Equal("parse & validate intent", app.Items[0].Name);

        Key(app, KeyCode.Enter);
        Type(app, "XYZ");
        Key(app, KeyCode.Tab, Modifiers.Shift);                 // Close button
        Key(app, KeyCode.Enter);
        Assert.Null(app.Edit);
        Assert.Equal("parse & validate intent", app.Items[0].Name);
    }

    [Fact]
    public void Escape_in_an_open_dropdown_only_closes_the_dropdown()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);
        for (int i = 0; i < 3; i++)
        {
            Key(app, KeyCode.Tab);
        }

        Key(app, KeyCode.Enter);
        Assert.Contains("spike", Render(app).ToString());
        Key(app, KeyCode.Escape);
        Assert.NotNull(app.Edit);
        Key(app, KeyCode.Escape);
        Assert.Null(app.Edit);
    }

    [Fact]
    public void Empty_name_is_rejected()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);
        Key(app, KeyCode.Char, 'u', Modifiers.Ctrl);
        Key(app, KeyCode.Char, 's', Modifiers.Ctrl);
        Assert.NotNull(app.Edit);
        Assert.Equal(EditDialog.Name, app.Edit!.Focus);
        Assert.Contains("name is required", Render(app).ToString());
    }

    [Fact]
    public void Paste_goes_into_the_focused_text_box()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);
        Key(app, KeyCode.Char, 'u', Modifiers.Ctrl);
        app.Handle(Event.FromPaste("pasted\nname"), 0);
        Key(app, KeyCode.Char, 's', Modifiers.Ctrl);
        Assert.Equal("pastedname", app.Items[0].Name);
    }

    [Fact]
    public void Progress_dialog_animates_and_finishes()
    {
        var app = new ShowcaseApp();
        app.Handle(Event.FromChar('p'), 1000);
        Assert.True(app.ProgressOpen);
        Assert.True(app.IsAnimating(1000));

        string early = Render(app, 2000).ToString();
        Assert.Contains("PIPELINE · progress", early);
        Assert.Contains("running", early);
        Assert.Contains("░", early);

        string done = Render(app, 60_000).ToString();
        Assert.False(app.IsAnimating(60_000));
        Assert.Contains("✓ done", done);
        Assert.Contains("0.92 sharp", done);
        Assert.Contains("100.0%", done);

        app.Handle(Event.FromKey(KeyCode.Escape), 60_000);
        Assert.False(app.ProgressOpen);
    }

    [Fact]
    public void Progress_restart_resets_the_clock()
    {
        var app = new ShowcaseApp();
        app.Handle(Event.FromChar('p'), 0);
        Assert.False(app.IsAnimating(60_000));
        app.Handle(Event.FromChar('r'), 60_000);
        Assert.True(app.IsAnimating(60_001));
    }

    [Fact]
    public void Narrow_screen_drops_whole_columns_instead_of_squeezing_them()
    {
        var buffer = new CellBuffer(80, 24);
        new ShowcaseApp().Render(buffer, nowMs: 0);
        string screen = buffer.ToString();
        Assert.Contains("01  parse & valid…  feature", screen);
        Assert.DoesNotContain("priority", screen);
        Assert.DoesNotContain("fea…", screen);
    }

    [Fact]
    public void Small_terminal_does_not_throw()
    {
        var app = new ShowcaseApp();
        foreach ((int w, int h) in (ReadOnlySpan<(int, int)>)[(1, 1), (20, 5), (40, 12), (80, 24)])
        {
            var buffer = new CellBuffer(w, h);
            app.Render(buffer, 0);
            Key(app, KeyCode.Enter);
            app.Render(buffer, 0);
            Key(app, KeyCode.Escape);
            app.Handle(Event.FromChar('p'), 0);
            app.Render(buffer, 500);
            Key(app, KeyCode.Escape);
        }
    }

    [Fact]
    public void Q_and_ctrl_c_quit()
    {
        Assert.False(new ShowcaseApp().Handle(Event.FromChar('q'), 0));
        Assert.False(new ShowcaseApp().Handle(Event.FromChar('c', Modifiers.Ctrl), 0));
    }

    [Fact]
    public void Clicking_a_row_selects_it()
    {
        var app = new ShowcaseApp();
        (int x, int y) = Find(Render(app), "05  schema");
        Click(app, x, y);
        Assert.Equal(4, app.Selected);
        Assert.Contains("SELECTED · 05", Render(app).ToString());
    }

    [Fact]
    public void Wheel_scrolls_the_table_and_keeps_the_selection()
    {
        var app = new ShowcaseApp();
        var buffer = new CellBuffer(80, 24);                            // 12 rows fit: the table can scroll
        app.Render(buffer, 0);
        (int x, int y) = Find(buffer, "01  parse");
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.ScrollDown, MouseButton.None, x, y, Modifiers.None)), 0);
        app.Render(buffer, 0);
        string screen = buffer.ToString();
        Assert.DoesNotContain("01  parse", screen);
        Assert.Contains("04  verify", screen);
        Assert.Contains("SELECTED · 01", screen);
        Assert.Equal(0, app.Selected);
    }

    [Fact]
    public void Scrollbar_clicks_scroll_and_never_count_as_a_double_click()
    {
        var app = new ShowcaseApp();
        var buffer = new CellBuffer(80, 24);
        app.Render(buffer, 0);
        (int x, int top) = Find(buffer, "feature█");                   // the thumb, beside row 01
        (_, int bottom) = Find(buffer, "12  diff");
        Click(app, x + 7, bottom, 1000);
        app.Render(buffer, 0);
        Assert.Contains("20  telemetry", buffer.ToString());

        Click(app, x + 7, top, 1100);
        Click(app, x + 7, top, 1200);
        app.Render(buffer, 0);
        Assert.Null(app.Edit);
        Assert.Contains("01  parse", buffer.ToString());
        Assert.Equal(0, app.Selected);
    }

    [Fact]
    public void Header_click_sorts_and_a_second_click_reverses()
    {
        var app = new ShowcaseApp();
        (int x, int y) = Find(Render(app), "name  ");
        Click(app, x, y);
        Assert.Equal(1, app.SortColumn);
        string screen = Render(app).ToString();
        Assert.Contains("#  name ▲", screen);
        Assert.Contains("18  advisor hooks", screen);
        Assert.Contains("SELECTED · 01", screen);                       // the same item stays selected
        Assert.Equal("parse & validate intent", app.Items[app.Selected].Name);

        Click(app, x, y);
        Assert.True(app.SortDescending);
        Assert.Contains("#  name ▼", Render(app).ToString());
        Assert.Equal("verify & return diff", app.Items[0].Name);
    }

    [Fact]
    public void Double_click_on_a_row_opens_the_edit_dialog()
    {
        var app = new ShowcaseApp();
        (int x, int y) = Find(Render(app), "03  run");
        Click(app, x, y, 1000);
        Click(app, x, y, 1000 + ShowcaseApp.DoubleClickMs + 1);         // too slow: two single clicks
        Assert.Null(app.Edit);
        Click(app, x, y, 1000 + ShowcaseApp.DoubleClickMs + 101);       // 100 ms after the last one
        Assert.NotNull(app.Edit);
        Assert.Contains("EDIT ITEM · 03", Render(app).ToString());
    }

    [Fact]
    public void Edit_dialog_controls_work_with_the_mouse()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);

        CellBuffer screen = Render(app);
        (int x, int y) = Find(screen, "ada");                           // the owner text
        Click(app, x + 1, y);
        Assert.Equal(EditDialog.Owner, app.Edit!.Focus);
        Type(app, "X");                                                 // caret went between a and da

        screen = Render(app);
        (x, y) = Find(screen, "● feature");
        Click(app, x, y);
        Assert.Equal(EditDialog.Kind, app.Edit.Focus);
        screen = Render(app);
        (x, y) = Find(screen, "● bugfix");                              // in the open popup
        Click(app, x, y);

        screen = Render(app);
        (x, y) = Find(screen, "enabled");
        Click(app, x, y);
        screen = Render(app);
        (x, y) = Find(screen, "Save");
        Click(app, x, y);

        Item item = app.Items[0];
        Assert.Null(app.Edit);
        Assert.Equal("aXda", item.Owner);
        Assert.Equal(1, item.Kind);
        Assert.False(item.Enabled);
    }

    [Fact]
    public void Clicking_outside_an_open_dropdown_closes_it()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);
        (int x, int y) = Find(Render(app), "● feature");
        Click(app, x, y);
        Assert.Contains("● spike", Render(app).ToString());
        Click(app, 0, 0);
        Assert.DoesNotContain("● spike", Render(app).ToString());
        Assert.Equal(0, app.Items[0].Kind);
    }

    /// <summary>Cell of the first occurrence of <paramref name="text"/> (the screen is one column per char here).</summary>
    private static (int X, int Y) Find(CellBuffer buffer, string text)
    {
        for (int y = 0; y < buffer.Height; y++)
        {
            int x = buffer.RowText(y).IndexOf(text, StringComparison.Ordinal);
            if (x >= 0)
            {
                return (x, y);
            }
        }

        throw new Xunit.Sdk.XunitException($"'{text}' is not on screen:\n{buffer}");
    }

    private static void Click(ShowcaseApp app, int x, int y, long now = 0)
    {
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Down, MouseButton.Left, x, y, Modifiers.None)), now);
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Up, MouseButton.Left, x, y, Modifiers.None)), now);
    }

    private static CellBuffer Render(ShowcaseApp app, long now = 0)
    {
        var buffer = new CellBuffer(110, 34);
        app.Render(buffer, now);
        return buffer;
    }

    private static void Press(ShowcaseApp app, params char[] keys)
    {
        foreach (char c in keys)
        {
            app.Handle(Event.FromChar(c), 0);
        }
    }

    private static void Type(ShowcaseApp app, string text)
    {
        foreach (char c in text)
        {
            app.Handle(Event.FromChar(c), 0);
        }
    }

    private static void Key(ShowcaseApp app, KeyCode code, Modifiers modifiers = Modifiers.None) =>
        app.Handle(Event.FromKey(code, modifiers), 0);

    private static void Key(ShowcaseApp app, KeyCode code, char c, Modifiers modifiers = Modifiers.None) =>
        app.Handle(Event.FromChar(c, modifiers), 0);
}
