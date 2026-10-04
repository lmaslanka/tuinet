using System.Text;
using Tuinet.Widgets;

namespace Tuinet.Tests;

public class WidgetTests
{
    [Fact]
    public void Block_draws_corners_edges_and_title()
    {
        var buffer = new CellBuffer(12, 4);
        buffer.Render(new Block { Title = "files" }, buffer.Area);
        Assert.Equal("┌─files────┐", buffer.RowText(0));
        Assert.Equal("│          │", buffer.RowText(1));
        Assert.Equal("└──────────┘", buffer.RowText(3));
    }

    [Fact]
    public void Block_inner_excludes_borders()
    {
        var block = new Block { Borders = Borders.Top | Borders.Left };
        Assert.Equal(new Rect(3, 2, 9, 7), block.Inner(new Rect(2, 1, 10, 8)));
        Assert.Equal(new Rect(3, 2, 8, 6), new Block().Inner(new Rect(2, 1, 10, 8)));
    }

    [Fact]
    public void Rounded_block_with_right_footer()
    {
        var buffer = new CellBuffer(12, 3);
        buffer.Render(new Block { BorderType = BorderType.Rounded, Footer = "( *)", FooterAlignment = Alignment.Right }, buffer.Area);
        Assert.Equal("╭──────────╮", buffer.RowText(0));
        Assert.Equal("╰─────( *)─╯", buffer.RowText(2));
    }

    [Fact]
    public void Block_title_is_clipped_inside_the_corners()
    {
        var buffer = new CellBuffer(6, 3);
        buffer.Render(new Block { Title = "a long title" }, buffer.Area);
        Assert.Equal("┌a lo┐", buffer.RowText(0));
    }

    [Fact]
    public void Block_style_layers_background()
    {
        var buffer = new CellBuffer(4, 3);
        buffer.Render(new Block { Style = new Style(Color.Default, Color.Blue), BorderStyle = new Style(Color.Red, Color.Default) }, buffer.Area);
        Assert.Equal(new Style(Color.Red, Color.Blue), buffer[0, 0].Style);
        Assert.Equal(new Style(Color.Default, Color.Blue), buffer[1, 1].Style);
    }

    [Fact]
    public void Paragraph_word_wraps()
    {
        var buffer = new CellBuffer(10, 4);
        buffer.Render(new Paragraph("the quick brown fox") { Wrap = TextWrap.Word }, buffer.Area);
        Assert.Equal("the quick ", buffer.RowText(0));
        Assert.Equal("brown fox ", buffer.RowText(1));
    }

    [Fact]
    public void Paragraph_char_wrap_newlines_and_scroll()
    {
        var buffer = new CellBuffer(4, 3);
        buffer.Render(new Paragraph("abcdef\ngh") { Wrap = TextWrap.Char, Scroll = 1 }, buffer.Area);
        Assert.Equal("ef  \ngh  \n    ", buffer.ToString());
        Assert.Equal(3, Paragraph.LineCount("abcdef\ngh", 4, TextWrap.Char));
    }

    [Fact]
    public void Paragraph_centers_lines()
    {
        var buffer = new CellBuffer(7, 1);
        buffer.Render(new Paragraph("abc") { Alignment = Alignment.Center }, buffer.Area);
        Assert.Equal("  abc  ", buffer.RowText(0));
    }

    [Fact]
    public void List_scrolls_to_keep_selection_visible_and_highlights_it()
    {
        string[] items = ["zero", "one", "two", "three", "four"];
        var highlight = new Style(Color.Black, Color.White);
        var state = new ListState(selected: 3);
        var buffer = new CellBuffer(8, 2);

        buffer.Render(new ListView<TextItems>(new TextItems(items)) { SelectedStyle = highlight, HighlightSymbol = "> " }, buffer.Area, ref state);

        Assert.Equal(2, state.Offset);
        Assert.Equal("  two   ", buffer.RowText(0));
        Assert.Equal("> three ", buffer.RowText(1));
        Assert.Equal(highlight, buffer[7, 1].Style);
        Assert.Equal(Style.Default, buffer[7, 0].Style);
    }

    [Fact]
    public void List_state_navigation_clamps()
    {
        var state = new ListState();
        state.Previous(3);
        Assert.Equal(0, state.Selected);
        state.Last(3);
        state.Next(3);
        Assert.Equal(2, state.Selected);
    }

