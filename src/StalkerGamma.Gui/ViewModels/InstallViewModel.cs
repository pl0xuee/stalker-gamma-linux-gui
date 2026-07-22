using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Microsoft.Extensions.DependencyInjection;
using Stalker.Gamma.Factories;
using Stalker.Gamma.GammaInstallerServices;
using Stalker.Gamma.GammaInstallerServices.GammaInstaller;
using StalkerGamma.Gui.Services;

namespace StalkerGamma.Gui.ViewModels;

public partial class ModProgressRow : ObservableObject
{
    public required string Name { get; init; }

    [ObservableProperty]
    public partial string Phase { get; set; } = "";

    [ObservableProperty]
    public partial double Progress { get; set; }
}

public partial class InstallViewModel : ViewModelBase
{
    public ObservableCollection<ModProgressRow> ModRows { get; } = [];

    [ObservableProperty]
    public partial bool Minimal { get; set; }

    [ObservableProperty]
    public partial bool Offline { get; set; }

    [ObservableProperty]
    public partial bool SkipGithubDownloads { get; set; }

    [ObservableProperty]
    public partial bool SkipExtractOnHashMatch { get; set; }

    [ObservableProperty]
    public partial bool PreserveUserSettings { get; set; } = true;

    [ObservableProperty]
    public partial bool PreserveMcmSettings { get; set; } = true;

    [ObservableProperty]
    public partial string? ModPackMakerPath { get; set; }

    [ObservableProperty]
    public partial string? ModListPath { get; set; }

    [ObservableProperty]
    public partial string CheckResultText { get; set; } = "";

    public InstallViewModel(
        OperationRunner runner,
        SettingsService settings,
        AnomalyService anomalyService,
        LogService log,
        GammaProgress gammaProgress
    )
    {
        _runner = runner;
        _settings = settings;
        _anomalyService = anomalyService;
        _log = log;

        // Batch (not sample) so no per-mod terminal state is lost, then update rows on the UI thread.
        Observable
            .FromEventPattern<GammaProgress.GammaInstallProgressEventArgs>(
                h => gammaProgress.ProgressChanged += h,
                h => gammaProgress.ProgressChanged -= h
            )
            .Select(x => x.EventArgs)
            .Buffer(TimeSpan.FromMilliseconds(250))
            .Where(batch => batch.Count > 0)
            .Subscribe(batch => Dispatcher.UIThread.Post(() => ApplyProgressBatch(batch)));
    }

    public string AnomalyPath => _settings.ActiveProfile?.Anomaly ?? "(no active profile)";
    public string GammaPath => _settings.ActiveProfile?.Gamma ?? "(no active profile)";
    public string CachePath => _settings.ActiveProfile?.Cache ?? "(no active profile)";
    public bool HasActiveProfile => _settings.ActiveProfile is not null;

    public void RefreshProfilePaths()
    {
        OnPropertyChanged(nameof(AnomalyPath));
        OnPropertyChanged(nameof(GammaPath));
        OnPropertyChanged(nameof(CachePath));
        OnPropertyChanged(nameof(HasActiveProfile));
    }

    [RelayCommand]
    private async Task FullInstallAsync()
    {
        var p = _settings.ActiveProfile;
        if (p is null)
        {
            _log.Append("No active profile — create one in Settings first.");
            return;
        }
        Dispatcher.UIThread.Post(ModRows.Clear);
        _rowLookup.Clear();

        await _runner.RunAsync(
            "Full install",
            async (sp, ct) =>
            {
                var installer = Offline
                    ? sp.GetRequiredService<OfflineGammaInstaller>()
                    : sp.GetRequiredService<IGammaInstaller>();
                var args = GammaInstallerArgs
                    .Create(p.Anomaly, p.Gamma, p.Cache)
                    .WithCancellationToken(ct)
                    .WithDownloadGithubArchives(!SkipGithubDownloads)
                    .WithSkipExtractOnHashMatch(SkipExtractOnHashMatch)
                    .WithMo2Profile(p.Mo2Profile)
                    .WithMinimal(Minimal)
                    .WithModPackMakerPath(NullIfEmpty(ModPackMakerPath))
                    .WithModListPath(NullIfEmpty(ModListPath))
                    .WithPreserveUserLtx(PreserveUserSettings)
                    .WithPreserveMcmSettings(PreserveMcmSettings)
                    .WithExperimentalPythonServerSettings(
                        _settings.Settings.ExperimentalModDbSettings.ToServerSettings()
                    )
                    .Build();
                args.GroupedAddonRecords = await installer.BuildGroupedAddonRecordsAsync(args);
                args.AnomalyRecord = installer.BuildAnomalyRecord(args);
                installer.BuildSpecialRepoRecords(args);
                await installer.InstallAsync(args);
            }
        );
    }

