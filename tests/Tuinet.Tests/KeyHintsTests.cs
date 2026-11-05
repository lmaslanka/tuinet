using Tuinet.Widgets;

namespace Tuinet.Tests;

public class KeyHintsTests
{
    private static readonly Style Key = new(Color.Blue, Color.Default, Attr.Bold);
    private static readonly Style Action = new(Color.BrightBlack, Color.Default);
    private static readonly Style Separator = new(Color.Red, Color.Default);

    // "j/k move · enter edit · m menu · q quit": 8 + 3 + 10 + 3 + 6 + 3 + 6 = 39 columns.
    private static readonly KeyHint[] Four = [new("j/k", "move", 2), new("enter", "edit", 1), new("m", "menu"), new("q", "quit", 3)];

    private static KeyHints Hints(KeyHint[] hints) =>
        new(hints) { KeyStyle = Key, ActionStyle = Action, Separator = " · ", SeparatorStyle = Separator };

    private static string Draw(KeyHint[] hints, int width)
    {
        var buffer = new CellBuffer(width, 1);
        buffer.Render(Hints(hints), buffer.Area);
        return buffer.RowText(0);
    }

    [Fact]
    public void Draws_every_hint_when_they_fit()
    {
        var buffer = new CellBuffer(42, 1);
        buffer.Render(Hints(Four), buffer.Area);
        Assert.Equal("j/k move · enter edit · m menu · q quit   ", buffer.RowText(0));
        Assert.Equal(Key, buffer[0, 0].Style);
        Assert.Equal(default, buffer[3, 0].Style);       // the space between key and action is left unstyled
        Assert.Equal(Action, buffer[4, 0].Style);
        Assert.Equal(Separator, buffer[9, 0].Style);
        Assert.Equal(default, buffer[39, 0].Style);
        Assert.Equal(39, Hints(Four).Width);
        Assert.Equal(39, Hints(Four).Measure(39));
    }

    [Fact]
    public void Drops_the_lowest_priority_first_and_keeps_the_order()
    {
        Assert.Equal("j/k move · enter edit · q quit", Draw(Four, 30));   // "m menu" (priority 0) goes first
        Assert.Equal("j/k move · q quit", Draw(Four, 29).TrimEnd());      // then "enter edit" (priority 1)
        Assert.Equal("j/k move · q quit", Draw(Four, 17));
        Assert.Equal("q quit   ", Draw(Four, 9));                   // "q quit" has the highest priority
        Assert.Equal(17, Hints(Four).Measure(29));
    }

    [Fact]
    public void Ties_drop_from_the_end()
    {
        KeyHint[] same = [new("a", "one"), new("b", "two"), new("c", "three")];
        Assert.Equal("a one · b two", Draw(same, 15).TrimEnd());
        Assert.Equal("a one", Draw(same, 12).TrimEnd());
    }

    [Fact]
    public void Never_cuts_a_hint()
    {
        for (int width = 1; width <= 42; width++)
        {
            string row = Draw(Four, width).TrimEnd();
            foreach (string part in row.Split(" · ", StringSplitOptions.RemoveEmptyEntries))
            {
                Assert.Contains(Four, hint => $"{hint.Key} {hint.Action}" == part);
            }
        }
    }

    [Fact]
    public void A_hint_wider_than_the_row_is_never_shown()
    {
        // "ctrl+shift+p commands" (21 columns) has the highest priority but can't fit in 12: the others still show.
        KeyHint[] hints = [new("ctrl+shift+p", "commands", 9), new("q", "quit")];
        Assert.Equal("q quit      ", Draw(hints, 12));
        Assert.Equal(6, Hints(hints).Measure(12));
        Assert.Equal("q quit", Draw([new("q", "quit")], 6));
        Assert.Equal("     ", Draw([new("q", "quit")], 5));
    }

    [Fact]
    public void Empty_hints_and_empty_areas_draw_nothing()
    {
        Assert.Equal("     ", Draw([], 5));
        Assert.Equal(0, Hints([]).Width);
        Assert.Equal(0, Hints(Four).Measure(0));
        Assert.Equal(0, Hints(Four).Measure(-3));

        var buffer = new CellBuffer(10, 2);
        buffer.Render(Hints(Four), new Rect(0, 0, 0, 1));
        buffer.Render(Hints(Four), new Rect(0, 0, 10, 0));
        Assert.Equal("          ", buffer.RowText(0));
        Assert.Equal(-1, Hints(Four).HintAt(0, 0, new Rect(0, 0, 0, 1)));
    }

    [Fact]
    public void A_hint_without_an_action_is_just_its_key()
    {
        KeyHint[] hints = [new("esc", ""), new("?", "help")];
        Assert.Equal("esc · ? help", Draw(hints, 12));
        Assert.Equal(12, Hints(hints).Width);
    }

    [Fact]
    public void Wide_glyphs_are_measured_by_columns()
    {
        KeyHint[] hints = [new("←/→", "移動", 1), new("q", "終了")];   // 3 + 1 + 4 and 1 + 1 + 4
        Assert.Equal(17, Hints(hints).Width);
        Assert.Equal("←/→ 移動 · q 終了", Draw(hints, 17));
        Assert.Equal("←/→ 移動", Draw(hints, 16).TrimEnd());
    }

    [Fact]
    public void Hint_at_agrees_with_the_drawn_row_at_every_column()
    {
        foreach (int width in (int[])[42, 30, 17, 9, 5])
        {
            var area = new Rect(3, 2, width, 1);
            var buffer = new CellBuffer(width + 6, 4);
            KeyHints hints = Hints(Four);
            buffer.Render(hints, area);
            string row = buffer.RowText(2);
            for (int x = 0; x < buffer.Width; x++)
            {
                int hit = hints.HintAt(x, area.Y, area);
                Style style = buffer[x, 2].Style;
                bool onHint = style == Key || style == Action || (row[x] == ' ' && x > 0 && buffer[x - 1, 2].Style == Key);
                Assert.Equal(onHint, hit >= 0);
                if (hit >= 0)
                {
                    // The cell belongs to the hint the hit names: its key or action is drawn around it.
                    KeyHint hint = Four[hit];
                    int start = row.IndexOf(hint.Key + " " + hint.Action, StringComparison.Ordinal);
                    Assert.True(start >= 0 && x < start + hint.Key.Length + 1 + hint.Action.Length, $"width {width}, x {x}");
                }

                Assert.Equal(-1, hints.HintAt(x, area.Y + 1, area));
            }
        }
    }

    [Fact]
    public void Hint_at_returns_minus_one_on_separators()
    {
        var area = new Rect(0, 0, 42, 1);
        Assert.Equal(0, Hints(Four).HintAt(7, 0, area));       // "move"'s last letter
        Assert.Equal(-1, Hints(Four).HintAt(8, 0, area));      // " · "
        Assert.Equal(-1, Hints(Four).HintAt(9, 0, area));
        Assert.Equal(-1, Hints(Four).HintAt(10, 0, area));
        Assert.Equal(1, Hints(Four).HintAt(11, 0, area));      // "enter"
        Assert.Equal(3, Hints(Four).HintAt(38, 0, area));
        Assert.Equal(-1, Hints(Four).HintAt(39, 0, area));     // past the last hint
        Assert.Equal(3, Hints(Four).HintAt(4, 0, new Rect(0, 0, 9, 1)));   // only "q quit" is left at 9 columns
    }
}
