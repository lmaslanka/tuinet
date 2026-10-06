using System.Text;
using Tuinet.Widgets;

namespace Tuinet.Tests;

public class TextAreaTests
{
    [Fact]
    public void Enter_splits_lines_and_the_index_follows()
    {
        var state = new TextAreaState();
        Type(state, "ab");
        state.Handle(Key(KeyCode.Enter));
        Type(state, "cd");
        Assert.Equal("ab\ncd", state.Text);
        Assert.Equal(2, state.LineCount);
        Assert.Equal("cd", state.Line(1).ToString());
        Assert.Equal(3, state.LineStart(1));
        Assert.Equal(1, state.CaretLine);
        Assert.Equal(2, state.CaretColumn);

        Assert.False(state.Handle(Key(KeyCode.Enter, Modifiers.Ctrl)));   // left to the app
        Assert.False(state.Handle(Key(KeyCode.Tab)));
    }

    [Fact]
    public void Arrows_cross_line_breaks_and_keep_the_column()
    {
        var state = new TextAreaState("hello\nhi\nworld");
        Assert.Equal(0, state.Caret);                                   // a text area starts at the top
        state.Handle(Key(KeyCode.End));
        state.Handle(Key(KeyCode.Right));
        Assert.Equal((1, 0), (state.CaretLine, state.CaretColumn));
        state.Handle(Key(KeyCode.Left));
        Assert.Equal(5, state.Caret);

        state.MoveCaret(4);
        state.Handle(Key(KeyCode.Down));
        Assert.Equal((1, 2), (state.CaretLine, state.CaretColumn));     // "hi" is shorter
        state.Handle(Key(KeyCode.Down));
        Assert.Equal((2, 4), (state.CaretLine, state.CaretColumn));     // back to column 4
        state.Handle(Key(KeyCode.Down));
        Assert.Equal(state.Length, state.Caret);                        // past the last line: the end
        state.Handle(Key(KeyCode.Home, Modifiers.Ctrl));
        Assert.Equal(0, state.Caret);
        state.Handle(Key(KeyCode.End, Modifiers.Ctrl));
        Assert.Equal(state.Length, state.Caret);
    }

    [Fact]
    public void Shift_selects_and_typing_replaces_the_selection()
    {
        var state = new TextAreaState("hello world");
        state.Handle(Key(KeyCode.Right, Modifiers.Ctrl | Modifiers.Shift));
        Assert.Equal("hello", state.Selection.ToString());
        state.Handle(KeyEvent.Char('J'));
        Assert.Equal("J world", state.Text);

        state.Handle(Key(KeyCode.End, Modifiers.Shift));
        Assert.Equal(" world", state.Selection.ToString());
        state.Handle(Key(KeyCode.Left));                                // collapses to the start
        Assert.False(state.HasSelection);
        Assert.Equal(1, state.Caret);

        state.Handle(KeyEvent.Char('a', Modifiers.Ctrl));
        Assert.Equal(state.Text, state.Selection.ToString());
        state.Handle(Key(KeyCode.Backspace));
        Assert.True(state.IsEmpty);
    }

    [Fact]
    public void Undo_goes_back_a_word_at_a_time_and_redo_replays()
    {
        var state = new TextAreaState();
        Type(state, "hello world");
        Assert.True(state.Handle(KeyEvent.Char('z', Modifiers.Ctrl)));
        Assert.Equal("hello", state.Text);
        state.Handle(KeyEvent.Char('_', Modifiers.Ctrl));               // Ctrl+/ in most terminals
        Assert.Equal("", state.Text);
        Assert.False(state.CanUndo);

        state.Handle(KeyEvent.Char('y', Modifiers.Ctrl));
        state.Handle(new KeyEvent(KeyCode.Char, new Rune('Z'), Modifiers.Ctrl | Modifiers.Shift));
        Assert.Equal("hello world", state.Text);
        Assert.Equal(11, state.Caret);

        state.Undo();
        Type(state, "!");                                               // a new edit drops the redo
        Assert.False(state.CanRedo);
        Assert.Equal("hello!", state.Text);
    }

