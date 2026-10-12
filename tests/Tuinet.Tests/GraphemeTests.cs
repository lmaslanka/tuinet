using System.Globalization;
using System.Text;

namespace Tuinet.Tests;

public class GraphemeTests
{
    [Theory]
    [InlineData("é", 1)]                       // e + combining acute
    [InlineData("é̂", 1)]                 // two marks
    [InlineData("कि", 2)]                  // Devanagari ki: base + spacing vowel sign
    [InlineData("กิ", 1)]                  // Thai: base + non-spacing vowel
    [InlineData("👨‍👩‍👧", 2)]                           // ZWJ family
    [InlineData("❤️", 2)]                            // VS16 asks for emoji presentation
    [InlineData("1️⃣", 2)]                 // keycap
    [InlineData("🇵🇱", 2)]                            // flag: a regional indicator pair
    [InlineData("🇵🇱🇩🇪", 4)]                          // two flags
    [InlineData("👍🏽", 2)]                            // skin tone modifier
    [InlineData("각", 2)]            // Hangul jamo L V T
    [InlineData("日本", 4)]
    [InlineData("a‍", 1)]                       // a joiner after a letter adds nothing
    [InlineData("́a", 1)]                       // a stray mark draws nothing
    [InlineData("abc", 3)]
    public void Width_counts_each_cluster_once(string text, int width)
    {
        Assert.Equal(width, TextWidth.Of(text));
        var buffer = new CellBuffer(10, 1);
        Assert.Equal(width, buffer.SetString(0, 0, text));
    }

    [Fact]
    public void A_cluster_is_one_cell_holding_the_whole_text()
    {
        var buffer = new CellBuffer(6, 1);
        buffer.SetString(0, 0, "x👨‍👩‍👧y");

        Assert.Equal("x👨‍👩‍👧y  ", buffer.RowText(0));
        Cell family = buffer[1, 0];
        Assert.True(family.IsGrapheme);
        Assert.Equal("👨‍👩‍👧", family.Text);
        Assert.Equal(new Rune(0x1F468), family.Rune);
        Assert.Equal(2, family.Width);
        Assert.True(buffer[2, 0].IsContinuation);
        Assert.Equal("y", buffer[3, 0].Text);
        Assert.False(buffer[3, 0].IsGrapheme);
    }

    [Fact]
    public void Equal_clusters_are_identical_cells_in_any_buffer()
    {
        // The renderer diffs rows with memcmp, so equal text must give equal bytes.
        var a = new CellBuffer(4, 1);
        var b = new CellBuffer(4, 1);
        a.SetString(0, 0, "🇵🇱é");
        b.SetString(1, 0, "x");
        b.SetString(0, 0, "🇵🇱é");
        Assert.True(a.Row(0).SequenceEqual(b.Row(0)));
        Assert.False(a[0, 0].Equals(new CellBuffer(4, 1).Apply(c => c.SetString(0, 0, "🇩🇪"))[0, 0]));
    }

    [Fact]
    public void Clusters_are_never_split_by_a_clip_or_an_ellipsis()
    {
        var buffer = new CellBuffer(5, 2);
        Assert.Equal(2, buffer.SetString(0, 0, "ab❤️", default, 3));                     // needs 2, has 1
        Assert.Equal("ab   ", buffer.RowText(0));
        buffer.SetString(0, 1, "👨‍👩‍👧👨‍👩‍👧", default, 3, Overflow.Ellipsis);
        Assert.Equal("👨‍👩‍👧…  ", buffer.RowText(1));
    }

    [Fact]
    public void A_mark_after_ascii_joins_the_last_letter_even_on_the_fast_path()
    {
        var buffer = new CellBuffer(8, 1);
        Assert.Equal(4, buffer.SetString(0, 0, "abcé"));
        Assert.Equal("abcé    ", buffer.RowText(0));
        Assert.Equal("é", buffer[3, 0].Text);
        Assert.Equal("c", buffer[2, 0].Text);

        // A mark written on its own has no letter to join: it is dropped, as before.
        Assert.Equal(6, buffer.SetString(6, 0, "́"));
    }

    [Fact]
    public void Overwriting_either_half_of_a_wide_cluster_blanks_it()
    {
        var buffer = new CellBuffer(4, 1);
        buffer.SetString(0, 0, "🇵🇱");
        buffer.SetString(1, 0, "x");
        Assert.Equal(" x  ", buffer.RowText(0));
        Assert.False(buffer[0, 0].IsGrapheme);
    }

