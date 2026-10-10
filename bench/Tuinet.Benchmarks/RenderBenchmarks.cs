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
    private readonly Renderer _margins = new(ColorMode.TrueColor) { LeftRightMargins = true };
    private readonly VtBuffer _out = new(1 << 20);
    private CellBuffer _a = null!;
    private CellBuffer _b = null!;
    private CellBuffer _same = null!;
    private CellBuffer _oneCell = null!;
    private CellBuffer _scrolled = null!;
    private CellBuffer _plain = null!;
    private CellBuffer _dialog = null!;
    private CellBuffer _short = null!;
    private CellBuffer _split = null!;
    private CellBuffer _splitScrolled = null!;

    [GlobalSetup]
    public void Setup()
    {
        _a = Screen(0);
        _b = Screen(1);
        _same = Screen(0);
        _oneCell = Screen(0);
        _oneCell.SetRune(100, 30, new System.Text.Rune('#'));
        _scrolled = Screen(0, shift: 1);
        _plain = Background();
        _dialog = Background();
        Rect box = _dialog.Area.Centered(80, 24);
        var dialogStyle = new Style(Color.Rgb(230, 230, 230), Color.Rgb(40, 44, 52));
        _dialog.Fill(box, dialogStyle);
        _dialog.Render(new Tuinet.Widgets.Block { BorderType = Tuinet.Widgets.BorderType.Rounded, Title = " dialog " }, box);
        for (int y = box.Y + 2; y < box.Bottom - 2; y++)
        {
            _dialog.SetString(box.X + 3, y, "a line of dialog text, then blank to the border", dialogStyle);
        }

        _split = Split(Screen(0));
        _splitScrolled = Split(Screen(0, shift: 1));
        _short = Screen(0);
        for (int y = 0; y < H; y++)
        {
            _short.Fill(new Rect(30, y, W - 30, 1), _short[0, y].Style);   // every row keeps 30 columns of text
        }
    }

    /// <summary>A centered 80×24 dialog closes over a themed (non-default) background.</summary>
    [Benchmark(Description = "dialog closed")]
    public int DialogClosed() => Render(_plain, _dialog);

    /// <summary>Every row gets shorter: text, then blanks in the row's background to the right edge.</summary>
    [Benchmark(Description = "rows shortened")]
    public int RowsShortened() => Render(_short, _a);

    [Benchmark(Description = "no change")]
    public int NoChange() => Render(_same, _a);

    [Benchmark(Description = "one cell")]
    public int OneCell() => Render(_oneCell, _a);

    /// <summary>Every row moved up by one, as when a full-width list scrolls.</summary>
    [Benchmark(Description = "scroll by one row")]
    public int ScrollOne() => Render(_scrolled, _a);

    [Benchmark(Description = "full repaint, styled")]
    public int FullRepaint() => Render(_b, _a);

    private int Render(CellBuffer current, CellBuffer previous) => Render(current, previous, _renderer);

    private int Render(CellBuffer current, CellBuffer previous, Renderer renderer)
    {
        _out.Clear();
        renderer.AfterClear();
        renderer.Render(current, previous, _out);
        return _out.Length;
    }

    /// <summary>A themed screen: blank cells with a dark background, as apps that paint their own background have.</summary>
    internal static CellBuffer Background()
    {
        var buffer = new CellBuffer(W, H);
        buffer.Fill(buffer.Area, new Style(Color.Rgb(200, 200, 200), Color.Rgb(20, 24, 28)));
        return buffer;
    }

    /// <summary>The left 150 columns move up by one beside a 50-column panel that stays: the terminal has no margins.</summary>
    [Benchmark(Description = "split scroll, no margins")]
    public int SplitScroll() => Render(_splitScrolled, _split);

    /// <summary>The same on a terminal with left/right margins (DECLRMM): the terminal moves the 150 columns.</summary>
    [Benchmark(Description = "split scroll, margins")]
    public int SplitScrollMargins() => Render(_splitScrolled, _split, _margins);

    /// <summary>A page change with left/right margins on: every row dirty, no shift to find (the detection's worst case).</summary>
    [Benchmark(Description = "full repaint, margins")]
    public int FullRepaintMargins() => Render(_b, _a, _margins);

    /// <summary>Overlay a panel on columns 150-199 whose rows all differ and don't move.</summary>
    private static CellBuffer Split(CellBuffer buffer)
    {
        var panel = new Style(Color.Rgb(220, 220, 220), Color.Rgb(30, 30, 40));
        for (int y = 0; y < H; y++)
        {
            buffer.Fill(new Rect(150, y, W - 150, 1), panel);
            buffer.SetString(151, y, $"│ panel row {y}: {y * 7919 % 1000} items", panel);
        }

        return buffer;
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