    [Fact]
    public void Undo_restores_the_selection_an_edit_replaced()
    {
        var state = new TextInputState("hello world");
        state.Select(6, 11);
        state.Handle(KeyEvent.Char('x'));
        Assert.Equal("hello x", state.Text);
        state.Undo();
        Assert.Equal("hello world", state.Text);
        Assert.Equal("world", state.Selection.ToString());
    }

    [Fact]
    public void A_run_of_backspaces_or_deletes_undoes_at_once()
    {
        var state = new TextAreaState("abcdef");
        state.MoveCaret(3);
        for (int i = 0; i < 3; i++)
        {
            state.Handle(Key(KeyCode.Backspace));
        }

        Assert.Equal("def", state.Text);
        state.Undo();
        Assert.Equal("abcdef", state.Text);
        Assert.Equal(3, state.Caret);

        for (int i = 0; i < 3; i++)
        {
            state.Handle(Key(KeyCode.Delete));
        }

        Assert.Equal("abc", state.Text);
        state.Undo();
        Assert.Equal("abcdef", state.Text);
        Assert.False(state.CanUndo);                                    // the deletes dropped the backspaces' redo
        Assert.True(state.Redo());
    }

    [Fact]
    public void Paste_keeps_line_breaks_and_expands_tabs()
    {
        var state = new TextAreaState();
        state.Insert("a\r\nb\rc\td\u0007e");
        Assert.Equal("a\nb\nc    de", state.Text);
        Assert.Equal(3, state.LineCount);
        state.Undo();
        Assert.True(state.IsEmpty);

        var input = new TextInputState();
        input.Insert("a\r\nb\tc");
        Assert.Equal("abc", input.Text);
    }

    [Fact]
    public void Clusters_are_one_step_across_lines()
    {
        var state = new TextAreaState("a\n👨‍👩‍👧é");
        state.Handle(Key(KeyCode.End, Modifiers.Ctrl));
        state.Handle(Key(KeyCode.Backspace));
        Assert.Equal("a\n👨‍👩‍👧", state.Text);
        state.Handle(Key(KeyCode.Left));
        Assert.Equal(2, state.Caret);
        state.Handle(Key(KeyCode.Left));
        Assert.Equal(1, state.Caret);                                   // before the line break
        state.MoveCaret(4);                                             // inside the family: snaps to its start
        Assert.Equal(2, state.Caret);
    }

    [Fact]
    public void Text_input_selects_with_shift_and_undoes()
    {
        var state = new TextInputState("hello world");
        state.Handle(Key(KeyCode.Home, Modifiers.Shift));
        Assert.Equal("hello world", state.Selection.ToString());
        state.Handle(KeyEvent.Char('x'));
        Assert.Equal("x", state.Text);
        state.Handle(KeyEvent.Char('z', Modifiers.Ctrl));
        Assert.Equal("hello world", state.Text);

        state.Select(1, 3);
        var buffer = new CellBuffer(12, 1);
        buffer.Render(new TextInput { Focused = true }, buffer.Area, ref state);
        Assert.Equal(Attr.None, buffer[0, 0].Style.Attrs);
        Assert.Equal(Attr.Reverse, buffer[1, 0].Style.Attrs);
        Assert.Equal(Attr.Reverse, buffer[2, 0].Style.Attrs);
        Assert.Equal(Attr.None, buffer[3, 0].Style.Attrs);
        Assert.Equal(3, buffer.CursorX);

        buffer.Render(new TextInput(), buffer.Area, ref state);         // unfocused: no selection shown
        Assert.Equal(Attr.None, buffer[1, 0].Style.Attrs);
    }

