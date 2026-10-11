using BenchmarkDotNet.Running;
using Tuinet;
using Tuinet.Benchmarks;

if (args is ["bytes"])
{
    // Bytes on the wire per scenario: the cost over SSH and the work the terminal must parse. "REP" is a terminal that
    // has REP (repeat the last glyph, detected with XTVERSION); without it the bytes are what every terminal gets.
    Console.WriteLine("                       no REP      REP");
    var plain = new RenderBenchmarks { Repeat = false };
    var repeat = new RenderBenchmarks { Repeat = true };
    plain.Setup();
    repeat.Setup();
    Row("no change", plain.NoChange(), repeat.NoChange());
    Row("one cell", plain.OneCell(), repeat.OneCell());
    Row("full repaint, styled", plain.FullRepaint(), repeat.FullRepaint());
    Row("full repaint, cyrillic", plain.FullRepaintCyrillic(), repeat.FullRepaintCyrillic());
    Row("scroll by one row", plain.ScrollOne(), repeat.ScrollOne());
    Row("split scroll", plain.SplitScroll(), repeat.SplitScroll());
    Row("split scroll, DECLRMM", plain.SplitScrollMargins(), repeat.SplitScrollMargins());
    Row("dialog closed", plain.DialogClosed(), repeat.DialogClosed());
    Row("dialog opened", plain.DialogOpened(), repeat.DialogOpened());
    Row("rows shortened", plain.RowsShortened(), repeat.RowsShortened());
    Row("rules", plain.Rules(), repeat.Rules());
    Row("bars", plain.Bars(), repeat.Bars());

    var frame = new FrameBenchmarks { Repeat = false };
    var frameRepeat = new FrameBenchmarks { Repeat = true };
    frame.Setup();
    frameRepeat.Setup();
    Row("list first frame", frame.ListScroll(), frameRepeat.ListScroll());
    Row("list scroll frame", frame.ListScroll(), frameRepeat.ListScroll());
    Row("table first frame", frame.TableScroll(), frameRepeat.TableScroll());
    Row("table scroll frame", frame.TableScroll(), frameRepeat.TableScroll());

    // Steady state: the selection is past the viewport, so every frame scrolls by one row.
    var scrolling = new FrameBenchmarks { Repeat = false };
    var scrollingRepeat = new FrameBenchmarks { Repeat = true };
    scrolling.Setup();
    scrollingRepeat.Setup();
    for (int i = 0; i < 200; i++)
    {
        scrolling.ListScroll();
        scrollingRepeat.ListScroll();
    }

    Row("list, scrolling", scrolling.ListScroll(), scrollingRepeat.ListScroll());
    for (int i = 0; i < 200; i++)
    {
        scrolling.TableScroll();
        scrollingRepeat.TableScroll();
    }

    Row("table, scrolling", scrolling.TableScroll(), scrollingRepeat.TableScroll());
    scrolling.Cleanup();
    scrollingRepeat.Cleanup();
    frame.Cleanup();
    frameRepeat.Cleanup();

    // The Showcase sample at 120×40: its first frame, moving the selection, the edit dialog, the other tabs.
    using var showcase = new ShowcaseBytes(repeat: false);
    using var showcaseRepeat = new ShowcaseBytes(repeat: true);
    Row("showcase first frame", showcase.FirstFrame(), showcaseRepeat.FirstFrame());
    Showcase("showcase j", Event.FromChar('j'));
    Showcase("showcase edit opens", Event.FromKey(KeyCode.Enter));
    Showcase("showcase edit closes", Event.FromKey(KeyCode.Escape));
    Showcase("showcase stats tab", Event.FromChar(']'));
    Showcase("showcase groups tab", Event.FromChar(']'));
    Showcase("showcase list tab", Event.FromChar(']'));
    return;

    void Showcase(string name, Event ev) => Row(name, showcase.After(ev), showcaseRepeat.After(ev));
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);

static void Row(string name, int plain, int repeat) =>
    Console.WriteLine($"{name,-22}{plain,7}  {repeat,7} bytes{(plain > 0 && repeat < plain ? $"  -{100 - 100 * repeat / plain}%" : "")}");
