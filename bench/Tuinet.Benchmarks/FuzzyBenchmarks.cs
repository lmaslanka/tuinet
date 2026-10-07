using BenchmarkDotNet.Attributes;
using Tuinet;

namespace Tuinet.Benchmarks;

/// <summary>Filtering a command list on a keystroke: 10,000 names against a 3-character pattern.</summary>
[MemoryDiagnoser]
public class FuzzyBenchmarks
{
    private static readonly string[] Words = ["open", "file", "toggle", "sidebar", "git", "commit", "search", "replace", "view", "terminal", "split", "editor"];
    private string[] _commands = null!;
    private int[] _matched = null!;

    [GlobalSetup]
    public void Setup()
    {
        var random = new Random(1);
        _commands = new string[10_000];
        for (int i = 0; i < _commands.Length; i++)
        {
            _commands[i] = $"{Words[random.Next(Words.Length)]} {Words[random.Next(Words.Length)]}: {Words[random.Next(Words.Length)]} {i}";
        }

        _matched = new int[8];
    }

    [Benchmark(Description = "score 10k commands, 3 chars", OperationsPerInvoke = 10_000)]
    public int Score()
    {
        int total = 0;
        foreach (string command in _commands)
        {
            total += Fuzzy.Score("tsb", command);
        }

        return total;
    }

    [Benchmark(Description = "score 10k commands, 3 chars, with indices", OperationsPerInvoke = 10_000)]
    public int ScoreWithIndices()
    {
        int total = 0;
        foreach (string command in _commands)
        {
            total += Fuzzy.Score("tsb", command, _matched);
        }

        return total;
    }
}
