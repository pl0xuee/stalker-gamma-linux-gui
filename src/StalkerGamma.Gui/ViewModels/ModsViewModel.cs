using System;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stalker.Gamma.Utilities;
using StalkerGamma.Gui.Services;
using StalkerGamma.Gui.Utilities;

namespace StalkerGamma.Gui.ViewModels;

public partial class ModRow(ModsViewModel owner) : ObservableObject
{
    public required string Name { get; init; }

    [ObservableProperty]
    public partial bool Enabled { get; set; }

    partial void OnEnabledChanged(bool value) => _ = owner.SaveAsync();
}

public partial class ModsViewModel : ViewModelBase
{
    public ObservableCollection<ModRow> Mods { get; } = [];

    [ObservableProperty]
    public partial ModRow? SelectedMod { get; set; }

    [ObservableProperty]
    public partial string ProfileName { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    public ModsViewModel(SettingsService settings, LogService log)
    {
        _settings = settings;
        _log = log;
    }

    [RelayCommand]
    public void Refresh()
    {
        Mods.Clear();
        _loading = true;
        try
        {
            var p = _settings.ActiveProfile;
            if (p is null)
            {
                StatusText = "No active profile — create one in Settings first.";
                return;
            }
            ProfileName = p.Mo2Profile;
            var modListPath = ModListPath(p.Gamma, p.Mo2Profile);
            if (!File.Exists(modListPath))
            {
                StatusText = $"modlist.txt not found ({modListPath}) — install GAMMA first.";
                return;
            }
            var mods = ModListUtility.GetModListAsync(modListPath).GetAwaiter().GetResult();
            foreach (var mod in mods)
            {
                Mods.Add(
                    new ModRow(this) { Name = mod.Name, Enabled = mod.Status == ModStatus.Enabled }
                );
            }
            StatusText = $"{Mods.Count} entries ({Mods.Count(m => m.Enabled)} enabled)";
        }
        finally
        {
            _loading = false;
        }
    }

    public async Task SaveAsync()
    {
        if (_loading)
        {
            return;
        }
        var p = _settings.ActiveProfile;
        if (p is null)
        {
            return;
        }
        var mods = Mods.Select(m => new ModListRecord
        {
            Name = m.Name,
            Status = m.Enabled ? ModStatus.Enabled : ModStatus.Disabled,
        }).ToList();
        await ModListUtility.SaveModListAsync(ModListPath(p.Gamma, p.Mo2Profile), mods);
        StatusText = $"{Mods.Count} entries ({Mods.Count(m => m.Enabled)} enabled) — saved";
    }

    [RelayCommand]
    private async Task DeleteSelectedAsync()
    {
        var p = _settings.ActiveProfile;
        var mod = SelectedMod;
        if (p is null || mod is null)
        {
            return;
        }
        // Separators ("_separator" suffix in MO2) and real mods both live in <gamma>/mods.
        var modDir = Path.Join(p.Gamma, "mods", mod.Name);
        if (Directory.Exists(modDir))
        {
            DirUtils.NormalizePermissions(modDir);
            Directory.Delete(modDir, true);
            _log.Append($"Deleted mod directory {modDir}");
        }
        Mods.Remove(mod);
        await SaveAsync();
    }

    private static string ModListPath(string gamma, string mo2Profile) =>
        Path.Join(gamma, "profiles", mo2Profile, "modlist.txt");

    private readonly SettingsService _settings;
    private readonly LogService _log;
    private bool _loading;
}
