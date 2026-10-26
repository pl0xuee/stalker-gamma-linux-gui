using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using System.Threading.Tasks;
using Avalonia.Input;
using Avalonia.Input.Platform;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stalker.Gamma.GammaInstallerServices;
using StalkerGamma.Gui.Services;

namespace StalkerGamma.Gui.ViewModels;

/// <summary>Icon is an SVG path (24x24 grid) drawn by a PathIcon, so every glyph renders at the same size.</summary>
public sealed record NavItem(string Name, string Icon, ViewModelBase Page)
{
    public Avalonia.Media.Geometry Geometry { get; } = Avalonia.Media.Geometry.Parse(Icon);
}

internal static class NavIcons
{
    public const string Install = "M5 20h14v-2H5v2zM19 9h-4V3H9v6H5l7 7 7-7z";
    public const string Updates =
        "M12 6v3l4-4-4-4v3c-4.42 0-8 3.58-8 8 0 1.57.46 3.03 1.24 4.26L6.7 14.8A5.87 5.87 0 0 1 6 12c0-3.31 2.69-6 6-6zm6.76 1.74L17.3 9.2c.44.84.7 1.79.7 2.8 0 3.31-2.69 6-6 6v-3l-4 4 4 4v-3c4.42 0 8-3.58 8-8 0-1.57-.46-3.03-1.24-4.26z";
    public const string Mods =
        "M3 14h4v-4H3v4zm0 5h4v-4H3v4zM3 9h4V5H3v4zm5 5h13v-4H8v4zm0 5h13v-4H8v4zM8 5v4h13V5H8z";
    public const string Profiles =
        "M11.99 18.54l-7.37-5.73L3 14.07l9 7 9-7-1.63-1.27-7.38 5.74zM12 16l7.36-5.73L21 9l-9-7-9 7 1.63 1.27L12 16z";
    public const string Settings =
        "M3 17v2h6v-2H3zM3 5v2h10V5H3zm10 16v-2h8v-2h-8v-2h-2v6h2zM7 9v2H3v2h4v2h2V9H7zm14 4v-2H11v2h10zm-6-4h2V7h4V5h-4V3h-2v6z";
    public const string Steam = "M8 5v14l11-7z";
}

public partial class MainViewModel : ViewModelBase
{
    public ObservableCollection<NavItem> NavItems { get; }
    public ObservableCollection<string> LogLines { get; } = [];

    [ObservableProperty]
    public partial NavItem? SelectedNavItem { get; set; }

    [ObservableProperty]
    public partial bool IsBusy { get; set; }

    [ObservableProperty]
    public partial string StatusText { get; set; } = "Ready";

    [ObservableProperty]
    public partial double OverallProgress { get; set; }

    [ObservableProperty]
    public partial bool LogPaneOpen { get; set; }

    /// <summary>Tail the log pane. Off lets you scroll back without a running install yanking you down.</summary>
    [ObservableProperty]
    public partial bool FollowLog { get; set; } = true;

