using BenchmarkDotNet.Attributes;
using Tuinet;

namespace Tuinet.Benchmarks;

/// <summary>Diff + VT encoding of a 200×60 screen (the renderer alone, no tty).</summary>
[MemoryDiagnoser]
public class RenderBenchmarks
{
    private const int W = 200;
    private const int H = 60;
    private readonly Renderer _renderer = new(ColorMode.TrueColor);
    private readonly VtBuffer _out = new(1 << 20);
    private CellBuffer _a = null!;
    private CellBuffer _b = null!;
    private CellBuffer _same = null!;
    private CellBuffer _oneCell = null!;
    private CellBuffer _scrolled = null!;

    [GlobalSetup]
    public void Setup()
    {
        _a = Screen(0);
        _b = Screen(1);
        _same = Screen(0);
        _oneCell = Screen(0);
        _oneCell.SetRune(100, 30, new System.Text.Rune('#'));
        _scrolled = Screen(0, shift: 1);
    }

    [Benchmark(Description = "no change")]
    public int NoChange() => Render(_same, _a);

    [Benchmark(Description = "one cell")]
    public int OneCell() => Render(_oneCell, _a);

    /// <summary>Every row moved up by one, as when a full-width list scrolls.</summary>
    [Benchmark(Description = "scroll by one row")]
    public int ScrollOne() => Render(_scrolled, _a);

    [Benchmark(Description = "full repaint, styled")]
    public int FullRepaint() => Render(_b, _a);

    private int Render(CellBuffer current, CellBuffer previous)
    {
        _out.Clear();
        _renderer.AfterClear();
        _renderer.Render(current, previous, _out);
        return _out.Length;
    }

    /// <summary>Text on every row; colors vary per row (a gradient) so every row needs its own SGR.</summary>
    internal static CellBuffer Screen(int seed, int shift = 0)
    {
        var buffer = new CellBuffer(W, H);
        for (int y = 0; y < H; y++)
        {
            int r = y + shift;
            var style = new Style(Color.Rgb((byte)(r * 4 + seed), 200, 215), Color.Rgb(20, 24, (byte)(28 + seed)));
            buffer.Fill(buffer.Area.Row(y), style);
            for (int x = 0; x < W; x += 44)
            {
                buffer.SetString(x, y, seed == 0 ? "the quick brown fox jumps over the lazy dog" : "THE QUICK BROWN FOX JUMPS OVER THE LAZY DOG", style);
            }
        }

        return buffer;
    }
}
