using System.Diagnostics;

namespace Tuinet;

public sealed class LocalGitBranches : IGitBranches
{
    public GitBranch[] ListLocal(string directory)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
            CreateNoWindow = true,
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(directory);
        start.ArgumentList.Add("for-each-ref");
        start.ArgumentList.Add("--format=%(HEAD)%09%(refname:short)");
        start.ArgumentList.Add("refs/heads");

        using Process? process = Process.Start(start);
        if (process is null)
        {
            return [];
        }

        string output = process.StandardOutput.ReadToEnd();
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            return [];
        }

        var branches = new List<GitBranch>();
        foreach (string line in output.Split('\n'))
        {
            string trimmed = line.TrimEnd('\r');
            if (trimmed.Length == 0)
            {
                continue;
            }

            bool current = trimmed.StartsWith('*');
            string name = trimmed.TrimStart('*', '\t', ' ');
            if (name.Length > 0)
            {
                branches.Add(new GitBranch(name, current));
            }
        }

        return [.. branches];
    }
}