    public MainViewModel(
        InstallViewModel install,
        UpdatesViewModel updates,
        ModsViewModel mods,
        Mo2ProfilesViewModel profiles,
        SettingsViewModel settings,
        SteamSetupViewModel steamSetup,
        OperationRunner runner,
        UtilitiesReadyService utilitiesReady,
        LogService log,
        GammaProgress gammaProgress
    )
    {
        _runner = runner;
        _log = log;
        NavItems =
        [
            new NavItem("INSTALL", NavIcons.Install, install),
            new NavItem("UPDATES", NavIcons.Updates, updates),
            new NavItem("MODS", NavIcons.Mods, mods),
            new NavItem("MO2 PROFILES", NavIcons.Profiles, profiles),
            new NavItem("SETTINGS", NavIcons.Settings, settings),
            new NavItem("STEAM", NavIcons.Steam, steamSetup),
        ];
        SelectedNavItem = NavItems[0];

        log.LineAdded += line =>
            Dispatcher.UIThread.Post(() =>
            {
                LogLines.Add(line);
                while (LogLines.Count > 2000)
                {
                    LogLines.RemoveAt(0);
                }
                // Mirror activity into the status bar during an operation so steps that
                // report no engine progress (protontricks, prefix creation) aren't silent
                // with the log pane closed. First line only: multi-line entries are error
                // dumps that belong in the pane, not squeezed into one status row.
                if (IsBusy)
                {
                    var text = StripTimestamp(line);
                    var newline = text.IndexOf('\n');
                    StatusText = (newline >= 0 ? text[..newline] : text).Trim();
                }
            });

        static string StripTimestamp(string line) =>
            line.Length > 10 && line[0] == '[' && line[9] == ']' ? line[10..] : line;

        runner.Started += name =>
            Dispatcher.UIThread.Post(() =>
            {
                IsBusy = true;
                OverallProgress = 0;
                StatusText = $"{name}…";
            });
        runner.Completed += (name, result) =>
            Dispatcher.UIThread.Post(() =>
            {
                IsBusy = false;
                StatusText = result.Outcome switch
                {
                    OperationOutcome.Succeeded => $"{name}: done",
                    OperationOutcome.Cancelled => $"{name}: cancelled",
                    _ => $"{name}: failed — {result.Error?.Message}",
                };
                if (result.Outcome == OperationOutcome.Failed)
                {
                    LogPaneOpen = true;
                }
            });

        // The engine fires progress from many download threads at high frequency; sample it
        // down before touching the UI thread.
        Observable
            .FromEventPattern<GammaProgress.GammaInstallProgressEventArgs>(
                h => gammaProgress.ProgressChanged += h,
                h => gammaProgress.ProgressChanged -= h
            )
            .Select(x => x.EventArgs)
            .Sample(TimeSpan.FromMilliseconds(150))
            .Subscribe(e =>
                Dispatcher.UIThread.Post(() =>
                {
                    if (!IsBusy)
                    {
                        return;
                    }
                    StatusText =
                        $"{e.Name} — {e.ProgressType.GetHumanReadableString()} {e.GetHumanReadableProgress()} [{e.Complete}/{e.Total}]";
                    OverallProgress = e.Total > 0 ? (double)e.Complete / e.Total : 0;
                })
            );
        Observable
            .FromEventPattern<GammaProgress.GammaInstallDebugProgressEventArgs>(
                h => gammaProgress.DebugProgressChanged += h,
                h => gammaProgress.DebugProgressChanged -= h
            )
            .Select(x => x.EventArgs)
            .Subscribe(e => log.Append(e.Text ?? ""));

        var (ready, reason) = utilitiesReady.Check();
        if (!ready)
        {
            StatusText = "Missing dependencies — see log";
            log.Append($"Dependency check failed:\n{reason}");
            LogPaneOpen = true;
        }
    }

    [RelayCommand]
    private void Cancel() => _runner.Cancel();

    [RelayCommand]
    private void ToggleLogPane() => LogPaneOpen = !LogPaneOpen;

    /// <summary>"See log" is useless if you can't get the log out of the app.</summary>
    [RelayCommand]
    private async Task CopyLogAsync()
    {
        if (
            Avalonia.Application.Current?.ApplicationLifetime
                is not Avalonia.Controls.ApplicationLifetimes.IClassicDesktopStyleApplicationLifetime desktop
            || desktop.MainWindow?.Clipboard is not { } clipboard
        )
        {
            return;
        }
        await clipboard.SetValueAsync(DataFormat.Text, string.Join('\n', LogLines));
        StatusText = $"Copied {LogLines.Count} log line(s) to the clipboard";
    }

    [RelayCommand]
    private void ClearLog() => LogLines.Clear();

    [RelayCommand]
    private void OpenLogFile()
    {
        var (ok, message) = PathPicker.Reveal(_log.LogFilePath);
        StatusText = ok ? $"Log folder: {message}" : message;
    }

    private readonly OperationRunner _runner;
    private readonly LogService _log;
}
