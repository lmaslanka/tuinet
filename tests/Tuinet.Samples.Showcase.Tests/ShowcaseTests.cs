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
    public void Description_is_multi_line()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);
        Key(app, KeyCode.Tab);
        Key(app, KeyCode.Tab);                                  // description
        Key(app, KeyCode.End);
        Key(app, KeyCode.Enter);                                // a line break, not the next field
        Type(app, "second line");
        app.Handle(Event.FromPaste("\r\npasted"), 0);
        Assert.Equal(EditDialog.Description, app.Edit!.Focus);
        string screen = Render(app).ToString();
        Assert.Contains("second line", screen);
        Assert.Contains("pasted", screen);

        Key(app, KeyCode.Char, 's', Modifiers.Ctrl);
        Assert.Equal("typed intent, schema checks and invariant gates before anything runs\nsecond line\npasted", app.Items[0].Description);
    }

    [Fact]
    public void Ctrl_c_copies_a_selection_and_ctrl_x_cuts_it()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);
        Key(app, KeyCode.Left, Modifiers.Ctrl | Modifiers.Shift);   // select "intent"
        Assert.True(app.Handle(Event.FromChar('c', Modifiers.Ctrl), 0));
        Assert.Equal("intent", app.TakeCopy());

        Assert.True(app.Handle(Event.FromChar('x', Modifiers.Ctrl), 0));
        Assert.Equal("intent", app.TakeCopy());
        Key(app, KeyCode.Char, 's', Modifiers.Ctrl);
        Assert.Equal("parse & validate", app.Items[0].Name);   // trimmed on save

        Key(app, KeyCode.Enter);
        Assert.False(app.Handle(Event.FromChar('c', Modifiers.Ctrl), 0));   // nothing selected: quits
    }

    [Fact]
    public void Dragging_in_a_text_box_selects()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);
        (int x, int y) = Find(Render(app), "ada");
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Down, MouseButton.Left, x, y, Modifiers.None)), 0);
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Drag, MouseButton.Left, x + 2, y, Modifiers.None)), 0);
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Up, MouseButton.Left, x + 2, y, Modifiers.None)), 0);
        Assert.Equal(EditDialog.Owner, app.Edit!.Focus);
        Assert.True(app.Handle(Event.FromChar('c', Modifiers.Ctrl), 0));
        Assert.Equal("ad", app.TakeCopy());
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
    public void Tabs_sit_in_the_panel_border()
    {
        CellBuffer screen = Render(new ShowcaseApp());
        Assert.StartsWith("  ╭─ List ─ Stats ───", screen.RowText(4));
        Assert.Contains("─ ITEMS · 20 ─╮", screen.RowText(4));
    }

    [Fact]
    public void Brackets_and_alt_digits_switch_pages_and_keep_the_selection()
    {
        var app = new ShowcaseApp();
        Press(app, 'j', 'j', ']');
        Assert.Equal(ShowcaseApp.StatsPage, app.Page);
        string stats = Render(app).ToString();
        Assert.Contains("BY KIND", stats);
        Assert.DoesNotContain("▲ #  name", stats);
        Assert.Contains("SELECTED · 03", stats);                // the details panel stays

        Press(app, ']');                                         // wraps
        Assert.Equal(ShowcaseApp.ListPage, app.Page);
        Press(app, '[');
        Assert.Equal(ShowcaseApp.StatsPage, app.Page);
        Key(app, KeyCode.Char, '1', Modifiers.Alt);
        Assert.Equal(ShowcaseApp.ListPage, app.Page);
        Key(app, KeyCode.Char, '9', Modifiers.Alt);              // no ninth page
        Assert.Equal(ShowcaseApp.ListPage, app.Page);
        Assert.Equal(2, app.Selected);
        Assert.Contains("▲ #  name", Render(app).ToString());
    }

    [Fact]
    public void Clicking_a_tab_switches_pages()
    {
        var app = new ShowcaseApp();
        (int x, int y) = Find(Render(app), "Stats");
        Click(app, x + 1, y);
        Assert.Equal(ShowcaseApp.StatsPage, app.Page);
        (x, y) = Find(Render(app), "List");
        Click(app, x, y);
        Assert.Equal(ShowcaseApp.ListPage, app.Page);
    }

    [Fact]
    public void Stats_page_charts_the_items_and_the_frame_cost()
    {
        var app = new ShowcaseApp();
        app.RecordFrame(1234, TimeSpan.FromMicroseconds(56));
        Press(app, ']');
        string screen = Render(app).ToString();
        foreach (string row in (string[])[@"BY KIND +FLAGS", @"   4       4       4       4       4 +■ notify +7 of 20",
            @"▅▅▅▅▅▅▅ ▅▅▅▅▅▅▅ ▅▅▅▅▅▅▅ ▅▅▅▅▅▅▅ ▅▅▅▅▅▅▅", @"feature bugfix   chore   docs    spike", @"■ enabled +17 of 20",
            @"critical ████████████▌ 5", @"BYTES PER FRAME +last 1234 B", @"RENDER \+ DIFF \+ WRITE +last 56 µs"])
        {
            Assert.Matches(row, screen);
        }
    }

    [Fact]
    public void Stats_page_bar_charts_follow_edits()
    {
        var app = new ShowcaseApp();
        app.Items[0].Kind = 4;
        app.Items[1].Kind = 4;
        app.Items[0].Priority = 3;
        Press(app, ']');
        string screen = Render(app).ToString();
        Assert.Contains("│                                    6      ■ enabled", screen);   // each value right above its bar
        Assert.Contains("│    3       3       4       4    ▃▃▃▃▃▃▃", screen);
        Assert.Contains("│ ▂▂▂▂▂▂▂ ▂▂▂▂▂▂▂ ▅▅▅▅▅▅▅ ▅▅▅▅▅▅▅ ███████", screen);
        Assert.Contains("low      ██████████ 4", screen);
        Assert.Contains("critical ███████████████ 6", screen);
    }

    [Fact]
    public void Stats_page_sparkline_shows_the_newest_frame_on_the_right_and_marks_the_peak()
    {
        var app = new ShowcaseApp();
        for (int i = 0; i < ShowcaseApp.FrameHistory + 80; i++)   // wraps the ring
        {
            app.RecordFrame(i == 0 ? 90_000 : i % 2 == 0 ? 400 : 100, TimeSpan.FromMicroseconds(50));
        }

        app.RecordFrame(5000, TimeSpan.FromMicroseconds(50));
        Press(app, ']');
        CellBuffer screen = Render(app);
        (int x, int y) = Find(screen, "last 5000 B");
        int right = x + "last 5000 B".Length - 1;
        int bottom = Find(screen, "RENDER + DIFF").Y - 2;
        Assert.Equal("█", screen[right, bottom].Text);
        Assert.Equal(Color.Hex(0xF5A623), screen[right, bottom].Style.Fg);   // the peak, in amber
        Assert.Equal("█", screen[right, y + 1].Text);                       // full height: the old 90 000 B spike scrolled out
        Assert.Equal("▁", screen[right - 1, bottom].Text);                  // 100 of 5000 rounds to 0 eighths, but shows as ▁
    }

    [Fact]
    public void List_keys_and_clicks_do_nothing_on_the_stats_page()
    {
        var app = new ShowcaseApp();
        (int x, int y) = Find(Render(app), "05  ");             // a row of the list
        Press(app, ']', 'j', 'j', ' ');
        Render(app);
        Click(app, x, y);                                        // where the row was
        Assert.Equal(0, app.Selected);
        Assert.True(app.Items[0].Enabled);
        Key(app, KeyCode.Enter);
        Assert.Null(app.Edit);
    }

    [Fact]
    public void Dialogs_cast_a_shadow()
    {
        var app = new ShowcaseApp();
        Key(app, KeyCode.Enter);
        CellBuffer screen = Render(app);                     // 110×34: the 64×22 box (+ shadow) starts at (22, 5)
        Color shadow = Color.Hex(0x05070B);
        Assert.Equal('╮', screen[85, 5].Rune.Value);
        Assert.NotEqual(shadow, screen[86, 5].Style.Bg);     // offset down by one
        Assert.Equal(shadow, screen[86, 6].Style.Bg);
        Assert.Equal(shadow, screen[87, 26].Style.Bg);
        Assert.Equal(shadow, screen[24, 27].Style.Bg);
        Assert.NotEqual(shadow, screen[88, 6].Style.Bg);
    }

    [Fact]
    public void A_click_outside_closes_the_progress_dialog_but_not_the_edit_dialog()
    {
        var app = new ShowcaseApp();
        app.Handle(Event.FromChar('p'), 0);
        (int x, int y) = Find(Render(app), "PIPELINE");
        Click(app, x, y);
        Assert.True(app.ProgressOpen);
        Click(app, 0, 0);
        Assert.False(app.ProgressOpen);

        Key(app, KeyCode.Enter);
        Render(app);
        Click(app, 0, 0);
        Assert.NotNull(app.Edit);
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
    public void Y_copies_the_selected_name_once()
    {
        var app = new ShowcaseApp();
        app.Handle(Event.FromChar('j'), 0);
        app.Handle(Event.FromChar('y'), 0);
        Assert.Equal("prepare workspace", app.TakeCopy());
        Assert.Null(app.TakeCopy());
        Assert.Contains("copied · prepare workspace", Render(app).ToString());
    }

    [Fact]
    public void Subtitle_links_to_the_repository()
    {
        CellBuffer buffer = Render(new ShowcaseApp());
        (int x, int y) = Find(buffer, "github ↗");
        Assert.Equal("https://github.com/lmaslanka/tuinet", buffer[x, y].Link);
        Assert.Null(buffer[x - 1, y].Link);
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

    [Fact]
    public void Right_click_on_a_row_opens_the_menu_there_and_choosing_sort_sorts()
    {
        var app = new ShowcaseApp();
        (int x, int y) = Find(Render(app), "05  schema");
        RightClick(app, x + 4, y);
        Assert.True(app.MenuOpen);
        Assert.Equal(4, app.Selected);                                   // the clicked row is selected
        CellBuffer screen = Render(app);
        Assert.Equal((x + 4, y), Find(screen, "╭──"));                   // the menu's corner at the pointer
        Assert.Contains("│ Edit              enter │", screen.ToString());
        (int sx, int sy) = Find(screen, "Sort by owner");
        Click(app, sx, sy);
        Assert.False(app.MenuOpen);
        Assert.Equal(4, app.SortColumn);
        Assert.Equal("ada", app.Items[0].Owner);
        Assert.Equal("schema registry", app.Items[app.Selected].Name);   // the selection follows the item
    }

    [Fact]
    public void M_opens_the_menu_under_the_selected_row_and_mnemonics_run_items()
    {
        var app = new ShowcaseApp();
        Press(app, 'j', 'j', 'm');
        CellBuffer screen = Render(app);
        Assert.Equal(Find(screen, "03  run").Y + 1, Find(screen, "╭──").Y);
        Assert.True(screen[Find(screen, "Sort by k").X + 9, Find(screen, "Sort by k").Y].Style.Attrs.HasFlag(Attr.Underline));   // the 'i' of k&ind
        Press(app, 'i');
        Assert.False(app.MenuOpen);
        Assert.Equal(2, app.SortColumn);

        Press(app, 'm');
        Key(app, KeyCode.Escape);
        Assert.False(app.MenuOpen);
        Assert.Equal(2, app.SortColumn);
    }

    [Fact]
    public void Menu_keys_skip_separators_and_the_disabled_delete()
    {
        var app = new ShowcaseApp();
        Press(app, 'm');
        Key(app, KeyCode.Up);                    // wraps past the disabled Delete and the separator
        Assert.Equal(8, app.MenuSelected);       // Sort by owner
        Key(app, KeyCode.Down);
        Assert.Equal(0, app.MenuSelected);
        Press(app, 'd');                         // Delete's mnemonic does nothing while it's disabled
        Assert.True(app.MenuOpen);
        Assert.Contains("Delete", Render(app).ToString());
    }

    [Fact]
    public void The_menu_toggle_is_labeled_for_the_selected_item()
    {
        var app = new ShowcaseApp();
        Press(app, 'm');
        Assert.Contains("│ Disable", Render(app).ToString());
        Press(app, 'b');                         // runs it: the item is now disabled
        Assert.False(app.Items[0].Enabled);
        Press(app, 'm');
        Assert.Contains("│ Enable ", Render(app).ToString());
    }

    [Fact]
    public void A_click_outside_the_menu_closes_it_and_still_selects_what_was_clicked()
    {
        var app = new ShowcaseApp();
        Press(app, 'm');
        CellBuffer screen = Render(app);
        (int x, int y) = Find(screen, "18  advisor");
        Click(app, x, y);
        Assert.False(app.MenuOpen);
        Assert.Equal(17, app.Selected);
    }

    [Fact]
    public void Ctrl_p_then_typing_prog_and_enter_opens_the_progress_dialog()
    {
        var app = new ShowcaseApp();
        app.Handle(Event.FromChar('p', Modifiers.Ctrl), 0);
        Assert.True(app.Palette.IsOpen);
        Assert.Contains("COMMANDS", Render(app).ToString());
        Type(app, "prog");
        Assert.Equal("Open the progress dialog", app.Palette.Match(0).Name);
        Key(app, KeyCode.Enter);
        Assert.False(app.Palette.IsOpen);
        Assert.True(app.ProgressOpen);
    }

    [Fact]
    public void Typing_in_the_palette_filters_best_first_and_highlights_the_matches()
    {
        var app = new ShowcaseApp();
        Press(app, ':');
        Assert.Equal(14, app.Palette.MatchCount);   // an empty query lists every command, in order
        Assert.Equal("Edit item", app.Palette.Match(0).Name);
        Type(app, "sk");
        Assert.Equal("Sort by kind", app.Palette.Match(0).Name);
        CellBuffer screen = Render(app);
        (int x, int y) = Find(screen, "Sort by kind");
        Assert.Equal(Color.Hex(0xF5A623), screen[x, y].Style.Fg);       // S
        Assert.Equal(Color.Hex(0xF5A623), screen[x + 8, y].Style.Fg);   // k
        Assert.NotEqual(Color.Hex(0xF5A623), screen[x + 1, y].Style.Fg);
        Type(app, "zzz");
        Assert.Equal(0, app.Palette.MatchCount);
        Assert.Contains("no matching commands", Render(app).ToString());
        Key(app, KeyCode.Escape);
        Assert.False(app.Palette.IsOpen);
    }

    [Fact]
    public void Clicking_a_palette_row_runs_it_and_quit_quits()
    {
        var app = new ShowcaseApp();
        Press(app, ':');
        (int x, int y) = Find(Render(app), "Show the stats");
        Click(app, x, y);
        Assert.False(app.Palette.IsOpen);
        Assert.Equal(ShowcaseApp.StatsPage, app.Page);

        Press(app, ':');
        Type(app, "quit");
        Assert.False(app.Handle(Event.FromKey(KeyCode.Enter), 0));
    }

    [Fact]
    public void Open_menu_and_palette_frames_and_refiltering_allocate_nothing()
    {
        var app = new ShowcaseApp();
        var buffer = new CellBuffer(110, 34);
        Press(app, 'm');

        void MenuStep(int i)
        {
            Key(app, i % 3 == 0 ? KeyCode.Up : KeyCode.Down);
            app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Move, MouseButton.None, 30, 12 + i % 5, Modifiers.None)), 0);
            app.Render(buffer, 0);
        }

        for (int i = 0; i < 50; i++) MenuStep(i);
        long before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++) MenuStep(i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);

        Key(app, KeyCode.Escape);
        Press(app, ':');

        // Type a char and undo it: the query changes, and the list is filtered again, on every key.
        void PaletteStep(int i)
        {
            if (i % 2 == 0) app.Handle(Event.FromChar("rbn"[i % 3]), 0);   // not Press: its params array allocates
            else Key(app, KeyCode.Char, 'z', Modifiers.Ctrl);
            app.Render(buffer, 0);
        }

        for (int i = 0; i < 50; i++) PaletteStep(i);
        before = GC.GetAllocatedBytesForCurrentThread();
        for (int i = 0; i < 500; i++) PaletteStep(i);
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Assert.True(app.Palette.IsOpen);
        Assert.Equal("", app.Palette.Query);
        Assert.Equal(14, app.Palette.MatchCount);
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

    private static void RightClick(ShowcaseApp app, int x, int y)
    {
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Down, MouseButton.Right, x, y, Modifiers.None)), 0);
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Up, MouseButton.Right, x, y, Modifiers.None)), 0);
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
