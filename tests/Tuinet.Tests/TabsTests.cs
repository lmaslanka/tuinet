using Tuinet.Widgets;

namespace Tuinet.Tests;

public class TabsTests
{
    private static readonly string[] Three = ["one", "two", "three"];
    private static readonly string[] Five = ["alpha", "beta", "gamma", "delta", "eps"];   // 7, 6, 7, 7, 5 wide
    private static readonly Style Selected = new(Color.Black, Color.White);
    private static readonly Style Divider = new(Color.Blue, Color.Default);

    private static Tabs Bar(string[] titles) => new(titles) { SelectedStyle = Selected, DividerStyle = Divider };

    [Fact]
    public void Draws_padded_titles_between_dividers()
    {
        var buffer = new CellBuffer(22, 1);
        var state = new TabsState(1);
        buffer.Render(Bar(Three), buffer.Area, ref state);
        Assert.Equal(" one │ two │ three    ", buffer.RowText(0));
        Assert.Equal(default, buffer[0, 0].Style);
        Assert.Equal(Divider, buffer[5, 0].Style);
        Assert.Equal(Selected, buffer[6, 0].Style);       // the padding is part of the tab
        Assert.Equal(Selected, buffer[10, 0].Style);
        Assert.Equal(default, buffer[12, 0].Style);
        Assert.Equal(19, Bar(Three).Width);
    }

    [Fact]
    public void Leaves_the_rest_of_the_row_alone()
    {
        var buffer = new CellBuffer(24, 2);
        buffer.SetString(0, 0, new string('─', 24));
        var state = new TabsState();
        buffer.Render(new Tabs(Three) { Divider = "", Padding = 0 }, new Rect(2, 0, 20, 2), ref state);
        Assert.Equal("──onetwothree───────────", buffer.RowText(0));
        Assert.Equal(new Rect(2, 0, 20, 1), state.Area);  // one row, whatever the area's height
    }

    [Fact]
    public void Overflow_shows_arrows_and_cuts_the_last_visible_tab()
    {
        var buffer = new CellBuffer(12, 1);
        var state = new TabsState();
        buffer.Render(Bar(Five), buffer.Area, ref state);
        Assert.Equal("  alpha │ b›", buffer.RowText(0));
    }

    [Fact]
    public void Selecting_a_hidden_tab_scrolls_it_into_view()
    {
        var buffer = new CellBuffer(12, 1);
        var state = new TabsState();
        buffer.Render(Bar(Five), buffer.Area, ref state);
        state.Select(4, Five.Length);
        buffer.Clear();                                    // as BeginFrame does: the bar draws only its tabs
        buffer.Render(Bar(Five), buffer.Area, ref state);
        Assert.Equal(4, state.Offset);
        Assert.Equal("‹ eps       ", buffer.RowText(0));

        state.Select(2, Five.Length);
        buffer.Clear();
        buffer.Render(Bar(Five), buffer.Area, ref state);
        Assert.Equal(2, state.Offset);
        Assert.Equal("‹ gamma │ d›", buffer.RowText(0));
    }

    [Fact]
    public void Offset_only_follows_the_selection_when_it_changes()
    {
        var buffer = new CellBuffer(12, 1);
        var state = new TabsState();
        buffer.Render(Bar(Five), buffer.Area, ref state);
        state.Offset = 3;                                  // e.g. scrolled with the wheel
        buffer.Render(Bar(Five), buffer.Area, ref state);
        Assert.Equal(3, state.Offset);
        state.Offset = 99;                                 // clamped to where the rest fits
        buffer.Render(Bar(Five), buffer.Area, ref state);
        Assert.Equal(4, state.Offset);
    }

    [Fact]
    public void Widening_the_bar_resets_the_offset()
    {
        var buffer = new CellBuffer(40, 1);
        var state = new TabsState(4);
        buffer.Render(Bar(Five), new Rect(0, 0, 12, 1), ref state);
        Assert.Equal(4, state.Offset);
        buffer.Render(Bar(Five), buffer.Area, ref state);
        Assert.Equal(0, state.Offset);
        Assert.StartsWith(" alpha │ beta │", buffer.RowText(0));
    }

    [Fact]
    public void Wide_titles_are_cut_at_a_cell_boundary()
    {
        var buffer = new CellBuffer(8, 1);
        var state = new TabsState();
        buffer.Render(new Tabs(["漢字漢字", "x"]), buffer.Area, ref state);
        Assert.Equal("  漢字 ›", buffer.RowText(0));
    }

