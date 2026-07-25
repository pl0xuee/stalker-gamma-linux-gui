using System;
using System.Collections.Generic;
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

    /// <summary>
    /// Comments and blank lines are kept so the file round-trips, but they are not mods —
    /// keep them out of the grid.
    /// </summary>
    public bool IsListed => Raw is null || (Raw.Length > 0 && Raw[0] != '#');

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
    /// <summary>Every entry, in modlist order — this is what gets written back.</summary>
    public List<ModRow> Mods { get; } = [];

    /// <summary>What the grid shows: <see cref="Mods"/> narrowed by <see cref="Filter"/>.</summary>
    public ObservableCollection<ModRow> VisibleMods { get; } = [];

    [ObservableProperty]
    public partial ModRow? SelectedMod { get; set; }

    [ObservableProperty]
    public partial string ProfileName { get; set; } = "";

    [ObservableProperty]
    public partial string StatusText { get; set; } = "";

    [ObservableProperty]
    public partial string Filter { get; set; } = "";

    /// <summary>Aspect ratios the bundled 2D-scope patches cover; see UltrawideFixService.</summary>
    public IReadOnlyList<string> AspectRatios => UltrawideFixService.AspectRatios;

    [ObservableProperty]
    public partial string SelectedAspectRatio { get; set; } = UltrawideFixService.AspectRatios[0];

    [ObservableProperty]
    public partial string UltrawideStatusText { get; set; } = "";

    public ModsViewModel(SettingsService settings, LogService log, UltrawideFixService ultrawideFix)
    {
        _settings = settings;
        _log = log;
        _ultrawideFix = ultrawideFix;
    }

    partial void OnFilterChanged(string value) => ApplyFilter();

    [RelayCommand]
    public async Task RefreshAsync()
    {
        Mods.Clear();
        VisibleMods.Clear();
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
            ApplyFilter();
            StatusText = CountsText();
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

    private void ApplyFilter()
    {
        VisibleMods.Clear();
        var needle = Filter?.Trim() ?? "";
        foreach (
            var mod in Mods.Where(m =>
                m.IsListed
                && (needle.Length == 0 || m.Name.Contains(needle, StringComparison.OrdinalIgnoreCase))
            )
        )
        {
            VisibleMods.Add(mod);
        }
    }

    private string CountsText()
    {
        var toggleable = Mods.Where(m => m.IsToggleable).ToList();
        var shown = VisibleMods.Count(m => m.IsToggleable);
        var basic = $"{toggleable.Count} entries ({toggleable.Count(m => m.Enabled)} enabled)";
        return shown == toggleable.Count ? basic : $"{basic} — {shown} shown";
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
            StatusText = $"{CountsText()} — saved";
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

    /// <summary>
    /// Bulk toggle of everything currently shown — the point of the filter box. Suppresses the
    /// per-row save so a 500-mod list is written once instead of 500 times.
    /// </summary>
    [RelayCommand]
    private async Task SetShownAsync(string? enable)
    {
        var target = enable == "on";
        var rows = VisibleMods.Where(m => m.IsToggleable && m.Enabled != target).ToList();
        if (rows.Count == 0)
        {
            return;
        }
        _loading = true;
        foreach (var row in rows)
        {
            row.Enabled = target;
        }
        _loading = false;
        await SaveAsync();
        _log.Append($"{(target ? "Enabled" : "Disabled")} {rows.Count} mod(s)");
    }

    /// <summary>
    /// Un-stretches the in-game UI on ultrawide monitors by switching on the widescreen fixes
    /// GAMMA already installs but ships disabled. Undo it by unticking them in the list.
    /// </summary>
    [RelayCommand]
    private async Task FixUltrawideAsync()
    {
        var p = _settings.ActiveProfile;
        if (p is null)
        {
            UltrawideStatusText = "No active profile — create one in Settings first.";
            return;
        }
        UltrawideStatusText = "Applying…";
        try
        {
            var result = await _ultrawideFix.ApplyAsync(p.Gamma, p.Mo2Profile, SelectedAspectRatio);
            UltrawideStatusText = result.Message;
            if (result.Ok)
            {
                await RefreshAsync();
            }
            else
            {
                _log.Append($"Ultrawide fix: {result.Message}");
            }
        }
        catch (Exception e)
        {
            UltrawideStatusText = $"Failed: {e.Message}";
            _log.Append($"Ultrawide fix failed: {e.Message}");
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
        VisibleMods.Remove(mod);
        await SaveAsync();
    }

    private static string ModListPath(string gamma, string mo2Profile) =>
        Path.Join(gamma, "profiles", mo2Profile, "modlist.txt");

    private readonly SettingsService _settings;
    private readonly LogService _log;
    private readonly UltrawideFixService _ultrawideFix;
    private readonly System.Threading.SemaphoreSlim _saveLock = new(1, 1);
    private bool _loading;
}