    [Fact]
    public void Text_input_drag_selects()
    {
        var state = new TextInputState("hello world");
        var buffer = new CellBuffer(20, 1);
        buffer.Render(new TextInput(), buffer.Area, ref state);
        Assert.True(state.HandleMouse(Mouse(MouseKind.Down, 2, 0)));
        Assert.True(state.HandleMouse(Mouse(MouseKind.Drag, 7, 0)));
        Assert.True(state.HandleMouse(Mouse(MouseKind.Up, 7, 0)));
        Assert.Equal("llo w", state.Selection.ToString());
        Assert.False(state.HandleMouse(Mouse(MouseKind.Drag, 9, 0)));   // the button is up
    }

    [Fact]
    public void Wraps_at_word_boundaries()
    {
        var state = new TextAreaState("the quick brown fox");
        CellBuffer buffer = Render(state, 10, 3);
        Assert.Equal("the quick ", buffer.RowText(0));
        Assert.Equal("brown fox ", buffer.RowText(1));
        Assert.Equal("          ", buffer.RowText(2));
    }

    [Fact]
    public void Wraps_wide_glyphs_and_clusters_whole()
    {
        var state = new TextAreaState("ab世界cd");
        CellBuffer buffer = Render(state, 5, 2);
        Assert.Equal("ab世 ", buffer.RowText(0));                       // 界 doesn't fit in the last column
        Assert.Equal("界cd ", buffer.RowText(1));

        state = new TextAreaState("👨‍👩‍👧👨‍👩‍👧👨‍👩‍👧");
        buffer = Render(state, 5, 2);
        Assert.Equal("👨‍👩‍👧👨‍👩‍👧 ", buffer.RowText(0));
        Assert.Equal("👨‍👩‍👧   ", buffer.RowText(1));
    }

    [Fact]
    public void Long_words_break_mid_word_and_spaces_hang_off_the_edge()
    {
        Assert.Equal(["abcde", "fgh"], Rows("abcdefgh", 5));
        Assert.Equal(["ab      ", "cd"], Rows("ab      cd", 5));    // the spaces stay on the first row
        Assert.Equal([""], Rows("", 5));
    }

    [Fact]
    public void Scrolls_to_keep_the_caret_in_view()
    {
        var state = new TextAreaState(string.Join('\n', Enumerable.Range(0, 10)));
        state.Handle(Key(KeyCode.End, Modifiers.Ctrl));
        CellBuffer buffer = Render(state, 6, 3);
        Assert.Equal(["7     ", "8     ", "9     "], Lines(buffer));
        Assert.Equal((1, 2), (buffer.CursorX, buffer.CursorY));
        Assert.Equal(7, state.TopLine);

        state.Handle(Key(KeyCode.Up));
        state.Handle(Key(KeyCode.Up));
        state.Handle(Key(KeyCode.Up));
        buffer = Render(state, 6, 3);
        Assert.Equal(6, state.TopLine);
        Assert.Equal(0, buffer.CursorY);
    }

    [Fact]
    public void Caret_stays_in_view_after_wheel_scrolls_and_edits_below_the_view()
    {
        var state = new TextAreaState(string.Join('\n', Enumerable.Range(0, 20)));
        state.MoveCaret(state.LineStart(10));
        Render(state, 6, 3);
        Assert.Equal(8, state.TopLine);                                 // caret on the bottom row
        state.HandleMouse(Mouse(MouseKind.ScrollUp, 1, 1));             // the view moves, the caret doesn't
        Render(state, 6, 3);
        Assert.Equal(5, state.TopLine);
        state.Handle(Key(KeyCode.Down));                                // line 11: below the view now
        CellBuffer buffer = Render(state, 6, 3);
        Assert.Equal(9, state.TopLine);
        Assert.Equal(2, buffer.CursorY);

        state.Insert("a\nb\nc");                                       // the caret goes to line 13
        buffer = Render(state, 6, 3);
        Assert.Equal(11, state.TopLine);
        Assert.Equal("c11   ", buffer.RowText(2));
    }

