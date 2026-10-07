namespace Tuinet.Tests;

public class FuzzyTests
{
    [Fact]
    public void No_match_is_minus_one()
    {
        Assert.Equal(-1, Fuzzy.Score("xyz", "abc"));
        Assert.Equal(-1, Fuzzy.Score("abcd", "abc"));   // longer than the text
        Assert.Equal(-1, Fuzzy.Score("a", ""));
    }

    [Fact]
    public void Chars_must_appear_in_order()
    {
        Assert.Equal(-1, Fuzzy.Score("ba", "ab"));
        Assert.True(Fuzzy.Score("ab", "ab") >= 0);
        Assert.True(Fuzzy.Score("prg", "progress dialog") >= 0);
    }

    [Fact]
    public void An_empty_pattern_matches_everything_equally()
    {
        Assert.Equal(0, Fuzzy.Score("", "anything"));
        Assert.Equal(0, Fuzzy.Score("", ""));
    }

    [Fact]
    public void Word_starts_beat_mid_word_matches()
    {
        Assert.True(Fuzzy.Score("ep", "Edit Palette") > Fuzzy.Score("ep", "deep"));
        Assert.True(Fuzzy.Score("sk", "sort by kind") > Fuzzy.Score("sk", "task"));
        Assert.True(Fuzzy.Score("fb", "FooBar") > Fuzzy.Score("fb", "Foobar"));   // camelCase hump
    }

    [Fact]
    public void Consecutive_beats_scattered_and_an_early_start_beats_a_late_one()
    {
        Assert.True(Fuzzy.Score("abc", "abcxxxx") > Fuzzy.Score("abc", "axbxcxx"));
        Assert.True(Fuzzy.Score("x", "x....") > Fuzzy.Score("x", "....x"));
        Assert.True(Fuzzy.Score("prog", "progress") > Fuzzy.Score("prog", "copy program"));
    }

    [Fact]
    public void Smart_case_ignores_case_unless_the_pattern_has_an_uppercase_letter()
    {
        Assert.True(Fuzzy.Score("e", "EDIT") >= 0);
        Assert.True(Fuzzy.Score("E", "Edit") >= 0);
        Assert.Equal(-1, Fuzzy.Score("E", "edit"));
        Assert.Equal(-1, Fuzzy.Score("eD", "edit"));
    }

    [Fact]
    public void Matched_indices_come_from_the_tightened_window()
    {
        Span<int> matched = stackalloc int[3];
        Assert.True(Fuzzy.Score("abc", "a_abc", matched) >= 0);
        Assert.Equal([2, 3, 4], matched.ToArray());   // not the first 'a', which starts a longer window

        matched = stackalloc int[2];
        Fuzzy.Score("ep", "Edit Palette", matched);
        Assert.Equal([0, 5], matched.ToArray());
    }

    [Fact]
    public void Scores_agree_with_and_without_indices_and_misses_leave_them_alone()
    {
        Span<int> matched = [-7, -7];
        Assert.Equal(Fuzzy.Score("tg", "toggle enabled"), Fuzzy.Score("tg", "toggle enabled", matched));
        matched.Fill(-7);
        Assert.Equal(-1, Fuzzy.Score("zz", "toggle", matched));
        Assert.Equal([-7, -7], matched.ToArray());
        Assert.Throws<ArgumentException>(() => Fuzzy.Score("abc", "abc", new int[2]));
    }
}
