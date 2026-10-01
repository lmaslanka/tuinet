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
    var frame = new FrameBenchmarks();
    frame.Setup();
    frame.ListScroll();
    Console.WriteLine($"list scroll frame    {frame.ListScroll(),7} bytes");
    frame.Cleanup();
    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
