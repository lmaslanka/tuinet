using Tuinet.Widgets;

namespace Tuinet.Tests;

public class MenuTests
{
    private static readonly MenuItem[] Items =
    [
        new("&Edit", "Enter"),
        new("&Toggle enabled", "Space"),
        MenuItem.Separator,
        new("&Delete", "Del", Enabled: false),
    ];

    private static MouseEvent Mouse(MouseKind kind, int x, int y, MouseButton button = MouseButton.Left) => new(kind, button, x, y, Modifiers.None);

    private static (CellBuffer Buffer, MenuState State) Render(MenuItem[] items, Menu menu, Rect? area = null, int width = 30, int height = 12)
    {
        var buffer = new CellBuffer(width, height);
        var state = new MenuState();
        state.Reset(items);
        buffer.Render(menu, area ?? new Rect(0, 0, Menu.Measure(items).Width, Menu.Measure(items).Height), ref state);
        return (buffer, state);
    }

    [Fact]
    public void Measure_fits_border_padding_label_gap_and_shortcut()
    {
        Assert.Equal(new Size(4 + 14 + 2 + 5, 6), Menu.Measure(Items));
        Assert.Equal(new Size(4 + 6, 1 + 2), Menu.Measure([new MenuItem("世界 &x")]));   // wide glyphs; '&' takes no room
        Assert.Equal(new Size(4 + 3, 1 + 2), Menu.Measure([new MenuItem("a&&b")]));     // "&&" draws one '&'
    }

    [Fact]
    public void Renders_labels_shortcuts_and_a_separator_joined_to_the_border()
    {
        (CellBuffer buffer, _) = Render(Items, new Menu(Items));
        Assert.Equal("┌───────────────────────┐", buffer.RowText(0)[..25]);
        Assert.Equal($"│ {"Edit",-14}  {"Enter",5} │", buffer.RowText(1)[..25]);
        Assert.Equal($"│ {"Toggle enabled",-14}  {"Space",5} │", buffer.RowText(2)[..25]);
        Assert.Equal("├───────────────────────┤", buffer.RowText(3)[..25]);
        Assert.Equal($"│ {"Delete",-14}  {"Del",5} │", buffer.RowText(4)[..25]);
        Assert.Equal("└───────────────────────┘", buffer.RowText(5)[..25]);
    }

    [Fact]
    public void Styles_the_highlight_mnemonics_shortcuts_and_disabled_items()
    {
        (CellBuffer buffer, _) = Render(Items, new Menu(Items));
        Assert.True(buffer[1, 1].Style.Attrs.HasFlag(Attr.Reverse));         // the highlight spans the row
        Assert.True(buffer[23, 1].Style.Attrs.HasFlag(Attr.Reverse));
        Assert.False(buffer[2, 2].Style.Attrs.HasFlag(Attr.Reverse));
        Assert.True(buffer[2, 1].Style.Attrs.HasFlag(Attr.Underline));       // E of &Edit
        Assert.False(buffer[3, 1].Style.Attrs.HasFlag(Attr.Underline));
        Assert.True(buffer[18, 2].Style.Attrs.HasFlag(Attr.Dim));            // shortcut
        Assert.False(buffer[2, 2].Style.Attrs.HasFlag(Attr.Dim));
        Assert.True(buffer[4, 4].Style.Attrs.HasFlag(Attr.Dim));             // disabled
    }

    [Theory]
    [InlineData(BorderType.Rounded, '├', '┤')]
    [InlineData(BorderType.Double, '╟', '╢')]
    [InlineData(BorderType.Thick, '┠', '┨')]
    public void Separators_use_tees_that_match_the_border(BorderType type, char left, char right)
    {
        var menu = new Menu(Items) { Popup = new Popup { Block = new Block { BorderType = type } } };
        (CellBuffer buffer, _) = Render(Items, menu);
        Assert.Equal(left, buffer.RowText(3)[0]);
        Assert.Equal(right, buffer.RowText(3)[24]);
    }