    [RelayCommand]
    private async Task AnomalyInstallAsync()
    {
        var p = _settings.ActiveProfile;
        if (p is null)
        {
            _log.Append("No active profile — create one in Settings first.");
            return;
        }
        await _runner.RunAsync(
            "Anomaly install",
            async (sp, ct) =>
            {
                var factory = sp.GetRequiredService<IDownloadableRecordFactory>();
                var progress = sp.GetRequiredService<GammaProgress>();
                var anomalyInstaller = (AnomalyInstaller)factory.CreateAnomalyRecord(p.Cache, p.Anomaly);
                progress.TotalMods = 1;
                await anomalyInstaller.DownloadAsync(ct);
                await anomalyInstaller.ExtractAsync(ct);
            }
        );
    }

    [RelayCommand]
    private async Task CheckAnomalyAsync()
    {
        var p = _settings.ActiveProfile;
        if (p is null)
        {
            _log.Append("No active profile — create one in Settings first.");
            return;
        }
        CheckResultText = "Checking…";
        var result = await _runner.RunAsync(
            "Anomaly verification",
            async (_, ct) =>
            {
                var check = await _anomalyService.CheckAsync(p.Anomaly, cancellationToken: ct);
                Dispatcher.UIThread.Post(() =>
                {
                    CheckResultText =
                        $"OK: {check.Ok}, corrupt: {check.Corrupt.Count}, missing: {check.Missing.Count}";
                });
                foreach (var f in check.Corrupt)
                {
                    _log.Append($"CORRUPT: {f}");
                }
                foreach (var f in check.Missing)
                {
                    _log.Append($"MISSING: {f}");
                }
            }
        );
        if (result.Outcome != OperationOutcome.Succeeded)
        {
            CheckResultText = "Check failed — see log";
        }
    }

    [RelayCommand]
    private void PurgeShaderCache()
    {
        var p = _settings.ActiveProfile;
        if (p is null)
        {
            return;
        }
        _log.Append(
            _anomalyService.PurgeShaderCache(p.Anomaly)
                ? "Shader cache deleted"
                : "Shader cache not found"
        );
    }

    [RelayCommand]
    private void DeleteReshade()
    {
        var p = _settings.ActiveProfile;
        if (p is null)
        {
            return;
        }
        var deleted = _anomalyService.DeleteReshade(p.Anomaly);
        _log.Append(deleted.Count > 0 ? $"Deleted: {string.Join(", ", deleted)}" : "No ReShade files found");
    }

    private void ApplyProgressBatch(IList<GammaProgress.GammaInstallProgressEventArgs> batch)
    {
        foreach (var e in batch)
        {
            if (string.IsNullOrEmpty(e.Name))
            {
                continue;
            }
            if (!_rowLookup.TryGetValue(e.Name, out var row))
            {
                row = new ModProgressRow { Name = e.Name };
                _rowLookup[e.Name] = row;
                ModRows.Add(row);
            }
            row.Phase = e.ProgressType.ToString();
            row.Progress = e.Progress;
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private readonly OperationRunner _runner;
    private readonly SettingsService _settings;
    private readonly AnomalyService _anomalyService;
    private readonly LogService _log;
    private readonly Dictionary<string, ModProgressRow> _rowLookup = [];
}
