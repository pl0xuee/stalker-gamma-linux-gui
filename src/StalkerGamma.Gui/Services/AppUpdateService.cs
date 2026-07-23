using System;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StalkerGamma.Gui.Services;

public sealed record AppUpdateCheck(
    bool UpdateAvailable,
    string CurrentVersion,
    string LatestTag,
    string ReleaseUrl,
    string? AssetUrl
);

/// <summary>Checks this app's own GitHub Releases for a newer version and self-updates.</summary>
public class AppUpdateService(IHttpClientFactory hcf)
{
    public const string RepoOwner = "pl0xuee";
    public const string RepoName = "stalker-gamma-linux-gui";

    public static string CurrentVersion =>
        Assembly.GetExecutingAssembly().GetName().Version is { } v
            ? $"{v.Major}.{v.Minor}.{v.Build}"
            : "0.0.0";

    /// <summary>The AppImage file we were launched from; null when not running as an AppImage.</summary>
    public static string? InstalledAppImagePath => Environment.GetEnvironmentVariable("APPIMAGE");

    public async Task<AppUpdateCheck> CheckAsync(CancellationToken ct = default)
    {
        var json = await _httpClient.GetStringAsync(
            $"https://api.github.com/repos/{RepoOwner}/{RepoName}/releases/latest",
            ct
        );
        using var doc = JsonDocument.Parse(json);
        var tag = doc.RootElement.GetProperty("tag_name").GetString() ?? "";
        var url = doc.RootElement.GetProperty("html_url").GetString() ?? "";
        string? assetUrl = null;
        if (doc.RootElement.TryGetProperty("assets", out var assets))
        {
            foreach (var asset in assets.EnumerateArray())
            {
                var name = asset.GetProperty("name").GetString() ?? "";
                if (name.EndsWith(".AppImage", StringComparison.OrdinalIgnoreCase))
                {
                    assetUrl = asset.GetProperty("browser_download_url").GetString();
                    break;
                }
            }
        }
        var hasUpdate =
            Version.TryParse(tag.TrimStart('v', 'V'), out var latest)
            && Version.TryParse(CurrentVersion, out var current)
            && latest > current;
        return new AppUpdateCheck(hasUpdate, CurrentVersion, tag, url, assetUrl);
    }

    /// <summary>
    /// Downloads the release AppImage next to the installed one and atomically swaps it in.
    /// The running instance keeps its mounted (old) image; caller relaunches and exits.
    /// Returns the path to the updated AppImage.
    /// </summary>
    public async Task<string> DownloadAndInstallAsync(
        string assetUrl,
        IProgress<double>? progress = null,
        CancellationToken ct = default
    )
    {
        var target =
            InstalledAppImagePath
            ?? throw new InvalidOperationException("Not running from an AppImage");
        // Same directory as the target so the final rename is atomic (same filesystem).
        var staging = target + ".update-new";
        try
        {
            using (var response = await _httpClient.GetAsync(
                assetUrl,
                HttpCompletionOption.ResponseHeadersRead,
                ct
            ))
            {
                response.EnsureSuccessStatusCode();
                var total = response.Content.Headers.ContentLength;
                await using var source = await response.Content.ReadAsStreamAsync(ct);
                await using var dest = File.Create(staging);
                var buffer = new byte[1 << 16];
                long done = 0;
                int read;
                while ((read = await source.ReadAsync(buffer, ct)) > 0)
                {
                    await dest.WriteAsync(buffer.AsMemory(0, read), ct);
                    done += read;
                    if (total > 0)
                    {
                        progress?.Report((double)done / total.Value);
                    }
                }
            }
            File.SetUnixFileMode(
                staging,
                UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute
            );
            File.Move(staging, target, overwrite: true);
            return target;
        }
        catch
        {
            try
            {
                File.Delete(staging);
            }
            catch
            {
                // best-effort cleanup of the partial download
            }
            throw;
        }
    }

    private readonly HttpClient _httpClient = hcf.CreateClient("dlAddon");
}
