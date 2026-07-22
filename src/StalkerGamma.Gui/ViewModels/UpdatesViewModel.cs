using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Stalker.Gamma.Extensions;
using Stalker.Gamma.GammaInstallerServices.GammaInstaller;
using Stalker.Gamma.Models;
using Stalker.Gamma.Services;
using StalkerGamma.Gui.Services;

namespace StalkerGamma.Gui.ViewModels;

public sealed record UpdateRow(string Status, string AddonName, string OldVersion, string NewVersion);

public partial class UpdatesViewModel : ViewModelBase
{
    public ObservableCollection<UpdateRow> Updates { get; } = [];

    [ObservableProperty]
    public partial string SummaryText { get; set; } = "Press Check to look for updates.";

    [ObservableProperty]
    public partial bool Minimal { get; set; }

    [ObservableProperty]
    public partial bool PreserveUserSettings { get; set; } = true;

    [ObservableProperty]
    public partial bool PreserveMcmSettings { get; set; } = true;

    public UpdatesViewModel(
        OperationRunner runner,
        SettingsService settings,
        GetRemoteGitRepoCommit getRemoteGitRepoCommit,
        LogService log
    )
    {
        _runner = runner;
        _settings = settings;
        _getRemoteGitRepoCommit = getRemoteGitRepoCommit;
        _log = log;
    }

    [RelayCommand]
    private async Task CheckAsync()
    {
        var p = _settings.ActiveProfile;
        if (p is null)
        {
            _log.Append("No active profile — create one in Settings first.");
            return;
        }
        Updates.Clear();
        SummaryText = "Checking…";
        var result = await _runner.RunAsync(
            "Update check",
            async (sp, ct) =>
            {
                var installer = sp.GetRequiredService<IGammaInstaller>();
                var gitService = sp.GetRequiredService<GitService>();
                var updateArgs = GammaInstallerArgs
                    .Create(p.Anomaly, p.Gamma, p.Cache)
                    .WithCancellationToken(ct)
                    .WithMo2Profile(p.Mo2Profile)
                    .Build();
                var diffed = await installer.DiffAddonRecordsAsync(updateArgs);

                var localGitRepoDiffs = await GetLocalGitRepoDiffsAsync(gitService, p.Gamma, ct);
                var diffs = diffed
                    .LocalRecords.Diff(diffed.OnlineRecords)
                    .Concat(localGitRepoDiffs)
                    .ToList();

                Dispatcher.UIThread.Post(() =>
                {
                    foreach (var diff in diffs)
                    {
                        Updates.Add(
                            new UpdateRow(
                                diff.DiffType.ToString(),
                                diff.NewListRecord?.AddonName
                                    ?? diff.OldListRecord?.AddonName
                                    ?? diff.NewListRecord?.DlLink
                                    ?? "N/A",
                                diff.OldListRecord?.ZipName ?? "—",
                                diff.NewListRecord?.ZipName ?? "—"
                            )
                        );
                    }
                    SummaryText = diffs.Count > 0
                        ? $"{diffs.Count} update(s) available"
                        : "No updates found";
                });
            }
        );
        if (result.Outcome != OperationOutcome.Succeeded)
        {
            SummaryText = "Check failed — see log";
        }
    }

    [RelayCommand]
    private async Task ApplyAsync()
    {
        var p = _settings.ActiveProfile;
        if (p is null)
        {
            _log.Append("No active profile — create one in Settings first.");
            return;
        }
        var result = await _runner.RunAsync(
            "Update apply",
            async (sp, ct) =>
            {
                var installer = sp.GetRequiredService<IGammaInstaller>();
                var updateArgs = GammaInstallerArgs
                    .Create(p.Anomaly, p.Gamma, p.Cache)
                    .WithCancellationToken(ct)
                    .WithMo2Profile(p.Mo2Profile)
                    .WithMinimal(Minimal)
                    .WithPreserveUserLtx(PreserveUserSettings)
                    .WithPreserveMcmSettings(PreserveMcmSettings)
                    .WithExperimentalPythonServerSettings(
                        _settings.Settings.ExperimentalModDbSettings.ToServerSettings()
                    )
                    .Build();
                updateArgs.GroupedAddonRecords = await installer.BuildUpdateGroupedAddonRecordsAsync(
                    updateArgs
                );
                installer.BuildSpecialRepoRecords(updateArgs);
                await installer.InstallAsync(updateArgs);
            }
        );
        if (result.Outcome == OperationOutcome.Succeeded)
        {
            SummaryText = "Updates applied";
            Updates.Clear();
        }
    }

    /// <summary>Mirror of the CLI's UpdateCmds.GetLocalGitRepoDiffs.</summary>
    private async Task<List<ModPackMakerRecordDiff>> GetLocalGitRepoDiffsAsync(
        GitService gitService,
        string gamma,
        System.Threading.CancellationToken cancellationToken
    )
    {
        var gammaDownloadsPath = Path.Join(gamma, "downloads");
        List<string> repos =
        [
            "gamma_setup",
            "gamma_large_files_v2",
            "Stalker_GAMMA",
            "teivaz_anomaly_gunslinger",
        ];
        const string repoOwner = "Grokitach";
        var localRepos = repos
            .Select(x => new { Name = x, Path = Path.Join(gammaDownloadsPath, $"{x}.git") })
            .Where(repoDir => Directory.Exists(repoDir.Path))
            .ToList();

        var diffs = new List<ModPackMakerRecordDiff>();
        foreach (var repoDir in localRepos)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var localSha = gitService.GetLatestCommitHash(repoDir.Path);
            var remoteSha =
                await _getRemoteGitRepoCommit.ExecuteAsync(repoOwner, repoDir.Name, cancellationToken)
                ?? "N/A";
            if (!string.Equals(localSha, remoteSha, StringComparison.OrdinalIgnoreCase))
            {
                diffs.Add(
                    new ModPackMakerRecordDiff(
                        DiffType.Modified,
                        new ModPackMakerRecord
                        {
                            DlLink = $"https://github.com/{repoOwner}/{repoDir.Name}",
                            AddonName = repoDir.Name,
                            Md5ModDb = localSha,
                            ZipName = localSha[..Math.Min(7, localSha.Length)],
                        },
                        new ModPackMakerRecord
                        {
                            DlLink = $"https://github.com/{repoOwner}/{repoDir.Name}",
                            AddonName = repoDir.Name,
                            Md5ModDb = remoteSha,
                            ZipName = remoteSha[..Math.Min(7, remoteSha.Length)],
                        }
                    )
                );
            }
        }
        return diffs;
    }

    private readonly OperationRunner _runner;
    private readonly SettingsService _settings;
    private readonly GetRemoteGitRepoCommit _getRemoteGitRepoCommit;
    private readonly LogService _log;
}
