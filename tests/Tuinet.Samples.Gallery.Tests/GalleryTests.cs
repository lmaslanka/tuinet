using System.Text.RegularExpressions;
using Tuinet;
using Tuinet.Samples.Gallery;
using Tuinet.Testing;

namespace Tuinet.Samples.Gallery.Tests;

public class GalleryTests
{
    [Fact]
    public void Every_page_renders_at_full_and_small_sizes()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        for (int page = 0; page < app.Pages.Count; page++)
        {
            app.Show(page);
            string screen = term.Render().ToString();
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
        using var term = new TestTerminal(app, 110, 34);
        for (int page = 0; page < app.Pages.Count; page++)
        {
            app.Show(page);
            (int x, int y) = Find(term.Render(), FirstLine(app.Current.Code));
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
        using var term = new TestTerminal(app, 110, 34);
        term.Press("]");
        Assert.Equal(1, app.PageIndex);
        term.Press("[[");
        Assert.Equal(app.Pages.Count - 1, app.PageIndex);
        term.Press("]");
        Assert.Equal(0, app.PageIndex);
        term.Press('7', Modifiers.Alt);
        Assert.IsType<TablePage>(app.Current);
        Assert.Contains("7 / 15", term.Render().ToString());
        term.Press('0', Modifiers.Alt);              // no page zero
        Assert.IsType<TablePage>(app.Current);
    }

    [Fact]
    public void Clicking_a_tab_switches_pages()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        (int x, int y) = Find(term.Render(), "Charts");
        term.Click(x + 1, y);
        Assert.IsType<ChartsPage>(app.Current);
        (x, y) = Find(term.Render(), " Block ");
        term.Click(x + 1, y);
        Assert.IsType<BlockPage>(app.Current);
    }