    [Fact]
    public void Tab_at_maps_cells_to_tabs()
    {
        var buffer = new CellBuffer(22, 1);
        var state = new TabsState();
        Tabs bar = Bar(Three);
        buffer.Render(bar, buffer.Area, ref state);
        Assert.Equal(0, bar.TabAt(0, 0, state));
        Assert.Equal(0, bar.TabAt(4, 0, state));
        Assert.Equal(-1, bar.TabAt(5, 0, state));          // divider
        Assert.Equal(1, bar.TabAt(6, 0, state));
        Assert.Equal(2, bar.TabAt(18, 0, state));
        Assert.Equal(-1, bar.TabAt(19, 0, state));         // past the last tab
        Assert.Equal(-1, bar.TabAt(6, 1, state));
    }

    [Fact]
    public void Tab_at_uses_the_rendered_offset()
    {
        var buffer = new CellBuffer(12, 1);
        var state = new TabsState(4);
        Tabs bar = Bar(Five);
        buffer.Render(bar, buffer.Area, ref state);
        state.Offset = 0;                                  // not drawn yet
        Assert.Equal(4, bar.TabAt(2, 0, state));
        Assert.Equal(-1, bar.TabAt(0, 0, state));          // the arrow
    }

    [Fact]
    public void Mouse_clicks_select_and_arrows_and_wheel_scroll()
    {
        var buffer = new CellBuffer(12, 1);
        var state = new TabsState(4);
        Tabs bar = Bar(Five);
        buffer.Render(bar, buffer.Area, ref state);

        Assert.True(bar.HandleMouse(Click(0, 0), ref state));                // ‹
        buffer.Render(bar, buffer.Area, ref state);
        Assert.Equal(3, state.Offset);
        Assert.Equal(4, state.Selected);
        Assert.Equal("‹ delta │ e›", buffer.RowText(0));

        Assert.True(bar.HandleMouse(Click(3, 0), ref state));                // delta
        Assert.Equal(3, state.Selected);

        buffer.Render(bar, buffer.Area, ref state);
        Assert.True(bar.HandleMouse(new MouseEvent(MouseKind.ScrollUp, MouseButton.None, 5, 0, Modifiers.None), ref state));
        Assert.Equal(2, state.Offset);
        buffer.Render(bar, buffer.Area, ref state);
        Assert.True(bar.HandleMouse(Click(11, 0), ref state));               // ›
        Assert.Equal(3, state.Offset);

        Assert.False(bar.HandleMouse(Click(5, 1), ref state));               // below the bar
    }

    [Fact]
    public void Clicks_on_a_divider_or_hidden_arrow_are_not_used()
    {
        var buffer = new CellBuffer(12, 1);
        var state = new TabsState();
        Tabs bar = Bar(Five);
        buffer.Render(bar, buffer.Area, ref state);
        Assert.False(bar.HandleMouse(Click(8, 0), ref state));               // divider
        Assert.False(bar.HandleMouse(Click(0, 0), ref state));               // no ‹ at offset 0
        Assert.Equal(0, state.Selected);
    }

    [Fact]
    public void Next_and_previous_wrap()
    {
        var state = new TabsState(2);
        state.Next(3);
        Assert.Equal(0, state.Selected);
        state.Previous(3);
        Assert.Equal(2, state.Selected);
        state.Selected = -1;
        state.Previous(3);
        Assert.Equal(2, state.Selected);
        state.Next(0);
        Assert.Equal(-1, state.Selected);
        state.Select(9, 3);
        Assert.Equal(2, state.Selected);
    }

    [Fact]
    public void Handle_uses_arrows_and_home_end()
    {
        var state = new TabsState();
        Assert.True(state.Handle(new KeyEvent(KeyCode.Right), 3));
        Assert.True(state.Handle(KeyEvent.Char('l'), 3));
        Assert.Equal(2, state.Selected);
        Assert.True(state.Handle(new KeyEvent(KeyCode.Home), 3));
        Assert.Equal(0, state.Selected);
        Assert.True(state.Handle(new KeyEvent(KeyCode.Left), 3));
        Assert.Equal(2, state.Selected);
        Assert.False(state.Handle(new KeyEvent(KeyCode.Enter), 3));
    }

    [Fact]
    public void Selection_past_the_end_is_clamped_and_empty_bars_draw_nothing()
    {
        var buffer = new CellBuffer(10, 2);
        var state = new TabsState(7);
        buffer.Render(Bar(Three), buffer.Area, ref state);
        Assert.Equal(2, state.Selected);

        state = new TabsState();
        buffer.Clear();
        buffer.Render(new Tabs([]), buffer.Area, ref state);
        buffer.Render(Bar(Three), new Rect(0, 0, 0, 0), ref state);
        Assert.Equal(new CellBuffer(10, 2).ToString(), buffer.ToString());
        Assert.Equal(-1, state.Selected);
    }

    private static MouseEvent Click(int x, int y) => new(MouseKind.Down, MouseButton.Left, x, y, Modifiers.None);
}
