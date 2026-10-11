using BenchmarkDotNet.Attributes;

namespace Tuinet.Comparisons;

/// <summary>
/// One frame of <see cref="Scenario"/> per invocation, in each library: state change, layout, draw and output
/// encoding, up to (not including) the write syscall.
/// </summary>
[MemoryDiagnoser]
public class ListScrollBenchmarks
{
    private TuinetList _tuinet = null!;
    private TuinetList _tuinetNoScrollRegions = null!;
    private SpectreList _spectre = null!;
    private TerminalGuiList _terminalGui = null!;

    [GlobalSetup(Target = nameof(Tuinet))]
    public void SetupTuinet() => _tuinet = new TuinetList();

    [GlobalSetup(Target = nameof(TuinetNoScrollRegions))]
    public void SetupTuinetNoScrollRegions() => _tuinetNoScrollRegions = new TuinetList(scrollRegions: false);

    [GlobalSetup(Target = nameof(Spectre))]
    public void SetupSpectre() => _spectre = new SpectreList();

    [GlobalSetup(Target = nameof(TerminalGui))]
    public void SetupTerminalGui() => _terminalGui = new TerminalGuiList();

    [GlobalCleanup]
    public void Cleanup()
    {
        _tuinet?.Dispose();
        _tuinetNoScrollRegions?.Dispose();
        _spectre?.Dispose();
        _terminalGui?.Dispose();
    }

    [Benchmark(Baseline = true, Description = "Tuinet")]
    public void Tuinet() => _tuinet.Frame();

    [Benchmark(Description = "Tuinet, no scroll regions")]
    public void TuinetNoScrollRegions() => _tuinetNoScrollRegions.Frame();

    [Benchmark(Description = "Spectre.Console live")]
    public void Spectre() => _spectre.Frame();

    [Benchmark(Description = "Terminal.Gui v2")]
    public void TerminalGui() => _terminalGui.Frame();
}
