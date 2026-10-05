using Tuinet.Widgets;

namespace Tuinet.Tests;

/// <summary>Mouse hit-testing against the last render: lists, tables, dropdowns and text inputs.</summary>
public class MouseTests
{
    private static MouseEvent Click(int x, int y) => new(MouseKind.Down, MouseButton.Left, x, y, Modifiers.None);
    private static MouseEvent Wheel(int delta, int x, int y) =>
        new(delta < 0 ? MouseKind.ScrollUp : MouseKind.ScrollDown, MouseButton.None, x, y, Modifiers.None);

    private static readonly string[] Ten = ["r0", "r1", "r2", "r3", "r4", "r5", "r6", "r7", "r8", "r9"];

    private static void RenderList(CellBuffer buffer, Rect area, ref ListState state) =>
        buffer.Render(new ListView<TextItems>(new TextItems(Ten)), area, ref state);

    [Fact]
    public void Mouse_event_helpers()
    {
        var area = new Rect(2, 2, 3, 1);
        Assert.True(Click(2, 2).IsClickIn(area));
        Assert.False(Click(5, 2).IsClickIn(area));
        Assert.False(new MouseEvent(MouseKind.Down, MouseButton.Right, 2, 2, Modifiers.None).IsClick);
        Assert.False(new MouseEvent(MouseKind.Up, MouseButton.Left, 2, 2, Modifiers.None).IsClickIn(area));
        Assert.True(Wheel(1, 0, 0).IsWheel);
        Assert.Equal(-1, Wheel(-1, 0, 0).WheelDelta);
        Assert.Equal(1, Wheel(1, 0, 0).WheelDelta);
        Assert.Equal(0, Click(0, 0).WheelDelta);
        Assert.True(Wheel(1, 4, 2).IsIn(area));
    }

    [Fact]
    public void Click_selects_the_row_under_the_pointer_with_the_scroll_offset()
    {
        var state = new ListState(selected: 5);
        var buffer = new CellBuffer(12, 6);
        RenderList(buffer, new Rect(2, 1, 8, 3), ref state);
        Assert.Equal(3, state.Offset);                                  // r3 r4 r5 on rows 1..3

        Assert.True(state.HandleMouse(Click(4, 2), Ten.Length));
        Assert.Equal(4, state.Selected);
        Assert.False(state.HandleMouse(Click(1, 2), Ten.Length));      // left of the list
        Assert.False(state.HandleMouse(Click(4, 4), Ten.Length));      // below it
        Assert.Equal(4, state.Selected);
    }

    [Fact]
    public void Click_below_the_last_item_selects_nothing()
    {
        var state = new ListState();
        var buffer = new CellBuffer(8, 5);
        buffer.Render(new ListView<TextItems>(new TextItems(["a", "b"])), buffer.Area, ref state);
        Assert.False(state.HandleMouse(Click(0, 3), 2));
        Assert.Equal(-1, state.RowAt(0, 3, 2));
        Assert.Equal(0, state.Selected);
    }

    [Fact]
    public void Wheel_scrolls_the_view_without_moving_the_selection()
    {
        var state = new ListState();
        var buffer = new CellBuffer(8, 3);
        RenderList(buffer, buffer.Area, ref state);

        Assert.True(state.HandleMouse(Wheel(1, 0, 0), Ten.Length));
        Assert.Equal(3, state.Offset);
        RenderList(buffer, buffer.Area, ref state);
        Assert.Equal(3, state.Offset);                                  // render doesn't snap back to the selection
        Assert.Equal(0, state.Selected);
        Assert.Equal("r3      ", buffer.RowText(0));

        state.Next(Ten.Length);                                         // a key moves the selection: the view follows it
        RenderList(buffer, buffer.Area, ref state);
        Assert.Equal(1, state.Offset);
    }

    [Fact]
    public void Wheel_clamps_at_both_ends()
    {
        var state = new ListState();
        var buffer = new CellBuffer(8, 3);
        RenderList(buffer, buffer.Area, ref state);
        for (int i = 0; i < 5; i++)
        {
            state.HandleMouse(Wheel(1, 0, 0), Ten.Length);
        }

        Assert.Equal(7, state.Offset);                                  // 10 items, 3 rows
        state.HandleMouse(Wheel(-1, 0, 0), Ten.Length);
        state.HandleMouse(Wheel(-1, 0, 0), Ten.Length);
        state.HandleMouse(Wheel(-1, 0, 0), Ten.Length);
        Assert.Equal(0, state.Offset);
        Assert.False(state.HandleMouse(Wheel(1, 0, 5), Ten.Length));   // outside the list
    }

