using System;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using StalkerGamma.Gui.Utilities;

namespace StalkerGamma.Gui.Models;

/// <summary>
/// Vendored from stalker-gamma-cli/Models/CliProfile.cs. Field names and JSON shape must stay
/// identical so ~/.config/stalker-gamma/settings.json remains interchangeable with the CLI.
/// Observable on top of that shape so the profile list, the Install page's target paths and the
/// unsaved-changes indicator track edits made in the Settings editors (ObservableObject adds no
/// public properties, so the serialized JSON is unchanged).
/// </summary>
public partial class CliProfile : ObservableObject
{
    [ObservableProperty]
    public partial bool Active { get; set; }

    [ObservableProperty]
    public partial string ProfileName { get; set; } = "Gamma";

    [ObservableProperty]
    public partial string Anomaly { get; set; } = Path.Join("gamma", "anomaly");

    [ObservableProperty]
    public partial string Gamma { get; set; } = Path.Join("gamma", "gamma");

    [ObservableProperty]
    public partial string Cache { get; set; } = Path.Join("gamma", "cache");

    [ObservableProperty]
    public partial string Mo2Profile { get; set; } = "G.A.M.M.A";

    [ObservableProperty]
    public partial int DownloadThreads { get; set; } = 2;

    [ObservableProperty]
    public partial string ModPackMakerUrl { get; set; } =
        "https://stalker-gamma.com/api/client/v1/mods/list";

    [ObservableProperty]
    public partial string ModListUrl { get; set; } =
        "https://raw.githubusercontent.com/Grokitach/Stalker_GAMMA/refs/heads/main/G.A.M.M.A/modpack_data/modlist.txt";

    [ObservableProperty]
    public partial string GammaSetupRepoUrl { get; set; } =
        "https://github.com/Grokitach/gamma_setup";

    [ObservableProperty]
    public partial string GammaSetupRepoBranch { get; set; } = "main";

    [ObservableProperty]
    public partial string StalkerGammaRepoUrl { get; set; } =
        "https://github.com/Grokitach/Stalker_GAMMA";

    [ObservableProperty]
    public partial string StalkerGammaRepoBranch { get; set; } = "main";

    [ObservableProperty]
    public partial string GammaLargeFilesRepoUrl { get; set; } =
        "https://github.com/Grokitach/gamma_large_files_v2";

    [ObservableProperty]
    public partial string GammaLargeFilesRepoBranch { get; set; } = "main";

    [ObservableProperty]
    public partial string TeivazAnomalyGunslingerRepoUrl { get; set; } =
        "https://github.com/Grokitach/teivaz_anomaly_gunslinger";

    [ObservableProperty]
    public partial string TeivazAnomalyGunslingerRepoBranch { get; set; } = "main";

    [ObservableProperty]
    public partial string PythonApiUrl { get; set; } = "http://localhost:8000";

    public async Task SetActiveAsync()
    {
        Active = true;
        var modOrganizerIniPath = Path.Join(Gamma, "ModOrganizer.ini");
        if (File.Exists(modOrganizerIniPath))
        {
            var profilePath = ProfileUtility.ValidateProfileExists(Gamma);
            var mo2ProfilePath = Path.Join(profilePath, Mo2Profile);
            if (!Directory.Exists(mo2ProfilePath))
            {
                Directory.CreateDirectory(mo2ProfilePath);
                var mo2ProfileModListPath = Path.Join(mo2ProfilePath, "modlist.txt");
                // Dispose the client and bound the wait: an offline "Set active" click would
                // otherwise leak the handler and block on the default 100s timeout.
                using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(30) };
                await File.WriteAllTextAsync(
                    mo2ProfileModListPath,
                    await http.GetStringAsync(ModListUrl)
                );
            }
            var profiles = new DirectoryInfo(profilePath)
                .GetDirectories()
                .Select(x => x.Name)
                .ToList();
            if (profiles.Contains(Mo2Profile))
            {
                var mo2Ini = await File.ReadAllTextAsync(modOrganizerIniPath);
                mo2Ini = SelectedProfileRx()
                    .Replace(mo2Ini, $"selected_profile=@ByteArray({Mo2Profile})");
                await File.WriteAllTextAsync(modOrganizerIniPath, mo2Ini);
            }
        }
    }

    [GeneratedRegex(@"selected_profile=@ByteArray\((?<profile>.+)\)")]
    private partial Regex SelectedProfileRx();
}
