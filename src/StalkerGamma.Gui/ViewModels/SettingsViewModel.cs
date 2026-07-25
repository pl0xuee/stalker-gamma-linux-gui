using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using StalkerGamma.Gui.Models;
using StalkerGamma.Gui.Services;

namespace StalkerGamma.Gui.ViewModels;

public partial class SettingsViewModel : ViewModelBase
{
    public ObservableCollection<CliProfile> Profiles { get; } = [];

    /// <summary>Bound by every page that needs to grey out actions during a run.</summary>
    public OperationRunner Runner { get; }

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial CliProfile? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial string PathWarning { get; set; } = "";

    [ObservableProperty]
    public partial bool HasUnsavedChanges { get; set; }

    public bool HasSelection => SelectedProfile is not null;

    [ObservableProperty]
    public partial string AppVersionText { get; set; } =
        $"Version {AppUpdateService.CurrentVersion}";

    [ObservableProperty]
    public partial string UpdateStatusText { get; set; } = "";

    [ObservableProperty]
    public partial string? UpdateUrl { get; set; }

    [ObservableProperty]
    public partial bool CanInstallUpdate { get; set; }

    public SettingsViewModel(
        SettingsService settings,
        LogService log,
        AppUpdateService appUpdate,
        OperationRunner runner
    )
    {
        _settings = settings;
        _log = log;
        _appUpdate = appUpdate;
        Runner = runner;
        Reload();
    }

    partial void OnSelectedProfileChanged(CliProfile? value) => UpdatePathWarning();

    [RelayCommand]
    private async Task CheckAppUpdateAsync()
    {
        UpdateStatusText = "Checking…";
        UpdateUrl = null;
        CanInstallUpdate = false;
        _pendingUpdate = null;
        try
        {
            var result = await _appUpdate.CheckAsync();
            if (result.UpdateAvailable)
            {
                _pendingUpdate = result;
                UpdateUrl = result.ReleaseUrl;
                // Self-install needs the AppImage path (from $APPIMAGE) and a downloadable
                // asset; otherwise leave the release page as the manual fallback.
                CanInstallUpdate =
                    result.AssetUrl is not null && AppUpdateService.InstalledAppImagePath is not null;
                UpdateStatusText = CanInstallUpdate
                    ? $"New release available: {result.LatestTag}"
                    : $"New release available: {result.LatestTag} — download it from the release page";
            }
            else
            {
                UpdateStatusText = $"Up to date ({result.CurrentVersion})";
            }
        }
        catch (Exception e)
        {
            UpdateStatusText = $"Check failed: {e.Message}";
        }
    }

    [RelayCommand]
    private async Task InstallUpdateAsync()
    {
        if (_pendingUpdate?.AssetUrl is not { } assetUrl)
        {
            return;
        }
        // Don't tear the app down mid-operation: a 40GB install shares this process.
        if (Runner.IsBusy)
        {
            UpdateStatusText = "Finish or cancel the running operation before updating.";
            return;
        }
        CanInstallUpdate = false;
        try
        {
            var progress = new Progress<double>(p =>
                UpdateStatusText = $"Downloading {_pendingUpdate.LatestTag}… {p:P0}"
            );
            var updated = await _appUpdate.DownloadAndInstallAsync(
                assetUrl,
                _pendingUpdate.AssetSha256,
                progress
            );
            _log.Append($"App updated to {_pendingUpdate.LatestTag}, restarting");
            UpdateStatusText = "Restarting…";
            System.Diagnostics.Process.Start(
                new System.Diagnostics.ProcessStartInfo { FileName = updated, UseShellExecute = false }
            );
            if (
                Avalonia.Application.Current?.ApplicationLifetime
                is Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            )
            {
                desktop.Shutdown();
            }
        }
        catch (Exception e)
        {
            CanInstallUpdate = true;
            UpdateStatusText = $"Update failed: {e.Message}";
            _log.Append($"App update failed: {e.Message}");
        }
    }

    [RelayCommand]
    private void OpenReleasePage()
    {
        if (UpdateUrl is null)
        {
            return;
        }
        System.Diagnostics.Process.Start(
            new System.Diagnostics.ProcessStartInfo { FileName = UpdateUrl, UseShellExecute = true }
        );
    }

    /// <summary>
    /// Re-syncs the list with the settings container. Called on every visit to the page because
    /// a one-click install can create the first profile while another page is showing.
    /// </summary>
    public void Reload()
    {
        foreach (var p in _watched)
        {
            p.PropertyChanged -= OnProfileEdited;
        }
        _watched.Clear();

        var previous = SelectedProfile;
        Profiles.Clear();
        foreach (var p in _settings.Settings.Profiles)
        {
            Profiles.Add(p);
            p.PropertyChanged += OnProfileEdited;
            _watched.Add(p);
        }
        // Keep the profile the user was editing selected; only fall back when it is gone.
        SelectedProfile = previous is not null && Profiles.Contains(previous)
            ? previous
            : _settings.ActiveProfile ?? Profiles.FirstOrDefault();
        StatusText = $"Settings file: {CliSettings.SettingsPath}";
        UpdatePathWarning();
    }