    [Fact]
    public void Click_after_a_wheel_in_the_same_burst_hits_what_is_on_screen()
    {
        var state = new ListState();
        var buffer = new CellBuffer(8, 3);
        RenderList(buffer, buffer.Area, ref state);
        state.HandleMouse(Wheel(1, 0, 0), Ten.Length);                 // not rendered yet: the screen still shows r0..r2
        state.HandleMouse(Click(0, 1), Ten.Length);
        Assert.Equal(1, state.Selected);
    }

    [Fact]
    public void Resize_still_keeps_the_selection_in_view()
    {
        var state = new ListState(selected: 4);
        var buffer = new CellBuffer(8, 10);
        RenderList(buffer, buffer.Area, ref state);
        Assert.Equal(0, state.Offset);
        RenderList(buffer, new Rect(0, 0, 8, 2), ref state);
        Assert.Equal(3, state.Offset);
    }

    private static readonly TableColumn[] Columns = [new("id", Constraint.Length(3)), new("name", Constraint.Fill())];

    private static Table<TextRows> Table(string[][] rows) =>
        new(new TextRows(rows), Columns)
        {
            HeaderSeparator = true,
            HighlightSymbol = "> ",
            ColumnSpacing = 2,
        };

    [Fact]
    public void Table_click_skips_the_header_and_rule()
    {
        string[][] rows = [["1", "a"], ["2", "b"], ["3", "c"], ["4", "d"], ["5", "e"]];
        var state = new ListState(selected: 4);
        var buffer = new CellBuffer(14, 6);
        buffer.Render(Table(rows), new Rect(0, 1, 14, 5), ref state);  // header y=1, rule y=2, rows y=3..5
        Assert.Equal(2, state.Offset);

        Assert.False(state.HandleMouse(Click(5, 1), rows.Length));     // header
        Assert.False(state.HandleMouse(Click(5, 2), rows.Length));     // rule
        Assert.True(state.HandleMouse(Click(5, 3), rows.Length));
        Assert.Equal(2, state.Selected);
        Assert.True(state.HandleMouse(Wheel(-1, 5, 1), rows.Length));  // the wheel works over the header too
    }

    [Fact]
    public void Table_wheel_scroll_survives_render()
    {
        string[][] rows = [["1", "a"], ["2", "b"], ["3", "c"], ["4", "d"], ["5", "e"], ["6", "f"]];
        var state = new ListState();
        var buffer = new CellBuffer(14, 5);
        buffer.Render(Table(rows), buffer.Area, ref state);            // 3 body rows
        Assert.True(state.HandleMouse(Wheel(1, 5, 3), rows.Length));
        Assert.Equal(3, state.Offset);
        buffer.Clear();
        buffer.Render(Table(rows), buffer.Area, ref state);
        Assert.Equal(3, state.Offset);
        Assert.Equal("  4    d      ", buffer.RowText(2));             // the selection (row 0) is scrolled off
        Assert.Equal(0, state.Selected);
    }

    [Fact]
    public void Header_column_at_follows_the_column_layout()
    {
        string[][] rows = [["1", "a"]];
        var state = new ListState();
        var buffer = new CellBuffer(14, 4);
        Table<TextRows> table = Table(rows);
        buffer.Render(table, buffer.Area, ref state);
        Assert.Equal("  id   name   ", buffer.RowText(0));        // indent 2, id 3 wide, gap 2, name 7

        Assert.Equal(-1, table.HeaderColumnAt(1, 0, state));            // the highlight indent
        Assert.Equal(0, table.HeaderColumnAt(2, 0, state));
        Assert.Equal(0, table.HeaderColumnAt(4, 0, state));
        Assert.Equal(-1, table.HeaderColumnAt(5, 0, state));            // the gap between columns
        Assert.Equal(1, table.HeaderColumnAt(7, 0, state));
        Assert.Equal(1, table.HeaderColumnAt(13, 0, state));
        Assert.Equal(-1, table.HeaderColumnAt(7, 2, state));            // a body row
        Assert.Equal(-1, table.HeaderColumnAt(14, 0, state));           // off the table
    }

    private static readonly string[] Levels = ["low", "medium", "high", "critical", "blocker"];

    private static void RenderDropdown(CellBuffer buffer, ref DropdownState state)
    {
        var dropdown = new Dropdown<TextItems>(new TextItems(Levels)) { MaxVisible = 3 };
        var field = new Rect(1, 1, 10, 1);
        dropdown.Render(field, buffer, ref state);
        dropdown.RenderPopup(field, buffer, ref state);
    }

