using System.Net.Http;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace StalkerGamma.Gui.Services;

/// <summary>
/// Vendored from stalker-gamma-cli/Services/GetRemoteGitRepoCommit.cs, simplified: reads only the
/// commit sha from the GitHub API instead of deserializing the full commit DTO graph.
/// </summary>
public class GetRemoteGitRepoCommit(IHttpClientFactory hcf)
{
    public async Task<string?> ExecuteAsync(
        string owner,
        string repo,
        CancellationToken cancellationToken = default
    )
    {
        var json = await _httpClient.GetStringAsync(
            $"https://api.github.com/repos/{owner}/{repo}/commits?per_page=1",
            cancellationToken
        );
        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
        {
            return null;
        }
        return doc.RootElement[0].GetProperty("sha").GetString();
    }

    private readonly HttpClient _httpClient = hcf.CreateClient("dlAddon");
}
