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

    /// <summary>True while a download reports no total size (shows a busy bar, not a bogus percentage).</summary>
    [ObservableProperty]
    public partial bool IsIndeterminate { get; set; }

    /// <summary>"47.30 %" or, when the size is unknown, "123.45 MB".</summary>
    [ObservableProperty]
    public partial string Detail { get; set; } = "";
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

    /// <summary>Experimental: pull the repacked Anomaly archive (extracts faster), falling back to ModDB.</summary>
    [ObservableProperty]
    public partial bool UseAnomalyMirror { get; set; }

    /// <summary>A previous full install failed and left a resume file behind.</summary>
    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasResume))]
    public partial string ResumeText { get; set; } = "";

    public bool HasResume => !string.IsNullOrEmpty(ResumeText);

    /// <summary>Bound by the view to grey out actions while an operation runs.</summary>
    public OperationRunner Runner => _runner;

    public InstallViewModel(
        OperationRunner runner,
        SettingsService settings,
        AnomalyService anomalyService,
        LogService log,
        GammaProgress gammaProgress,
        Services.Steam.SteamLocator steamLocator,
        Services.Steam.CompatToolCatalog compatToolCatalog,
        Services.Steam.SteamIntegrationService steamIntegration,
        Services.Steam.ProtontricksService protontricks,
        ResumeService resume
    )
    {
        _runner = runner;
        _resume = resume;
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

        // A run that fails leaves resume.txt behind; one that succeeds removes it.
        runner.Completed += (_, _) => Dispatcher.UIThread.Post(RefreshResume);
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
        RefreshResume();
    }

    private void RefreshResume()
    {
        var pending = _resume.Find(_settings.ActiveProfile?.Gamma);
        // An unfinished update is the Updates page's to offer.
        _resumeIsFullInstall = pending?.Command == ResumeService.FullInstallCommand;
        ResumeText = _resumeIsFullInstall
            ? "The last install did not finish. Resume skips everything that already completed."
            : "";
    }

    [RelayCommand]
    private async Task ResumeInstallAsync()
    {
        if (!_resumeIsFullInstall)
        {
            return;
        }
        await RunFullInstallAsync(resume: true);
    }

    [RelayCommand]
    private void DiscardResume()
    {
        if (_settings.ActiveProfile is { } p)
        {
            _resume.Delete(p.Gamma);
        }
        RefreshResume();
    }

    [RelayCommand]
    private Task FullInstallAsync() => RunFullInstallAsync(resume: false);

    private async Task RunFullInstallAsync(bool resume)
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

        await _runner.RunAsync(
            resume ? "Resume install" : "Full install",
            (sp, ct) => FullInstallCoreAsync(sp, p, resume, ct)
        );
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
                await FullInstallCoreAsync(sp, p, resume: false, ct);
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
        bool resume,
        System.Threading.CancellationToken ct
    )
    {
        sp.GetRequiredService<Stalker.Gamma.Models.StalkerGammaSettings>().UseAnomalyMirror =
            UseAnomalyMirror;
        var installer = Offline
            ? sp.GetRequiredService<OfflineGammaInstaller>()
            : sp.GetRequiredService<IGammaInstaller>();
        var settings = sp.GetRequiredService<Stalker.Gamma.Models.StalkerGammaSettings>();
        var resumeState = await _resume.LoadAsync(resume, p.Gamma, ResumeService.FullInstallCommand, ct);
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
            .WithUseStalkerGammaServerUrl(settings.StalkerGammaServerUrl)
            .WithResumeState(resumeState)
            .Build();
        try
        {
            args.GroupedAddonRecords = await installer.BuildGroupedAddonRecordsAsync(args);
            args.AnomalyRecord = installer.BuildAnomalyRecord(args);
            installer.BuildSpecialRepoRecords(args);
            await installer.InstallAsync(args);
        }
        catch
        {
            // Leave resume.txt so the next run can continue instead of re-downloading 40 GB.
            _resume.Write(resumeState, p.Gamma);
            throw;
        }
        _resume.Delete(p.Gamma);
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
                sp.GetRequiredService<Stalker.Gamma.Models.StalkerGammaSettings>().UseAnomalyMirror =
                    UseAnomalyMirror;
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
                // Hashing a few GB takes a while; report whole-percent steps only so the UI
                // thread isn't flooded with one post per file.
                var lastPct = -1;
                var check = await _anomalyService.CheckAsync(
                    p.Anomaly,
                    onProgress: fraction =>
                    {
                        var pct = (int)(fraction * 100);
                        if (pct == lastPct)
                        {
                            return;
                        }
                        lastPct = pct;
                        Dispatcher.UIThread.Post(() => CheckResultText = $"Checking… {pct}%");
                    },
                    cancellationToken: ct
                );
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
            row.Phase = e.ProgressType.GetHumanReadableString();
            row.IsIndeterminate = e.Progress is null;
            row.Progress = e.Progress ?? 0;
            row.Detail = e.GetHumanReadableProgress();
        }
    }

    [RelayCommand]
    private async Task BrowseOfflineListAsync(string? which)
    {
        if (which == "ModPackMaker")
        {
            var picked = await PathPicker.PickFileAsync(
                "Select modpack_maker_list.txt",
                ModPackMakerPath ?? _settings.ActiveProfile?.Cache,
                ".txt"
            );
            if (picked is not null)
            {
                ModPackMakerPath = picked;
            }
        }
        else
        {
            var picked = await PathPicker.PickFileAsync(
                "Select modlist.txt",
                ModListPath ?? _settings.ActiveProfile?.Gamma,
                ".txt"
            );
            if (picked is not null)
            {
                ModListPath = picked;
            }
        }
    }

    [RelayCommand]
    private void OpenGammaFolder()
    {
        var (ok, message) = PathPicker.Reveal(_settings.ActiveProfile?.Gamma);
        if (!ok)
        {
            CheckResultText = message;
        }
    }

    private static string? NullIfEmpty(string? s) => string.IsNullOrWhiteSpace(s) ? null : s;

    private readonly OperationRunner _runner;
    private readonly ResumeService _resume;
    private bool _resumeIsFullInstall;
    private readonly SettingsService _settings;
    private readonly AnomalyService _anomalyService;
    private readonly LogService _log;
    private readonly Services.Steam.SteamLocator _steamLocator;
    private readonly Services.Steam.CompatToolCatalog _compatToolCatalog;
    private readonly Services.Steam.SteamIntegrationService _steamIntegration;
    private readonly Services.Steam.ProtontricksService _protontricks;
    private readonly Dictionary<string, ModProgressRow> _rowLookup = [];
}