    [Fact]
    public void Text_items_truncate_with_ellipsis()
    {
        var state = new ListState(-1);
        var buffer = new CellBuffer(5, 1);
        buffer.Render(new ListView<TextItems>(new TextItems(["abcdefgh"])), buffer.Area, ref state);
        Assert.Equal("abcd…", buffer.RowText(0));
    }

    [Fact]
    public void Text_input_edits_and_places_cursor()
    {
        var state = new TextInputState();
        foreach (char c in "hello world")
        {
            state.Handle(KeyEvent.Char(c));
        }

        state.Handle(KeyEvent.Char('w', Modifiers.Ctrl));
        Assert.Equal("hello ", state.Text);
        state.Handle(new KeyEvent(KeyCode.Left, default, Modifiers.Ctrl));
        Assert.Equal(0, state.Caret);
        state.Handle(new KeyEvent(KeyCode.End));
        state.Handle(new KeyEvent(KeyCode.Backspace));
        Assert.Equal("hello", state.Text);
        Assert.False(state.Handle(new KeyEvent(KeyCode.Enter)));

        var buffer = new CellBuffer(10, 1);
        buffer.Render(new TextInput { Focused = true }, buffer.Area, ref state);
        Assert.Equal("hello     ", buffer.RowText(0));
        Assert.Equal(5, buffer.CursorX);
        Assert.Equal(0, buffer.CursorY);
    }

    [Fact]
    public void Text_input_scrolls_to_keep_caret_visible()
    {
        var state = new TextInputState("abcdefghij");
        var buffer = new CellBuffer(5, 1);
        buffer.Render(new TextInput { Focused = true }, buffer.Area, ref state);
        Assert.Equal("ghij ", buffer.RowText(0));
        Assert.Equal(4, buffer.CursorX);

        state.Handle(new KeyEvent(KeyCode.Home));
        buffer.Clear();
        buffer.Render(new TextInput { Focused = true }, buffer.Area, ref state);
        Assert.Equal("abcde", buffer.RowText(0));
        Assert.Equal(0, buffer.CursorX);
    }

    [Fact]
    public void Unfocused_text_input_shows_the_beginning()
    {
        var state = new TextInputState("abcdefghij");
        var buffer = new CellBuffer(5, 1);
        buffer.Render(new TextInput(), buffer.Area, ref state);
        Assert.Equal("abcde", buffer.RowText(0));
        Assert.False(buffer.CursorVisible);
    }

    [Fact]
    public void Masked_input_never_draws_the_secret()
    {
        var state = new TextInputState("secret", mask: '•');
        var buffer = new CellBuffer(10, 1);
        buffer.Render(new TextInput(), buffer.Area, ref state);
        Assert.Equal("••••••    ", buffer.RowText(0));
        Assert.Equal("secret", state.Text);
        Assert.False(buffer.CursorVisible);
    }

    [Fact]
    public void Text_input_shows_placeholder_when_empty()
    {
        var state = new TextInputState();
        var buffer = new CellBuffer(8, 1);
        buffer.Render(new TextInput { Placeholder = "search" }, buffer.Area, ref state);
        Assert.Equal("search  ", buffer.RowText(0));
    }

    [Fact]
    public void Text_input_paste_drops_controls()
    {
        var state = new TextInputState();
        state.Insert("a\nb\u001bc");
        Assert.Equal("abc", state.Text);
    }

    [Fact]
    public void Button_pads_label_and_uses_focused_style()
    {
        var focused = new Style(Color.Black, Color.Cyan);
        var buffer = new CellBuffer(12, 1);
        buffer.Render(new Button("Save") { Focused = true, FocusedStyle = focused }, buffer.Area);
        Assert.Equal("  Save      ", buffer.RowText(0));
        Assert.Equal(focused, buffer[7, 0].Style);
        Assert.Equal(Style.Default, buffer[8, 0].Style);
        Assert.Equal(8, Button.WidthOf("Save"));
    }

    [Fact]
    public void Clear_blanks_an_area()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.SetString(0, 0, "abcd");
        buffer.Render(new Clear(Style.Default), new Rect(1, 0, 2, 1));
        Assert.Equal("a  d", buffer.RowText(0));
    }
}

