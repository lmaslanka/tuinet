using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace Tuinet.Samples.Branches;

public sealed class AzureDevOpsProjects : IAzureProjects
{
    private static readonly HttpClient Shared = new() { Timeout = TimeSpan.FromSeconds(15) };
    private readonly HttpClient _http;

    public AzureDevOpsProjects(HttpClient? http = null) => _http = http ?? Shared;

    public string[] ListProjects(string organization, string pat)
    {
        if (string.IsNullOrWhiteSpace(organization))
        {
            throw new InvalidOperationException("Organization is required.");
        }

        if (string.IsNullOrWhiteSpace(pat))
        {
            throw new InvalidOperationException("PAT is required.");
        }

        string url =
            $"https://dev.azure.com/{Uri.EscapeDataString(organization.Trim())}/_apis/projects?api-version=7.1";

        using var request = new HttpRequestMessage(HttpMethod.Get, url);
        request.Headers.Authorization = new AuthenticationHeaderValue(
            "Basic",
            Convert.ToBase64String(Encoding.ASCII.GetBytes(":" + pat)));
        request.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/json"));

        using HttpResponseMessage response = _http.Send(request);
        if (!response.IsSuccessStatusCode)
        {
            throw new InvalidOperationException($"Azure DevOps HTTP {(int)response.StatusCode}");
        }

        using Stream stream = response.Content.ReadAsStream();
        using JsonDocument doc = JsonDocument.Parse(stream);
        if (!doc.RootElement.TryGetProperty("value", out JsonElement value) ||
            value.ValueKind != JsonValueKind.Array)
        {
            return [];
        }

        var names = new List<string>();
        foreach (JsonElement project in value.EnumerateArray())
        {
            if (project.TryGetProperty("name", out JsonElement name) &&
                name.ValueKind == JsonValueKind.String)
            {
                string? text = name.GetString();
                if (!string.IsNullOrWhiteSpace(text))
                {
                    names.Add(text);
                }
            }
        }

        names.Sort(StringComparer.OrdinalIgnoreCase);
        return [.. names];
    }
}