    [Fact]
    public void Pathological_clusters_are_cut_when_stored()
    {
        var buffer = new CellBuffer(3, 1);
        string zalgo = "a" + new string('́', 500);
        Assert.Equal(1, buffer.SetString(0, 0, zalgo));
        Assert.Equal(Graphemes.MaxChars, buffer[0, 0].Text.Length);
        Assert.Equal(1, buffer[0, 0].Width);
    }

    [Theory]
    [InlineData("🇵", "🇱", true)]          // two lone regional indicators would form a flag
    [InlineData("👍", "🏽", true)]          // an emoji then a lone skin tone
    [InlineData("ᄀ", "가", true)]      // Hangul L jamo then a syllable
    [InlineData("؀", "1", true)]       // a prepended Arabic number sign
    [InlineData("a", "b", false)]
    [InlineData("│", "─", false)]
    [InlineData("日", "本", false)]
    [InlineData("👍", "👍", false)]
    public void Neighbouring_cells_that_would_merge_are_detected(string left, string right, bool joins)
    {
        Rune.DecodeFromUtf16(left, out Rune a, out _);
        Rune.DecodeFromUtf16(right, out Rune b, out _);
        Assert.Equal(joins, Graphemes.MayJoin(a, b));
    }

    [Theory]
    [InlineData(0x044F, true)]    // я
    [InlineData(0x03C9, true)]    // ω
    [InlineData(0x05D0, true)]    // א
    [InlineData(0x0628, true)]    // ب
    [InlineData(0x0E01, true)]    // ก
    [InlineData(0x0915, true)]    // क
    [InlineData(0x10D0, true)]    // ა
    [InlineData(0x1E9E, true)]    // ẞ (Latin Extended Additional)
    [InlineData(0xFF21, true)]    // fullwidth A
    [InlineData(0x4F60, true)]    // 你
    [InlineData(0xAC00, true)]    // 가 (a precomposed syllable joins only with jamo)
    [InlineData(0x2605, true)]    // ★ (Extended_Pictographic in the BMP joins only through VS16/ZWJ)
    [InlineData(0x00A9, true)]    // ©
    [InlineData(0x0301, false)]   // combining acute (Extend)
    [InlineData(0x0E31, false)]   // Thai mai han-akat (Extend)
    [InlineData(0x093F, false)]   // Devanagari vowel sign i (SpacingMark)
    [InlineData(0x0600, false)]   // Arabic number sign (Prepend)
    [InlineData(0x200D, false)]   // ZWJ
    [InlineData(0xFE0F, false)]   // VS16
    [InlineData(0x1100, false)]   // Hangul choseong (L)
    [InlineData(0x1161, false)]   // Hangul jungseong (V)
    [InlineData(0x11A8, false)]   // Hangul jongseong (T)
    [InlineData(0xD83D, false)]   // a surrogate
    [InlineData(0x1F1F5, false)]  // regional indicator P
    [InlineData(0x1F525, false)]  // 🔥: the emoji plane is placed explicitly by the renderer
    [InlineData(0x1F3FD, false)]  // skin tone modifier
    public void Simple_is_true_for_letters_of_every_script_and_false_for_what_joins(int codePoint, bool simple)
    {
        Assert.Equal(simple, Graphemes.Simple(codePoint));
    }

    [Fact]
    public void Two_simple_code_points_never_form_one_cluster()
    {
        // The generated bit must agree with the segmenter: for every simple BMP code point, a pair with another
        // simple code point (either way round, or itself) is two text elements. Controls are left out: CR LF is one
        // element, but controls are zero-width and never drawn, so the pair never reaches a cell.
        int[] partners = ['a', 0x044F, 0x4F60, 0xAC00, 0x2500, 0x05D0, 0x0E01];
        Span<char> pair = stackalloc char[2];
        int checkedCount = 0;
        for (int cp = 0x20; cp < 0x10000; cp++)
        {
            if (cp is >= 0x7F and <= 0x9F || cp is >= 0xD800 and <= 0xDFFF || !Graphemes.Simple(cp))
            {
                continue;
            }

            checkedCount++;
            foreach (int partner in partners)
            {
                pair[0] = (char)cp;
                pair[1] = (char)partner;
                Assert.True(StringInfo.GetNextTextElementLength(pair) == 1, $"U+{cp:X4} then U+{partner:X4} join");
                pair[0] = (char)partner;
                pair[1] = (char)cp;
                Assert.True(StringInfo.GetNextTextElementLength(pair) == 1, $"U+{partner:X4} then U+{cp:X4} join");
            }

            pair[0] = pair[1] = (char)cp;
            Assert.True(StringInfo.GetNextTextElementLength(pair) == 1, $"U+{cp:X4} joins itself");
        }

        Assert.True(checkedCount > 50_000, $"{checkedCount} simple code points");
    }

