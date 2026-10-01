using Tuinet;
using Tuinet.Samples.Branches;

namespace Tuinet.Samples.Branches.Tests;

public class SettingsStoreTests
{
    [Fact]
    public void Missing_file_loads_empty_settings()
    {
        string path = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"), "settings.json");
        var store = new SettingsStore(path);
        Settings settings = store.Load();
        Assert.Equal("", settings.Organization);
        Assert.Equal("", settings.Pat);
        Assert.Equal("", settings.Project);
    }

    [Fact]
    public void Save_round_trips_settings()
    {
        string dir = System.IO.Path.Combine(System.IO.Path.GetTempPath(), Guid.NewGuid().ToString("N"));
        string path = System.IO.Path.Combine(dir, "settings.json");
        var store = new SettingsStore(path);
        store.Save(new Settings
        {
            Organization = "contoso",
            Pat = "token",
            Project = "Web",
        });

        Settings loaded = store.Load();
        Assert.Equal("contoso", loaded.Organization);
        Assert.Equal("token", loaded.Pat);
        Assert.Equal("Web", loaded.Project);
    }
}
