using BenchmarkDotNet.Running;
using Tuinet.Comparisons;

if (args is ["bytes", ..])
{
    // Bytes on the wire per steady-state frame (every frame scrolls the list by one row), and how many write
    // calls carry them. Time and allocations come from the BenchmarkDotNet run instead. With a directory, each
    // library's whole output also goes to <dir>/<library>.ansi: `cat` it in a 200x60 terminal to watch it.
    const int warmup = 100;
    const int frames = 1000;
    string? captureDir = args.Length > 1 ? args[1] : null;
    Console.WriteLine($"{Scenario.Width}x{Scenario.Height}, {Scenario.Items.Length} rows, {frames} frames after {warmup} warm-up frames");
    Console.WriteLine($"{"library",-28}{"bytes/frame",12}{"writes/frame",14}");
    (string File, Func<Stream?, IListScenario> Create)[] libraries =
    [
        ("tuinet", capture => new TuinetList(capture: capture)),
        ("tuinet-no-scroll-regions", capture => new TuinetList(scrollRegions: false, capture)),
        ("spectre", capture => new SpectreList(capture)),
        ("terminal-gui", capture => new TerminalGuiList(capture)),
    ];

    foreach ((string file, Func<Stream?, IListScenario> create) in libraries)
    {
        using Stream? capture = captureDir is null ? null : File.Create(Path.Combine(captureDir, file + ".ansi"));
        using IListScenario scenario = create(capture);
        for (int i = 0; i < warmup; i++)
        {
            scenario.Frame();
        }

        long bytes = scenario.Bytes;
        long writes = scenario.Writes;
        for (int i = 0; i < frames; i++)
        {
            scenario.Frame();
        }

        Console.WriteLine($"{scenario.Name,-28}{(scenario.Bytes - bytes) / (double)frames,12:N0}{(scenario.Writes - writes) / (double)frames,14:N1}");
    }

    return;
}

BenchmarkSwitcher.FromAssembly(typeof(Program).Assembly).Run(args);
