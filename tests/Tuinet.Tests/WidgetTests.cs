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
