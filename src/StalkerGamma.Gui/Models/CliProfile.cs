using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using StalkerGamma.Gui.Utilities;

namespace StalkerGamma.Gui.Models;

/// <summary>
/// Vendored from stalker-gamma-cli/Models/CliProfile.cs. Field names and JSON shape must stay
/// identical so ~/.config/stalker-gamma/settings.json remains interchangeable with the CLI.
/// </summary>
public partial class CliProfile
{
    public bool Active { get; set; }
    public string ProfileName { get; set; } = "Gamma";
    public string Anomaly { get; set; } = Path.Join("gamma", "anomaly");
    public string Gamma { get; set; } = Path.Join("gamma", "gamma");
    public string Cache { get; set; } = Path.Join("gamma", "cache");
    public string Mo2Profile { get; set; } = "G.A.M.M.A";
    public int DownloadThreads { get; set; } = 2;
    public string ModPackMakerUrl { get; set; } =
        "https://stalker-gamma.com/api/client/v1/mods/list";
    public string ModListUrl { get; set; } =
        "https://raw.githubusercontent.com/Grokitach/Stalker_GAMMA/refs/heads/main/G.A.M.M.A/modpack_data/modlist.txt";
    public string GammaSetupRepoUrl { get; set; } = "https://github.com/Grokitach/gamma_setup";
    public string GammaSetupRepoBranch { get; set; } = "main";
    public string StalkerGammaRepoUrl { get; set; } = "https://github.com/Grokitach/Stalker_GAMMA";
    public string StalkerGammaRepoBranch { get; set; } = "main";
    public string GammaLargeFilesRepoUrl { get; set; } =
        "https://github.com/Grokitach/gamma_large_files_v2";
    public string GammaLargeFilesRepoBranch { get; set; } = "main";
    public string TeivazAnomalyGunslingerRepoUrl { get; set; } =
        "https://github.com/Grokitach/teivaz_anomaly_gunslinger";
    public string TeivazAnomalyGunslingerRepoBranch { get; set; } = "main";
    public string PythonApiUrl { get; set; } = "http://localhost:8000";

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

    public CliProfile Clone() => (CliProfile)MemberwiseClone();

    [GeneratedRegex(@"selected_profile=@ByteArray\((?<profile>.+)\)")]
    private partial Regex SelectedProfileRx();
}
