namespace Tuinet;

public interface IAzureProjects
{
    string[] ListProjects(string organization, string pat);
}