    [Fact]
    public void Keys_skip_separators_and_disabled_items_and_wrap()
    {
        MenuItem[] items = [new("a"), MenuItem.Separator, new("b", Enabled: false), new("c"), new("d", Enabled: false)];
        var state = new MenuState();
        state.Reset(items);
        Assert.Equal(0, state.Selected);
        Assert.Equal(MenuResult.Handled, state.Handle(new KeyEvent(KeyCode.Down), items));
        Assert.Equal(3, state.Selected);
        state.Handle(new KeyEvent(KeyCode.Down), items);
        Assert.Equal(0, state.Selected);   // wrapped
        state.Handle(new KeyEvent(KeyCode.Up), items);
        Assert.Equal(3, state.Selected);
        state.Handle(KeyEvent.Char('k'), items);
        Assert.Equal(0, state.Selected);
        state.Handle(KeyEvent.Char('j'), items);
        Assert.Equal(3, state.Selected);
        state.Handle(new KeyEvent(KeyCode.Home), items);
        Assert.Equal(0, state.Selected);
        state.Handle(new KeyEvent(KeyCode.End), items);
        Assert.Equal(3, state.Selected);
    }

    [Fact]
    public void A_menu_with_nothing_selectable_selects_nothing_and_does_not_hang()
    {
        MenuItem[] items = [MenuItem.Separator, new("off", Enabled: false)];
        var state = new MenuState();
        state.Reset(items);
        Assert.Equal(-1, state.Selected);
        Assert.Equal(MenuResult.Handled, state.Handle(new KeyEvent(KeyCode.Down), items));
        Assert.Equal(MenuResult.Handled, state.Handle(new KeyEvent(KeyCode.Up), items));
        Assert.Equal(MenuResult.Handled, state.Handle(new KeyEvent(KeyCode.Enter), items));
        Assert.Equal(-1, state.Selected);

        state = new MenuState();
        state.Reset([]);
        Assert.Equal(-1, state.Selected);
        Assert.Equal(MenuResult.Handled, state.Handle(new KeyEvent(KeyCode.Down), []));
    }

    [Fact]
    public void Enter_and_space_activate_esc_cancels_other_keys_pass_through()
    {
        var state = new MenuState();
        state.Reset(Items);
        Assert.Equal(MenuResult.Activated, state.Handle(new KeyEvent(KeyCode.Enter), Items));
        Assert.Equal(MenuResult.Activated, state.Handle(KeyEvent.Char(' '), Items));
        Assert.Equal(MenuResult.Cancelled, state.Handle(new KeyEvent(KeyCode.Escape), Items));
        Assert.Equal(MenuResult.Unhandled, state.Handle(KeyEvent.Char('x'), Items));
        Assert.Equal(MenuResult.Unhandled, state.Handle(new KeyEvent(KeyCode.Tab), Items));
        Assert.Equal(MenuResult.Unhandled, state.Handle(new KeyEvent(KeyCode.Enter, default, Modifiers.None, KeyKind.Release), Items));
    }

    [Fact]
    public void A_mnemonic_activates_its_item_unless_it_is_disabled()
    {
        var state = new MenuState();
        state.Reset(Items);
        Assert.Equal(MenuResult.Activated, state.Handle(KeyEvent.Char('t'), Items));
        Assert.Equal(1, state.Selected);
        Assert.Equal(MenuResult.Activated, state.Handle(KeyEvent.Char('E', Modifiers.Shift), Items));
        Assert.Equal(0, state.Selected);
        Assert.Equal(MenuResult.Unhandled, state.Handle(KeyEvent.Char('d'), Items));
        Assert.Equal(0, state.Selected);
        Assert.Equal(MenuResult.Unhandled, state.Handle(KeyEvent.Char('e', Modifiers.Ctrl), Items));
    }

