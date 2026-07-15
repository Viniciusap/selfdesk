using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json.Serialization;

namespace SelfDesk.Viewer.Settings;

// Issue #10: non-blocking startup check against GitHub Releases. Fails silently on
// any error (no internet, rate limit, malformed response) — never blocks startup or
// surfaces an error to the user, per the issue's explicit notes.
public static class UpdateChecker
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(5) };

    static UpdateChecker()
    {
        // GitHub API requires a User-Agent header on all requests.
        Http.DefaultRequestHeaders.UserAgent.ParseAdd("SelfDesk-Viewer");
    }

    /// <returns>The latest release tag (e.g. "v0.9.0") if newer than <paramref name="currentVersion"/>, else null.</returns>
    public static async Task<string?> CheckForUpdateAsync(string currentVersion)
    {
        try
        {
            var release = await Http.GetFromJsonAsync<GitHubRelease>(
                "https://api.github.com/repos/Viniciusap/selfdesk/releases/latest");
            return PickUpdateTag(release?.TagName, currentVersion);
        }
        catch
        {
            return null;
        }
    }

    // Pure comparison, split out from the network call above so it can be unit tested
    // without hitting the real GitHub API.
    public static string? PickUpdateTag(string? latestTagName, string currentVersion)
    {
        if (latestTagName is not { Length: > 0 } tag) return null;
        if (!Version.TryParse(tag.TrimStart('v', 'V'), out var latest)) return null;
        if (!Version.TryParse(currentVersion, out var current)) return null;
        return latest > current ? tag : null;
    }

    private sealed class GitHubRelease
    {
        [JsonPropertyName("tag_name")]
        public string? TagName { get; set; }
    }
}
