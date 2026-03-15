using System.Net.Http;
using System.Reflection;
using System.Text.Json;

namespace RadioV2.Services;

/// <summary>
/// Checks the GitHub Releases API for a newer version of RadioV2.
/// Silently does nothing if the check fails (no internet, API down, etc.).
/// </summary>
public class UpdateCheckerService
{
    private static readonly HttpClient _http = new();
    private const string ApiUrl = "https://api.github.com/repos/Natboa/RadioV2/releases/latest";

    static UpdateCheckerService()
    {
        _http.DefaultRequestHeaders.UserAgent.ParseAdd("RadioV2-UpdateChecker/1.0");
        _http.Timeout = TimeSpan.FromSeconds(10);
    }

    /// <summary>
    /// Returns the latest version string (e.g. "1.1.0") if a newer release exists,
    /// or null if the app is up to date or the check could not be completed.
    /// </summary>
    public async Task<string?> CheckForUpdateAsync()
    {
        try
        {
            var json = await _http.GetStringAsync(ApiUrl);
            using var doc = JsonDocument.Parse(json);

            if (!doc.RootElement.TryGetProperty("tag_name", out var tagProp)) return null;
            var tagName = tagProp.GetString();
            if (string.IsNullOrEmpty(tagName)) return null;

            // Strip leading 'v' from tag (e.g. "v1.1.0" → "1.1.0")
            var latestStr = tagName.TrimStart('v');
            if (!Version.TryParse(latestStr, out var latest)) return null;

            var current = Assembly.GetExecutingAssembly().GetName().Version;
            if (current is null) return null;

            return latest > current ? latestStr : null;
        }
        catch
        {
            return null; // silently ignore all errors
        }
    }
}
