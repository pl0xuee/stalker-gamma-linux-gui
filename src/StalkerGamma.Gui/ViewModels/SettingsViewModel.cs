using System;
using System.Collections.ObjectModel;
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

    [ObservableProperty]
    [NotifyPropertyChangedFor(nameof(HasSelection))]
    public partial CliProfile? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

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

    public SettingsViewModel(SettingsService settings, LogService log, AppUpdateService appUpdate)
    {
        _settings = settings;
        _log = log;
        _appUpdate = appUpdate;
        Reload();
    }

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
        CanInstallUpdate = false;
        try
        {
            var progress = new Progress<double>(p =>
                UpdateStatusText = $"Downloading {_pendingUpdate.LatestTag}… {p:P0}"
            );
            var updated = await _appUpdate.DownloadAndInstallAsync(assetUrl, progress);
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

    public void Reload()
    {
        Profiles.Clear();
        foreach (var p in _settings.Settings.Profiles)
        {
            Profiles.Add(p);
        }
        SelectedProfile = _settings.ActiveProfile ?? Profiles.FirstOrDefault();
        StatusText = $"Settings file: {CliSettings.SettingsPath}";
    }

    [RelayCommand]
    private void NewProfile()
    {
        // Absolute defaults: relative paths would resolve against the AppImage's launch
        // directory, scattering a 40GB install wherever the app happened to start.
        var gamesDir = System.IO.Path.Join(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            "Games",
            "GAMMA"
        );
        var profile = new CliProfile
        {
            ProfileName = UniqueName("Gamma"),
            Active = Profiles.Count == 0,
            Anomaly = System.IO.Path.Join(gamesDir, "anomaly"),
            Gamma = System.IO.Path.Join(gamesDir, "gamma"),
            Cache = System.IO.Path.Join(gamesDir, "cache"),
        };
        _settings.Settings.Profiles.Add(profile);
        Profiles.Add(profile);
        SelectedProfile = profile;
    }

    [RelayCommand]
    private async Task SaveAsync()
    {
        // Force the bound editors to flush by re-reading SelectedProfile state; bindings write
        // directly into the CliProfile instances, so persisting the container is enough.
        await _settings.SaveAsync();
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
        var name = SelectedProfile.ProfileName;
        Reload();
        SelectedProfile = Profiles.FirstOrDefault(x => x.ProfileName == name);
        StatusText = $"Active profile: {name}";
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
        Profiles.Remove(SelectedProfile);
        if (_settings.ActiveProfile is null && _settings.Settings.Profiles.FirstOrDefault() is { } first)
        {
            first.Active = true;
        }
        await _settings.SaveAsync();
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
    private AppUpdateCheck? _pendingUpdate;
}
