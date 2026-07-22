using Microsoft.Extensions.DependencyInjection;
using Stalker.Gamma.Services;

namespace StalkerGamma.Gui.Services;

/// <summary>
/// Vendored from stalker-gamma-cli/Utilities/UtilitiesReady.cs, adapted to resolve the engine's
/// scoped probe services on demand.
/// </summary>
public class UtilitiesReadyService(IServiceProvider serviceProvider)
{
    public (bool IsReady, string Reason) Check()
    {
        using var scope = serviceProvider.CreateScope();
        var curl = scope.ServiceProvider.GetRequiredService<CurlService>();
        var sevenZip = scope.ServiceProvider.GetRequiredService<SevenZipService>();
        var tar = scope.ServiceProvider.GetRequiredService<TarService>();
        var unzip = scope.ServiceProvider.GetRequiredService<UnzipService>();

        var ready =
            curl.Ready
            && GitService.Ready
            && sevenZip.Ready
            && (OperatingSystem.IsWindows() || tar.Ready)
            && (OperatingSystem.IsWindows() || unzip.Ready);

        var reason = ready
            ? ""
            : $"""
                Curl: {(curl.Ready ? "Ready" : "Not Ready")}
                Git: {(GitService.Ready ? "Ready" : "Not Ready")}
                7z: {(sevenZip.Ready ? "Ready" : "Not Ready")}
                Tar: {(tar.Ready ? "Ready" : "Not Ready")}
                Unzip: {(unzip.Ready ? "Ready" : "Not Ready")}
                """;
        return (ready, reason);
    }
}
