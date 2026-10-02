using BenchmarkDotNet.Running;
using Tuinet.Benchmarks;

if (args is ["bytes"])
{
    // Bytes on the wire per scenario: the cost over SSH and the work the terminal must parse.
    var render = new RenderBenchmarks();
    render.Setup();
    Console.WriteLine($"no change            {render.NoChange(),7} bytes");
    Console.WriteLine($"one cell             {render.OneCell(),7} bytes");
    Console.WriteLine($"full repaint, styled {render.FullRepaint(),7} bytes");
    Console.WriteLine($"scroll by one row    {render.ScrollOne(),7} bytes");
    var frame = new FrameBenchmarks();
    frame.Setup();
    frame.ListScroll();
    Console.WriteLine($"list scroll frame    {frame.ListScroll(),7} bytes");
    frame.TableScroll();
    Console.WriteLine($"table scroll frame   {frame.TableScroll(),7} bytes");

    // Steady state: the selection is past the viewport, so every frame scrolls by one row.
    var scrolling = new FrameBenchmarks();
    scrolling.Setup();
    for (int i = 0; i < 200; i++)
    {
        scrolling.ListScroll();
    }

    Console.WriteLine($"list, scrolling      {scrolling.ListScroll(),7} bytes");
    for (int i = 0; i < 200; i++)
    {
        scrolling.TableScroll();
    }

    Console.WriteLine($"table, scrolling     {scrolling.TableScroll(),7} bytes");
    scrolling.Cleanup();
    frame.Cleanup();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
