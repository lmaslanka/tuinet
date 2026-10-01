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
}
