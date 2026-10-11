namespace Tuinet.Comparisons;

/// <summary>One library drawing <see cref="Scenario"/> into a sink that counts what would reach the terminal.</summary>
internal interface IListScenario : IDisposable
{
    string Name { get; }

    /// <summary>Bytes the library handed to its output so far (UTF-8, escape sequences included).</summary>
    long Bytes { get; }

    /// <summary>Write calls so far; on a real terminal each one is a write syscall.</summary>
    long Writes { get; }

    /// <summary>Move the selection down one row and draw the frame all the way to the output.</summary>
    void Frame();
}
