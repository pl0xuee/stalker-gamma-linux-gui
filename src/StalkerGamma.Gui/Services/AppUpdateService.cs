using System;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StalkerGamma.Gui.Services;

public sealed record AppUpdateCheck(bool UpdateAvailable, string CurrentVersion, string LatestTag, string ReleaseUrl);

/// <summary>Checks this app's own GitHub Releases for a newer version.</summary>
public class AppUpdateService(IHttpClientFactory hcf)
{
    public const string RepoOwner = "pl0xuee";
    public const string RepoName = "stalker-gamma-linux-gui";

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";

    public async Task<AppUpdateCheck> CheckAsync(CancellationToken ct = default)
    {
        var json = await _httpClient.GetStringAsync(
            $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest",
            ct
        );
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var url = doc.RootElement.GetProperty("html_url").GetString() ?? "";
        var hasUpdate =
            Version.TryParse(tag.TrimStart('v', 'V'), out var latest)
            && Version.TryParse(CurrentVersion, out var current)
            && latest > current;
        return new AppUpdateCheck(hasUpdate, CurrentVersion, tag, url);
    }

    private readonly HttpClient _httpClient = hcf.CreateClient("dlAddon");
}
