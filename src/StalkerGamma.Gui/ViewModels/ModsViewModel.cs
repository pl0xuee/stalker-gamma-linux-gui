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

    /// <summary>Non-null for MO2 lines we don't manage (not +/-); preserved verbatim.</summary>
    public string? Raw { get; init; }

    public bool IsToggleable => Raw is null;

    partial void OnEnabledChanged(bool value)
    {
        if (Raw is null)
        {
            _ = owner.SaveAsync();
        }
    }
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
    public async Task RefreshAsync()
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
            var mods = await ModListUtility.GetModListAsync(modListPath);
            foreach (var mod in mods)
            {
                Mods.Add(
                    new ModRow(this)
                    {
                        Name = mod.Name,
                        Enabled = mod.Status == ModStatus.Enabled,
                        Raw = mod.Status == ModStatus.Passthrough ? mod.Raw : null,
                    }
                );
            }
            var toggleable = Mods.Where(m => m.IsToggleable).ToList();
            StatusText = $"{toggleable.Count} entries ({toggleable.Count(m => m.Enabled)} enabled)";
        }
        catch (Exception e)
        {
            StatusText = e.Message;
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
        // Serialize writes: rapid checkbox toggling must not overlap writes to modlist.txt,
        // and IO failures must surface instead of vanishing as unobserved task exceptions.
        await _saveLock.WaitAsync();
        try
        {
            var mods = Mods.Select(m => m.Raw is not null
                ? new ModListRecord { Name = m.Name, Status = ModStatus.Passthrough, Raw = m.Raw }
                : new ModListRecord
                {
                    Name = m.Name,
                    Status = m.Enabled ? ModStatus.Enabled : ModStatus.Disabled,
                }).ToList();
            await ModListUtility.SaveModListAsync(ModListPath(p.Gamma, p.Mo2Profile), mods);
            StatusText = $"{Mods.Count} entries ({Mods.Count(m => m.Enabled)} enabled) — saved";
        }
        catch (Exception e)
        {
            StatusText = $"Save failed: {e.Message}";
            _log.Append($"modlist.txt save failed: {e.Message}");
        }
        finally
        {
            _saveLock.Release();
        }
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
        if (
            !await ConfirmDialog.ShowAsync(
                "Delete mod",
                $"Delete '{mod.Name}' and its files from the mods directory? This cannot be undone."
            )
        )
        {
            return;
        }
        // Separators ("_separator" suffix in MO2) and real mods both live in <gamma>/mods.
        var modDir = Path.Join(p.Gamma, "mods", mod.Name);
        try
        {
            if (Directory.Exists(modDir))
            {
                DirUtils.NormalizePermissions(modDir);
                Directory.Delete(modDir, true);
                _log.Append($"Deleted mod directory {modDir}");
            }
        }
        catch (Exception e)
        {
            // Files held open by the running game would otherwise crash the whole app.
            _log.Append($"Could not delete '{mod.Name}': {e.Message}");
            return;
        }
        Mods.Remove(mod);
        await SaveAsync();
    }

    private static string ModListPath(string gamma, string mo2Profile) =>
        Path.Join(gamma, "profiles", mo2Profile, "modlist.txt");

    private readonly SettingsService _settings;
    private readonly LogService _log;
    private readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);
    private bool _loading;
}
