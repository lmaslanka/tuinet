using System.Diagnostics;
using System.Text;
using Tuinet.Widgets;

namespace Tuinet.Tests;

public class ParagraphTests
{
    private static readonly string[] Parts =
    [
        "the ", "quick ", "brown ", "fox ", "jumps\n", "over\r\n", "съешь ", "же ", "ещё ", "αβγ ", "日本語 ", "e\u0301 ", "👨‍👩‍👧 ", "🇵🇱",
        "supercalifragilistic ", "  ", "\n\n", "a", "b", " ", "x\u0301y", "ok",
    ];

    [Fact]
    public void Rendering_with_a_state_matches_rendering_without_one()
    {
        var random = new Random(11);
        var state = new ParagraphState();
        for (int round = 0; round < 300; round++)
        {
            string text = RandomText(random, random.Next(0, 40));
            var wrap = (TextWrap)random.Next(3);
            int width = random.Next(1, 14);
            int height = random.Next(1, 6);
            var scrollbar = (ScrollbarMode)random.Next(3);
            int count = Paragraph.LineCount(text, width, wrap);
            for (int scroll = -1; scroll <= count + 1; scroll++)
            {
                var plain = new CellBuffer(width, height);
                var cached = new CellBuffer(width, height);
                plain.Render(new Paragraph(text) { Wrap = wrap, Scroll = scroll, Scrollbar = scrollbar }, plain.Area);
                cached.Render(new Paragraph(text) { Wrap = wrap, Scroll = scroll, Scrollbar = scrollbar }, cached.Area, ref state);
                Assert.True(plain.ToString() == cached.ToString(), $"round {round}: {wrap} w{width} h{height} scroll {scroll} {scrollbar}\n{Escape(text)}\n--\n{plain}\n--\n{cached}");
                Assert.Equal(count, state.LineCount(text, width, wrap));
            }
        }
    }

    [Fact]
    public void Styled_runs_keep_their_styles_across_lines_with_and_without_a_state()
    {
        var red = new Style(Color.Red, Color.Default);
        var blue = new Style(Color.Blue, Color.Default);
        var random = new Random(5);
        var state = new ParagraphState();
        for (int round = 0; round < 100; round++)
        {
            string text = RandomText(random, random.Next(1, 30));
            var runs = new List<StyledRun>();
            for (int at = 0; at < text.Length;)
            {
                int length = Math.Min(text.Length - at, random.Next(1, 9));
                runs.Add(new StyledRun(length, random.Next(2) == 0 ? red : blue));
                at += length;
            }

            var styled = new StyledText(text, runs.ToArray());
            int width = random.Next(1, 12);
            int count = Paragraph.LineCount(text, width, TextWrap.Word);
            for (int scroll = 0; scroll <= count; scroll += Math.Max(1, count / 4))
            {
                var plain = new CellBuffer(width, 4);
                var cached = new CellBuffer(width, 4);
                plain.Render(new Paragraph(styled) { Wrap = TextWrap.Word, Scroll = scroll }, plain.Area);
                cached.Render(new Paragraph(styled) { Wrap = TextWrap.Word, Scroll = scroll }, cached.Area, ref state);
                for (int y = 0; y < 4; y++)
                {
                    Assert.True(plain.Row(y).SequenceEqual(cached.Row(y)), $"round {round} scroll {scroll} row {y}");
                }
            }

            // The style of each cell is the style of the run its char came from.
            var check = new CellBuffer(width, count);
            check.Render(new Paragraph(styled) { Wrap = TextWrap.Word }, check.Area, ref state);
            int seen = 0;
            for (int y = 0; y < count; y++)
            {
                for (int x = 0; x < width; x++)
                {
                    Cell cell = check[x, y];
                    if (cell.IsContinuation || cell.Text == " ")
                    {
                        continue;
                    }

                    int index = text.IndexOf(cell.Text, seen, StringComparison.Ordinal);
                    Assert.True(index >= 0, $"{cell.Text} not found after {seen}");
                    Assert.Equal(StyleAt(runs, index).Fg, cell.Style.Fg);
                    seen = index + cell.Text.Length;
                }
            }
        }
    }

    [Fact]
    public void A_scrolled_paragraph_with_a_state_only_touches_the_lines_in_view()
    {
        string text = string.Join('\n', Enumerable.Range(0, 2000).Select(i => $"{i:D5} the quick brown fox jumps over the lazy dog and keeps running until the line wraps"));
        var buffer = new CellBuffer(60, 10);
        var state = new ParagraphState();
        buffer.Render(View(), buffer.Area, ref state);   // builds the layout
        var expected = new CellBuffer(60, 10);
        expected.Render(View(), expected.Area);
        Assert.Equal(expected.ToString(), buffer.ToString());

        long before = GC.GetAllocatedBytesForCurrentThread();
        Cached(30);   // warm both paths up past tier-0 JIT before timing either
        Assert.Equal(0, GC.GetAllocatedBytesForCurrentThread() - before);
        Uncached(30);

        // Interleaved rounds, best of each: a noisy CI runner or a late tier-up slows a round, not one side only.
        TimeSpan cached = TimeSpan.MaxValue, uncached = TimeSpan.MaxValue;
        for (int round = 0; round < 10; round++)
        {
            var sw = Stopwatch.StartNew();
            Cached(20);
            if (sw.Elapsed < cached)
            {
                cached = sw.Elapsed;
            }

            sw.Restart();
            Uncached(20);
            if (sw.Elapsed < uncached)
            {
                uncached = sw.Elapsed;
            }
        }

        // Without the state every frame wraps 3,900 lines to find the view; with it, the compare of the text and 10 lines.
        Assert.True(cached < uncached / 3, $"cached {cached.TotalMilliseconds:F2} ms vs {uncached.TotalMilliseconds:F2} ms");

        void Cached(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                buffer.Clear();
                buffer.Render(View(), buffer.Area, ref state);
            }
        }