    [Theory]
    [InlineData("съешь же ещё этих мягких французских булок", 42)]
    [InlineData("αβγδεζηθικλμνξοπρστυφχψω", 24)]
    [InlineData("שלום עולם", 9)]
    [InlineData("مرحبا بالعالم", 13)]
    [InlineData("你好世界こんにちは안녕하세요", 28)]
    [InlineData("─│┌┐└┘├┤┬┴┼ ★☆", 14)]
    public void Text_without_marks_in_any_script_is_written_without_join_checks(string text, int width)
    {
        var buffer = new CellBuffer(64, 1);
        Assert.Equal(width, TextWidth.Of(text));
        Assert.Equal(width, buffer.SetString(0, 0, text));
        Assert.Equal(text, buffer.RowText(0).TrimEnd());
        Assert.False(buffer.RowMayJoin(0));   // so the renderer takes its join-free row loop
        for (int x = 0; x < width; x++)
        {
            Assert.False(buffer[x, 0].IsGrapheme);
        }
    }

    [Theory]
    [InlineData("กิน", 2)]          // Thai with a vowel above
    [InlineData("مَرحبا", 5)]   // Arabic with a fatha
    [InlineData("й", 1)]                // Cyrillic и + breve
    [InlineData("a️", 2)]                     // VS16 after ASCII asks for emoji presentation
    [InlineData("ab́c", 3)]
    public void Marks_after_letters_of_any_script_still_form_clusters(string text, int width)
    {
        var buffer = new CellBuffer(16, 1);
        Assert.Equal(width, TextWidth.Of(text));
        Assert.Equal(width, buffer.SetString(0, 0, text));
        Assert.True(buffer.RowMayJoin(0));
        Assert.Contains(Enumerable.Range(0, width), x => buffer[x, 0].IsGrapheme);
    }

    [Fact]
    public void Wide_glyph_runs_are_written_with_continuations_and_clip_whole()
    {
        var buffer = new CellBuffer(6, 1);
        Assert.Equal(5, buffer.SetString(0, 0, "你好a"));
        Assert.Equal(2, buffer[0, 0].Width);
        Assert.True(buffer[1, 0].IsContinuation);
        Assert.Equal(2, buffer[2, 0].Width);
        Assert.True(buffer[3, 0].IsContinuation);
        Assert.Equal("a", buffer[4, 0].Text);

        buffer = new CellBuffer(6, 1);
        Assert.Equal(2, buffer.SetString(0, 0, "你好", default, 3));   // the second glyph needs 2 columns, 1 is left
        Assert.Equal("你    ", buffer.RowText(0));
        Assert.False(buffer[2, 0].IsContinuation);
    }

    [Fact]
    public void Fast_path_runs_layer_over_each_cell_they_cover()
    {
        var buffer = new CellBuffer(8, 1);
        buffer.Fill(new Rect(0, 0, 4, 1), new Style(Color.Default, Color.Blue));
        buffer.Fill(new Rect(4, 0, 4, 1), new Style(Color.Default, Color.Green));
        buffer.SetString(0, 0, "яяя你好", new Style(Color.Red, Color.Default));
        Assert.Equal(new Style(Color.Red, Color.Blue), buffer[0, 0].Style);
        Assert.Equal(new Style(Color.Red, Color.Blue), buffer[3, 0].Style);    // 你 starts on blue
        Assert.Equal(new Style(Color.Red, Color.Blue), buffer[4, 0].Style);    // its continuation takes the lead's
        Assert.Equal(new Style(Color.Red, Color.Green), buffer[5, 0].Style);   // 好 starts on green
        Assert.Equal("яяя你好 ", buffer.RowText(0));
    }

    [Fact]
    public void Text_width_matches_what_set_string_writes()
    {
        string[] parts = ["a", "é", "é", "日", "👨‍👩‍👧", "❤️", "🇵🇱", "कि", " ", "\t", "👍🏽", "́", "\u044F", "\u0436 ", "\u2605", "a\uFE0F", "\u0E01", "\u0E34"];
        var random = new Random(3);
        for (int round = 0; round < 500; round++)
        {
            var text = new StringBuilder();
            for (int i = random.Next(1, 8); i > 0; i--)
            {
                text.Append(parts[random.Next(parts.Length)]);
            }

            var buffer = new CellBuffer(64, 1);
            Assert.Equal(TextWidth.Of(text.ToString()), buffer.SetString(0, 0, text.ToString()));
        }
    }
}

internal static class CellBufferTestExtensions
{
    public static CellBuffer Apply(this CellBuffer buffer, Action<CellBuffer> action)
    {
        action(buffer);
        return buffer;
    }
}
