using Tuinet.Widgets;

namespace Tuinet.Tests;

public class StyledTextTests
{
    private static readonly Style Red = new(Color.Red, Color.Default);
    private static readonly Style Blue = new(Color.Blue, Color.Default);
    private static readonly Style Bold = new(Color.Default, Color.Default, Attr.Bold);

    [Fact]
    public void Each_run_gets_its_style_layered_over_the_base()
    {
        var buffer = new CellBuffer(8, 1);
        int end = buffer.SetText(0, 0, new StyledText("j/k move", [new(3, Red), new(5, Bold)], new Style(Color.Green, Color.Black)));

        Assert.Equal(8, end);
        Assert.Equal("j/k move", buffer.RowText(0));
        Assert.Equal(new Style(Color.Red, Color.Black), buffer[0, 0].Style);
        Assert.Equal(new Style(Color.Green, Color.Black, Attr.Bold), buffer[4, 0].Style);
    }

    [Fact]
    public void Text_past_the_runs_uses_the_base_and_runs_past_the_text_are_ignored()
    {
        var buffer = new CellBuffer(6, 2);
        buffer.SetText(0, 0, new StyledText("abcd", [new(2, Red)], Blue));
        buffer.SetText(0, 1, new StyledText("ab", [new(10, Red), new(5, Blue)]));

        Assert.Equal(Color.Red, buffer[1, 0].Style.Fg);
        Assert.Equal(Color.Blue, buffer[2, 0].Style.Fg);
        Assert.Equal("ab    ", buffer.RowText(1));
        Assert.Equal(Color.Red, buffer[1, 1].Style.Fg);
    }

    [Fact]
    public void Writing_stops_at_the_first_glyph_that_does_not_fit()
    {
        // "x" from the next run would fit in the last column, but that would skip the wide glyph.
        var buffer = new CellBuffer(3, 1);
        Assert.Equal(2, buffer.SetText(0, 0, new StyledText("ab日x", [new(3, Red), new(1, Blue)])));
        Assert.Equal("ab ", buffer.RowText(0));
    }

    [Fact]
    public void Ellipsis_takes_the_style_of_the_text_it_replaces()
    {
        var buffer = new CellBuffer(6, 1);
        buffer.SetText(0, 0, new StyledText("key value-long", [new(4, Red), new(10, Blue)]), 6, Overflow.Ellipsis);
        Assert.Equal("key v…", buffer.RowText(0));
        Assert.Equal(Color.Red, buffer[0, 0].Style.Fg);
        Assert.Equal(Color.Blue, buffer[5, 0].Style.Fg);

        buffer.SetText(0, 0, new StyledText("short", [new(5, Red)]), 6, Overflow.Ellipsis);
        Assert.Equal("short…", buffer.RowText(0));   // fits: no ellipsis written (the old one stays)
        Assert.Equal('t', buffer[4, 0].Rune.Value);
    }

    [Fact]
    public void A_run_boundary_inside_a_cluster_moves_to_the_cluster_end()
    {
        // The accent belongs to "e": the run boundary after "e" would split the cluster.
        var buffer = new CellBuffer(5, 1);
        buffer.SetText(0, 0, new StyledText("aébc", [new(2, Red), new(1, Blue), new(2, Bold)]));
        Assert.Equal("aébc ", buffer.RowText(0));
        Assert.Equal("é", buffer[1, 0].Text);
        Assert.Equal(Color.Red, buffer[1, 0].Style.Fg);
        Assert.Equal(Attr.Bold, buffer[2, 0].Style.Attrs);   // the accent used up the blue run
        Assert.Equal(Attr.Bold, buffer[3, 0].Style.Attrs);
    }

    [Fact]
    public void Slices_keep_the_runs_that_cover_them()
    {
        var text = new StyledText("aabbbcc", [new(2, Red), new(3, Blue), new(2, Bold)]);
        var buffer = new CellBuffer(4, 1);
        buffer.SetText(0, 0, text.Slice(3, 4));
        Assert.Equal("bbcc", buffer.RowText(0));
        Assert.Equal(Color.Blue, buffer[1, 0].Style.Fg);
        Assert.Equal(Attr.Bold, buffer[2, 0].Style.Attrs);
    }

