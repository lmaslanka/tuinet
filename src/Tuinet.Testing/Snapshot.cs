using System.Runtime.CompilerServices;
using System.Text;

namespace Tuinet.Testing;

/// <summary>
/// A check from <see cref="Tuinet.Testing"/> failed: text missing from a buffer, a buffer that doesn't match its snapshot,
/// a step that keeps allocating. Any test framework reports it as a failure.
/// </summary>
public sealed class ScreenAssertionException(string message) : Exception(message);

/// <summary>
/// Snapshot tests for frames: <see cref="Verify(CellBuffer, string, bool, string?, string)"/> compares a buffer with
/// <c>__snapshots__/&lt;name&gt;.txt</c> next to the test's source file. On a mismatch (or a missing snapshot) it writes
/// <c>&lt;name&gt;.received.txt</c> beside it and throws; set <c>TUINET_ACCEPT_SNAPSHOTS=1</c> to write the snapshots
/// instead, then review them in the diff.
/// <code>
/// var buffer = new CellBuffer(40, 10);
/// app.Render(buffer, nowMs: 0);
/// Snapshot.Verify(buffer, "main-screen");
/// </code>
/// </summary>
public static class Snapshot
{
    /// <summary>The environment variable that makes <see cref="Verify(string, string, string?, string)"/> write snapshots instead of comparing.</summary>
    public const string AcceptVariable = "TUINET_ACCEPT_SNAPSHOTS";

    /// <summary>
    /// Compare <paramref name="buffer"/> with snapshot <paramref name="name"/>: as <see cref="CellBufferTesting.ToStyledString"/>
    /// when <paramref name="styled"/> (colors and attributes count), else as plain text (<see cref="CellBuffer.ToString"/>).
    /// Trailing spaces and blank lines are ignored. Snapshots live in <paramref name="directory"/>, by default a
    /// <c>__snapshots__</c> folder next to the calling test's source file.
    /// </summary>
    public static void Verify(CellBuffer buffer, string name, bool styled = true, string? directory = null, [CallerFilePath] string sourceFile = "")
    {
        ArgumentNullException.ThrowIfNull(buffer);
        Verify(styled ? buffer.ToStyledString() : buffer.ToString(), name, directory, sourceFile);
    }

    /// <summary>
    /// Compare <paramref name="actual"/> with snapshot <paramref name="name"/>, ignoring trailing spaces, trailing blank
    /// lines and line-ending style. Snapshots live in <paramref name="directory"/>, by default <c>__snapshots__</c> next to the calling source file.
    /// </summary>
    public static void Verify(string actual, string name, string? directory = null, [CallerFilePath] string sourceFile = "")
    {
        ArgumentNullException.ThrowIfNull(actual);
        ArgumentException.ThrowIfNullOrEmpty(name);
        if (name.IndexOfAny(Path.GetInvalidFileNameChars()) >= 0)
        {
            throw new ArgumentException($"'{name}' is not a valid file name.", nameof(name));
        }

        directory ??= Path.Combine(SourceDirectory(sourceFile), "__snapshots__");
        string expectedPath = Path.Combine(directory, name + ".txt");
        string receivedPath = Path.Combine(directory, name + ".received.txt");
        string normalized = Normalize(actual);

        if (IsAccepting())
        {
            Directory.CreateDirectory(directory);
            File.WriteAllText(expectedPath, normalized);
            File.Delete(receivedPath);
            return;
        }

        string? expected = File.Exists(expectedPath) ? Normalize(File.ReadAllText(expectedPath)) : null;
        if (expected == normalized)
        {
            File.Delete(receivedPath);
            return;
        }

        Directory.CreateDirectory(directory);
        File.WriteAllText(receivedPath, normalized);
        throw new ScreenAssertionException(expected is null
            ? $"No snapshot '{name}' yet. Wrote {receivedPath}: review it and rename it to {name}.txt, or run with {AcceptVariable}=1.\n{normalized}"
            : $"Snapshot '{name}' differs. Wrote {receivedPath}; accept it with {AcceptVariable}=1.\n{FirstDifference(expected, normalized)}");
    }

    /// <summary>
    /// Read a styled snapshot (<see cref="CellBufferTesting.ToStyledString"/>) back into a buffer as wide as its
    /// widest line and one row per line. Plain text parses too, since it is markup without tags (escape <c>[</c> as <c>[[</c>).
    /// </summary>
    public static CellBuffer Parse(string styled)
    {
        ArgumentNullException.ThrowIfNull(styled);
        string[] lines = styled.ReplaceLineEndings("\n").Split('\n');
        int width = 1;
        foreach (string line in lines)
        {
            StyledText text = Markup.Parse(line, new char[line.Length], new StyledRun[line.Length / 3 + 2]);
            width = Math.Max(width, TextWidth.Of(text));
        }

        var buffer = new CellBuffer(width, lines.Length);
        for (int y = 0; y < lines.Length; y++)
        {
            buffer.SetMarkup(0, y, lines[y]);
        }

        return buffer;
    }

    private static bool IsAccepting() =>
        Environment.GetEnvironmentVariable(AcceptVariable) is { } value && value is "1" or "true" or "TRUE" or "True";

    /// <summary>
    /// LF line endings, no trailing spaces or blank lines, one final newline: what editors and git keep a text file as.
    /// </summary>
    private static string Normalize(string text)
    {
        var builder = new StringBuilder(text.Length + 1);
        foreach (string line in text.ReplaceLineEndings("\n").Split('\n'))
        {
            builder.Append(line.TrimEnd(' ')).Append('\n');
        }

        return builder.ToString().TrimEnd('\n') + "\n";
    }

    private static string FirstDifference(string expected, string actual)
    {
        string[] want = expected.Split('\n');
        string[] got = actual.Split('\n');
        for (int i = 0; i < Math.Max(want.Length, got.Length); i++)
        {
            string a = i < want.Length ? want[i] : "<missing>";
            string b = i < got.Length ? got[i] : "<missing>";
            if (a != b)
            {
                return $"First difference at line {i + 1}:\n  expected: {a}\n  actual:   {b}";
            }
        }

        return "";
    }

    /// <summary>
    /// The folder of the test's source file. CI builds map source paths (<c>/_/tests/…</c>), so when that folder
    /// doesn't exist, find the mapped path under an ancestor of the test assembly's folder instead.
    /// </summary>
    private static string SourceDirectory(string sourceFile)
    {
        string? directory = Path.GetDirectoryName(sourceFile);
        if (string.IsNullOrEmpty(directory))
        {
            throw new InvalidOperationException("No source file to put snapshots next to: pass a directory.");
        }

        if (Directory.Exists(directory))
        {
            return directory;
        }

        string[] parts = directory.Split(['/', '\\'], StringSplitOptions.RemoveEmptyEntries);
        for (DirectoryInfo? root = new(AppContext.BaseDirectory); root is not null; root = root.Parent)
        {
            for (int skip = 0; skip < parts.Length; skip++)
            {
                string candidate = Path.Combine([root.FullName, .. parts[skip..]]);
                if (Directory.Exists(candidate))
                {
                    return candidate;
                }
            }
        }

        throw new InvalidOperationException($"Can't find the folder of {sourceFile}: pass a directory.");
    }
}
