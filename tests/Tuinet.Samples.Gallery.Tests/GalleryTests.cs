using System.Text.RegularExpressions;
using Tuinet;
using Tuinet.Samples.Gallery;

namespace Tuinet.Samples.Gallery.Tests;

public class GalleryTests
{
    [Fact]
    public void Every_page_renders_at_full_and_small_sizes()
    {
        var app = new GalleryApp();
        for (int page = 0; page < app.Pages.Count; page++)
        {
            app.Show(page);
            string screen = Render(app).ToString();
            Assert.Contains(app.Current.Heading.Trim(), screen);
            Assert.Contains(app.Current.FileName, screen);
            Assert.Contains(FirstLine(app.Current.Code), screen);
            foreach ((int w, int h) in (ReadOnlySpan<(int, int)>)[(80, 24), (60, 20), (40, 12), (20, 5), (1, 1)])
            {
                app.Render(new CellBuffer(w, h), 0);
            }
        }
    }

    [Fact]
    public void Code_sits_beside_the_widget_at_110_columns_and_below_it_at_80()
    {
        var app = new GalleryApp();
        for (int page = 0; page < app.Pages.Count; page++)
        {
            app.Show(page);
            (int x, int y) = Find(Render(app), FirstLine(app.Current.Code));
            Assert.True(x >= 2 + GalleryApp.MinLiveWidth && y < 8, $"{app.Current.Title}: the code starts at ({x}, {y})");

            var narrow = new CellBuffer(80, 24);
            app.Render(narrow, 0);
            (x, y) = Find(narrow, FirstLine(app.Current.Code));
            Assert.True(x < 10 && y > 10, $"{app.Current.Title}: at 80×24 the code starts at ({x}, {y})");
        }
    }

    [Fact]
    public void Every_snippet_is_short_and_narrow_enough_to_sit_beside_its_widget()
    {
        foreach (Page page in new GalleryApp().Pages)
        {
            Assert.True(page.CodeWidth + 4 + 2 + GalleryApp.MinLiveWidth <= 106, $"{page.Title}: {page.CodeWidth} columns");
            Assert.True(page.CodeLines <= 24, $"{page.Title}: {page.CodeLines} lines");
        }
    }

    /// <summary>
    /// The code beside each widget is what draws it: every part of a snippet (parts are split by "// …" lines) appears in
    /// its page's source file, comparing with whitespace collapsed, after the snippet itself (a raw string) is removed.
    /// </summary>
    [Fact]
    public void Snippets_are_excerpts_of_their_pages_source()
    {
        string root = Path.Combine(RepositoryRoot(), "samples", "Tuinet.Samples.Gallery");
        foreach (Page page in new GalleryApp().Pages)
        {
            string file = Directory.GetFiles(root, page.FileName, SearchOption.AllDirectories).Single();
            string source = Normalize(Regex.Replace(File.ReadAllText(file), "\"\"\"[\\s\\S]*?\"\"\"", ""));
            string[] parts = Regex.Split(page.Code, @"^\s*// …\s*$", RegexOptions.Multiline);
            Assert.True(parts.Length >= 1);
            foreach (string part in parts)
            {
                string excerpt = Normalize(part);
                Assert.True(excerpt.Length > 10, $"{page.Title}: an empty part");
                Assert.True(source.Contains(excerpt, StringComparison.Ordinal),
                    $"{page.Title}: this part of the snippet is not in {page.FileName}:\n{part}");
            }
        }
    }

    [Fact]
    public void Brackets_and_alt_digits_switch_pages_and_wrap()
    {
        var app = new GalleryApp();
        Press(app, ']');
        Assert.Equal(1, app.PageIndex);
        Press(app, '[', '[');
        Assert.Equal(app.Pages.Count - 1, app.PageIndex);
        Press(app, ']');
        Assert.Equal(0, app.PageIndex);
        Key(app, KeyCode.Char, '7', Modifiers.Alt);
        Assert.IsType<TablePage>(app.Current);
        Assert.Contains("7 / 15", Render(app).ToString());
        Key(app, KeyCode.Char, '0', Modifiers.Alt);              // no page zero
        Assert.IsType<TablePage>(app.Current);
    }

