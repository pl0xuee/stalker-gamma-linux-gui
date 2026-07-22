using System;
using System.Collections.ObjectModel;
using System.Reactive.Linq;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using Stalker.Gamma.GammaInstallerServices;
using StalkerGamma.Gui.Services;

namespace StalkerGamma.Gui.ViewModels;

public sealed record NavItem(string Name, string Icon, ViewModelBase Page);

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
        NavItems =
        [
            new NavItem("INSTALL", "⇣", install),
            new NavItem("UPDATES", "⟳", updates),
            new NavItem("MODS", "▤", mods),
            new NavItem("MO2 PROFILES", "◈", profiles),
            new NavItem("SETTINGS", "⚙︎", settings),
            new NavItem("STEAM", "▶", steamSetup),
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
            });

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
                        $"{e.Name} — {e.ProgressType} {e.Progress:P0} [{e.Complete}/{e.Total}]";
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

    private readonly OperationRunner _runner;
}