    [Fact]
    public void Up_and_down_move_by_wrapped_rows()
    {
        var state = new TextAreaState("the quick brown fox");
        Render(state, 10, 3);
        state.MoveCaret(2);
        state.Handle(Key(KeyCode.Down));
        Assert.Equal(12, state.Caret);                                  // "br|own"
        state.Handle(Key(KeyCode.Down));
        Assert.Equal(19, state.Caret);
        state.Handle(Key(KeyCode.Up));
        Assert.Equal(2, state.Caret);                                   // the column survives the trip to the end

        state.MoveCaret(19);
        state.Handle(Key(KeyCode.Up));
        Assert.Equal(9, state.Caret);                                   // column 9 of row 0: before its trailing space
    }

    [Fact]
    public void Page_down_moves_a_screen_of_rows()
    {
        var state = new TextAreaState(string.Join('\n', Enumerable.Range(0, 20)));
        Render(state, 6, 5);
        state.Handle(Key(KeyCode.PageDown));
        Assert.Equal(5, state.CaretLine);
        state.Handle(Key(KeyCode.PageDown, Modifiers.Shift));
        Assert.Equal(10, state.CaretLine);
        Assert.Equal(state.LineStart(5), state.SelectionStart);
        state.Handle(Key(KeyCode.PageUp));
        Assert.Equal(5, state.CaretLine);
    }

    [Fact]
    public void Without_wrap_long_lines_scroll_sideways()
    {
        var state = new TextAreaState("abcdefghijklmnop\nxy") { SoftWrap = false };
        state.Handle(Key(KeyCode.End));
        CellBuffer buffer = Render(state, 5, 2);
        Assert.Equal("mnop ", buffer.RowText(0));
        Assert.Equal("     ", buffer.RowText(1));                       // "xy" is scrolled off too
        Assert.Equal(4, buffer.CursorX);

        state.Handle(Key(KeyCode.Home));
        buffer = Render(state, 5, 2);
        Assert.Equal("abcde", buffer.RowText(0));
        Assert.Equal("xy   ", buffer.RowText(1));
    }

    [Fact]
    public void Wide_glyph_cut_by_the_left_edge_leaves_a_blank()
    {
        var state = new TextAreaState("世世世abc") { SoftWrap = false };
        state.Handle(Key(KeyCode.End));                                 // caret at column 9: scrolled by 5
        CellBuffer buffer = Render(state, 5, 1);
        Assert.Equal(" abc ", buffer.RowText(0));                       // the third 世's right half is column 5
        Assert.Equal(4, buffer.CursorX);
    }

    [Fact]
    public void Line_numbers_go_in_a_gutter_on_first_rows()
    {
        var state = new TextAreaState(string.Join('\n', ["the quick brown", "b", "c", "d", "e", "f", "g", "h", "i", "j"]));
        var buffer = new CellBuffer(12, 4);
        buffer.Render(new TextArea { LineNumbers = true, Focused = true }, buffer.Area, ref state);
        Assert.Equal([" 1 the quick", "   brown    ", " 2 b        ", " 3 c        "], Lines(buffer));
        Assert.Equal(new Rect(3, 0, 9, 4), state.Area);
        Assert.Equal(3, buffer.CursorX);
    }

    [Fact]
    public void Placeholder_shows_while_empty()
    {
        var state = new TextAreaState();
        var buffer = new CellBuffer(10, 2);
        buffer.Render(new TextArea { Placeholder = "notes…" }, buffer.Area, ref state);
        Assert.Equal("notes…    ", buffer.RowText(0));
        Assert.False(buffer.CursorVisible);
    }

