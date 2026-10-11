using Tuinet;
using Tuinet.Samples.Showcase;
using Tuinet.Testing;

namespace Tuinet.Samples.Showcase.Tests;

public class ShowcaseTests
{
    [Fact]
    public void Main_screen_lists_twenty_items_with_details()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        string screen = term.Text;
        Assert.Equal(20, app.Items.Count);
        Assert.Contains("ITEMS · 20", screen);
        Assert.Contains("▲ #  name", screen);
        Assert.Contains("01  parse & validate inte…  feature  ▲ low", screen);
        Assert.Contains("20  telemetry export", screen);
        Assert.Contains("SELECTED · 01", screen);
    }

    [Fact]
    public void Shows_the_title()   // the testing docs' examples (this and the next), kept honest
    {
        var buffer = new CellBuffer(80, 24);
        new ShowcaseApp().Render(buffer, nowMs: 0);
        Assert.Contains("ITEMS · 20", buffer.ToString());
        Assert.Equal(Color.Hex(0xF5A623), buffer[2, 4].Style.Fg);   // the list's amber border
    }

    [Fact]
    public void Enter_edits_the_selected_item()
    {
        using var term = new TestTerminal(new ShowcaseApp(), 110, 34);
        term.Press("jj").Key(KeyCode.Enter);
        Assert.Contains("EDIT ITEM · 03", term.Text);
        term.Click("Close");
        Assert.DoesNotContain("EDIT ITEM", term.Text);
        Assert.Equal(Color.Hex(0xF5A623), term.Screen.StyleOf("ITEMS · 20").Fg);
    }

    [Fact]
    public void Screens_match_their_snapshots()   // __snapshots__/*.txt; rerun with TUINET_ACCEPT_SNAPSHOTS=1 after a deliberate change
    {
        using var term = new TestTerminal(new ShowcaseApp(), 110, 34);
        Snapshot.Verify(term.Screen, "main");
        term.Press("jjj").Key(KeyCode.Enter);
        Snapshot.Verify(term.Screen, "edit");
        term.Key(KeyCode.Escape).Press("]]");
        Snapshot.Verify(term.Screen, "groups", styled: false);
    }

    [Fact]
    public void J_and_k_move_the_selection()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("jjjk");
        Assert.Equal(2, app.Selected);
        Assert.Contains("SELECTED · 03", term.Text);
    }

    [Fact]
    public void Enter_opens_the_edit_form_with_all_controls()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);
        string screen = term.Text;
        foreach (string label in (string[])["EDIT ITEM · 01", "name", "owner", "description", "kind", "priority", "enabled", "notify on finish", "Save", "Close"])
        {
            Assert.Contains(label, screen);
        }
    }

    [Fact]
    public void Focused_text_box_shows_the_real_cursor()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);
        CellBuffer buffer = term.Screen;
        Assert.True(buffer.CursorVisible);
        Assert.Contains("parse & validate intent", buffer.RowText(buffer.CursorY));
    }

    [Fact]
    public void Editing_all_controls_and_saving_updates_the_item()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);
        term.Press('u', Modifiers.Ctrl);          // clear name
        term.Press("renamed");
        term.Key(KeyCode.Tab);
        term.Press('u', Modifiers.Ctrl);
        term.Press("zoe");
        term.Key(KeyCode.Tab);                                  // description: keep
        term.Key(KeyCode.Tab);                                  // kind dropdown
        term.Key(KeyCode.Enter);
        term.Key(KeyCode.Down);
        term.Key(KeyCode.Enter);                                // feature → bugfix
        term.Key(KeyCode.Tab);                                  // priority dropdown
        term.Key(KeyCode.Enter);
        term.Key(KeyCode.End);
        term.Key(KeyCode.Enter);                                // → critical
        term.Key(KeyCode.Tab);
        term.Press(" ");                            // enabled off
        term.Key(KeyCode.Tab);
        term.Key(KeyCode.Enter);                                // notify toggled
        term.Key(KeyCode.Tab);
        term.Key(KeyCode.Enter);                                // Save

        Item item = app.Items[0];
        Assert.Null(app.Edit);
        Assert.Equal("renamed", item.Name);
        Assert.Equal("zoe", item.Owner);
        Assert.Equal(1, item.Kind);
        Assert.Equal(3, item.Priority);
        Assert.False(item.Enabled);
        Assert.False(item.Notify);
        Assert.Contains("saved · renamed", term.Text);
    }

    [Fact]
    public void Close_and_escape_discard_changes()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);
        term.Press("XYZ");
        term.Key(KeyCode.Escape);
        Assert.Null(app.Edit);
        Assert.Equal("parse & validate intent", app.Items[0].Name);

        term.Key(KeyCode.Enter);
        term.Press("XYZ");
        term.Key(KeyCode.Tab, Modifiers.Shift);                 // Close button
        term.Key(KeyCode.Enter);
        Assert.Null(app.Edit);
        Assert.Equal("parse & validate intent", app.Items[0].Name);
    }

    [Fact]
    public void Escape_in_an_open_dropdown_only_closes_the_dropdown()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);
        for (int i = 0; i < 3; i++)
        {
            term.Key(KeyCode.Tab);
        }

        term.Key(KeyCode.Enter);
        Assert.Contains("spike", term.Text);
        term.Key(KeyCode.Escape);
        Assert.NotNull(app.Edit);
        term.Key(KeyCode.Escape);
        Assert.Null(app.Edit);
    }

    [Fact]
    public void Empty_name_is_rejected()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);
        term.Press('u', Modifiers.Ctrl);
        term.Press('s', Modifiers.Ctrl);
        Assert.NotNull(app.Edit);
        Assert.Equal(EditDialog.Name, app.Edit!.Focus);
        Assert.Contains("name is required", term.Text);
    }

    [Fact]
    public void Paste_goes_into_the_focused_text_box()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);
        term.Press('u', Modifiers.Ctrl);
        term.Paste("pasted\nname");
        term.Press('s', Modifiers.Ctrl);
        Assert.Equal("pastedname", app.Items[0].Name);
    }

    [Fact]
    public void Description_is_multi_line()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);
        term.Key(KeyCode.Tab);
        term.Key(KeyCode.Tab);                                  // description
        term.Key(KeyCode.End);
        term.Key(KeyCode.Enter);                                // a line break, not the next field
        term.Press("second line");
        term.Paste("\r\npasted");
        Assert.Equal(EditDialog.Description, app.Edit!.Focus);
        string screen = term.Text;
        Assert.Contains("second line", screen);
        Assert.Contains("pasted", screen);

        term.Press('s', Modifiers.Ctrl);
        Assert.Equal("typed intent, schema checks and invariant gates before anything runs\nsecond line\npasted", app.Items[0].Description);
    }

    [Fact]
    public void Ctrl_c_copies_a_selection_and_ctrl_x_cuts_it()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);
        term.Key(KeyCode.Left, Modifiers.Ctrl | Modifiers.Shift);   // select "intent"
        term.Press('c', Modifiers.Ctrl);
        Assert.True(term.IsRunning);
        Assert.Equal("intent", app.TakeCopy());

        term.Press('x', Modifiers.Ctrl);
        Assert.Equal("intent", app.TakeCopy());
        term.Press('s', Modifiers.Ctrl);
        Assert.Equal("parse & validate", app.Items[0].Name);   // trimmed on save

        term.Key(KeyCode.Enter);
        term.Press('c', Modifiers.Ctrl);
        Assert.False(term.IsRunning);                          // nothing selected: quits
    }

    [Fact]
    public void Dragging_in_a_text_box_selects()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);
        (int x, int y) = Find(term.Screen, "ada");
        term.Mouse(MouseKind.Down, x, y, MouseButton.Left);
        term.Mouse(MouseKind.Drag, x + 2, y, MouseButton.Left);
        term.Mouse(MouseKind.Up, x + 2, y, MouseButton.Left);
        Assert.Equal(EditDialog.Owner, app.Edit!.Focus);
        term.Press('c', Modifiers.Ctrl);
        Assert.True(term.IsRunning);
        Assert.Equal("ad", app.TakeCopy());
    }

    [Fact]
    public void Progress_dialog_animates_and_finishes()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.NowMs = 1000;
        term.Press("p");
        Assert.True(app.ProgressOpen);
        Assert.True(app.IsAnimating(1000));

        term.Advance(1000);                                      // animating: the loop wakes with a tick
        Assert.Contains("PIPELINE · progress", term.Text);
        Assert.Contains("running", term.Text);
        Assert.Contains("░", term.Text);

        term.Advance(58_000);
        Assert.False(app.IsAnimating(60_000));
        Assert.Contains("✓ done", term.Text);
        Assert.Contains("0.92 sharp", term.Text);
        Assert.Contains("100.0%", term.Text);

        long frames = term.Terminal.Frames;
        term.Advance(10_000);                                    // done: nothing moves, so no frame
        Assert.Equal(frames, term.Terminal.Frames);

        term.Key(KeyCode.Escape);
        Assert.False(app.ProgressOpen);
    }

    [Fact]
    public void Progress_restart_resets_the_clock()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("p");
        Assert.False(app.IsAnimating(60_000));
        term.NowMs = 60_000;
        term.Press("r");
        Assert.True(app.IsAnimating(60_001));
    }

    [Fact]
    public void Tabs_sit_in_the_panel_border()
    {
        using var term = new TestTerminal(new ShowcaseApp(), 110, 34);
        CellBuffer screen = term.Screen;
        Assert.StartsWith("  ╭─ List ─ Stats ─ Groups ───", screen.RowText(4));
        Assert.Contains("─ ITEMS · 20 ─╮", screen.RowText(4));
    }

    [Fact]
    public void Brackets_and_alt_digits_switch_pages_and_keep_the_selection()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("jj]");
        Assert.Equal(ShowcaseApp.StatsPage, app.Page);
        string stats = term.Text;
        Assert.Contains("BY KIND", stats);
        Assert.DoesNotContain("▲ #  name", stats);
        Assert.Contains("SELECTED · 03", stats);                // the details panel stays

        term.Press("]");
        Assert.Equal(ShowcaseApp.GroupsPage, app.Page);
        term.Press("]");                                         // wraps
        Assert.Equal(ShowcaseApp.ListPage, app.Page);
        term.Press("[");
        Assert.Equal(ShowcaseApp.GroupsPage, app.Page);
        term.Press('2', Modifiers.Alt);
        Assert.Equal(ShowcaseApp.StatsPage, app.Page);
        term.Press('1', Modifiers.Alt);
        Assert.Equal(ShowcaseApp.ListPage, app.Page);
        term.Press('9', Modifiers.Alt);              // no ninth page
        Assert.Equal(ShowcaseApp.ListPage, app.Page);
        Assert.Equal(2, app.Selected);
        Assert.Contains("▲ #  name", term.Text);
    }

    [Fact]
    public void Clicking_a_tab_switches_pages()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        (int x, int y) = Find(term.Screen, "Stats");
        term.Click(x + 1, y);
        Assert.Equal(ShowcaseApp.StatsPage, app.Page);
        (x, y) = Find(term.Screen, "List");
        term.Click(x, y);
        Assert.Equal(ShowcaseApp.ListPage, app.Page);
    }

    [Fact]
    public void Stats_page_charts_the_items_and_the_frame_cost()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        app.RecordFrame(1234, TimeSpan.FromMicroseconds(56));
        term.Press("]");
        string screen = term.Text;
        foreach (string row in (string[])[@"BY KIND +FLAGS", @"   4       4       4       4       4 +■ notify +7 of 20",
            @"▅▅▅▅▅▅▅ ▅▅▅▅▅▅▅ ▅▅▅▅▅▅▅ ▅▅▅▅▅▅▅ ▅▅▅▅▅▅▅", @"feature bugfix   chore   docs    spike", @"■ enabled +17 of 20",
            @"critical ████████████▌ 5", @"BYTES PER FRAME +last 1234 B", @"RENDER \+ DIFF \+ WRITE +last 56 µs",
            @"TERMINAL", @"■ L/R margins +off", @"■ repeat \(REP\) +off"])
        {
            Assert.Matches(row, screen);
        }
    }

    [Fact]
    public void Stats_page_says_when_the_terminal_repeats_glyphs()
    {
        var app = new ShowcaseApp { RepeatSequences = true };
        Press(app, ']');
        string screen = Render(app).ToString();
        Assert.Matches(@"■ repeat \(REP\) +on", screen);
        Assert.Matches(@"■ L/R margins +off", screen);
        Assert.Matches(@"BY PRIORITY", screen);   // the flag fits above the next chart
    }

    [Fact]
    public void Stats_page_says_when_the_terminal_moves_split_bands()
    {
        var app = new ShowcaseApp { LeftRightMargins = true };
        using var term = new TestTerminal(app, 110, 34);
        term.Press("]");
        Assert.Matches(@"■ L/R margins +on", term.Text);
    }

    [Fact]
    public void Groups_page_shows_kinds_open_and_priorities_closed()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("]]");
        Assert.Equal(ShowcaseApp.GroupsPage, app.Page);
        string screen = term.Text;
        foreach (string row in (string[])[@"List ─ Stats ─ Groups", @"\[-\] feature \(4\)", @" ├─ \[\+\] critical \(1\)",
            @" └─ \[\+\] low \(1\)", @"\[-\] spike \(4\)", @"←/→ fold"])
        {
            Assert.Matches(row, screen);
        }
    }

    [Fact]
    public void Opening_the_groups_page_reveals_the_lists_selected_item()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("jjj");                                   // 04 verify & return diff: docs, medium
        term.Press("]]");
        Assert.Equal(3, app.Groups.SelectedNode);
        CellBuffer screen = term.Screen;
        (int x, int y) = Find(screen, "04 verify & return diff");
        Assert.Equal("▌", screen.RowText(y).Substring(x - 9, 1));   // the highlight symbol, before the guides
        Assert.Contains("├─ [-] medium (1)", screen.RowText(y - 1));    // its group was opened
    }

    [Fact]
    public void Selecting_an_item_in_the_tree_selects_it_in_the_list()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("]]");                                        // item 01 (feature, low)
        term.Key(KeyCode.Down);                                      // bugfix
        term.Key(KeyCode.Right);                                     // into its first child: critical
        term.Key(KeyCode.Right);                                     // open it
        term.Key(KeyCode.Right);                                     // into its item
        Item item = app.Items[app.Selected];
        Assert.Equal("bugfix", Item.Kinds[item.Kind]);
        Assert.Equal("critical", Item.Priorities[item.Priority]);
        Assert.Equal(ItemTree.NodeOf(item), app.Groups.SelectedNode);
        Assert.Contains($"SELECTED · {item.Number:D2}", term.Text);
        term.Key(KeyCode.Left);                                      // back to its group: the list keeps the item
        Assert.Equal(item, app.Items[app.Selected]);
    }

    [Fact]
    public void Enter_folds_a_group_and_edits_an_item_and_space_toggles_an_item()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("]]");
        term.Key(KeyCode.Home);                                      // feature
        term.Key(KeyCode.Enter);
        Assert.False(app.Groups.IsExpanded(ItemTree.KindGroup));
        Assert.Contains("[+] feature (4)", term.Text);
        term.Key(KeyCode.Enter);
        Assert.True(app.Groups.IsExpanded(ItemTree.KindGroup));

        app.Groups.Select(ItemTree.NodeOf(app.Items[5]), new ItemTree([.. app.Items]));
        term.Key(KeyCode.Down);                                      // through the tree, so the list follows
        term.Key(KeyCode.Up);
        Item item = app.Items[app.Selected];
        bool enabled = item.Enabled;
        term.Press(" ");
        Assert.NotEqual(enabled, item.Enabled);
        term.Key(KeyCode.Enter);
        Assert.NotNull(app.Edit);
    }

    [Fact]
    public void Clicks_fold_groups_and_select_items_and_a_double_click_edits()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("]]");
        (int x, int y) = Find(term.Screen, "[+] critical (1)");     // feature's critical group
        term.Click(x, y);
        Assert.True(app.Groups.IsExpanded(ItemTree.PriorityGroup + 3));
        Assert.Null(app.Edit);

        CellBuffer screen = term.Screen;
        string name = app.Items.Single(i => i.Kind == 0 && i.Priority == 3).Name;
        (x, y) = Find(screen, name);
        term.NowMs = 1000;
        term.Click(x, y);
        Assert.Equal(name, app.Items[app.Selected].Name);
        Assert.Contains(name, term.Screen.RowText(5));               // the details panel follows
        term.NowMs = 1000 + ShowcaseApp.DoubleClickMs - 1;
        term.Click(x, y);
        Assert.NotNull(app.Edit);
    }

    [Fact]
    public void Groups_page_frames_allocate_nothing()   // the app called directly: TestTerminal copies every frame
    {
        var app = new ShowcaseApp();
        var buffer = new CellBuffer(110, 34);
        app.Handle(Event.FromChar(']'), 0);
        app.Handle(Event.FromChar(']'), 0);

        // Move through the tree, then fold and unfold a group: a rebuild reuses the rows' arrays.
        void GroupsStep(int i)
        {
            app.Handle(Event.FromKey(i % 5 == 0 ? KeyCode.Up : KeyCode.Down), 0);
            if (i % 7 == 0)
            {
                app.Handle(Event.FromChar(' '), 0);
            }

            app.Render(buffer, 0);
        }

        Allocations.AssertNone(GroupsStep);
    }

    [Fact]
    public void Stats_page_bar_charts_follow_edits()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Items[0].Kind = 4;
        app.Items[1].Kind = 4;
        app.Items[0].Priority = 3;
        term.Press("]");
        string screen = term.Text;
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
        using var term = new TestTerminal(app, 110, 34);
        for (int i = 0; i < ShowcaseApp.FrameHistory + 80; i++)   // wraps the ring
        {
            app.RecordFrame(i == 0 ? 90_000 : i % 2 == 0 ? 400 : 100, TimeSpan.FromMicroseconds(50));
        }

        app.RecordFrame(5000, TimeSpan.FromMicroseconds(50));
        term.Press("]");
        CellBuffer screen = term.Screen;
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
        using var term = new TestTerminal(app, 110, 34);
        (int x, int y) = Find(term.Screen, "05  ");             // a row of the list
        term.Press("]jj ");
        term.Click(x, y);                                        // where the row was
        Assert.Equal(0, app.Selected);
        Assert.True(app.Items[0].Enabled);
        term.Key(KeyCode.Enter);
        Assert.Null(app.Edit);
    }

    [Fact]
    public void Dialogs_cast_a_shadow()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);
        CellBuffer screen = term.Screen;                     // 110×34: the 64×22 box (+ shadow) starts at (22, 5)
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
        using var term = new TestTerminal(app, 110, 34);
        term.Press("p");
        (int x, int y) = Find(term.Screen, "PIPELINE");
        term.Click(x, y);
        Assert.True(app.ProgressOpen);
        term.Click(0, 0);
        Assert.False(app.ProgressOpen);

        term.Key(KeyCode.Enter);
        term.Click(0, 0);
        Assert.NotNull(app.Edit);
    }

    [Fact]
    public void Narrow_screen_drops_whole_columns_instead_of_squeezing_them()
    {
        using var term = new TestTerminal(new ShowcaseApp(), 80, 24);
        string screen = term.Text;
        Assert.Contains("01  parse & valid…  feature", screen);
        Assert.DoesNotContain("priority", screen);
        Assert.DoesNotContain("fea…", screen);
    }

    [Fact]
    public void Small_terminal_does_not_throw()
    {
        foreach ((int w, int h) in (ReadOnlySpan<(int, int)>)[(1, 1), (20, 5), (40, 12), (80, 24)])
        {
            using var term = new TestTerminal(new ShowcaseApp(), w, h);
            term.Key(KeyCode.Enter).Key(KeyCode.Escape).Press("p").Advance(500).Key(KeyCode.Escape);
            term.Resize(w + 7, h + 3);
        }
    }

    [Fact]
    public void Q_and_ctrl_c_quit()
    {
        using var q = new TestTerminal(new ShowcaseApp());
        Assert.False(q.Press("q").IsRunning);
        using var ctrlC = new TestTerminal(new ShowcaseApp());
        Assert.False(ctrlC.Press('c', Modifiers.Ctrl).IsRunning);
    }

    [Fact]
    public void Clicking_a_row_selects_it()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        (int x, int y) = Find(term.Screen, "05  schema");
        term.Click(x, y);
        Assert.Equal(4, app.Selected);
        Assert.Contains("SELECTED · 05", term.Text);
    }

    [Fact]
    public void Wheel_scrolls_the_table_and_keeps_the_selection()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 80, 24);                // 12 rows fit: the table can scroll
        (int x, int y) = Find(term.Screen, "01  parse");
        term.Wheel(x, y, 1);
        string screen = term.Text;
        Assert.DoesNotContain("01  parse", screen);
        Assert.Contains("04  verify", screen);
        Assert.Contains("SELECTED · 01", screen);
        Assert.Equal(0, app.Selected);
    }

    [Fact]
    public void Scrollbar_clicks_scroll_and_never_count_as_a_double_click()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 80, 24);
        (int x, int top) = Find(term.Screen, "feature█");              // the thumb, beside row 01
        (_, int bottom) = Find(term.Screen, "12  diff");
        term.NowMs = 1000;
        term.Click(x + 7, bottom);
        Assert.Contains("20  telemetry", term.Text);

        term.NowMs = 1100;
        term.Click(x + 7, top);
        term.NowMs = 1200;
        term.Click(x + 7, top);
        Assert.Null(app.Edit);
        Assert.Contains("01  parse", term.Text);
        Assert.Equal(0, app.Selected);
    }

    [Fact]
    public void Y_copies_the_selected_name_once()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("j");
        term.Press("y");
        Assert.Equal("prepare workspace", app.TakeCopy());
        Assert.Null(app.TakeCopy());
        Assert.Contains("copied · prepare workspace", term.Text);
    }

    [Fact]
    public void A_message_hides_itself_after_three_seconds()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        Assert.Equal(long.MaxValue, app.NextDueMs);   // nothing to wake for: the loop sleeps until input
        term.NowMs = 1000;
        term.Press("y");
        Assert.Equal(1000 + ShowcaseApp.FlashMs, app.NextDueMs);

        term.Advance(2999);                           // not due yet: no tick
        Assert.Contains("copied · parse", term.Text);
        term.Advance(1);                              // a tick at NextDueMs, as Terminal.Run sends
        Assert.DoesNotContain("copied ·", term.Text);
        Assert.Equal(long.MaxValue, app.NextDueMs);
    }

    [Fact]
    public void A_new_message_restarts_the_clock_and_a_cleared_one_cancels_it()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("y");
        term.NowMs = 2000;
        term.Press("y");                              // the same text again: shown for another 3 s
        Assert.Equal(2000 + ShowcaseApp.FlashMs, app.NextDueMs);
        term.NowMs = 2500;
        term.Press("j");                              // a key hides it at once
        Assert.Equal(long.MaxValue, app.NextDueMs);
    }

    [Fact]
    public void Subtitle_links_to_the_repository()
    {
        using var term = new TestTerminal(new ShowcaseApp(), 110, 34);
        CellBuffer buffer = term.Screen;
        (int x, int y) = Find(buffer, "github ↗");
        Assert.Equal("https://github.com/lmaslanka/tuinet", buffer[x, y].Link);
        Assert.Null(buffer[x - 1, y].Link);
    }

    [Fact]
    public void Header_click_sorts_and_a_second_click_reverses()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        (int x, int y) = Find(term.Screen, "name  ");
        term.Click(x, y);
        Assert.Equal(1, app.SortColumn);
        string screen = term.Text;
        Assert.Contains("#  name ▲", screen);
        Assert.Contains("18  advisor hooks", screen);
        Assert.Contains("SELECTED · 01", screen);                       // the same item stays selected
        Assert.Equal("parse & validate intent", app.Items[app.Selected].Name);

        term.Click(x, y);
        Assert.True(app.SortDescending);
        Assert.Contains("#  name ▼", term.Text);
        Assert.Equal("verify & return diff", app.Items[0].Name);
    }

    [Fact]
    public void Double_click_on_a_row_opens_the_edit_dialog()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        (int x, int y) = Find(term.Screen, "03  run");
        term.NowMs = 1000;
        term.Click(x, y);
        term.NowMs = 1000 + ShowcaseApp.DoubleClickMs + 1;               // too slow: two single clicks
        term.Click(x, y);
        Assert.Null(app.Edit);
        term.NowMs += 100;                                              // 100 ms after the last one
        term.Click(x, y);
        Assert.NotNull(app.Edit);
        Assert.Contains("EDIT ITEM · 03", term.Text);
    }

    [Fact]
    public void Edit_dialog_controls_work_with_the_mouse()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);

        (int x, int y) = Find(term.Screen, "ada");                      // the owner text
        term.Click(x + 1, y);
        Assert.Equal(EditDialog.Owner, app.Edit!.Focus);
        term.Press("X");                                                // caret went between a and da

        term.Click("● feature");
        Assert.Equal(EditDialog.Kind, app.Edit.Focus);
        term.Click("● bugfix");                                         // in the open popup
        term.Click("enabled");
        term.Click("Save");

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
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Enter);
        (int x, int y) = Find(term.Screen, "● feature");
        term.Click(x, y);
        Assert.Contains("● spike", term.Text);
        term.Click(0, 0);
        Assert.DoesNotContain("● spike", term.Text);
        Assert.Equal(0, app.Items[0].Kind);
    }

    [Fact]
    public void Right_click_on_a_row_opens_the_menu_there_and_choosing_sort_sorts()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        (int x, int y) = Find(term.Screen, "05  schema");
        term.Click(x + 4, y, MouseButton.Right);
        Assert.True(app.MenuOpen);
        Assert.Equal(4, app.Selected);                                   // the clicked row is selected
        CellBuffer screen = term.Screen;
        Assert.Equal((x + 4, y), Find(screen, "╭──"));                   // the menu's corner at the pointer
        Assert.Contains("│ Edit              enter │", screen.ToString());
        (int sx, int sy) = Find(screen, "Sort by owner");
        term.Click(sx, sy);
        Assert.False(app.MenuOpen);
        Assert.Equal(4, app.SortColumn);
        Assert.Equal("ada", app.Items[0].Owner);
        Assert.Equal("schema registry", app.Items[app.Selected].Name);   // the selection follows the item
    }

    [Fact]
    public void M_opens_the_menu_under_the_selected_row_and_mnemonics_run_items()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("jjm");
        CellBuffer screen = term.Screen;
        Assert.Equal(Find(screen, "03  run").Y + 1, Find(screen, "╭──").Y);
        Assert.True(screen[Find(screen, "Sort by k").X + 9, Find(screen, "Sort by k").Y].Style.Attrs.HasFlag(Attr.Underline));   // the 'i' of k&ind
        term.Press("i");
        Assert.False(app.MenuOpen);
        Assert.Equal(2, app.SortColumn);

        term.Press("m");
        term.Key(KeyCode.Escape);
        Assert.False(app.MenuOpen);
        Assert.Equal(2, app.SortColumn);
    }

    [Fact]
    public void Menu_keys_skip_separators_and_the_disabled_delete()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("m");
        term.Key(KeyCode.Up);                    // wraps past the disabled Delete and the separator
        Assert.Equal(8, app.MenuSelected);       // Sort by owner
        term.Key(KeyCode.Down);
        Assert.Equal(0, app.MenuSelected);
        term.Press("d");                         // Delete's mnemonic does nothing while it's disabled
        Assert.True(app.MenuOpen);
        Assert.Contains("Delete", term.Text);
    }

    [Fact]
    public void The_menu_toggle_is_labeled_for_the_selected_item()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("m");
        Assert.Contains("│ Disable", term.Text);
        term.Press("b");                         // runs it: the item is now disabled
        Assert.False(app.Items[0].Enabled);
        term.Press("m");
        Assert.Contains("│ Enable ", term.Text);
    }

    [Fact]
    public void A_click_outside_the_menu_closes_it_and_still_selects_what_was_clicked()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("m");
        CellBuffer screen = term.Screen;
        (int x, int y) = Find(screen, "18  advisor");
        term.Click(x, y);
        Assert.False(app.MenuOpen);
        Assert.Equal(17, app.Selected);
    }

    [Fact]
    public void Ctrl_p_then_typing_prog_and_enter_opens_the_progress_dialog()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press('p', Modifiers.Ctrl);
        Assert.True(app.Palette.IsOpen);
        Assert.Contains("COMMANDS", term.Text);
        term.Press("prog");
        Assert.Equal("Open the progress dialog", app.Palette.Match(0).Name);
        term.Key(KeyCode.Enter);
        Assert.False(app.Palette.IsOpen);
        Assert.True(app.ProgressOpen);
    }

    [Fact]
    public void Typing_in_the_palette_filters_best_first_and_highlights_the_matches()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press(":");
        Assert.Equal(15, app.Palette.MatchCount);   // an empty query lists every command, in order
        Assert.Equal("Edit item", app.Palette.Match(0).Name);
        term.Press("sk");
        Assert.Equal("Sort by kind", app.Palette.Match(0).Name);
        CellBuffer screen = term.Screen;
        (int x, int y) = Find(screen, "Sort by kind");
        Assert.Equal(Color.Hex(0xF5A623), screen[x, y].Style.Fg);       // S
        Assert.Equal(Color.Hex(0xF5A623), screen[x + 8, y].Style.Fg);   // k
        Assert.NotEqual(Color.Hex(0xF5A623), screen[x + 1, y].Style.Fg);
        term.Press("zzz");
        Assert.Equal(0, app.Palette.MatchCount);
        Assert.Contains("no matching commands", term.Text);
        term.Key(KeyCode.Escape);
        Assert.False(app.Palette.IsOpen);
    }

    [Fact]
    public void Clicking_a_palette_row_runs_it_and_quit_quits()
    {
        var app = new ShowcaseApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press(":");
        (int x, int y) = Find(term.Screen, "Show the stats");
        term.Click(x, y);
        Assert.False(app.Palette.IsOpen);
        Assert.Equal(ShowcaseApp.StatsPage, app.Page);

        term.Press(":quit\n");
        Assert.False(term.IsRunning);
    }

    [Fact]
    public void Open_menu_and_palette_frames_and_refiltering_allocate_nothing()
    {
        var app = new ShowcaseApp();
        var buffer = new CellBuffer(110, 34);
        app.Handle(Event.FromChar('m'), 0);

        void MenuStep(int i)
        {
            app.Handle(Event.FromKey(i % 3 == 0 ? KeyCode.Up : KeyCode.Down), 0);
            app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Move, MouseButton.None, 30, 12 + i % 5, Modifiers.None)), 0);
            app.Render(buffer, 0);
        }

        Allocations.AssertNone(MenuStep);

        app.Handle(Event.FromKey(KeyCode.Escape), 0);
        app.Handle(Event.FromChar(':'), 0);

        // Type a char and undo it: the query changes, and the list is filtered again, on every key.
        void PaletteStep(int i)
        {
            if (i % 2 == 0) app.Handle(Event.FromChar("rbn"[i % 3]), 0);
            else app.Handle(Event.FromChar('z', Modifiers.Ctrl), 0);
            app.Render(buffer, 0);
        }

        Allocations.AssertNone(PaletteStep);
        Assert.True(app.Palette.IsOpen);
        Assert.Equal("", app.Palette.Query);
        Assert.Equal(15, app.Palette.MatchCount);
    }

    /// <summary>The first cell of <paramref name="text"/> (<see cref="CellBufferTesting.FindText"/> gives all the cells it covers).</summary>
    private static (int X, int Y) Find(CellBuffer buffer, string text)
    {
        Rect at = buffer.FindText(text);
        return (at.X, at.Y);
    }
}
