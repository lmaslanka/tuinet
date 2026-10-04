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

    [Fact]
    public void Text_width_matches_what_set_string_writes()
    {
        string[] parts = ["a", "é", "é", "日", "👨‍👩‍👧", "❤️", "🇵🇱", "कि", " ", "\t", "👍🏽", "́"];
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