    [Fact]
    public void All_tabs_fit_at_110_columns_and_scroll_when_narrower()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        string wide = term.Render().RowText(3);
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
        using var q = new TestTerminal(new GalleryApp());
        Assert.False(q.Press("q").IsRunning);
        using var ctrlC = new TestTerminal(new GalleryApp());
        Assert.False(ctrlC.Press('c', Modifiers.Ctrl).IsRunning);
    }

    [Fact]
    public void C_hides_the_code_and_the_wheel_scrolls_it()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Press("c");
        Assert.False(app.ShowCode);
        Assert.DoesNotContain("CODE", term.Render().ToString());
        term.Press("c");

        using var narrow = new TestTerminal(app, 80, 24);   // stacked: only the first lines of the code show
        (int x, int y) = Find(narrow.Screen, "Span<Rect> rows");
        narrow.Wheel(x, y, 1);
        Assert.DoesNotContain("Span<Rect> rows", narrow.Text);
        Assert.Contains("Constraint.Fill(), Constraint.Length(3)]", narrow.Text);
    }

    [Fact]
    public void Layout_arrows_change_the_percent_and_the_spacing()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        term.Key(KeyCode.Right);
        term.Key(KeyCode.Up);
        var page = app.Get<LayoutPage>();
        Assert.Equal((35, 2), (page.Percent, page.Spacing));
        Assert.Contains("Percent(35)", term.Render().ToString());
    }

    [Fact]
    public void Block_arrows_move_the_titles_and_space_hides_the_footers()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<BlockPage>();
        Assert.Contains("┌─ plain ─", term.Render().ToString());
        term.Key(KeyCode.Right);
        term.Key(KeyCode.Right);
        Assert.Equal(Tuinet.Widgets.Alignment.Right, app.Get<BlockPage>().TitleAlignment);
        string screen = term.Render().ToString();
        Assert.Contains("─ plain ─┐", screen);
        Assert.Contains("─ footer ─┘", screen);
        term.Press(" ");
        Assert.DoesNotContain("─ footer ─", term.Render().ToString());
    }

    [Fact]
    public void Text_w_cycles_the_wrap_and_arrows_scroll()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<TextPage>();
        Assert.Contains("word wrap · line 1", term.Render().ToString());
        term.Press("w");
        Assert.Equal(Tuinet.Widgets.TextWrap.Char, app.Get<TextPage>().Wrap);
        term.Key(KeyCode.Down);
        term.Key(KeyCode.Down);
        Assert.Contains("char wrap · line 3", term.Render().ToString());
        for (int i = 0; i < 100; i++)
        {
            term.Key(KeyCode.Down);
        }

        term.Render();
        Assert.InRange(app.Get<TextPage>().Scroll, 3, 30);   // clamped to the text
    }

    [Fact]
    public void Forms_keys_toggle_pick_and_save()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<FormsPage>();
        var page = app.Get<FormsPage>();
        term.Press(" ");
        Assert.False(page.NotifyChecked);
        Assert.Contains("[ ] notify me", term.Render().ToString());

        term.Key(KeyCode.Tab);
        term.Key(KeyCode.Tab);                                   // the dropdown
        term.Key(KeyCode.Enter);
        Assert.True(page.WantsKeys);
        Assert.Contains("critical", term.Render().ToString());
        term.Press("]");                                         // an open list takes every key
        Assert.IsType<FormsPage>(app.Current);
        term.Key(KeyCode.Down);
        term.Key(KeyCode.Enter);
        Assert.Equal(2, page.Priority);

        term.Key(KeyCode.Tab);                                   // Save
        term.Key(KeyCode.Enter);
        Assert.Equal("saved · notify off · dark off · high", page.Status);
        Assert.Contains("✓ saved · notify off", term.Render().ToString());
    }

    [Fact]
    public void Forms_clicks_focus_and_use_controls()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<FormsPage>();
        var page = app.Get<FormsPage>();
        (int x, int y) = Find(term.Render(), "dark mode");
        term.Click(x, y);
        Assert.Equal(FormsPage.Dark, page.Focus);
        Assert.Contains("███", term.Render().ToString());          // checked: the box is filled

        (x, y) = Find(term.Render(), "medium");
        term.Click(x, y);                                        // opens the list
        (x, y) = Find(term.Render(), "low");
        term.Click(x, y);
        Assert.Equal(0, page.Priority);

        (x, y) = Find(term.Render(), "Reset");
        term.Click(x, y);
        Assert.Equal(1, page.Priority);
    }

    [Fact]
    public void Input_types_into_the_focused_field_and_keeps_q_and_brackets()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<InputPage>();
        var page = app.Get<InputPage>();
        term.Key(KeyCode.Enter);
        Assert.True(page.IsEditing);
        term.Press("q[ada]");
        Assert.Equal("q[ada]", page.NameText);
        Assert.IsType<InputPage>(app.Current);

        term.Key(KeyCode.Tab);                                   // the password: masked
        term.Press("hunter2");
        CellBuffer screen = term.Render();
        Assert.Contains("•••••••", screen.ToString());
        Assert.DoesNotContain("hunter2", screen.ToString());
        Assert.True(screen.CursorVisible);

        term.Key(KeyCode.Tab);                                   // the notes: Enter is a line break
        term.Key(KeyCode.End, Modifiers.Ctrl);
        term.Key(KeyCode.Enter);
        term.Paste("third\nfourth");
        Assert.EndsWith("undoes.\nthird\nfourth", page.NotesText);
        Assert.Contains("4 fourth", term.Render().ToString());

        term.Key(KeyCode.Escape);
        Assert.False(page.IsEditing);
        Assert.False(term.Render().CursorVisible);
        term.Press("]");
        Assert.IsType<ListPage>(app.Current);
    }

    [Fact]
    public void Input_click_on_a_field_starts_editing_there()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<InputPage>();
        (int x, int y) = Find(term.Render(), "2 Shift+arrows");
        term.Click(x + 2, y);
        var page = app.Get<InputPage>();
        Assert.True(page.IsEditing);
        Assert.Equal(InputPage.Notes, page.Focus);
        term.Press("X");
        Assert.Contains("\nXShift+arrows", page.NotesText);
    }

    [Fact]
    public void List_keys_and_clicks_move_the_selection()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<ListPage>();
        term.Press("jjjk");
        Assert.Equal(2, app.Get<ListPage>().Selected);
        Assert.Contains("item 3 of 32", term.Render().ToString());
        (int x, int y) = Find(term.Render(), "Cell.cs");
        term.Click(x, y);
        Assert.Equal(4, app.Get<ListPage>().Selected);
        term.Key(KeyCode.End);
        Assert.Contains("TreeView.cs", term.Render().ToString());
    }

    [Fact]
    public void Table_header_clicks_sort_and_reverse()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<TablePage>();
        var page = app.Get<TablePage>();
        (int x, int y) = Find(term.Render(), "cpu %");
        term.Click(x + 1, y);
        Assert.Equal((2, false), (page.SortColumn, page.Descending));
        Assert.Equal("dotnet", page.SelectedProc.Name);         // the selection follows its row
        Assert.Contains("▲ cpu %", term.Render().ToString());   // on the left of a right-aligned header

        (x, y) = Find(term.Render(), "▲ cpu %");
        term.Click(x, y);
        Assert.True(page.Descending);
        term.Key(KeyCode.Home);
        CellBuffer screen = term.Render();
        Assert.Contains("cargo", screen.RowText(Find(screen, "▼ cpu %").Y + 2));
    }

    [Fact]
    public void Tree_keys_and_clicks_fold_folders()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<TreePage>();
        var page = app.Get<TreePage>();
        Assert.DoesNotContain("ListView.cs", term.Render().ToString());
        term.Key(KeyCode.Down);
        term.Key(KeyCode.Down);                                  // Widgets
        term.Key(KeyCode.Right);
        Assert.Contains("ListView.cs", term.Render().ToString());
        term.Key(KeyCode.Enter);                                 // folds it again
        Assert.DoesNotContain("ListView.cs", term.Render().ToString());

        (int x, int y) = Find(term.Render(), "▸ samples");
        term.Click(x, y);
        Assert.Contains("Gallery", term.Render().ToString());
        Assert.Equal("samples", page.SelectedName);
    }

    [Fact]
    public void Scroll_keys_and_bar_drags_move_the_view()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<ScrollPage>();
        var page = app.Get<ScrollPage>();
        CellBuffer screen = term.Render();
        term.Key(KeyCode.Down);
        term.Key(KeyCode.Down);
        term.Key(KeyCode.Right);
        Assert.Equal((2, 4), (page.Top, page.Left));
        Assert.Contains("lines 3–", term.Render().ToString());

        // Press on the vertical bar's last cell, then drag up to its first: the end, then the top.
        (int x, int y) = Find(screen, "lines 1–");
        int barX = Find(screen, "╮ │").X - 1;
        int top = Find(screen, "app.log").Y + 1;
        int bottom = y - 4;
        term.Mouse(MouseKind.Down, barX, bottom, MouseButton.Left);
        Assert.Equal(60 - (bottom - top + 1), page.Top);
        term.Mouse(MouseKind.Drag, x, top - 5, MouseButton.Left);
        Assert.Equal(0, page.Top);
        term.Mouse(MouseKind.Up, x, top - 5, MouseButton.Left);
    }

    [Fact]
    public void Progress_animates_with_time_and_space_pauses_it()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<ProgressPage>();
        Assert.True(app.IsAnimating(0));
        term.NowMs = 1000;
        string early = term.Render().ToString();
        term.NowMs = 2000;
        string later = term.Render().ToString();
        Assert.NotEqual(early, later);
        Assert.Contains("running · 2.0 s", later);

        term.Press(" ");
        Assert.False(app.IsAnimating(2000));
        term.NowMs = 3000;
        string paused = term.Render().ToString();
        term.NowMs = 9000;
        Assert.Equal(paused, term.Render().ToString());
        Assert.Contains("paused · 2.0 s", term.Text);
        term.Press(" ");
        term.NowMs = 9500;
        Assert.Contains("running · 2.5 s", term.Render().ToString());
    }

    [Fact]
    public void Charts_arrows_change_a_bar_and_ticks_feed_the_sparkline()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<ChartsPage>();
        var page = app.Get<ChartsPage>();
        term.Key(KeyCode.Right);
        term.Key(KeyCode.Up);
        term.Key(KeyCode.Up);
        Assert.Equal((1, 78.0), (page.SelectedBar, page.Value(1)));
        Assert.Contains("tue ███", term.Render().ToString());
        Assert.Contains(" 78", term.Render().ToString());

        int samples = page.SampleCount;
        term.Advance(ChartsPage.SampleMs);                       // animating: each Advance is a tick
        term.Advance(10);                                        // too soon for another
        term.Advance(ChartsPage.SampleMs - 10);
        Assert.Equal(samples + 2, page.SampleCount);
        term.Press(" ");
        Assert.False(app.IsAnimating(0));
    }

    [Fact]
    public void Tabs_arrows_and_clicks_switch_and_the_bar_scrolls()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<TabsPage>();
        var page = app.Get<TabsPage>();
        term.Key(KeyCode.Right);
        Assert.Equal(1, page.Selected);
        Assert.Contains("tab 2 of 12", term.Render().ToString());
        term.Key(KeyCode.End);
        CellBuffer screen = term.Render();
        Assert.Equal(11, page.Selected);
        Assert.True(page.Offset > 0);
        Assert.Contains("‹", screen.RowText(Find(screen, "PopupPage.cs").Y));
        (int x, int y) = Find(screen, "MenuPage.cs");
        term.Click(x, y);
        Assert.Equal(10, page.Selected);
    }

    [Fact]
    public void Menu_opens_from_the_button_and_runs_items_by_mnemonic()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<MenuPage>();
        var page = app.Get<MenuPage>();
        term.Press("m");
        Assert.True(page.IsOpen);
        CellBuffer screen = term.Render();
        Assert.Contains("│ Copy path         y │", screen.ToString());
        Assert.Equal(Find(screen, "Actions ▾").Y + 1, Find(screen, "│ Open").Y - 1);   // under the button
        term.Press("d");                                         // Delete is disabled
        Assert.True(page.IsOpen);
        term.Press("q");                                         // the menu takes every key while open
        Assert.True(page.IsOpen);
        term.Press("c");
        Assert.False(page.IsOpen);
        Assert.Equal(1, page.Ran);
        Assert.Contains("last: Copy path", term.Render().ToString());
    }

    [Fact]
    public void Menu_right_click_opens_at_the_pointer_and_a_click_runs()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<MenuPage>();
        (int x, int y) = Find(term.Render(), "Actions ▾");
        term.Mouse(MouseKind.Down, x + 20, y + 3, MouseButton.Right);
        CellBuffer screen = term.Screen;
        Assert.Equal((x + 20, y + 3), Find(screen, "╭──"));   // the corner at the pointer
        (int sx, int sy) = Find(screen, "Sort by size");
        term.Click(sx, sy);
        Assert.Equal(5, app.Get<MenuPage>().Ran);
        Assert.False(app.Get<MenuPage>().IsOpen);
    }

    [Fact]
    public void Popup_dialog_opens_casts_a_shadow_and_closes_on_a_click_outside()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<PopupPage>();
        var page = app.Get<PopupPage>();
        term.Key(KeyCode.Enter);
        CellBuffer screen = term.Render();
        (int x, int y) = Find(screen, "Delete 3 files?");
        Assert.True(page.IsOpen);
        (int title, int top) = Find(screen, "─ confirm ─");
        int right = screen.RowText(top).IndexOf('╮', title);
        Assert.Equal(Color.Hex(0x05070B), screen[right + 1, top + 1].Style.Bg);   // the shadow, right of the box
        Assert.NotEqual(Color.Hex(0x05070B), screen[right + 1, top].Style.Bg);    // offset down by one
        term.Click(x, y);                                        // inside: stays
        Assert.True(page.IsOpen);
        term.Click(3, 6);                                        // outside
        Assert.False(page.IsOpen);

        term.Key(KeyCode.Enter);
        term.Key(KeyCode.Enter);                                 // confirms
        Assert.Equal(1, page.Confirmed);
        term.Click(10, 12);
        Assert.True(page.HasPointPopup);
        Assert.Contains("clicked ", term.Render().ToString());
        term.Key(KeyCode.Escape);
        Assert.False(page.HasPointPopup);
    }

    [Fact]
    public void Fuzzy_filters_best_first_while_typing_and_highlights_the_matches()
    {
        var app = new GalleryApp();
        using var term = new TestTerminal(app, 110, 34);
        app.Show<FuzzyPage>();
        var page = app.Get<FuzzyPage>();
        Assert.Equal(45, page.MatchCount);
        term.Press("/");
        term.Press("tv");
        Assert.Equal("TreeView<T>", page.Match(0));
        CellBuffer screen = term.Render();
        Assert.Contains("3 of 45 names match", screen.ToString());
        (int x, int y) = Find(screen, "TreeView<T>");
        Assert.Equal(Color.Hex(0xF5A623), screen[x, y].Style.Fg);       // T
        Assert.Equal(Color.Hex(0xF5A623), screen[x + 4, y].Style.Fg);   // V
        Assert.NotEqual(Color.Hex(0xF5A623), screen[x + 1, y].Style.Fg);

        term.Press("q]");                                         // typed, not quit or switch
        Assert.Equal(0, page.MatchCount);
        Assert.IsType<FuzzyPage>(app.Current);
        term.Key(KeyCode.Backspace);
        term.Key(KeyCode.Backspace);
        term.Key(KeyCode.Down);
        Assert.Equal(1, page.Selected);
        term.Key(KeyCode.Escape);
        Assert.False(page.Typing);
        term.Press("]");
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

        Allocations.AssertNone(Step);
    }

    [Fact]
    public void Open_menus_lists_and_typing_allocate_nothing()
    {
        var app = new GalleryApp();
        var buffer = new CellBuffer(110, 34);

        app.Show<MenuPage>();
        app.Handle(Event.FromChar('m'), 0);
        Allocations.AssertNone(i =>
        {
            app.Handle(Event.FromKey(i % 3 == 0 ? KeyCode.Up : KeyCode.Down), 0);
            app.Render(buffer, 0);
        });

        app.Show<FormsPage>();
        app.Handle(Event.FromKey(KeyCode.Tab), 0);
        app.Handle(Event.FromKey(KeyCode.Tab), 0);
        app.Handle(Event.FromKey(KeyCode.Enter), 0);
        Allocations.AssertNone(i =>
        {
            app.Handle(Event.FromKey(i % 3 == 0 ? KeyCode.Up : KeyCode.Down), 0);
            app.Render(buffer, 0);
        });

        // Type a char and undo it, so the query changes and the list is filtered again on every key.
        app.Show<FuzzyPage>();
        app.Handle(Event.FromChar('/'), 0);
        Allocations.AssertNone(i =>
        {
            if (i % 2 == 0) app.Handle(Event.FromChar("tvs"[i % 3]), 0);
            else app.Handle(Event.FromChar('z', Modifiers.Ctrl), 0);
            app.Render(buffer, 0);
        });

        app.Show<InputPage>();
        app.Handle(Event.FromKey(KeyCode.Tab), 0);
        app.Handle(Event.FromKey(KeyCode.Tab), 0);               // the notes
        Allocations.AssertNone(i =>
        {
            if (i % 2 == 0) app.Handle(Event.FromChar("abc"[i % 3]), 0);
            else app.Handle(Event.FromChar('z', Modifiers.Ctrl), 0);
            app.Render(buffer, 0);
        });
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

    /// <summary>The first cell of <paramref name="text"/> (<see cref="CellBufferTesting.FindText"/> gives all the cells it covers).</summary>
    private static (int X, int Y) Find(CellBuffer buffer, string text)
    {
        Rect at = buffer.FindText(text);
        return (at.X, at.Y);
    }
}