    [Fact]
    public void Clicking_a_tab_switches_pages()
    {
        var app = new GalleryApp();
        (int x, int y) = Find(Render(app), "Charts");
        Click(app, x + 1, y);
        Assert.IsType<ChartsPage>(app.Current);
        (x, y) = Find(Render(app), " Block ");
        Click(app, x + 1, y);
        Assert.IsType<BlockPage>(app.Current);
    }

    [Fact]
    public void All_tabs_fit_at_110_columns_and_scroll_when_narrower()
    {
        var app = new GalleryApp();
        string wide = Render(app).RowText(3);
        Assert.Contains("Layout", wide);
        Assert.Contains("Fuzzy", wide);
        Assert.DoesNotContain("›", wide);

        app.Show<FuzzyPage>();
        var narrow = new CellBuffer(80, 24);
        app.Render(narrow, 0);
        Assert.Contains("‹", narrow.RowText(2));
        Assert.Contains("Fuzzy", narrow.RowText(2));
    }

    [Fact]
    public void Q_and_ctrl_c_quit()
    {
        Assert.False(new GalleryApp().Handle(Event.FromChar('q'), 0));
        Assert.False(new GalleryApp().Handle(Event.FromChar('c', Modifiers.Ctrl), 0));
    }

    [Fact]
    public void C_hides_the_code_and_the_wheel_scrolls_it()
    {
        var app = new GalleryApp();
        Press(app, 'c');
        Assert.False(app.ShowCode);
        Assert.DoesNotContain("CODE", Render(app).ToString());
        Press(app, 'c');

        var buffer = new CellBuffer(80, 24);                 // stacked: only the first lines of the code show
        app.Render(buffer, 0);
        (int x, int y) = Find(buffer, "Span<Rect> rows");
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.ScrollDown, MouseButton.None, x, y, Modifiers.None)), 0);
        app.Render(buffer, 0);
        Assert.DoesNotContain("Span<Rect> rows", buffer.ToString());
        Assert.Contains("Constraint.Fill(), Constraint.Length(3)]", buffer.ToString());
    }

    [Fact]
    public void Layout_arrows_change_the_percent_and_the_spacing()
    {
        var app = new GalleryApp();
        Key(app, KeyCode.Right);
        Key(app, KeyCode.Up);
        var page = app.Get<LayoutPage>();
        Assert.Equal((35, 2), (page.Percent, page.Spacing));
        Assert.Contains("Percent(35)", Render(app).ToString());
    }

    [Fact]
    public void Block_arrows_move_the_titles_and_space_hides_the_footers()
    {
        var app = new GalleryApp();
        app.Show<BlockPage>();
        Assert.Contains("┌─ plain ─", Render(app).ToString());
        Key(app, KeyCode.Right);
        Key(app, KeyCode.Right);
        Assert.Equal(Tuinet.Widgets.Alignment.Right, app.Get<BlockPage>().TitleAlignment);
        string screen = Render(app).ToString();
        Assert.Contains("─ plain ─┐", screen);
        Assert.Contains("─ footer ─┘", screen);
        Press(app, ' ');
        Assert.DoesNotContain("─ footer ─", Render(app).ToString());
    }

    [Fact]
    public void Text_w_cycles_the_wrap_and_arrows_scroll()
    {
        var app = new GalleryApp();
        app.Show<TextPage>();
        Assert.Contains("word wrap · line 1", Render(app).ToString());
        Press(app, 'w');
        Assert.Equal(Tuinet.Widgets.TextWrap.Char, app.Get<TextPage>().Wrap);
        Key(app, KeyCode.Down);
        Key(app, KeyCode.Down);
        Assert.Contains("char wrap · line 3", Render(app).ToString());
        for (int i = 0; i < 100; i++)
        {
            Key(app, KeyCode.Down);
        }

        Render(app);
        Assert.InRange(app.Get<TextPage>().Scroll, 3, 30);   // clamped to the text
    }

    [Fact]
    public void Forms_keys_toggle_pick_and_save()
    {
        var app = new GalleryApp();
        app.Show<FormsPage>();
        var page = app.Get<FormsPage>();
        Press(app, ' ');
        Assert.False(page.NotifyChecked);
        Assert.Contains("[ ] notify me", Render(app).ToString());

        Key(app, KeyCode.Tab);
        Key(app, KeyCode.Tab);                                   // the dropdown
        Key(app, KeyCode.Enter);
        Assert.True(page.WantsKeys);
        Assert.Contains("critical", Render(app).ToString());
        Press(app, ']');                                         // an open list takes every key
        Assert.IsType<FormsPage>(app.Current);
        Key(app, KeyCode.Down);
        Key(app, KeyCode.Enter);
        Assert.Equal(2, page.Priority);

        Key(app, KeyCode.Tab);                                   // Save
        Key(app, KeyCode.Enter);
        Assert.Equal("saved · notify off · dark off · high", page.Status);
        Assert.Contains("✓ saved · notify off", Render(app).ToString());
    }

    [Fact]
    public void Forms_clicks_focus_and_use_controls()
    {
        var app = new GalleryApp();
        app.Show<FormsPage>();
        var page = app.Get<FormsPage>();
        (int x, int y) = Find(Render(app), "dark mode");
        Click(app, x, y);
        Assert.Equal(FormsPage.Dark, page.Focus);
        Assert.Contains("███", Render(app).ToString());          // checked: the box is filled

        (x, y) = Find(Render(app), "medium");
        Click(app, x, y);                                        // opens the list
        (x, y) = Find(Render(app), "low");
        Click(app, x, y);
        Assert.Equal(0, page.Priority);

        (x, y) = Find(Render(app), "Reset");
        Click(app, x, y);
        Assert.Equal(1, page.Priority);
    }

    [Fact]
    public void Input_types_into_the_focused_field_and_keeps_q_and_brackets()
    {
        var app = new GalleryApp();
        app.Show<InputPage>();
        var page = app.Get<InputPage>();
        Key(app, KeyCode.Enter);
        Assert.True(page.IsEditing);
        Type(app, "q[ada]");
        Assert.Equal("q[ada]", page.NameText);
        Assert.IsType<InputPage>(app.Current);

        Key(app, KeyCode.Tab);                                   // the password: masked
        Type(app, "hunter2");
        CellBuffer screen = Render(app);
        Assert.Contains("•••••••", screen.ToString());
        Assert.DoesNotContain("hunter2", screen.ToString());
        Assert.True(screen.CursorVisible);

        Key(app, KeyCode.Tab);                                   // the notes: Enter is a line break
        Key(app, KeyCode.End, Modifiers.Ctrl);
        Key(app, KeyCode.Enter);
        app.Handle(Event.FromPaste("third\nfourth"), 0);
        Assert.EndsWith("undoes.\nthird\nfourth", page.NotesText);
        Assert.Contains("4 fourth", Render(app).ToString());

        Key(app, KeyCode.Escape);
        Assert.False(page.IsEditing);
        Assert.False(Render(app).CursorVisible);
        Press(app, ']');
        Assert.IsType<ListPage>(app.Current);
    }

    [Fact]
    public void Input_click_on_a_field_starts_editing_there()
    {
        var app = new GalleryApp();
        app.Show<InputPage>();
        (int x, int y) = Find(Render(app), "2 Shift+arrows");
        Click(app, x + 2, y);
        var page = app.Get<InputPage>();
        Assert.True(page.IsEditing);
        Assert.Equal(InputPage.Notes, page.Focus);
        Type(app, "X");
        Assert.Contains("\nXShift+arrows", page.NotesText);
    }

    [Fact]
    public void List_keys_and_clicks_move_the_selection()
    {
        var app = new GalleryApp();
        app.Show<ListPage>();
        Press(app, 'j', 'j', 'j', 'k');
        Assert.Equal(2, app.Get<ListPage>().Selected);
        Assert.Contains("item 3 of 32", Render(app).ToString());
        (int x, int y) = Find(Render(app), "Cell.cs");
        Click(app, x, y);
        Assert.Equal(4, app.Get<ListPage>().Selected);
        Key(app, KeyCode.End);
        Assert.Contains("TreeView.cs", Render(app).ToString());
    }

    [Fact]
    public void Table_header_clicks_sort_and_reverse()
    {
        var app = new GalleryApp();
        app.Show<TablePage>();
        var page = app.Get<TablePage>();
        (int x, int y) = Find(Render(app), "cpu %");
        Click(app, x + 1, y);
        Assert.Equal((2, false), (page.SortColumn, page.Descending));
        Assert.Equal("dotnet", page.SelectedProc.Name);         // the selection follows its row
        Assert.Contains("▲ cpu %", Render(app).ToString());   // on the left of a right-aligned header

        (x, y) = Find(Render(app), "▲ cpu %");
        Click(app, x, y);
        Assert.True(page.Descending);
        Key(app, KeyCode.Home);
        CellBuffer screen = Render(app);
        Assert.Contains("cargo", screen.RowText(Find(screen, "▼ cpu %").Y + 2));
    }

    [Fact]
    public void Tree_keys_and_clicks_fold_folders()
    {
        var app = new GalleryApp();
        app.Show<TreePage>();
        var page = app.Get<TreePage>();
        Assert.DoesNotContain("ListView.cs", Render(app).ToString());
        Key(app, KeyCode.Down);
        Key(app, KeyCode.Down);                                  // Widgets
        Key(app, KeyCode.Right);
        Assert.Contains("ListView.cs", Render(app).ToString());
        Key(app, KeyCode.Enter);                                 // folds it again
        Assert.DoesNotContain("ListView.cs", Render(app).ToString());

        (int x, int y) = Find(Render(app), "▸ samples");
        Click(app, x, y);
        Assert.Contains("Gallery", Render(app).ToString());
        Assert.Equal("samples", page.SelectedName);
    }

    [Fact]
    public void Scroll_keys_and_bar_drags_move_the_view()
    {
        var app = new GalleryApp();
        app.Show<ScrollPage>();
        var page = app.Get<ScrollPage>();
        CellBuffer screen = Render(app);
        Key(app, KeyCode.Down);
        Key(app, KeyCode.Down);
        Key(app, KeyCode.Right);
        Assert.Equal((2, 4), (page.Top, page.Left));
        Assert.Contains("lines 3–", Render(app).ToString());

        // Press on the vertical bar's last cell, then drag up to its first: the end, then the top.
        (int x, int y) = Find(screen, "lines 1–");
        int barX = Find(screen, "╮ │").X - 1;
        int top = Find(screen, "app.log").Y + 1;
        int bottom = y - 4;
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Down, MouseButton.Left, barX, bottom, Modifiers.None)), 0);
        Assert.Equal(60 - (bottom - top + 1), page.Top);
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Drag, MouseButton.Left, x, top - 5, Modifiers.None)), 0);
        Assert.Equal(0, page.Top);
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Up, MouseButton.Left, x, top - 5, Modifiers.None)), 0);
    }

    [Fact]
    public void Progress_animates_with_time_and_space_pauses_it()
    {
        var app = new GalleryApp();
        app.Show<ProgressPage>();
        Assert.True(app.IsAnimating(0));
        string early = Render(app, 1000).ToString();
        string later = Render(app, 2000).ToString();
        Assert.NotEqual(early, later);
        Assert.Contains("running · 2.0 s", later);

        app.Handle(Event.FromChar(' '), 2000);
        Assert.False(app.IsAnimating(2000));
        Assert.Equal(Render(app, 3000).ToString(), Render(app, 9000).ToString());
        Assert.Contains("paused · 2.0 s", Render(app, 9000).ToString());
        app.Handle(Event.FromChar(' '), 9000);
        Assert.Contains("running · 2.5 s", Render(app, 9500).ToString());
    }

    [Fact]
    public void Charts_arrows_change_a_bar_and_ticks_feed_the_sparkline()
    {
        var app = new GalleryApp();
        app.Show<ChartsPage>();
        var page = app.Get<ChartsPage>();
        Key(app, KeyCode.Right);
        Key(app, KeyCode.Up);
        Key(app, KeyCode.Up);
        Assert.Equal((1, 78.0), (page.SelectedBar, page.Value(1)));
        Assert.Contains("tue ███", Render(app).ToString());
        Assert.Contains(" 78", Render(app).ToString());

        int samples = page.SampleCount;
        app.Handle(Event.Tick, ChartsPage.SampleMs);
        app.Handle(Event.Tick, ChartsPage.SampleMs + 10);       // too soon for another
        app.Handle(Event.Tick, 2 * ChartsPage.SampleMs);
        Assert.Equal(samples + 2, page.SampleCount);
        Press(app, ' ');
        Assert.False(app.IsAnimating(0));
    }

    [Fact]
    public void Tabs_arrows_and_clicks_switch_and_the_bar_scrolls()
    {
        var app = new GalleryApp();
        app.Show<TabsPage>();
        var page = app.Get<TabsPage>();
        Key(app, KeyCode.Right);
        Assert.Equal(1, page.Selected);
        Assert.Contains("tab 2 of 12", Render(app).ToString());
        Key(app, KeyCode.End);
        CellBuffer screen = Render(app);
        Assert.Equal(11, page.Selected);
        Assert.True(page.Offset > 0);
        Assert.Contains("‹", screen.RowText(Find(screen, "PopupPage.cs").Y));
        (int x, int y) = Find(screen, "MenuPage.cs");
        Click(app, x, y);
        Assert.Equal(10, page.Selected);
    }

    [Fact]
    public void Menu_opens_from_the_button_and_runs_items_by_mnemonic()
    {
        var app = new GalleryApp();
        app.Show<MenuPage>();
        var page = app.Get<MenuPage>();
        Press(app, 'm');
        Assert.True(page.IsOpen);
        CellBuffer screen = Render(app);
        Assert.Contains("│ Copy path         y │", screen.ToString());
        Assert.Equal(Find(screen, "Actions ▾").Y + 1, Find(screen, "│ Open").Y - 1);   // under the button
        Press(app, 'd');                                         // Delete is disabled
        Assert.True(page.IsOpen);
        Press(app, 'q');                                         // the menu takes every key while open
        Assert.True(page.IsOpen);
        Press(app, 'c');
        Assert.False(page.IsOpen);
        Assert.Equal(1, page.Ran);
        Assert.Contains("last: Copy path", Render(app).ToString());
    }

    [Fact]
    public void Menu_right_click_opens_at_the_pointer_and_a_click_runs()
    {
        var app = new GalleryApp();
        app.Show<MenuPage>();
        (int x, int y) = Find(Render(app), "Actions ▾");
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Down, MouseButton.Right, x + 20, y + 3, Modifiers.None)), 0);
        CellBuffer screen = Render(app);
        Assert.Equal((x + 20, y + 3), Find(screen, "╭──"));   // the corner at the pointer
        (int sx, int sy) = Find(screen, "Sort by size");
        Click(app, sx, sy);
        Assert.Equal(5, app.Get<MenuPage>().Ran);
        Assert.False(app.Get<MenuPage>().IsOpen);
    }

    [Fact]
    public void Popup_dialog_opens_casts_a_shadow_and_closes_on_a_click_outside()
    {
        var app = new GalleryApp();
        app.Show<PopupPage>();
        var page = app.Get<PopupPage>();
        Key(app, KeyCode.Enter);
        CellBuffer screen = Render(app);
        (int x, int y) = Find(screen, "Delete 3 files?");
        Assert.True(page.IsOpen);
        (int title, int top) = Find(screen, "─ confirm ─");
        int right = screen.RowText(top).IndexOf('╮', title);
        Assert.Equal(Color.Hex(0x05070B), screen[right + 1, top + 1].Style.Bg);   // the shadow, right of the box
        Assert.NotEqual(Color.Hex(0x05070B), screen[right + 1, top].Style.Bg);    // offset down by one
        Click(app, x, y);                                        // inside: stays
        Assert.True(page.IsOpen);
        Click(app, 3, 6);                                        // outside
        Assert.False(page.IsOpen);

        Key(app, KeyCode.Enter);
        Key(app, KeyCode.Enter);                                 // confirms
        Assert.Equal(1, page.Confirmed);
        Click(app, 10, 12);
        Assert.True(page.HasPointPopup);
        Assert.Contains("clicked ", Render(app).ToString());
        Key(app, KeyCode.Escape);
        Assert.False(page.HasPointPopup);
    }

    [Fact]
    public void Fuzzy_filters_best_first_while_typing_and_highlights_the_matches()
    {
        var app = new GalleryApp();
        app.Show<FuzzyPage>();
        var page = app.Get<FuzzyPage>();
        Assert.Equal(45, page.MatchCount);
        Press(app, '/');
        Type(app, "tv");
        Assert.Equal("TreeView<T>", page.Match(0));
        CellBuffer screen = Render(app);
        Assert.Contains("3 of 45 names match", screen.ToString());
        (int x, int y) = Find(screen, "TreeView<T>");
        Assert.Equal(Color.Hex(0xF5A623), screen[x, y].Style.Fg);       // T
        Assert.Equal(Color.Hex(0xF5A623), screen[x + 4, y].Style.Fg);   // V
        Assert.NotEqual(Color.Hex(0xF5A623), screen[x + 1, y].Style.Fg);

        Type(app, "q]");                                         // typed, not quit or switch
        Assert.Equal(0, page.MatchCount);
        Assert.IsType<FuzzyPage>(app.Current);
        Key(app, KeyCode.Backspace);
        Key(app, KeyCode.Backspace);
        Key(app, KeyCode.Down);
        Assert.Equal(1, page.Selected);
        Key(app, KeyCode.Escape);
        Assert.False(page.Typing);
        Press(app, ']');
        Assert.IsType<LayoutPage>(app.Current);
    }

    [Fact]
    public void Steady_state_frames_allocate_nothing_on_any_page()
    {
        var app = new GalleryApp();
        var buffer = new CellBuffer(110, 34);
        KeyCode[] keys = [KeyCode.Down, KeyCode.Right, KeyCode.Down, KeyCode.Up, KeyCode.Left, KeyCode.Down];

        // Every page in turn: keys that move things on it, a tick, a pointer move, a frame.
        void Step(int i)
        {
            app.Handle(i % 12 == 11 ? Event.FromChar(']') : Event.FromKey(keys[i % keys.Length]), i * 33L);
            app.Handle(Event.Tick, i * 33L);
            app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Move, MouseButton.None, 20 + i % 7, 10, Modifiers.None)), i * 33L);
            app.Render(buffer, i * 33L);
        }

        AssertSteadyStateAllocatesNothing(Step);
    }

    [Fact]
    public void Open_menus_lists_and_typing_allocate_nothing()
    {
        var app = new GalleryApp();
        var buffer = new CellBuffer(110, 34);

        app.Show<MenuPage>();
        app.Handle(Event.FromChar('m'), 0);
        AssertSteadyStateAllocatesNothing(i =>
        {
            app.Handle(Event.FromKey(i % 3 == 0 ? KeyCode.Up : KeyCode.Down), 0);
            app.Render(buffer, 0);
        });

        app.Show<FormsPage>();
        app.Handle(Event.FromKey(KeyCode.Tab), 0);
        app.Handle(Event.FromKey(KeyCode.Tab), 0);
        app.Handle(Event.FromKey(KeyCode.Enter), 0);
        AssertSteadyStateAllocatesNothing(i =>
        {
            app.Handle(Event.FromKey(i % 3 == 0 ? KeyCode.Up : KeyCode.Down), 0);
            app.Render(buffer, 0);
        });

        // Type a char and undo it, so the query changes and the list is filtered again on every key.
        app.Show<FuzzyPage>();
        app.Handle(Event.FromChar('/'), 0);
        AssertSteadyStateAllocatesNothing(i =>
        {
            if (i % 2 == 0) app.Handle(Event.FromChar("tvs"[i % 3]), 0);
            else app.Handle(Event.FromChar('z', Modifiers.Ctrl), 0);
            app.Render(buffer, 0);
        });

        app.Show<InputPage>();
        app.Handle(Event.FromKey(KeyCode.Tab), 0);
        app.Handle(Event.FromKey(KeyCode.Tab), 0);               // the notes
        AssertSteadyStateAllocatesNothing(i =>
        {
            if (i % 2 == 0) app.Handle(Event.FromChar("abc"[i % 3]), 0);
            else app.Handle(Event.FromChar('z', Modifiers.Ctrl), 0);
            app.Render(buffer, 0);
        });
    }

    /// <summary>
    /// After a warm-up, some 500-step round must allocate nothing. Up to five rounds, because a whole app frame runs
    /// enough code that one-time runtime work (tiering on a slow CI runner) can land in the first; a per-frame
    /// allocation shows up in every round, and the failure lists them.
    /// </summary>
    /// <remarks>
    /// Between rounds the test pauses briefly. Until the JIT recompiles them, generic formatting paths (an interpolated
    /// <c>{value:F1}</c> into a span) box their argument, and the recompile waits for a quiet moment that a busy loop
    /// over fifteen pages may never give it. The pause is that moment; real frames are drawn far apart anyway.
    /// </remarks>
    private static void AssertSteadyStateAllocatesNothing(Action<int> step)
    {
        for (int i = 0; i < 50; i++)
        {
            step(i);
        }

        var rounds = new long[5];
        for (int round = 0; round < rounds.Length; round++)
        {
            long before = GC.GetAllocatedBytesForCurrentThread();
            for (int i = 0; i < 500; i++)
            {
                step(i);
            }

            rounds[round] = GC.GetAllocatedBytesForCurrentThread() - before;
            if (rounds[round] == 0)
            {
                return;
            }

            Thread.Sleep(250);
        }

        Assert.Fail($"Every round allocated: {string.Join(", ", rounds)} bytes per 500 steps.");
    }

    private static string Normalize(string text) => Regex.Replace(text, @"\s+", " ").Trim();

    private static string FirstLine(string code) => code.Split('\n')[0].TrimEnd();

    /// <summary>The checkout this test was built from: the first directory up from the test binaries with tuinet.slnx.</summary>
    private static string RepositoryRoot()
    {
        for (var dir = new DirectoryInfo(AppContext.BaseDirectory); dir is not null; dir = dir.Parent)
        {
            if (File.Exists(Path.Combine(dir.FullName, "tuinet.slnx")))
            {
                return dir.FullName;
            }
        }

        throw new DirectoryNotFoundException($"No tuinet.slnx above {AppContext.BaseDirectory}.");
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

    private static void Click(GalleryApp app, int x, int y)
    {
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Down, MouseButton.Left, x, y, Modifiers.None)), 0);
        app.Handle(Event.FromMouse(new MouseEvent(MouseKind.Up, MouseButton.Left, x, y, Modifiers.None)), 0);
    }

    private static CellBuffer Render(GalleryApp app, long now = 0)
    {
        var buffer = new CellBuffer(110, 34);
        app.Render(buffer, now);
        return buffer;
    }

    private static void Press(GalleryApp app, params char[] keys)
    {
        foreach (char c in keys)
        {
            app.Handle(Event.FromChar(c), 0);
        }
    }

    private static void Type(GalleryApp app, string text)
    {
        foreach (char c in text)
        {
            app.Handle(Event.FromChar(c), 0);
        }
    }

    private static void Key(GalleryApp app, KeyCode code, Modifiers modifiers = Modifiers.None) =>
        app.Handle(Event.FromKey(code, modifiers), 0);

    private static void Key(GalleryApp app, KeyCode code, char c, Modifiers modifiers = Modifiers.None) =>
        app.Handle(Event.FromChar(c, modifiers), 0);
}