    [Fact]
    public void Selection_is_drawn_across_the_line_break()
    {
        var state = new TextAreaState("hello\nworld");
        state.Select(3, 8);
        CellBuffer buffer = Render(state, 8, 2);
        Attr[] row0 = [.. Enumerable.Range(0, 8).Select(x => buffer[x, 0].Style.Attrs)];
        Attr[] row1 = [.. Enumerable.Range(0, 8).Select(x => buffer[x, 1].Style.Attrs)];
        Attr r = Attr.Reverse;
        Attr n = Attr.None;
        Assert.Equal([n, n, n, r, r, r, n, n], row0);                   // "lo" and the line break
        Assert.Equal([r, r, n, n, n, n, n, n], row1);
        Assert.Equal((2, 1), (buffer.CursorX, buffer.CursorY));
    }

    [Fact]
    public void Mouse_places_the_caret_on_wrapped_rows_and_drags_select()
    {
        var state = new TextAreaState("the quick brown fox\nsecond");
        Render(state, 10, 4);
        state.HandleMouse(Mouse(MouseKind.Down, 2, 1));
        Assert.Equal(12, state.Caret);
        state.HandleMouse(Mouse(MouseKind.Down, 9, 0));
        Assert.Equal(9, state.Caret);                                   // the space at the end of row 0
        state.HandleMouse(Mouse(MouseKind.Down, 9, 1));
        Assert.Equal(19, state.Caret);                                  // past the line's last row: its end
        state.HandleMouse(Mouse(MouseKind.Down, 8, 3));
        Assert.Equal(state.Length, state.Caret);                        // below the text: the end

        state.HandleMouse(Mouse(MouseKind.Down, 0, 0));
        state.HandleMouse(Mouse(MouseKind.Drag, 4, 1));
        state.HandleMouse(Mouse(MouseKind.Up, 4, 1));
        Assert.Equal("the quick brow", state.Selection.ToString());

        state.HandleMouse(Mouse(MouseKind.Down, 3, 2, Modifiers.Shift));
        Assert.Equal("the quick brown fox\nsec", state.Selection.ToString());
    }

    [Fact]
    public void Wheel_scrolls_without_moving_the_caret_until_it_moves()
    {
        var state = new TextAreaState(string.Join('\n', Enumerable.Range(0, 20)));
        Render(state, 6, 3);
        Assert.True(state.HandleMouse(Mouse(MouseKind.ScrollDown, 1, 1)));
        Render(state, 6, 3);
        Assert.Equal(3, state.TopLine);
        Assert.Equal(0, state.Caret);

        for (int i = 0; i < 10; i++)
        {
            state.HandleMouse(Mouse(MouseKind.ScrollDown, 1, 1));
        }

        Assert.Equal(17, state.TopLine);                                // stops with the last line at the bottom

        state.Handle(KeyEvent.Char('x'));
        Render(state, 6, 3);
        Assert.Equal(0, state.TopLine);
    }

    [Fact]
    public void Edits_keep_the_line_index_consistent()
    {
        var random = new Random(8);
        const string alphabet = "ab \n\n世é👍́";
        string initial = "one\ntwo\nthree";
        var state = new TextAreaState(initial);
        var typed = new StringBuilder();
        for (int step = 0; step < 600; step++)
        {
            switch (random.Next(9))
            {
                case 0:
                case 1:
                    typed.Clear();
                    int count = random.Next(1, 6);
                    for (int i = 0; i < count; i++)
                    {
                        typed.Append(char.ConvertFromUtf32(char.ConvertToUtf32(alphabet, PickRuneStart(alphabet, random))));
                    }

                    state.Insert(typed.ToString());
                    break;
                case 2:
                    state.Handle(KeyEvent.Char("xy z"[random.Next(4)]));
                    break;
                case 3:
                    state.Handle(Key(KeyCode.Enter));
                    break;
                case 4:
                    state.Handle(Key(random.Next(2) == 0 ? KeyCode.Backspace : KeyCode.Delete, random.Next(4) == 0 ? Modifiers.Ctrl : Modifiers.None));
                    break;
                case 5:
                    state.Select(random.Next(state.Length + 1), random.Next(state.Length + 1));
                    break;
                case 6:
                    state.MoveCaret(random.Next(state.Length + 1));
                    break;
                case 7:
                    state.Undo();
                    break;
                default:
                    state.Redo();
                    break;
            }

            AssertConsistent(state);
        }

        string final = state.Text;
        while (state.Undo())
        {
            AssertConsistent(state);
        }

        Assert.Equal(initial, state.Text);
        while (state.Redo())
        {
        }

        Assert.Equal(final, state.Text);
    }

