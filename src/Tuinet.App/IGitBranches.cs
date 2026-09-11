namespace Tuinet;

public interface IGitBranches
{
    GitBranch[] ListLocal(string directory);
}

public sealed record GitBranch(string Name, bool Current);
