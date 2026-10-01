using System.Text.Json;
using System.Text.Json.Serialization;

namespace Tuinet.Samples.Branches;

public sealed class SettingsStore
{
    public SettingsStore(string path) => Path = path;

    public string Path { get; }

    public static SettingsStore Default()
    {
        string? root = Environment.GetEnvironmentVariable("XDG_CONFIG_HOME");
        if (string.IsNullOrWhiteSpace(root))
        {
            root = System.IO.Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config");
        }
        return new SettingsStore(System.IO.Path.Combine(root, "tuinet", "settings.json"));
    }

    public Settings Load()
    {
        if (!File.Exists(Path))
        {
            return new Settings();
        }

        try
        {
            string json = File.ReadAllText(Path);
            return JsonSerializer.Deserialize(json, SettingsJson.Default.Settings) ?? new Settings();
        }
        catch (JsonException)
        {
            return new Settings();
        }
    }

    public void Save(Settings settings)
    {
        string? dir = System.IO.Path.GetDirectoryName(Path);
        if (!string.IsNullOrEmpty(dir))
        {
            Directory.CreateDirectory(dir);
        }

        string tmp = Path + ".tmp";
        string json = JsonSerializer.Serialize(settings, SettingsJson.Default.Settings);
        File.WriteAllText(tmp, json);
        File.Move(tmp, Path, overwrite: true);

        if (!OperatingSystem.IsWindows())
        {
            File.SetUnixFileMode(Path, UnixFileMode.UserRead | UnixFileMode.UserWrite);
        }
    }
}

[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase, WriteIndented = true)]
[JsonSerializable(typeof(Settings))]
internal sealed partial class SettingsJson : JsonSerializerContext;
