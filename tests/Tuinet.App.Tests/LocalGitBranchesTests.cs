using System.Diagnostics;
using Tuinet;

namespace Tuinet.App.Tests;

public class LocalGitBranchesTests
{
    [Fact]
    public void ListLocal_returns_local_branches_and_marks_current()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Git(dir, "init", "-b", "main");
        Git(dir, "config", "user.email", "t@t");
        Git(dir, "config", "user.name", "t");
        File.WriteAllText(System.IO.Path.Combine(dir, "f"), "x");
        Git(dir, "add", "f");
        Git(dir, "commit", "-m", "init");
        Git(dir, "branch", "feature");
        Git(dir, "checkout", "-b", "topic");

        GitBranch[] branches = new LocalGitBranches().ListLocal(dir);

        Assert.Contains(branches, b => b.Name == "main" && !b.Current);
        Assert.Contains(branches, b => b.Name == "feature" && !b.Current);
        Assert.Contains(branches, b => b.Name == "topic" && b.Current);
    }

    [Fact]
    public void ListLocal_returns_empty_when_not_a_repo()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(dir);
        Assert.Empty(new LocalGitBranches().ListLocal(dir));
    }

    private static void Git(string dir, params string[] args)
    {
        var start = new ProcessStartInfo
        {
            FileName = "git",
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        start.ArgumentList.Add("-C");
        start.ArgumentList.Add(dir);
        start.ArgumentList.Add("-c");
        start.ArgumentList.Add("commit.gpgsign=false");
        foreach (string arg in args)
        {
            start.ArgumentList.Add(arg);
        }

        using Process process = Process.Start(start) ?? throw new InvalidOperationException("git missing");
        process.WaitForExit();
        if (process.ExitCode != 0)
        {
            throw new InvalidOperationException(process.StandardError.ReadToEnd());
        }
    }
}
