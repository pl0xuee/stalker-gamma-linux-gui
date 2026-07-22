using System.Threading.Tasks;
using Stalker.Gamma.Models;
using StalkerGamma.Gui.Models;

namespace StalkerGamma.Gui.Services;

/// <summary>
/// Owns the CLI-compatible settings.json and copies the active profile into the engine's
/// StalkerGammaSettings singleton before each operation (the engine never reads settings.json).
/// </summary>
public class SettingsService(StalkerGammaSettings stalkerGammaSettings)
{
    public CliSettings Settings { get; private set; } = CliSettings.Load();

    public CliProfile? ActiveProfile => Settings.ActiveProfile;

    public Task SaveAsync() => Settings.SaveAsync();

    public void Reload() => Settings = CliSettings.Load();

    /// <summary>Mirror of the CLI's FullInstallCmd.InitializeSettings.</summary>
    public void ApplyActiveProfileToEngine(int? downloadThreadsOverride = null)
    {
        var p = ActiveProfile;
        if (p is null)
        {
            return;
        }
        stalkerGammaSettings.DownloadThreads = downloadThreadsOverride ?? p.DownloadThreads;
        stalkerGammaSettings.ModpackMakerList = p.ModPackMakerUrl;
        stalkerGammaSettings.ModListUrl = p.ModListUrl;
        stalkerGammaSettings.GammaSetupRepo = p.GammaSetupRepoUrl;
        stalkerGammaSettings.GammaSetupRepoBranch = p.GammaSetupRepoBranch;
        stalkerGammaSettings.StalkerGammaRepo = p.StalkerGammaRepoUrl;
        stalkerGammaSettings.StalkerGammaRepoBranch = p.StalkerGammaRepoBranch;
        stalkerGammaSettings.GammaLargeFilesRepo = p.GammaLargeFilesRepoUrl;
        stalkerGammaSettings.GammaLargeFilesRepoBranch = p.GammaLargeFilesRepoBranch;
        stalkerGammaSettings.TeivazAnomalyGunslingerRepo = p.TeivazAnomalyGunslingerRepoUrl;
        stalkerGammaSettings.TeivazAnomalyGunslingerRepoBranch = p.TeivazAnomalyGunslingerRepoBranch;
        stalkerGammaSettings.PythonApiUrl = p.PythonApiUrl;
    }
}