    [Fact]
    public void Clicks_activate_motion_highlights_and_a_press_outside_cancels()
    {
        (_, MenuState state) = Render(Items, new Menu(Items), new Rect(3, 2, 25, 6));
        Assert.Equal(new Rect(3, 2, 25, 6), state.Area);
        Assert.Equal(MenuResult.Handled, state.HandleMouse(Mouse(MouseKind.Move, 10, 4), Items));
        Assert.Equal(1, state.Selected);
        Assert.Equal(MenuResult.Handled, state.HandleMouse(Mouse(MouseKind.Move, 10, 5), Items));   // separator
        Assert.Equal(1, state.Selected);
        Assert.Equal(MenuResult.Handled, state.HandleMouse(Mouse(MouseKind.Down, 10, 6), Items));   // disabled
        Assert.Equal(1, state.Selected);
        Assert.Equal(MenuResult.Handled, state.HandleMouse(Mouse(MouseKind.Up, 10, 3), Items));
        Assert.Equal(MenuResult.Activated, state.HandleMouse(Mouse(MouseKind.Down, 10, 3), Items));
        Assert.Equal(0, state.Selected);
        Assert.Equal(MenuResult.Handled, state.HandleMouse(Mouse(MouseKind.Down, 3, 2), Items));    // the border
        Assert.Equal(MenuResult.Unhandled, state.HandleMouse(Mouse(MouseKind.Move, 0, 0), Items));
        Assert.Equal(MenuResult.Cancelled, state.HandleMouse(Mouse(MouseKind.Down, 0, 0), Items));
        Assert.Equal(MenuResult.Cancelled, state.HandleMouse(Mouse(MouseKind.Down, 29, 11, MouseButton.Right), Items));
    }

    [Fact]
    public void A_menu_taller_than_its_area_scrolls_to_the_highlight()
    {
        MenuItem[] items = [.. Enumerable.Range(0, 10).Select(i => new MenuItem($"item {i}"))];
        var buffer = new CellBuffer(20, 10);
        var state = new MenuState();
        state.Reset(items);
        var area = new Rect(0, 0, 12, 5);
        state.Handle(new KeyEvent(KeyCode.End), items);
        buffer.Render(new Menu(items), area, ref state);
        Assert.StartsWith("│ item 7", buffer.RowText(1));
        Assert.StartsWith("│ item 9", buffer.RowText(3));
        Assert.Equal(MenuResult.Activated, state.HandleMouse(Mouse(MouseKind.Down, 4, 2), items));
        Assert.Equal(8, state.Selected);
        Assert.Equal(MenuResult.Handled, state.HandleMouse(Mouse(MouseKind.ScrollUp, 4, 2), items));
        buffer.Render(new Menu(items), area, ref state);
        Assert.StartsWith("│ item 6", buffer.RowText(1));
    }

    [Fact]
    public void Shadow_hangs_off_the_box()
    {
        var menu = new Menu(Items) { Popup = new Popup { Shadow = true } };
        var buffer = new CellBuffer(30, 10);
        buffer.Fill(buffer.Area, new Style(Color.Default, Color.Blue));
        var state = new MenuState();
        state.Reset(Items);
        buffer.Render(menu, new Rect(0, 0, 25, 6), ref state);
        Assert.Equal(new Rect(0, 0, 25, 6), state.Area);
        Assert.Equal(Color.Black, buffer[25, 1].Style.Bg);
        Assert.Equal(Color.Black, buffer[3, 6].Style.Bg);
        Assert.Equal(Color.Blue, buffer[27, 1].Style.Bg);
    }

    [Fact]
    public void Swapping_in_a_disabled_item_moves_the_highlight_off_it()
    {
        MenuItem[] items = [new("a"), new("b"), new("c")];
        var state = new MenuState();
        state.Reset(items);
        state.Handle(new KeyEvent(KeyCode.Down), items);
        items[1] = items[1] with { Enabled = false };
        new CellBuffer(10, 6).Render(new Menu(items), new Rect(0, 0, 5, 5), ref state);
        Assert.Equal(2, state.Selected);
    }
}
