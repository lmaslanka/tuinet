namespace Tuinet.Samples.Branches;

public interface IAzureProjects
{
    string[] ListProjects(string organization, string pat);
}