    private void OnProfileEdited(object? sender, PropertyChangedEventArgs e)
    {
        HasUnsavedChanges = true;
        if (sender == SelectedProfile)
        {
            UpdatePathWarning();
        }
    }

    /// <summary>
    /// Relative paths resolve against the AppImage's launch directory, which scatters a 40GB
    /// install wherever the app happened to start — warn while editing, not 30GB in.
    /// </summary>
    private void UpdatePathWarning()
    {
        if (SelectedProfile is not { } p)
        {
            PathWarning = "";
            return;
        }
        var bad = new[] { ("Anomaly", p.Anomaly), ("GAMMA", p.Gamma), ("Cache", p.Cache) }
            .Where(x => string.IsNullOrWhiteSpace(x.Item2) || !Path.IsPathRooted(x.Item2))
            .Select(x => x.Item1)
            .ToList();
        PathWarning = bad.Count > 0
            ? $"⚠ {string.Join(", ", bad)} must be an absolute path (starting with /)."
            : "";
    }

    [RelayCommand]
    private async Task BrowseAsync(string? which)
    {
        if (SelectedProfile is not { } p || which is null)
        {
            return;
        }
        var current = which switch
        {
            "Anomaly" => p.Anomaly,
            "Gamma" => p.Gamma,
            _ => p.Cache,
        };
        var picked = await PathPicker.PickFolderAsync($"Select the {which} directory", current);
        if (picked is null)
        {
            return;
        }
        switch (which)
        {
            case "Anomaly":
                p.Anomaly = picked;
                break;
            case "Gamma":
                p.Gamma = picked;
                break;
            default:
                p.Cache = picked;
                break;
        }
    }

    [RelayCommand]
    private void Reveal(string? which)
    {
        if (SelectedProfile is not { } p || which is null)
        {
            return;
        }
        var path = which switch
        {
            "Anomaly" => p.Anomaly,
            "Gamma" => p.Gamma,
            _ => p.Cache,
        };
        var (ok, message) = PathPicker.Reveal(path);
        if (!ok)
        {
            StatusText = message;
        }
    }

    [RelayCommand]
    private void OpenSettingsFolder()
    {
        var (ok, message) = PathPicker.Reveal(CliSettings.AppDataPath);
        if (!ok)
        {
            StatusText = message;
        }
    }

    [RelayCommand]
    private void OpenLogFile()
    {
        var (ok, message) = PathPicker.Reveal(_log.LogFilePath);
        StatusText = ok ? $"Log folder: {message}" : message;
    }

    [RelayCommand]
    private void NewProfile()
    {
        // Absolute defaults: relative paths would resolve against the AppImage's launch
        // directory, scattering a 40GB install wherever the app happened to start.
        var gamesDir = Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Games",
            "GAMMA"
        );
        var profile = new CliProfile
        {
            ProfileName = UniqueName("Gamma"),
            Active = Profiles.Count == 0,
            Anomaly = Path.Join(gamesDir, "anomaly"),
            Gamma = Path.Join(gamesDir, "gamma"),
            Cache = Path.Join(gamesDir, "cache"),
        };
        _settings.Settings.Profiles.Add(profile);
        Profiles.Add(profile);
        profile.PropertyChanged += OnProfileEdited;
        _watched.Add(profile);
        SelectedProfile = profile;
        HasUnsavedChanges = true;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        // Bindings write directly into the CliProfile instances, so persisting the container
        // is enough.
        await _settings.SaveAsync();
        HasUnsavedChanges = false;
        StatusText = $"Saved to {CliSettings.SettingsPath}";
        _log.Append("Settings saved");
    }

    [RelayCommand]
    private async Task SetActiveAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }
        foreach (var p in _settings.Settings.Profiles)
        {
            p.Active = false;
        }
        try
        {
            await SelectedProfile.SetActiveAsync();
        }
        catch (Exception e)
        {
            // SetActiveAsync touches the MO2 install when present; a missing GAMMA dir is fine here.
            _log.Append($"Note: {e.Message}");
            SelectedProfile.Active = true;
        }
        await _settings.SaveAsync();
        HasUnsavedChanges = false;
        StatusText = $"Active profile: {SelectedProfile.ProfileName}";
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        if (SelectedProfile is null)
        {
            return;
        }
        if (
            !await ConfirmDialog.ShowAsync(
                "Delete profile",
                $"Delete the config profile '{SelectedProfile.ProfileName}'? Installed files are not touched."
            )
        )
        {
            return;
        }
        _settings.Settings.Profiles.Remove(SelectedProfile);
        SelectedProfile = null;
        if (_settings.ActiveProfile is null && _settings.Settings.Profiles.FirstOrDefault() is { } first)
        {
            first.Active = true;
        }
        await _settings.SaveAsync();
        HasUnsavedChanges = false;
        Reload();
    }

    private string UniqueName(string baseName)
    {
        var name = baseName;
        var i = 1;
        while (Profiles.Any(p => p.ProfileName == name))
        {
            name = $"{baseName}{i++}";
        }
        return name;
    }

    private readonly SettingsService _settings;
    private readonly LogService _log;
    private readonly AppUpdateService _appUpdate;
    private readonly List<CliProfile> _watched = [];
    private AppUpdateCheck? _pendingUpdate;
}