    [Fact]
    public void Dropdown_click_opens_and_a_click_on_an_item_commits_it()
    {
        var state = new DropdownState(0);
        var buffer = new CellBuffer(14, 8);
        RenderDropdown(buffer, ref state);
        Assert.False(state.HandleMouse(Click(0, 1), Levels.Length));   // beside the field
        Assert.True(state.HandleMouse(Click(5, 1), Levels.Length));
        Assert.True(state.IsOpen);

        RenderDropdown(buffer, ref state);                              // popup rows y=2..4: low medium high
        Assert.Equal(new Rect(1, 2, 10, 3), state.Popup);
        Assert.True(state.HandleMouse(Click(3, 4), Levels.Length));
        Assert.False(state.IsOpen);
        Assert.Equal(2, state.Selected);
    }

    [Fact]
    public void Dropdown_wheel_scrolls_the_open_list()
    {
        var state = new DropdownState(0);
        var buffer = new CellBuffer(14, 8);
        state.Open();
        RenderDropdown(buffer, ref state);
        Assert.True(state.HandleMouse(Wheel(1, 3, 3), Levels.Length));
        RenderDropdown(buffer, ref state);
        Assert.Equal("high      ", buffer.RowText(2)[1..11]);           // scrolled to the end: high critical blocker
        Assert.True(state.HandleMouse(Click(3, 4), Levels.Length));
        Assert.Equal(4, state.Selected);
    }

    [Fact]
    public void Dropdown_click_elsewhere_cancels_but_is_not_used()
    {
        var state = new DropdownState(1);
        var buffer = new CellBuffer(14, 8);
        state.Open();
        RenderDropdown(buffer, ref state);
        Assert.False(state.HandleMouse(Click(12, 7), Levels.Length));
        Assert.False(state.IsOpen);
        Assert.Equal(1, state.Selected);

        state.Open();
        RenderDropdown(buffer, ref state);
        Assert.True(state.HandleMouse(Click(5, 1), Levels.Length));    // the field again: closes, used
        Assert.False(state.IsOpen);
    }

    [Fact]
    public void Text_input_click_places_the_caret_on_the_clicked_cluster()
    {
        var state = new TextInputState("a世b");                         // a=col 0, 世=cols 1-2, b=col 3
        var buffer = new CellBuffer(10, 1);
        buffer.Render(new TextInput(), new Rect(2, 0, 8, 1), ref state);

        Assert.True(state.HandleMouse(Click(2, 0)));
        Assert.Equal(0, state.Caret);
        state.HandleMouse(Click(3, 0));
        Assert.Equal(1, state.Caret);
        state.HandleMouse(Click(4, 0));                                 // second half of the wide glyph
        Assert.Equal(1, state.Caret);
        state.HandleMouse(Click(5, 0));
        Assert.Equal(2, state.Caret);
        state.HandleMouse(Click(9, 0));                                 // past the end
        Assert.Equal(3, state.Caret);
        Assert.False(state.HandleMouse(Click(1, 0)));
    }

    [Fact]
    public void Text_input_click_accounts_for_scroll_and_clusters()
    {
        var state = new TextInputState("🇵🇱🇵🇱🇵🇱🇵🇱");                       // 4 flags, 2 runes and 2 columns each
        var buffer = new CellBuffer(5, 1);
        buffer.Render(new TextInput { Focused = true }, buffer.Area, ref state);
        Assert.Equal(4, state.Scroll);                                  // shows flags 3 and 4

        state.HandleMouse(Click(1, 0));
        Assert.Equal(4, state.Caret);
        state.HandleMouse(Click(2, 0));
        Assert.Equal(6, state.Caret);
    }

    [Fact]
    public void Clicking_an_unfocused_input_maps_to_the_beginning_it_shows()
    {
        var state = new TextInputState("abcdefghij");
        var buffer = new CellBuffer(4, 1);
        buffer.Render(new TextInput { Focused = true }, buffer.Area, ref state);
        Assert.Equal(7, state.Scroll);
        buffer.Render(new TextInput(), buffer.Area, ref state);         // unfocused: shows "abcd"

        state.HandleMouse(Click(2, 0));
        Assert.Equal(2, state.Caret);
        buffer.Render(new TextInput { Focused = true }, buffer.Area, ref state);
        Assert.Equal("abcd", buffer.RowText(0));                        // focusing doesn't jump to the old scroll
        Assert.Equal(2, buffer.CursorX);
    }

    [Fact]
    public void Masked_input_click_counts_one_mask_per_cluster()
    {
        var state = new TextInputState("pässwörd", mask: '•');
        var buffer = new CellBuffer(10, 1);
        buffer.Render(new TextInput(), buffer.Area, ref state);
        state.HandleMouse(Click(3, 0));
        Assert.Equal(3, state.Caret);
    }
}
