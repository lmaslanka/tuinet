using Tuinet.Widgets;

namespace Tuinet.Tests;

public class PopupTests
{
    private static readonly Style Fill = new(Color.Default, Color.Blue);
    private static readonly Style ShadowStyle = new(Color.Default, Color.Black, Attr.Dim);

    [Fact]
    public void Frame_inner_and_outer_account_for_border_padding_and_shadow()
    {
        var popup = new Popup { Shadow = true, Padding = 1 };
        var area = new Rect(0, 0, 20, 10);
        Assert.Equal(new Rect(0, 0, 18, 9), popup.Frame(area));
        Assert.Equal(new Rect(3, 2, 12, 5), popup.Inner(area));
        Assert.Equal(new Size(20, 10), popup.Outer(12, 5));   // the content size round-trips
    }

    [Fact]
    public void Plain_popup_is_its_area()
    {
        var popup = new Popup { Block = new Block { Borders = Borders.None } };
        var area = new Rect(2, 3, 10, 4);
        Assert.Equal(area, popup.Frame(area));
        Assert.Equal(area, popup.Inner(area));
        Assert.Equal(new Size(10, 4), popup.Outer(10, 4));
    }

    [Fact]
    public void Outer_counts_only_the_borders_that_are_drawn()
    {
        var popup = new Popup { Block = new Block { Borders = Borders.Top | Borders.Left } };
        Assert.Equal(new Size(11, 5), popup.Outer(10, 4));
        Assert.Equal(new Rect(1, 1, 10, 4), popup.Inner(new Rect(0, 0, 11, 5)));
    }

    [Fact]
    public void Draws_a_filled_box_over_content()
    {
        CellBuffer buffer = Underneath(12, 6);
        buffer.Render(new Popup { Block = new Block { Title = "hi", Style = Fill } }, new Rect(1, 1, 6, 3));
        Assert.Equal("xxxxxxxxxxxx", buffer.RowText(0));
        Assert.Equal("x┌─hi─┐xxxxx", buffer.RowText(1));
        Assert.Equal("x│    │xxxxx", buffer.RowText(2));
        Assert.Equal("x└────┘xxxxx", buffer.RowText(3));
        Assert.Equal(Fill, buffer[3, 2].Style);
        Assert.Equal(Fill.Bg, buffer[1, 1].Style.Bg);
    }

    [Fact]
    public void Shadow_darkens_the_cells_below_and_right_keeping_their_glyphs()
    {
        CellBuffer buffer = Underneath(12, 6);
        buffer.Render(new Popup { Block = new Block { Style = Fill }, Shadow = true }, new Rect(0, 0, 8, 4));

        Assert.Equal("┌────┐xxxxxx", buffer.RowText(0));
        Assert.Equal("xxxxxxxxxxxx", buffer.RowText(3));
        Assert.Equal(default, buffer[6, 0].Style);               // the top row has no shadow: it's offset down
        Assert.Equal(ShadowStyle, buffer[6, 1].Style);
        Assert.Equal(ShadowStyle, buffer[7, 3].Style);
        Assert.Equal(default, buffer[8, 1].Style);               // two columns wide
        Assert.Equal(default, buffer[1, 3].Style);               // offset right by two
        Assert.Equal(ShadowStyle, buffer[2, 3].Style);
        Assert.Equal(default, buffer[0, 4].Style);
    }

    [Fact]
    public void Shadow_style_layers_over_what_is_there()
    {
        var buffer = new CellBuffer(8, 4);
        buffer.SetString(0, 0, "abcdefgh", new Style(Color.Green, Color.Red, Attr.Bold));
        buffer.SetString(0, 1, "abcdefgh", new Style(Color.Green, Color.Red, Attr.Bold));
        buffer.Render(new Popup { Shadow = true, ShadowStyle = new Style(Color.Default, Color.Black) }, new Rect(0, 0, 6, 2));
        Assert.Equal('e', buffer[4, 1].Rune.Value);                // the box is 4×1; the shadow starts at (4, 1)
        Assert.Equal(new Style(Color.Green, Color.Black, Attr.Bold), buffer[4, 1].Style);
        Assert.Equal(new Style(Color.Green, Color.Red, Attr.Bold), buffer[6, 1].Style);
    }

    [Fact]
    public void Shadow_is_clipped_at_the_screen_edges()
    {
        CellBuffer buffer = Underneath(10, 5);
        buffer.Render(new Popup { Shadow = true }, new Rect(5, 2, 8, 5));   // runs off the right and bottom
        Assert.Equal("xxxxx┌───┐", buffer.RowText(2));   // the block closes at the screen edge
        Assert.Equal("xxxxx└───┘", buffer.RowText(4));
    }

    [Fact]
    public void Shadow_over_a_wide_glyph_keeps_it_whole()
    {
        var buffer = new CellBuffer(10, 4);
        for (int y = 0; y < 4; y++)
        {
            buffer.SetString(0, y, "漢字漢字漢");
        }

        // The box (5 wide) cuts the third glyph; its other half is blanked. The shadow then covers it and 字.
        buffer.Render(new Popup { Shadow = true }, new Rect(0, 0, 7, 3));
        Assert.Equal("┌───┐ 字漢", buffer.RowText(0));
        Assert.Equal("└───┘ 字漢", buffer.RowText(1));
        Assert.Equal("漢字漢字漢", buffer.RowText(2));                  // the bottom shadow keeps its glyphs
        Assert.Equal(new Style(Color.Default, Color.Black, Attr.Dim), buffer[6, 1].Style);
        Assert.Equal(new Style(Color.Default, Color.Black, Attr.Dim), buffer[2, 2].Style);
    }

    [Fact]
    public void Tiny_areas_draw_nothing_outside()
    {
        foreach (Rect area in (Rect[])[new(2, 2, 0, 0), new(2, 2, 1, 1), new(2, 2, 2, 1), new(2, 2, 3, 2)])
        {
            CellBuffer buffer = Underneath(8, 6);
            buffer.Render(new Popup { Shadow = true, Padding = 1, Block = new Block { Title = "title", Style = Fill } }, area);
            for (int y = 0; y < buffer.Height; y++)
            {
                for (int x = 0; x < buffer.Width; x++)
                {
                    if (!area.Contains(x, y))
                    {
                        Assert.Equal(new Cell(new System.Text.Rune('x'), default), buffer[x, y]);
                    }
                }
            }
        }
    }

    [Fact]
    public void Dropdown_popup_shadow_hangs_off_the_list_without_moving_it()
    {
        string[] items = ["a", "b"];
        var state = new DropdownState(0);
        state.Open();
        CellBuffer buffer = Underneath(12, 6);
        var dropdown = new Dropdown<TextItems>(new TextItems(items)) { PopupBordered = true, PopupShadow = true };
        dropdown.RenderPopup(new Rect(0, 0, 6, 1), buffer, ref state);

        Assert.Equal("┌────┐xxxxxx", buffer.RowText(1));
        Assert.Equal("│a   │xxxxxx", buffer.RowText(2));
        Assert.Equal("└────┘xxxxxx", buffer.RowText(4));
        Assert.Equal(ShadowStyle, buffer[6, 2].Style);
        Assert.Equal(ShadowStyle, buffer[2, 5].Style);
        Assert.Equal(new Rect(0, 1, 6, 4), state.Popup);       // clicks on the shadow count as outside
    }

    private static CellBuffer Underneath(int width, int height)
    {
        var buffer = new CellBuffer(width, height);
        for (int y = 0; y < height; y++)
        {
            buffer.SetString(0, y, new string('x', width));
        }

        return buffer;
    }
}