        void Uncached(int frames)
        {
            for (int i = 0; i < frames; i++)
            {
                expected.Clear();
                expected.Render(View(), expected.Area);
            }
        }

        Paragraph View() => new(text) { Wrap = TextWrap.Word, Scroll = 3900, Scrollbar = ScrollbarMode.Auto };
    }

    [Fact]
    public void A_state_notices_a_text_change_of_the_same_length()
    {
        var state = new ParagraphState();
        var buffer = new CellBuffer(5, 2);
        buffer.Render(new Paragraph("ab cd ef") { Wrap = TextWrap.Word, Scroll = 1 }, buffer.Area, ref state);
        Assert.Equal("ef   ", buffer.RowText(0));
        buffer.Clear();
        buffer.Render(new Paragraph("abcde fg") { Wrap = TextWrap.Word, Scroll = 1 }, buffer.Area, ref state);
        Assert.Equal("fg   ", buffer.RowText(0));
        Assert.Equal(2, state.LineCount("abcde fg", 5, TextWrap.Word));
        Assert.Equal(1, state.LineCount("abcde fg", 8, TextWrap.Word));
        Assert.Equal(8, state.LineCount("abcde fg", 1, TextWrap.Char));

        // Without verification a same-length change is missed until Invalidate.
        state.VerifyText = false;
        Assert.Equal(8, state.LineCount("abcde fg", 1, TextWrap.Char));
        Assert.Equal(8, state.LineCount("ab cd ef", 1, TextWrap.Char));
        Assert.Equal(2, state.LineCount("ab cd ef", 5, TextWrap.Word));   // the stale copy "abcde fg" wraps in 2 as well
        Assert.Equal(3, state.LineCount("abcdefgh", 5, TextWrap.Word) + 1);   // stale: "abcde fg" is 2 lines
        state.Invalidate();
        Assert.Equal(2, state.LineCount("abcdefgh", 5, TextWrap.Word));
    }

    [Fact]
    public void Line_enumeration_of_a_long_single_line_is_linear()
    {
        // Each visual line used to search the rest of the text for the next '\n': quadratic in the line's length.
        string line = string.Concat(Enumerable.Repeat("word ", 800_000));   // 4M chars, 400 K visual lines
        var sw = Stopwatch.StartNew();
        int count = Paragraph.LineCount(line, 10, TextWrap.Word);
        Assert.Equal(400_000, count);
        Assert.True(sw.ElapsedMilliseconds < 2000, $"{sw.ElapsedMilliseconds} ms");   // quadratic: tens of seconds
    }

    [Theory]
    [InlineData("съешь же ещё этих", 8, TextWrap.Word, "съешь же|ещё этих")]
    [InlineData("съешь же ещё этих", 8, TextWrap.Char, "съешь же| ещё эти|х")]
    [InlineData("日本語 テキスト", 7, TextWrap.Word, "日本語|テキス|ト")]
    [InlineData("ab e\u0301f gh", 5, TextWrap.Word, "ab e\u0301f|gh")]
    [InlineData("abc\u0301 def", 3, TextWrap.Char, "abc\u0301| de|f")]
    [InlineData("a\uFE0Fbc", 2, TextWrap.Char, "a\uFE0F|bc")]
    [InlineData("hello   world", 5, TextWrap.Word, "hello|world")]
    [InlineData("a\r\nb\r\n", 4, TextWrap.None, "a|b|")]
    public void Wrapping_treats_every_script_the_same(string text, int width, TextWrap wrap, string expected)
    {
        var lines = new List<string>();
        var buffer = new CellBuffer(width, 1);
        int count = Paragraph.LineCount(text, width, wrap);
        for (int scroll = 0; scroll < count; scroll++)
        {
            buffer.Clear();
            buffer.Render(new Paragraph(text) { Wrap = wrap, Scroll = scroll }, buffer.Area);
            lines.Add(buffer.RowText(0).TrimEnd());
        }

        Assert.Equal(expected, string.Join('|', lines));
    }

    private static Style StyleAt(List<StyledRun> runs, int index)
    {
        foreach (StyledRun run in runs)
        {
            if (index < run.Length)
            {
                return run.Style;
            }

            index -= run.Length;
        }

        return default;
    }

    private static string RandomText(Random random, int parts)
    {
        var text = new StringBuilder();
        for (int i = 0; i < parts; i++)
        {
            text.Append(Parts[random.Next(Parts.Length)]);
        }

        return text.ToString();
    }

    private static string Escape(string text) => text.Replace("\r", "\\r").Replace("\n", "\\n\n");
}
