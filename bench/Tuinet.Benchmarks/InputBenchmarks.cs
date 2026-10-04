using System.Text;
using BenchmarkDotNet.Attributes;
using Tuinet;

namespace Tuinet.Benchmarks;

[MemoryDiagnoser]
public class InputBenchmarks
{
    private readonly VtParser _parser = new();
    private byte[] _keys = null!;

    [GlobalSetup]
    public void Setup()
    {
        var input = new StringBuilder();
        for (int i = 0; i < 1000; i++)
        {
            input.Append("jk\u001b[A\u001b[1;5B\u001b[<0;10;5Mé你\u001bx");
        }

        _keys = Encoding.UTF8.GetBytes(input.ToString());
    }

    [Benchmark(Description = "parse 9000 events")]
    public int Parse()
    {
        _parser.Feed(_keys);
        int n = 0;
        while (_parser.TryTake(out _))
        {
            n++;
        }

        return n;
    }
}

[MemoryDiagnoser]
public class TextBenchmarks
{
    private readonly CellBuffer _buffer = new(200, 60);
    private const string Ascii = "the quick brown fox jumps over the lazy dog, again and again and again and again and again and again and again!";
    private const string Cjk = "你好世界こんにちは안녕하세요你好世界こんにちは안녕하세요你好世界こんにちは안녕하세요";
    private const string Clusters = "status 👍🏽 done ❤️ café 🇵🇱 team 👨‍👩‍👧 ok 1️⃣ नमस्ते ok status 👍🏽 done ❤️ café 🇵🇱 team 👨‍👩‍👧";

    [Benchmark(Description = "SetString ascii 60 rows")]
    public int SetStringAscii()
    {
        int x = 0;
        for (int y = 0; y < 60; y++)
        {
            x += _buffer.SetString(0, y, Ascii);
        }

        return x;
    }

    [Benchmark(Description = "SetString ascii 60 rows, explicit colors")]
    public int SetStringAsciiExplicit()
    {
        var style = new Style(Color.Rgb(200, 210, 215), Color.Rgb(20, 24, 28));
        int x = 0;
        for (int y = 0; y < 60; y++)
        {
            x += _buffer.SetString(0, y, Ascii, style);
        }

        return x;
    }

    [Benchmark(Description = "SetString CJK 60 rows")]
    public int SetStringCjk()
    {
        int x = 0;
        for (int y = 0; y < 60; y++)
        {
            x += _buffer.SetString(0, y, Cjk);
        }

        return x;
    }

    /// <summary>Text mixing ASCII with emoji sequences, flags, decomposed accents and Devanagari.</summary>
    [Benchmark(Description = "SetString grapheme clusters 60 rows")]
    public int SetStringClusters()
    {
        int x = 0;
        for (int y = 0; y < 60; y++)
        {
            x += _buffer.SetString(0, y, Clusters);
        }

        return x;
    }

    private static readonly Style Key = new(Color.Rgb(56, 189, 248), Color.Default, Attr.Bold);
    private static readonly Style Dim = new(Color.Rgb(125, 134, 150), Color.Default);

    /// <summary>A key-hint bar (10 styled pieces) per row, written piece by piece: the baseline.</summary>
    [Benchmark(Description = "key bar 60 rows, chained SetString")]
    public int KeyBarChained()
    {
        int x = 0;
        for (int y = 0; y < 60; y++)
        {
            x = 0;
            x = _buffer.SetString(x, y, "j/k", Key);
            x = _buffer.SetString(x, y, " move  ", Dim);
            x = _buffer.SetString(x, y, "enter", Key);
            x = _buffer.SetString(x, y, " edit  ", Dim);
            x = _buffer.SetString(x, y, "space", Key);
            x = _buffer.SetString(x, y, " toggle  ", Dim);
            x = _buffer.SetString(x, y, "p", Key);
            x = _buffer.SetString(x, y, " progress  ", Dim);
            x = _buffer.SetString(x, y, "q", Key);
            x = _buffer.SetString(x, y, " quit", Dim);
        }

        return x;
    }

    [Benchmark(Description = "key bar 60 rows, StyledTextBuilder + SetText")]
    public int KeyBarBuilder()
    {
        int x = 0;
        Span<char> chars = stackalloc char[64];
        Span<StyledRun> runs = stackalloc StyledRun[10];
        for (int y = 0; y < 60; y++)
        {
            var keys = new StyledTextBuilder(chars, runs);
            keys.Append("j/k", Key);
            keys.Append(" move  ", Dim);
            keys.Append("enter", Key);
            keys.Append(" edit  ", Dim);
            keys.Append("space", Key);
            keys.Append(" toggle  ", Dim);
            keys.Append("p", Key);
            keys.Append(" progress  ", Dim);
            keys.Append("q", Key);
            keys.Append(" quit", Dim);
            x = _buffer.SetText(0, y, keys.Build());
        }

        return x;
    }

    [Benchmark(Description = "key bar 60 rows, SetMarkup")]
    public int KeyBarMarkup()
    {
        int x = 0;
        for (int y = 0; y < 60; y++)
        {
            x = _buffer.SetMarkup(0, y, "[b fg=#38BDF8]j/k[/] move  [b fg=#38BDF8]enter[/] edit  [b fg=#38BDF8]space[/] toggle  [b fg=#38BDF8]p[/] progress  [b fg=#38BDF8]q[/] quit", Dim);
        }

        return x;
    }

    private static readonly StyledRun[] KeyBarRuns =
        [new(3, Key), new(7, Dim), new(5, Key), new(7, Dim), new(5, Key), new(9, Dim), new(1, Key), new(11, Dim), new(1, Key), new(5, Dim)];

    [Benchmark(Description = "key bar 60 rows, prebuilt StyledText + SetText")]
    public int KeyBarPrebuilt()
    {
        var text = new StyledText("j/k move  enter edit  space toggle  p progress  q quit", KeyBarRuns);
        int x = 0;
        for (int y = 0; y < 60; y++)
        {
            x = _buffer.SetText(0, y, text);
        }

        return x;
    }
}
