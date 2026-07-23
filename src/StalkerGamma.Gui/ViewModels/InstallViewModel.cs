using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
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

    [ObservableProperty]
    public partial bool SetupSteamAfter { get; set; } = true;

    public InstallViewModel(
        OperationRunner runner,
        SettingsService settings,
        AnomalyService anomalyService,
        LogService log,
        GammaProgress gammaProgress,
        Services.Steam.SteamLocator steamLocator,
        Services.Steam.CompatToolCatalog compatToolCatalog,
        Services.Steam.SteamIntegrationService steamIntegration,
        Services.Steam.ProtontricksService protontricks
    )
    {
        _runner = runner;
        _settings = settings;
        _anomalyService = anomalyService;
        _log = log;
        _steamLocator = steamLocator;
        _compatToolCatalog = compatToolCatalog;
        _steamIntegration = steamIntegration;
        _protontricks = protontricks;

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
        if (!ValidateInstallInputs(p))
        {
            return;
        }
        // Don't wipe the live progress table if a run is already going (the runner would
        // reject this click anyway).
        if (_runner.IsBusy)
        {
            return;
        }
        Dispatcher.UIThread.Post(ModRows.Clear);
        _rowLookup.Clear();

        await _runner.RunAsync("Full install", (sp, ct) => FullInstallCoreAsync(sp, p, ct));
    }

    /// <summary>Fail before the download starts, not 30GB in.</summary>
    private bool ValidateInstallInputs(Models.CliProfile p)
    {
        // Relative paths resolve against the AppImage's arbitrary launch CWD — a CLI-written
        // settings.json still carries relative defaults like "gamma/anomaly".
        foreach (var (label, path) in new[] { ("Anomaly", p.Anomaly), ("GAMMA", p.Gamma), ("Cache", p.Cache) })
        {
            if (!Path.IsPathRooted(path))
            {
                CheckResultText = $"{label} path '{path}' is relative — make it absolute in Settings.";
                _log.Append(CheckResultText);
                return false;
            }
        }
        if (Offline && (string.IsNullOrWhiteSpace(ModPackMakerPath) || string.IsNullOrWhiteSpace(ModListPath)))
        {
            CheckResultText = "Offline install needs both modpack_maker_list and modlist.txt paths.";
            _log.Append(CheckResultText);
            return false;
        }
        return true;
    }

    /// <summary>One click: default profile if needed → full install → Steam setup.</summary>
    [RelayCommand]
    private async Task OneClickInstallAsync()
    {
        var p = _settings.ActiveProfile;
        if (p is null)
        {
            p = CreateDefaultProfile();
            await _settings.SaveAsync();
            RefreshProfilePaths();
            _log.Append(
                $"Created default profile '{p.ProfileName}' (Anomaly: {p.Anomaly}, GAMMA: {p.Gamma})"
            );
        }

        if (!ValidateInstallInputs(p))
        {
            return;
        }

        Services.Steam.SteamSetupContext? steamCtx = null;
        if (SetupSteamAfter)
        {
            var (ctx, problem) = await BuildSteamContextAsync(p);
            if (ctx is null)
            {
                CheckResultText = $"Steam setup blocked: {problem}";
                _log.Append($"Cannot set up Steam: {problem}");
                _log.Append("Fix the issue or untick 'Set up Steam afterwards', then retry.");
                return;
            }
            steamCtx = ctx;
        }

        if (_runner.IsBusy)
        {
            return;
        }
        Dispatcher.UIThread.Post(ModRows.Clear);
        _rowLookup.Clear();

        await _runner.RunAsync(
            "One-click install",
            async (sp, ct) =>
            {
                await FullInstallCoreAsync(sp, p, ct);
                if (steamCtx is not null)
                {
                    if (!File.Exists(steamCtx.Mo2Exe))
                    {
                        throw new FileNotFoundException(
                            $"Install finished but {steamCtx.Mo2Exe} does not exist — skipping Steam setup."
                        );
                    }
                    _log.Append("Install finished — starting Steam setup");
                    await _steamIntegration.RunAsync(steamCtx, ReportSteamStep, ct);
                    _log.Append(
                        $"All done! Launch '{steamCtx.AppName}' from your Steam library to play."
                    );
                }
            }
        );
    }

    private async Task FullInstallCoreAsync(
        IServiceProvider sp,
        Models.CliProfile p,
        System.Threading.CancellationToken ct
    )
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

    private Models.CliProfile CreateDefaultProfile()
    {
        var gamesDir = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Games",
            "GAMMA"
        );
        var name = "Gamma";
        var i = 1;
        while (_settings.Settings.Profiles.Any(x => x.ProfileName == name))
        {
            name = $"Gamma{i++}";
        }
        var profile = new Models.CliProfile
        {
            ProfileName = name,
            Active = true,
            Anomaly = Path.Join(gamesDir, "anomaly"),
            Gamma = Path.Join(gamesDir, "gamma"),
            Cache = Path.Join(gamesDir, "cache"),
        };
        foreach (var other in _settings.Settings.Profiles)
        {
            other.Active = false;
        }
        _settings.Settings.Profiles.Add(profile);
        return profile;
    }

    private async Task<(Services.Steam.SteamSetupContext? Ctx, string Problem)> BuildSteamContextAsync(
        Models.CliProfile p
    )
    {
        var steam = _steamLocator.Locate();
        if (steam is null)
        {
            return (null, "native Steam installation not found.");
        }
        var (ptOk, _) = await _protontricks.IsAvailableAsync();
        if (!ptOk)
        {
            return (null, "protontricks not found (sudo pacman -S protontricks).");
        }
        var tool = _compatToolCatalog.PickBest(_compatToolCatalog.Scan(steam.Root));
        if (tool is null)
        {
            return (null, "no Proton found in compatibilitytools.d (install proton-cachyos or GE-Proton).");
        }
        // ModOrganizer.exe existence is verified between the install and Steam phases.
        return (
            new Services.Steam.SteamSetupContext(
                steam,
                tool,
                "STALKER GAMMA",
                Path.GetFullPath(Path.Join(p.Gamma, "ModOrganizer.exe")),
                Services.Steam.SteamIntegrationService.BuildLaunchOptions([p.Anomaly, p.Gamma, p.Cache])
            ),
            ""
        );
    }

    private void ReportSteamStep(int index, Services.Steam.StepState state, string detail)
    {
        var name = Services.Steam.SteamIntegrationService.StepNames[index];
        _log.Append(
            state switch
            {
                Services.Steam.StepState.Running => $"Steam setup: {name}…",
                Services.Steam.StepState.Ok => $"Steam setup: {name} ✅",
                _ => $"Steam setup: {name} FAILED — {detail}",
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
        if (!ValidateInstallInputs(p))
        {
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
        try
        {
            _log.Append(
                _anomalyService.PurgeShaderCache(p.Anomaly)
                    ? "Shader cache deleted"
                    : "Shader cache not found"
            );
        }
        catch (Exception e)
        {
            _log.Append($"Could not purge shader cache: {e.Message}");
        }
    }

    [RelayCommand]
    private void DeleteReshade()
    {
        var p = _settings.ActiveProfile;
        if (p is null)
        {
            return;
        }
        try
        {
            var deleted = _anomalyService.DeleteReshade(p.Anomaly);
            _log.Append(deleted.Count > 0 ? $"Deleted: {string.Join(", ", deleted)}" : "No ReShade files found");
        }
        catch (Exception e)
        {
            _log.Append($"Could not delete ReShade: {e.Message}");
        }
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
    private readonly Services.Steam.SteamLocator _steamLocator;
    private readonly Services.Steam.CompatToolCatalog _compatToolCatalog;
    private readonly Services.Steam.SteamIntegrationService _steamIntegration;
    private readonly Services.Steam.ProtontricksService _protontricks;
    private readonly Dictionary<string, ModProgressRow> _rowLookup = [];
}