    [Fact]
    public void Large_text_renders_and_edits_by_its_visible_lines()
    {
        string text = string.Join('\n', Enumerable.Range(0, 100_000).Select(i => $"line {i}"));
        var state = new TextAreaState(text);
        Assert.Equal(100_000, state.LineCount);
        state.Handle(Key(KeyCode.End, Modifiers.Ctrl));
        CellBuffer buffer = Render(state, 20, 24);
        Assert.StartsWith("line 99999", buffer.RowText(23));

        state.Handle(Key(KeyCode.Home, Modifiers.Ctrl));
        Type(state, "x");
        state.Handle(Key(KeyCode.Enter));
        Assert.Equal(100_001, state.LineCount);
        Assert.Equal("line 99999", state.Line(100_000).ToString());
        Assert.Equal(state.Length - "line 99999".Length, state.LineStart(100_000));
        Assert.Equal(50_001, state.LineOf(state.LineStart(50_001) + 3));
    }

    [Fact]
    public void Undo_history_is_bounded()
    {
        var state = new TextAreaState();
        for (int i = 0; i < 1500; i++)
        {
            state.Insert("a");                                          // each paste is its own step
        }

        int undone = 0;
        while (state.Undo())
        {
            undone++;
        }

        Assert.InRange(undone, 750, 1000);
        Assert.Equal(1500 - undone, state.Length);
    }

    private static void AssertConsistent(TextAreaState state)
    {
        string text = state.Text;
        string[] lines = text.Split('\n');
        Assert.Equal(lines.Length, state.LineCount);
        int start = 0;
        for (int i = 0; i < lines.Length; i++)
        {
            Assert.Equal(start, state.LineStart(i));
            Assert.Equal(lines[i], state.Line(i).ToString());
            start += lines[i].Length + 1;
        }

        Assert.InRange(state.Caret, 0, text.Length);
        Assert.Equal(text[..state.Caret].Count(c => c == '\n'), state.CaretLine);
    }

    private static int PickRuneStart(string text, Random random)
    {
        int i = random.Next(text.Length);
        return char.IsLowSurrogate(text[i]) ? i - 1 : i;
    }

    private static string[] Rows(string line, int width)
    {
        var rows = new List<string>();
        int start = 0;
        do
        {
            int end = TextAreaState.RowEnd(line, start, width);
            rows.Add(line[start..end]);
            start = end;
        }
        while (start < line.Length);
        return [.. rows];
    }

    private static CellBuffer Render(TextAreaState state, int width, int height)
    {
        var buffer = new CellBuffer(width, height);
        buffer.Render(new TextArea { Focused = true }, buffer.Area, ref state);
        return buffer;
    }

    private static string[] Lines(CellBuffer buffer) => [.. Enumerable.Range(0, buffer.Height).Select(buffer.RowText)];

    private static void Type(EditableText state, string text)
    {
        foreach (char c in text)
        {
            if (state is TextAreaState area)
            {
                area.Handle(KeyEvent.Char(c));
            }
            else
            {
                ((TextInputState)state).Handle(KeyEvent.Char(c));
            }
        }
    }

    private static KeyEvent Key(KeyCode code, Modifiers modifiers = Modifiers.None) => new(code, default, modifiers);

    private static MouseEvent Mouse(MouseKind kind, int x, int y, Modifiers modifiers = Modifiers.None) =>
        new(kind, kind is MouseKind.Down or MouseKind.Up or MouseKind.Drag ? MouseButton.Left : MouseButton.None, x, y, modifiers);
}