public class InputWidgetTests
{
    [Fact]
    public void Checkbox_shows_state_and_styles()
    {
        var accent = new Style(Color.Green, default);
        var buffer = new CellBuffer(14, 2);
        buffer.Render(new Checkbox("enabled", true) { CheckedStyle = accent }, new Rect(0, 0, 14, 1));
        buffer.Render(new Checkbox("notify", false) { CheckedSymbol = "◆", UncheckedSymbol = "◇" }, new Rect(0, 1, 14, 1));
        Assert.Equal("[x] enabled   ", buffer.RowText(0));
        Assert.Equal(accent, buffer[1, 0].Style);
        Assert.Equal(Style.Default, buffer[5, 0].Style);
        Assert.Equal("◇ notify      ", buffer.RowText(1));
    }

    [Fact]
    public void Boxed_checkbox_is_a_filled_square()
    {
        var green = new Style(Color.Green, default);
        var buffer = new CellBuffer(14, 3);
        buffer.Render(new Checkbox("enabled", true) { Boxed = true, CheckedStyle = green }, buffer.Area);
        Assert.Equal("╭───╮         \n│███│ enabled \n╰───╯         ", buffer.ToString());
        Assert.Equal(Color.Green, buffer[0, 0].Style.Fg);
        Assert.Equal(Color.Green, buffer[2, 1].Style.Fg);

        buffer.Clear();
        buffer.Render(new Checkbox("enabled", false) { Boxed = true, CheckedStyle = green }, buffer.Area);
        Assert.Equal("│   │ enabled ", buffer.RowText(1));
        Assert.Equal(Color.Default, buffer[0, 0].Style.Fg);
    }

    [Fact]
    public void Unchecked_style_colors_only_the_symbol()
    {
        var faint = new Style(Color.BrightBlack, default);
        var buffer = new CellBuffer(12, 1);
        buffer.Render(new Checkbox("notify", false) { CheckedSymbol = "▐█▌", UncheckedSymbol = "▐█▌", UncheckedStyle = faint }, buffer.Area);
        Assert.Equal("▐█▌ notify  ", buffer.RowText(0));
        Assert.Equal(Color.BrightBlack, buffer[1, 0].Style.Fg);
        Assert.Equal(Color.Default, buffer[4, 0].Style.Fg);
    }

    [Fact]
    public void Boxed_checkbox_falls_back_to_one_row_when_short()
    {
        var buffer = new CellBuffer(14, 1);
        buffer.Render(new Checkbox("enabled", true) { Boxed = true }, buffer.Area);
        Assert.Equal("[x] enabled   ", buffer.RowText(0));
    }

    [Theory]
    [InlineData(0.0, "░░░░░░░░░░")]
    [InlineData(0.5, "█████░░░░░")]
    [InlineData(1.0, "██████████")]
    [InlineData(0.55, "█████▌░░░░")]
    [InlineData(2.0, "██████████")]
    [InlineData(double.NaN, "░░░░░░░░░░")]
    public void Progress_bar_fills_with_eighth_precision(double ratio, string expected)
    {
        var buffer = new CellBuffer(10, 1);
        buffer.Render(new ProgressBar(ratio), buffer.Area);
        Assert.Equal(expected, buffer.RowText(0));
    }