    [Fact]
    public void Builder_merges_runs_formats_values_and_reports_overflow()
    {
        var builder = new StyledTextBuilder(stackalloc char[16], stackalloc StyledRun[2]);
        builder.Append("n=", Red);
        builder.Append(42, Red);
        builder.Append(" ok", Blue);
        StyledText text = builder.Build();

        Assert.Equal("n=42 ok", text.ToString());
        Assert.Equal([new StyledRun(4, Red), new StyledRun(3, Blue)], text.Runs.ToArray());
        Assert.False(builder.Overflowed);

        builder.Append("!", Bold);                        // no run slot left
        Assert.True(builder.Overflowed);
        Assert.Equal("n=42 ok", builder.Build().ToString());

        var small = new StyledTextBuilder(stackalloc char[3], stackalloc StyledRun[4]);
        small.Append("ab😀", Red);                         // the emoji's surrogate pair would be cut in half
        Assert.Equal("ab", small.Build().ToString());
        Assert.True(small.Overflowed);
        small.Append(12345.6789, Blue, "F2");
        Assert.Equal("ab", small.Build().ToString());
    }

    [Fact]
    public void Markup_sets_attributes_colors_and_nests()
    {
        StyledText text = Markup.Parse("[b]q[/] quit [fg=#F5A623 u]warn [bg=blue]x[/][/]!", new char[64], new StyledRun[16]);

        Assert.Equal("q quit warn x!", text.ToString());
        var buffer = new CellBuffer(16, 1);
        buffer.SetText(0, 0, text);
        Assert.Equal(Attr.Bold, buffer[0, 0].Style.Attrs);
        Assert.Equal(Attr.None, buffer[2, 0].Style.Attrs);
        Assert.Equal(new Style(Color.Hex(0xF5A623), Color.Default, Attr.Underline), buffer[7, 0].Style);
        Assert.Equal(new Style(Color.Hex(0xF5A623), Color.Blue, Attr.Underline), buffer[12, 0].Style);
        Assert.Equal(Style.Default, buffer[13, 0].Style);
    }

    [Theory]
    [InlineData("[[b]] literal", "[b]] literal")]
    [InlineData("page [1/3]", "page [1/3]")]
    [InlineData("[1] item", "[1] item")]
    [InlineData("[x] done", "[x] done")]
    [InlineData("[] empty", "[] empty")]
    [InlineData("open [b", "open [b")]
    [InlineData("[/] stray close", " stray close")]
    [InlineData("[red]r[/][bright-cyan]c", "rc")]
    public void Markup_keeps_anything_that_is_not_a_tag_as_text(string markup, string visible)
    {
        Assert.Equal(visible, Markup.Parse(markup, new char[64], new StyledRun[16]).ToString());
    }

    [Theory]
    [InlineData("red", 1)]
    [InlineData("bright-red", 9)]
    [InlineData("fg=200", 200)]
    public void Markup_colors_by_name_or_index(string tag, int index)
    {
        Assert.True(Markup.TryParseTag(tag, default, out Style style));
        Assert.Equal(Color.Indexed((byte)index), style.Fg);
        Assert.False(Markup.TryParseTag("fg=nope", default, out _));
        Assert.False(Markup.TryParseTag("200", default, out _));
    }

    [Fact]
    public void Set_markup_writes_without_a_caller_buffer()
    {
        var buffer = new CellBuffer(10, 1);
        Assert.Equal(6, buffer.SetMarkup(0, 0, "[b]j/k[/] go", Blue));
        Assert.Equal("j/k go    ", buffer.RowText(0));
        Assert.Equal(new Style(Color.Blue, Color.Default, Attr.Bold), buffer[0, 0].Style);
        Assert.Equal(Blue, buffer[4, 0].Style);
    }

    [Fact]
    public void Paragraph_keeps_run_styles_across_wrapped_lines()
    {
        var buffer = new CellBuffer(6, 2);
        buffer.Render(new Paragraph(new StyledText("one two three", [new(4, Red), new(9, Blue)])) { Wrap = TextWrap.Word }, buffer.Area);
        Assert.Equal("one   ", buffer.RowText(0));
        Assert.Equal("two   ", buffer.RowText(1));
        Assert.Equal(Color.Red, buffer[0, 0].Style.Fg);
        Assert.Equal(Color.Blue, buffer[0, 1].Style.Fg);
    }

    [Fact]
    public void Block_title_can_be_styled()
    {
        var buffer = new CellBuffer(12, 3);
        var title = new StyledText(" A · 20 ", [new(4, Bold), new(4, Red)]);
        buffer.Render(new Block { StyledTitle = title, BorderStyle = Blue }, buffer.Area);
        Assert.Equal("┌─ A · 20 ─┐", buffer.RowText(0));
        Assert.Equal(new Style(Color.Blue, Color.Default, Attr.Bold), buffer[3, 0].Style);   // over the border style
        Assert.Equal(Color.Red, buffer[7, 0].Style.Fg);
    }
}
