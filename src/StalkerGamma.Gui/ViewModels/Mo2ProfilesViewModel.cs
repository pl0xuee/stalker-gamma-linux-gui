using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stalker.Gamma.Utilities;
using StalkerGamma.Gui.Services;
using StalkerGamma.Gui.Utilities;

namespace StalkerGamma.Gui.ViewModels;

public partial class Mo2ProfilesViewModel : ViewModelBase
{
    public ObservableCollection<string> Profiles { get; } = [];

    [ObservableProperty]
    public partial string? SelectedProfile { get; set; }

    [ObservableProperty]
    public partial string CurrentProfileText { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    public Mo2ProfilesViewModel(SettingsService settings, LogService log)
    {
        _settings = settings;
        _log = log;
    }

    [RelayCommand]
    public void Refresh()
    {
        Profiles.Clear();
        var p = _settings.ActiveProfile;
        if (p is null)
        {
            StatusText = "No active profile — create one in Settings first.";
            return;
        }
        try
        {
            var profilesPath = ProfileUtility.ValidateProfileExists(p.Gamma);
            foreach (var dir in new DirectoryInfo(profilesPath).GetDirectories())
            {
                Profiles.Add(dir.Name);
            }
            CurrentProfileText = GetSelectedMo2Profile(p.Gamma) is { } sel
                ? $"Selected in ModOrganizer.ini: {sel}"
                : "ModOrganizer.ini not found or has no selected profile";
            StatusText = $"{Profiles.Count} profile(s)";
        }
        catch (Exception e)
        {
            StatusText = e.Message;
        }
    }

    [RelayCommand]
    private async Task SetSelectedAsync()
    {
        var p = _settings.ActiveProfile;
        if (p is null || SelectedProfile is null)
        {
            return;
        }
        var iniPath = Path.Join(p.Gamma, "ModOrganizer.ini");
        if (!File.Exists(iniPath))
        {
            StatusText = "ModOrganizer.ini not found";
            return;
        }
        try
        {
            var ini = await File.ReadAllTextAsync(iniPath);
            ini = SelectedProfileRx().Replace(ini, $"selected_profile=@ByteArray({SelectedProfile})");
            await Services.AtomicFile.WriteAllTextAsync(iniPath, ini);
            _log.Append($"MO2 selected profile set to {SelectedProfile}");
        }
        catch (Exception e)
        {
            StatusText = $"Could not set profile: {e.Message}";
            _log.Append($"Could not set MO2 profile: {e.Message}");
        }
        Refresh();
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var p = _settings.ActiveProfile;
        if (p is null || SelectedProfile is null)
        {
            return;
        }
        var profilePath = Path.Join(p.Gamma, "profiles", SelectedProfile);
        if (!Directory.Exists(profilePath))
        {
            return;
        }
        if (
            !await ConfirmDialog.ShowAsync(
                "Delete MO2 profile",
                $"Delete the MO2 profile '{SelectedProfile}' and its settings? This cannot be undone."
            )
        )
        {
            return;
        }
        try
        {
            DirUtils.NormalizePermissions(profilePath);
            Directory.Delete(profilePath, true);
            _log.Append($"Deleted MO2 profile {SelectedProfile}");
        }
        catch (Exception e)
        {
            StatusText = $"Could not delete profile: {e.Message}";
            _log.Append($"Could not delete MO2 profile: {e.Message}");
        }
        Refresh();
    }

    private static string? GetSelectedMo2Profile(string gamma)
    {
        var iniPath = Path.Join(gamma, "ModOrganizer.ini");
        if (!File.Exists(iniPath))
        {
            return null;
        }
        var match = SelectedProfileRx().Match(File.ReadAllText(iniPath));
        return match.Success ? match.Groups["profile"].Value : null;
    }

    [GeneratedRegex(@"selected_profile=@ByteArray\((?<profile>.+)\)")]
    private static partial Regex SelectedProfileRx();

    private readonly SettingsService _settings;
    private readonly LogService _log;
}