    [Fact]
    public void Progress_bar_segments_without_smoothing()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.Render(new ProgressBar(0.6) { FilledChar = '▮', EmptyChar = '▯' }, buffer.Area);
        Assert.Equal("▮▮▯▯", buffer.RowText(0));
    }

    [Fact]
    public void Spinner_frame_follows_time()
    {
        Assert.Equal('|', Spinner.Frame(Spinner.Line, 0));
        Assert.Equal('/', Spinner.Frame(Spinner.Line, 100));
        Assert.Equal('|', Spinner.Frame(Spinner.Line, 400));
    }

    [Fact]
    public void Dropdown_opens_moves_commits_and_cancels()
    {
        string[] items = ["low", "medium", "high"];
        var state = new DropdownState(0);
        Assert.False(state.Handle(new KeyEvent(KeyCode.Tab), 3));
        Assert.True(state.Handle(new KeyEvent(KeyCode.Enter), 3));
        Assert.True(state.IsOpen);
        state.Handle(new KeyEvent(KeyCode.Down), 3);
        state.Handle(new KeyEvent(KeyCode.Down), 3);
        state.Handle(new KeyEvent(KeyCode.Enter), 3);
        Assert.False(state.IsOpen);
        Assert.Equal(2, state.Selected);

        state.Handle(new KeyEvent(KeyCode.Enter), 3);
        state.Handle(new KeyEvent(KeyCode.Up), 3);
        state.Handle(new KeyEvent(KeyCode.Escape), 3);
        Assert.Equal(2, state.Selected);

        var buffer = new CellBuffer(10, 5);
        buffer.Render(new Dropdown<TextItems>(new TextItems(items)), new Rect(0, 0, 10, 1), ref state);
        Assert.Equal("high     ▾", buffer.RowText(0));
    }

    [Fact]
    public void Dropdown_popup_draws_below_the_anchor_on_top()
    {
        string[] items = ["low", "medium", "high"];
        var state = new DropdownState(1);
        state.Open();
        var buffer = new CellBuffer(10, 6);
        buffer.SetString(0, 1, "underneath");
        var dropdown = new Dropdown<TextItems>(new TextItems(items)) { SelectedStyle = new Style(Color.Black, Color.White) };
        dropdown.Render(new Rect(0, 0, 10, 1), buffer, ref state);
        dropdown.RenderPopup(new Rect(0, 0, 10, 1), buffer, ref state);
        Assert.Equal("medium   ▴", buffer.RowText(0));
        Assert.Equal("low       ", buffer.RowText(1));
        Assert.Equal("medium    ", buffer.RowText(2));
        Assert.Equal(new Style(Color.Black, Color.White), buffer[0, 2].Style);
    }

    [Fact]
    public void Dropdown_popup_flips_above_when_no_room_below()
    {
        string[] items = ["a", "b", "c"];
        var state = new DropdownState(0);
        state.Open();
        var buffer = new CellBuffer(6, 6);
        new Dropdown<TextItems>(new TextItems(items)).RenderPopup(new Rect(0, 5, 6, 1), buffer, ref state);
        Assert.Equal("a     \nb     \nc     ", string.Join('\n', buffer.ToString().Split('\n')[2..5]));
    }

    [Fact]
    public void Text_input_moves_and_deletes_by_grapheme_cluster()
    {
        var state = new TextInputState("a👨‍👩‍👧e\u0301");
        Assert.Equal(1 + 5 + 2, state.Length);                       // runes
        state.Handle(new KeyEvent(KeyCode.Left));
        Assert.Equal(6, state.Caret);                                 // before "é", not between e and the accent
        state.Handle(new KeyEvent(KeyCode.Left));
        Assert.Equal(1, state.Caret);                                 // the whole family is one step
        state.Handle(new KeyEvent(KeyCode.Delete));
        Assert.Equal("ae\u0301", state.Text);
        state.Handle(new KeyEvent(KeyCode.End));
        state.Handle(new KeyEvent(KeyCode.Backspace));
        Assert.Equal("a", state.Text);
    }

    [Fact]
    public void Text_input_keeps_marks_and_joiners_that_are_typed()
    {
        var state = new TextInputState();
        foreach (char c in "e\u0301❤\uFE0F")
        {
            state.Insert(new Rune(c));
        }

        state.Insert(new Rune(0x07));                                 // controls are still dropped
        Assert.Equal("e\u0301❤️", state.Text);

        var buffer = new CellBuffer(6, 1);
        buffer.Render(new TextInput { Focused = true }, buffer.Area, ref state);
        Assert.Equal("e\u0301❤️   ", buffer.RowText(0));
        Assert.Equal(3, buffer.CursorX);                              // 1 + 2 columns
    }

    [Fact]
    public void Masked_text_input_shows_one_mask_per_cluster()
    {
        var state = new TextInputState("👨‍👩‍👧é", mask: '•');
        var buffer = new CellBuffer(4, 1);
        buffer.Render(new TextInput(), buffer.Area, ref state);
        Assert.Equal("••  ", buffer.RowText(0));
    }

    [Fact]
    public void Text_input_scrolls_by_cluster_to_keep_the_caret_visible()
    {
        var state = new TextInputState("🇵🇱🇵🇱🇵🇱🇵🇱");                       // 8 columns
        var buffer = new CellBuffer(5, 1);
        buffer.Render(new TextInput { Focused = true }, buffer.Area, ref state);
        Assert.Equal("🇵🇱🇵🇱 ", buffer.RowText(0));
        Assert.Equal(4, buffer.CursorX);
        Assert.Equal(4, state.Scroll);                                // two flags (4 runes) scrolled off
    }

    [Fact]
    public void Paragraph_wraps_between_clusters_only()
    {
        var buffer = new CellBuffer(3, 3);
        buffer.Render(new Paragraph("ae\u0301👨‍👩‍👧b") { Wrap = TextWrap.Char }, buffer.Area);
        Assert.Equal("ae\u0301 ", buffer.RowText(0));                // the family doesn't fit in the last column
        Assert.Equal("👨‍👩‍👧b", buffer.RowText(1));
    }

    [Fact]
    public void Table_right_aligns_clusters_by_their_width()
    {
        var state = new ListState(-1);
        var buffer = new CellBuffer(5, 2);
        buffer.Render(new Table<TextRows>(new TextRows([["🇵🇱e\u0301"]]), [new TableColumn("c", Constraint.Fill(), Alignment.Right)]), buffer.Area, ref state);
        Assert.Equal("  🇵🇱e\u0301", buffer.RowText(1));
    }

    [Fact]
    public void Dashed_block()
    {
        var buffer = new CellBuffer(5, 3);
        buffer.Render(new Block { BorderType = BorderType.Dashed }, buffer.Area);
        Assert.Equal("╭┄┄┄╮\n┆   ┆\n╰┄┄┄╯", buffer.ToString());
    }

    [Fact]
    public void Table_aligns_header_and_cells_per_column()
    {
        TableColumn[] columns =
        [
            new("name", Constraint.Length(6)),
            new("qty", Constraint.Length(5), Alignment.Right),
            new("st", Constraint.Length(4), Alignment.Center),
        ];
        string[][] rows = [["apple", "3", "ok"], ["fig", "120", "bad"]];
        var state = new ListState(-1);
        var buffer = new CellBuffer(17, 3);

        buffer.Render(new Table<TextRows>(new TextRows(rows), columns), buffer.Area, ref state);

        Assert.Equal("name     qty  st ", buffer.RowText(0));
        Assert.Equal("apple      3  ok ", buffer.RowText(1));
        Assert.Equal("fig      120 bad ", buffer.RowText(2));
    }

    [Fact]
    public void Table_cells_wider_than_the_column_are_cut()
    {
        TableColumn[] columns =
        [
            new("n", Constraint.Length(4), Alignment.Right),
            new("s", Constraint.Length(4), Alignment.Left, Overflow.Clip),
        ];
        var state = new ListState(-1);
        var buffer = new CellBuffer(9, 2);

        buffer.Render(new Table<TextRows>(new TextRows([["123456", "abcdef"]]), columns), buffer.Area, ref state);

        Assert.Equal("   n s   ", buffer.RowText(0));
        Assert.Equal("123… abcd", buffer.RowText(1));
    }

    [Fact]
    public void Table_scrolls_under_a_fixed_header_and_highlights_the_selection()
    {
        string[][] rows = [["r0"], ["r1"], ["r2"], ["r3"], ["r4"]];
        var highlight = new Style(Color.Black, Color.White);
        var state = new ListState(selected: 3);
        var buffer = new CellBuffer(6, 3);

        buffer.Render(new Table<TextRows>(new TextRows(rows), [new TableColumn("id", Constraint.Fill())])
        {
            SelectedStyle = highlight,
            HighlightSymbol = "> ",
        }, buffer.Area, ref state);

        Assert.Equal(2, state.Offset);
        Assert.Equal(2, state.Viewport);
        Assert.Equal("  id  \n  r2  \n> r3  ", buffer.ToString());
        Assert.Equal(highlight, buffer[5, 2].Style);
        Assert.Equal(Style.Default, buffer[5, 1].Style);
    }

    [Fact]
    public void Table_draws_separators_header_rule_and_stripes()
    {
        TableColumn[] columns = [new("a", Constraint.Length(2)), new("b", Constraint.Length(2))];
        var stripe = new Style(Color.Default, Color.Blue);
        var state = new ListState(-1);
        var buffer = new CellBuffer(5, 4);

        buffer.Render(new Table<TextRows>(new TextRows([["x", "y"], ["z", "w"]]), columns)
        {
            ColumnSeparator = '│',
            HeaderSeparator = true,
            AlternateRowStyle = stripe,
        }, buffer.Area, ref state);

        Assert.Equal("a │b \n──┼──\nx │y \nz │w ", buffer.ToString());
        Assert.Equal(Color.Default, buffer[0, 2].Style.Bg);
        Assert.Equal(Color.Blue, buffer[0, 3].Style.Bg);
        Assert.Equal(Color.Blue, buffer[2, 3].Style.Bg);   // the separator keeps the stripe
    }

    [Fact]
    public void Table_sort_arrow_sits_on_the_far_side_of_the_alignment()
    {
        TableColumn[] columns = [new("name", Constraint.Length(8)), new("size", Constraint.Length(8), Alignment.Right)];
        var state = new ListState(-1);
        var ascending = new CellBuffer(17, 1);
        var descending = new CellBuffer(17, 1);

        ascending.Render(new Table<TextRows>(new TextRows([]), columns) { SortColumn = 0 }, ascending.Area, ref state);
        descending.Render(new Table<TextRows>(new TextRows([]), columns) { SortColumn = 1, SortDescending = true }, descending.Area, ref state);

        Assert.Equal("name ▲  " + " " + "    size", ascending.RowText(0));
        Assert.Equal("name    " + " " + "  ▼ size", descending.RowText(0));
    }

    [Fact]
    public void Table_right_aligns_wide_text_by_display_width()
    {
        var state = new ListState(-1);
        var buffer = new CellBuffer(6, 2);
        buffer.Render(new Table<TextRows>(new TextRows([["日本"]]), [new TableColumn("w", Constraint.Length(6), Alignment.Right)]), buffer.Area, ref state);
        Assert.Equal("     w", buffer.RowText(0));
        Assert.Equal("  日本", buffer.RowText(1));
        Assert.Equal(2, buffer[2, 1].Width);
    }

    [Fact]
    public void Table_cells_can_be_formatted_into_scratch_with_their_own_style()
    {
        var state = new ListState(-1);
        var buffer = new CellBuffer(5, 3);
        buffer.Render(new Table<Numbers>(new Numbers([42, -7]), [new TableColumn("n", Constraint.Fill(), Alignment.Right)]), buffer.Area, ref state);
        Assert.Equal("   42", buffer.RowText(1));
        Assert.Equal("   -7", buffer.RowText(2));
        Assert.Equal(Color.Default, buffer[4, 1].Style.Fg);
        Assert.Equal(Color.Red, buffer[4, 2].Style.Fg);
    }

    [Fact]
    public void Table_measure_returns_the_widest_cell_or_header()
    {
        var rows = new TextRows([["a"], ["abcd"], ["ab"]]);
        Assert.Equal(4, Table.Measure(rows, 0, "id"));
        Assert.Equal(6, Table.Measure(rows, 0, "header"));
        Assert.Equal(1, Table.Measure(rows, 0, maxRows: 1));
        Assert.Equal(3, Table.Measure(new Numbers([5, -12]), 0));
    }

    [Fact]
    public void Table_handles_no_rows_no_columns_and_no_space()
    {
        var state = new ListState(0);
        var buffer = new CellBuffer(4, 2);
        buffer.Render(new Table<TextRows>(new TextRows([]), [new TableColumn("id", Constraint.Fill())]), buffer.Area, ref state);
        Assert.Equal("id  \n    ", buffer.ToString());
        Assert.Equal(-1, state.Selected);

        buffer.Render(new Table<TextRows>(new TextRows([["x"]]), []), buffer.Area, ref state);
        buffer.Render(new Table<TextRows>(new TextRows([["x"]]), [new TableColumn("id", Constraint.Fill())]), new Rect(0, 0, 4, 0), ref state);
        buffer.Render(new Table<TextRows>(new TextRows([["x"]]), [new TableColumn("id", Constraint.Fill())]) { HighlightSymbol = "> " }, new Rect(0, 0, 1, 2), ref state);
    }

    private readonly struct Numbers(int[] values) : ITableSource
    {
        public int RowCount => values.Length;

        public ReadOnlySpan<char> Cell(int row, int column, Span<char> scratch, out Style style)
        {
            int value = values[row];
            style = value < 0 ? new Style(Color.Red, Color.Default) : default;
            value.TryFormat(scratch, out int written);
            return scratch[..written];
        }
    }
}
